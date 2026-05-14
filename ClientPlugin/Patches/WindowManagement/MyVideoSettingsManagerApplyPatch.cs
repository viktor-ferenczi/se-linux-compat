using HarmonyLib;
using Sandbox.Engine.Platform.VideoMode;
using VRageRender;

namespace ClientPlugin.Patches.WindowManagement;

// User-driven entry point for resolution / window-mode changes (Display
// Settings dialog → Apply / OnOkClick). Drives the SDL window directly with
// the picked settings BEFORE the original Apply runs SwitchSettings.
//
// This is the only backbuffer-settings code path that is allowed to
// initiate a window resize, because it represents an explicit user choice.
// Once the SDL window resize completes, WINDOW_RESIZED fires and the normal
// request → frame-processor flow brings the backbuffer in line. The original
// Apply still runs SwitchSettings (so the backbuffer changes immediately
// without waiting for the event); ModeChangeForwarder.Forward then sees an
// already-applied mode and skips the second window resize.
//
// Same-mode size changes that did NOT originate here (e.g. internal
// SwitchSettings from BackbufferResizeRequest) bypass this patch entirely
// and never touch the SDL window.
[HarmonyPatch(typeof(MyVideoSettingsManager), "Apply", typeof(MyRenderDeviceSettings))]
[HarmonyPatchCategory("Finish")]
static class MyVideoSettingsManagerApplyPatch
{
    static void Prefix(MyRenderDeviceSettings settings)
    {
        var current = MyVideoSettingsManager.CurrentDeviceSettings;
        // Skip when nothing user-visible would change. The original Apply
        // performs the same equality check and bails out with NothingChanged.
        if (settings.BackBufferWidth == current.BackBufferWidth
            && settings.BackBufferHeight == current.BackBufferHeight
            && settings.WindowMode == current.WindowMode
            && settings.AdapterOrdinal == current.AdapterOrdinal)
            return;
        ModeChangeForwarder.DriveDirect(settings);
    }
}
