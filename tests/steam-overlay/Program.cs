using System.Diagnostics;
using System.Runtime.InteropServices;
using ClientPlugin.Compatibility;

void Check(bool value, string message)
{
    if (!value)
        throw new Exception(message);
}
Check(SteamOverlayInput.ToXModifiers(0) == 0, "No modifiers");
Check(
    SteamOverlayInput.ToXModifiers(1) == 1 && SteamOverlayInput.ToXModifiers(2) == 1,
    "Both Shift keys"
);
Check(
    SteamOverlayInput.ToXModifiers(0x40) == 4 && SteamOverlayInput.ToXModifiers(0x80) == 4,
    "Both Control keys"
);
Check(
    SteamOverlayInput.ToXModifiers(0x100) == 8 && SteamOverlayInput.ToXModifiers(0x200) == 8,
    "Both Alt keys"
);
Check(SteamOverlayInput.ToXModifiers(0x7fc3) == 223, "Combined modifiers");
Check(SteamOverlayInput.TryCreate(false) == null, "X11 never creates an input bridge");
if (!args.Contains("--native"))
{
    Environment.SetEnvironmentVariable("DISABLE_VK_LAYER_VALVE_steam_overlay_1", "1");
    Check(SteamOverlayInput.TryCreate(true) == null, "Disabled overlay creates no proxy");
    Console.WriteLine(
        "Steam overlay input checks passed. Use --native for the SDL/Vulkan/controller integration check."
    );
    return;
}
if (!Sdl.Init(0x20 | 0x200 | 0x2000))
    throw new Exception(Marshal.PtrToStringUTF8(Sdl.Error()));
string driver = Marshal.PtrToStringUTF8(Sdl.Driver());
Console.WriteLine("Video driver: " + driver);
var window = Sdl.CreateWindow("Pulsar overlay input check", 640, 360, 0x10000000);
var renderer = Sdl.CreateRenderer(window, "vulkan");
if (renderer == IntPtr.Zero)
    throw new Exception(Marshal.PtrToStringUTF8(Sdl.Error()));
using var overlay = SteamOverlayInput.TryCreate(driver == "wayland");
Check((overlay != null) == (driver == "wayland"), "Only Wayland creates a bridge");
Sdl.VirtualJoystickDesc desc = new()
{
    Version = 136,
    Type = 1,
    Axes = 6,
    Buttons = 15,
    ButtonMask = 32767,
    AxisMask = 63,
};
uint joystickId = Sdl.AttachVirtualJoystick(ref desc);
if (joystickId == 0)
    throw new Exception(Marshal.PtrToStringUTF8(Sdl.Error()));
var joystick = Sdl.OpenJoystick(joystickId);
var gamepad = Sdl.OpenGamepad(joystickId);
Check(gamepad != IntPtr.Zero, "Virtual controller has gamepad mapping");
var clock = Stopwatch.StartNew();
int stage = 0;
int ticks = 0;
int joystickEvents = 0;
int eventsAtOpen = 0;
int eventsAtClose = 0;
bool inputResumed = false;

while (clock.Elapsed.TotalSeconds < (overlay == null ? 4 : 14))
{
    Sdl.SetJoystickVirtualAxis(joystick, 0, (short)(ticks++ % 2 == 0 ? 12345 : -12345));
    Sdl.SetJoystickVirtualButton(joystick, 0, ticks % 2 == 0);
    while (Sdl.PollEvent(out var e))
    {
        if (e.Type >= 0x600 && e.Type < 0x700)
            joystickEvents++;
    }
    if (Math.Abs(Sdl.GetJoystickAxis(joystick, 0)) != 12345)
        throw new Exception("Virtual joystick axis lost");
    Check(Math.Abs(Sdl.GetGamepadAxis(gamepad, 0)) == 12345, "Gamepad mapping preserves axis");
    Check(Sdl.GetGamepadButton(gamepad, 0) == (ticks % 2 == 0), "Gamepad mapping preserves button");
    double elapsed = clock.Elapsed.TotalSeconds;
    if (overlay != null && stage == 0 && elapsed > 1)
    {
        overlay.Focus(true);
        stage++;
    }
    if (stage == 1 && elapsed > 3)
    {
        Check(Sdl.GetKeyboardFocus() == window, "Proxy preserves SDL keyboard focus");
        Console.WriteLine("Toggle overlay open");
        Toggle();
        eventsAtOpen = joystickEvents;
        stage++;
    }
    if (stage == 2 && elapsed > 6)
    {
        Check(overlay.Motion(320, 180, 0), "Open overlay consumes pointer motion");
        Console.WriteLine("Toggle overlay closed");
        Toggle();
        eventsAtClose = joystickEvents;
        stage++;
    }
    if (stage == 3 && elapsed > 9)
    {
        Check(!overlay.Key(30, 0, true, 0), "Keyboard input resumes after closing");
        overlay.Key(30, 0, false, 0);
        overlay.Focus(false);
        Check(!overlay.Key(15, 1, true, 0), "Unfocused keys are not forwarded");
        overlay.Focus(true);
        inputResumed = true;
        stage++;
    }
    Sdl.SetRenderDrawColor(renderer, 30, 70, 90, 255);
    Sdl.RenderClear(renderer);
    Sdl.RenderPresent(renderer);
    Thread.Sleep(8);
}
Console.WriteLine("Virtual joystick events delivered: " + joystickEvents);
if (joystickEvents < 100)
    throw new Exception("Joystick event delivery interrupted");
if (overlay != null)
{
    Check(
        eventsAtOpen > 100
            && eventsAtClose - eventsAtOpen > 100
            && joystickEvents - eventsAtClose > 100,
        "Controller events flow before, during, and after overlay"
    );
    Check(inputResumed, "Overlay toggle checks completed");
}
Sdl.CloseGamepad(gamepad);
Sdl.CloseJoystick(joystick);
Sdl.DetachVirtualJoystick(joystickId);
Console.WriteLine("Native overlay/controller check passed.");
void Toggle()
{
    overlay.Key(42, 1, true, 0);
    Check(overlay.Key(15, 1, true, 0), "Steam consumes Shift+Tab");
    overlay.Key(15, 1, false, 0);
    overlay.Key(42, 0, false, 0);
}

static class Sdl
{
    const string Lib = "libSDL3.so.0";

    [DllImport(Lib, EntryPoint = "SDL_Init")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool Init(uint flags);

    [DllImport(Lib, EntryPoint = "SDL_GetError")]
    internal static extern IntPtr Error();

    [DllImport(Lib, EntryPoint = "SDL_GetCurrentVideoDriver")]
    internal static extern IntPtr Driver();

    [DllImport(Lib, EntryPoint = "SDL_CreateWindow")]
    internal static extern IntPtr CreateWindow(string title, int w, int h, ulong flags);

    [DllImport(Lib, EntryPoint = "SDL_CreateRenderer")]
    internal static extern IntPtr CreateRenderer(IntPtr window, string name);

    [DllImport(Lib, EntryPoint = "SDL_PollEvent")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool PollEvent(out Event e);

    [DllImport(Lib, EntryPoint = "SDL_SetRenderDrawColor")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SetRenderDrawColor(IntPtr renderer, byte r, byte g, byte b, byte a);

    [DllImport(Lib, EntryPoint = "SDL_RenderClear")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool RenderClear(IntPtr renderer);

    [DllImport(Lib, EntryPoint = "SDL_RenderPresent")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool RenderPresent(IntPtr renderer);

    [DllImport(Lib, EntryPoint = "SDL_GetKeyboardFocus")]
    internal static extern IntPtr GetKeyboardFocus();

    [DllImport(Lib, EntryPoint = "SDL_AttachVirtualJoystick")]
    internal static extern uint AttachVirtualJoystick(ref VirtualJoystickDesc desc);

    [DllImport(Lib, EntryPoint = "SDL_OpenGamepad")]
    internal static extern IntPtr OpenGamepad(uint id);

    [DllImport(Lib, EntryPoint = "SDL_GetGamepadAxis")]
    internal static extern short GetGamepadAxis(IntPtr gamepad, int axis);

    [DllImport(Lib, EntryPoint = "SDL_GetGamepadButton")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool GetGamepadButton(IntPtr gamepad, int button);

    [DllImport(Lib, EntryPoint = "SDL_CloseGamepad")]
    internal static extern void CloseGamepad(IntPtr gamepad);

    [DllImport(Lib, EntryPoint = "SDL_OpenJoystick")]
    internal static extern IntPtr OpenJoystick(uint id);

    [DllImport(Lib, EntryPoint = "SDL_SetJoystickVirtualAxis")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SetJoystickVirtualAxis(IntPtr joystick, int axis, short value);

    [DllImport(Lib, EntryPoint = "SDL_SetJoystickVirtualButton")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SetJoystickVirtualButton(
        IntPtr joystick,
        int button,
        [MarshalAs(UnmanagedType.I1)] bool value
    );

    [DllImport(Lib, EntryPoint = "SDL_GetJoystickAxis")]
    internal static extern short GetJoystickAxis(IntPtr joystick, int axis);

    [DllImport(Lib, EntryPoint = "SDL_CloseJoystick")]
    internal static extern void CloseJoystick(IntPtr joystick);

    [DllImport(Lib, EntryPoint = "SDL_DetachVirtualJoystick")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool DetachVirtualJoystick(uint id);

    [StructLayout(LayoutKind.Explicit, Size = 136)]
    internal struct VirtualJoystickDesc
    {
        [FieldOffset(0)]
        internal uint Version;

        [FieldOffset(4)]
        internal ushort Type;

        [FieldOffset(12)]
        internal ushort Axes;

        [FieldOffset(14)]
        internal ushort Buttons;

        [FieldOffset(28)]
        internal uint ButtonMask;

        [FieldOffset(32)]
        internal uint AxisMask;
    }

    [StructLayout(LayoutKind.Explicit, Size = 128)]
    internal struct Event
    {
        [FieldOffset(0)]
        internal uint Type;
    }
}
