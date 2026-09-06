using System;
using System.Runtime.InteropServices;

namespace ClientPlugin.Compatibility;

/// <summary>
/// Feeds Wayland input to Steam's existing Xlib hooks. The game window and
/// Vulkan surface stay on Wayland; X11 games already pass through these hooks.
/// All calls belong to the SDL thread, on a separate X connection.
/// </summary>
internal sealed class SteamOverlayInput : IDisposable
{
    private const string Xlib = "libX11.so.6";
    private readonly IntPtr display;
    private readonly nuint root;
    private readonly nuint window;
    private readonly CheckEvent checkEvent;
    private readonly MatchEvent matchEvent;
    private nuint serial;
    private int eventType;
    private uint modifiers;
    private uint buttons;
    private int x;
    private int y;
    private bool focused;
    private bool disposed;

    private SteamOverlayInput(IntPtr display, SetWindowType setWindowType, CheckEvent checkEvent)
    {
        this.display = display;
        this.checkEvent = checkEvent;
        matchEvent = (IntPtr _, ref XEvent e, IntPtr _) =>
            e.Serial == serial && e.Window == window && e.Type == eventType ? 1 : 0;
        root = XDefaultRootWindow(display);
        WindowAttributes attributes = new()
        {
            OverrideRedirect = 1,
            EventMask = 1 | 2 | 4 | 8 | 64 | (1 << 21),
        };
        // Unmanaged 1x1 proxy outside the visible desktop; never a game surface.
        window = XCreateWindow(
            display,
            root,
            -1,
            -1,
            1,
            1,
            0,
            0,
            1,
            IntPtr.Zero,
            (1u << 9) | (1u << 11),
            ref attributes
        );
        // Steam Input must associate proxy focus with this game, not a desktop profile.
        string name = "steam_app_" + Environment.GetEnvironmentVariable("SteamAppId");
        ClassHint classHint = new() { Name = name, Class = name };
        XSetClassHint(display, window, ref classHint);
        XStoreName(display, window, name);
        nuint pid = (nuint)Environment.ProcessId;
        XChangeProperty(
            display,
            window,
            XInternAtom(display, "_NET_WM_PID", 0),
            6,
            32,
            0,
            ref pid,
            1
        ); // XA_CARDINAL
        setWindowType(window, 2); // Steam's Xlib window type.
        XMapWindow(display, window);
        XSync(display, 0);
    }

    internal static SteamOverlayInput TryCreate(bool isWayland)
    {
        if (
            !isWayland
            || !OperatingSystem.IsLinux()
            || IntPtr.Size != 8
            || Environment.GetEnvironmentVariable("ENABLE_VK_LAYER_VALVE_steam_overlay_1") != "1"
            || Environment.GetEnvironmentVariable("DISABLE_VK_LAYER_VALVE_steam_overlay_1") == "1"
        )
            return null;

        // Resolve the interposed functions from the process, not libX11's unhooked exports.
        IntPtr process = NativeLibrary.GetMainProgramHandle();
        if (
            !NativeLibrary.TryGetExport(process, "VulkanSteamOverlaySetWindowType", out var set)
            || !NativeLibrary.TryGetExport(process, "XCheckIfEvent", out var check)
        )
            return null;

        try
        {
            XInitThreads();
            IntPtr display = XOpenDisplay(null);
            if (display == IntPtr.Zero)
                return null;

            var bridge = new SteamOverlayInput(
                display,
                Marshal.GetDelegateForFunctionPointer<SetWindowType>(set),
                Marshal.GetDelegateForFunctionPointer<CheckEvent>(check)
            );
            Console.WriteLine("[LinuxCompat] Steam overlay Wayland input bridge enabled");
            return bridge;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    internal void Focus(bool value)
    {
        if (focused == value || disposed)
            return;
        focused = value;
        if (value)
            XSetInputFocus(display, window, 2, 0);
        else
        {
            XGetInputFocus(display, out nuint current, out _);
            if (current == window)
                XSetInputFocus(display, 1, 1, 0); // PointerRoot, RevertToPointerRoot.
            modifiers = buttons = 0;
        }
        XSync(display, 0);
        XEvent e = new()
        {
            Type = value ? 9 : 10,
            FocusMode = 0,
            FocusDetail = 3,
        };
        Forward(ref e);
    }

    internal bool Key(ushort raw, ushort sdlModifiers, bool down, ulong timestamp)
    {
        // SDL's Wayland raw key is the evdev keycode. XKB/X11 add eight.
        if (!focused || raw == 0 || raw > 247)
            return false;
        XEvent e = InputEvent(down ? 2 : 3, timestamp);
        e.Code = (uint)raw + 8;
        bool consumed = Forward(ref e);
        modifiers = ToXModifiers(sdlModifiers);
        return consumed;
    }

    internal bool Motion(float px, float py, ulong timestamp)
    {
        x = (int)px;
        y = (int)py;
        XEvent e = InputEvent(6, timestamp);
        return focused && Forward(ref e);
    }

    internal bool Button(byte button, bool down, ulong timestamp)
    {
        // SDL and X11 agree on left/middle/right; extra buttons follow the wheel slots.
        uint code = button <= 3 ? button : (uint)button + 4;
        XEvent e = InputEvent(down ? 4 : 5, timestamp);
        e.Code = code;
        bool consumed = focused && Forward(ref e);
        uint mask = code <= 5 ? 1u << ((int)code + 7) : 0;
        buttons = down ? buttons | mask : buttons & ~mask;
        return consumed;
    }

    internal bool Wheel(int horizontal, int vertical, ulong timestamp) =>
        WheelAxis(vertical, 4, 5, timestamp) | WheelAxis(horizontal, 7, 6, timestamp);

    private bool WheelAxis(int amount, uint positive, uint negative, ulong timestamp)
    {
        bool consumed = false;
        for (int i = 0; i < Math.Abs(amount); i++)
        {
            XEvent e = InputEvent(4, timestamp);
            e.Code = amount > 0 ? positive : negative;
            consumed |= focused && Forward(ref e);
            e.Type = 5;
            consumed |= focused && Forward(ref e);
        }
        return consumed;
    }

    internal static uint ToXModifiers(ushort value) =>
        ((value & 0x0003) != 0 ? 1u : 0) // Shift
        | ((value & 0x2000) != 0 ? 2u : 0) // CapsLock
        | ((value & 0x00c0) != 0 ? 4u : 0) // Control
        | ((value & 0x0300) != 0 ? 8u : 0) // Alt
        | ((value & 0x1000) != 0 ? 16u : 0) // NumLock
        | ((value & 0x0c00) != 0 ? 64u : 0) // Super
        | ((value & 0x4000) != 0 ? 128u : 0); // AltGr

    private XEvent InputEvent(int type, ulong timestamp) =>
        new()
        {
            Type = type,
            Root = root,
            Time = (nuint)(timestamp / 1_000_000),
            X = x,
            Y = y,
            RootX = x,
            RootY = y,
            State = modifiers | buttons,
            SameScreen = 1,
        };

    private bool Forward(ref XEvent e)
    {
        e.Display = display;
        e.Window = window;
        e.Serial = ++serial;
        eventType = e.Type;
        // Bypass Steam's public XPutBackEvent wrapper so the hook sees the event once.
        XLockDisplay(display);
        PutBackEvent(display, ref e);
        XUnlockDisplay(display);
        return checkEvent(display, out _, matchEvent, IntPtr.Zero) == 0;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        Focus(false);
        XDestroyWindow(display, window);
        XCloseDisplay(display);
        disposed = true;
    }

    // LP64 Xlib ABI. TryCreate declines other architectures before using these layouts.
    [StructLayout(LayoutKind.Explicit, Size = 192)]
    private struct XEvent
    {
        [FieldOffset(0)]
        internal int Type;

        [FieldOffset(8)]
        internal nuint Serial;

        [FieldOffset(24)]
        internal IntPtr Display;

        [FieldOffset(32)]
        internal nuint Window;

        [FieldOffset(40)]
        internal nuint Root;

        [FieldOffset(40)]
        internal int FocusMode;

        [FieldOffset(44)]
        internal int FocusDetail;

        [FieldOffset(56)]
        internal nuint Time;

        [FieldOffset(64)]
        internal int X;

        [FieldOffset(68)]
        internal int Y;

        [FieldOffset(72)]
        internal int RootX;

        [FieldOffset(76)]
        internal int RootY;

        [FieldOffset(80)]
        internal uint State;

        [FieldOffset(84)]
        internal uint Code;

        [FieldOffset(88)]
        internal int SameScreen;
    }

    [StructLayout(LayoutKind.Explicit, Size = 112)]
    private struct WindowAttributes
    {
        [FieldOffset(72)]
        internal nint EventMask;

        [FieldOffset(88)]
        internal int OverrideRedirect;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct ClassHint
    {
        [MarshalAs(UnmanagedType.LPStr)]
        internal string Name;

        [MarshalAs(UnmanagedType.LPStr)]
        internal string Class;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetWindowType(nuint window, int type);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MatchEvent(IntPtr display, ref XEvent e, IntPtr arg);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CheckEvent(IntPtr display, out XEvent e, MatchEvent match, IntPtr arg);

    [DllImport(Xlib)]
    private static extern int XInitThreads();

    [DllImport(Xlib)]
    private static extern IntPtr XOpenDisplay(string name);

    [DllImport(Xlib)]
    private static extern nuint XDefaultRootWindow(IntPtr display);

    [DllImport(Xlib)]
    private static extern nuint XCreateWindow(
        IntPtr display,
        nuint parent,
        int x,
        int y,
        uint width,
        uint height,
        uint border,
        int depth,
        uint windowClass,
        IntPtr visual,
        nuint mask,
        ref WindowAttributes attributes
    );

    [DllImport(Xlib)]
    private static extern int XMapWindow(IntPtr display, nuint window);

    [DllImport(Xlib)]
    private static extern int XSetClassHint(IntPtr display, nuint window, ref ClassHint hint);

    [DllImport(Xlib)]
    private static extern int XStoreName(IntPtr display, nuint window, string name);

    [DllImport(Xlib)]
    private static extern nuint XInternAtom(IntPtr display, string name, int onlyIfExists);

    [DllImport(Xlib)]
    private static extern int XChangeProperty(
        IntPtr display,
        nuint window,
        nuint property,
        nuint type,
        int format,
        int mode,
        ref nuint data,
        int count
    );

    [DllImport(Xlib)]
    private static extern int XSync(IntPtr display, int discard);

    [DllImport(Xlib)]
    private static extern int XSetInputFocus(IntPtr display, nuint window, int revert, nuint time);

    [DllImport(Xlib)]
    private static extern int XGetInputFocus(IntPtr display, out nuint window, out int revert);

    [DllImport(Xlib)]
    private static extern void XLockDisplay(IntPtr display);

    [DllImport(Xlib)]
    private static extern void XUnlockDisplay(IntPtr display);

    [DllImport(Xlib, EntryPoint = "_XPutBackEvent")]
    private static extern int PutBackEvent(IntPtr display, ref XEvent e);

    [DllImport(Xlib)]
    private static extern int XDestroyWindow(IntPtr display, nuint window);

    [DllImport(Xlib)]
    private static extern int XCloseDisplay(IntPtr display);
}
