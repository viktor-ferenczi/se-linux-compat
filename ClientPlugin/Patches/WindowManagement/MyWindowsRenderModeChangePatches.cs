using ClientPlugin.Patches.PlatformGuards;
using HarmonyLib;
using VRage;
using VRage.Platform.Windows.Render;
using VRageRender;

namespace ClientPlugin.Patches.WindowManagement;

// The original IL in MyWindowsRender.CreateRenderDevice and ApplyRenderSettings
// calls `m_windows.GameWindow?.OnModeChanged(...)`. GameWindow is typed
// MyGameWindow (WinForms) and is null on Linux because we replace the Windows
// window-creation path with our SDL window. The preloader NOPs that dead call;
// these postfixes invoke OnModeChanged on our SdlGameWindow — but ONLY for
// mode transitions (Window ↔ Fullscreen ↔ FullscreenWindow), never for
// same-mode size changes.
//
// Why: this forwarder is the only "backbuffer settings → SDL window" edge.
// Letting it fire on size changes creates a feedback loop on HiDPI / fractional
// scaling: SwitchSettings(width=1906) → SDL_SetWindowSize(1906) → WM picks
// pixel size 2048 → WINDOW_RESIZED → Request → SwitchSettings(2048) → …
// (sizes diverge per-iteration and may never converge).
//
// New invariant: window → backbuffer is the only direction. Same-mode size
// changes coming through SwitchSettings stay confined to the backbuffer.
// User-driven resolution picks (Display Settings dialog) are routed to the
// SDL window by MyVideoSettingsManagerApplyPatch *before* the original Apply
// runs SwitchSettings; the resulting WINDOW_RESIZED event then drives the
// backbuffer through the normal Request flow.

internal static class ModeChangeForwarder
{
    // Last WindowMode we have driven the SDL window into. Null until the
    // first device creation / explicit mode set.
    private static MyWindowModeEnum? s_lastForwardedMode;

    public static void Forward(MyRenderDeviceSettings? settings)
    {
        if (!settings.HasValue)
            return;
        var s = settings.Value;

        // Forward only mode TRANSITIONS. Same-mode size changes are handled
        // window-first: user picks → SDL window resize → backbuffer follows.
        if (s_lastForwardedMode.HasValue && s_lastForwardedMode.Value == s.WindowMode)
            return;
        s_lastForwardedMode = s.WindowMode;

        var sdl = SdlInput2Provider.Instance;
        if (sdl == null)
            return;
        var adapters = MyPlatformRender.GetAdaptersList();
        if (adapters == null || s.AdapterOrdinal < 0 || s.AdapterOrdinal >= adapters.Length)
            return;
        sdl.OnModeChanged(s.WindowMode, s.BackBufferWidth, s.BackBufferHeight, adapters[s.AdapterOrdinal].DesktopBounds);
    }

    // Drive the SDL window directly with explicit settings (used by
    // MyVideoSettingsManagerApplyPatch for user-initiated resolution changes
    // that don't change the window mode — Forward would otherwise skip).
    // Updates the last-forwarded-mode tracker so a subsequent Forward call
    // arriving from the render thread won't re-apply the same mode.
    public static void DriveDirect(MyRenderDeviceSettings settings)
    {
        s_lastForwardedMode = settings.WindowMode;
        var sdl = SdlInput2Provider.Instance;
        if (sdl == null)
            return;
        var adapters = MyPlatformRender.GetAdaptersList();
        if (adapters == null || settings.AdapterOrdinal < 0 || settings.AdapterOrdinal >= adapters.Length)
            return;
        sdl.OnModeChanged(settings.WindowMode, settings.BackBufferWidth, settings.BackBufferHeight, adapters[settings.AdapterOrdinal].DesktopBounds);
    }
}

[HarmonyPatch(typeof(MyWindowsRender), nameof(MyWindowsRender.CreateRenderDevice))]
[HarmonyPatchCategory("Finish")]
static class MyWindowsRenderCreateRenderDevicePatch
{
    static void Postfix(MyRenderDeviceSettings? settings) => ModeChangeForwarder.Forward(settings);
}

[HarmonyPatch(typeof(MyWindowsRender), nameof(MyWindowsRender.ApplyRenderSettings))]
[HarmonyPatchCategory("Finish")]
static class MyWindowsRenderApplyRenderSettingsPatch
{
    static void Postfix(MyRenderDeviceSettings? settings) => ModeChangeForwarder.Forward(settings);
}
