using ClientPlugin.Patches.PlatformGuards;
using HarmonyLib;
using Sandbox;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Patches.WindowManagement;

// Single-flag, frame-driven backbuffer resize coordinator.
//
// One-directional design (window → backbuffer ONLY):
//   * Anything that observes a window/screen size change calls Request().
//     Setters live in this assembly's SDL window event handlers and in
//     ApplyModeChange (when the user picks a new mode/resolution from the
//     Display Settings dialog and we drive the SDL window directly).
//   * Once per frame on the main thread (prefix on MySandboxGame.Update,
//     before any rendering work for the frame is queued), we look at the
//     flag. If set, we clear it, compute the target size (the SDL drawable
//     pixel size in all modes — windowed inner client area, or screen size
//     in fullscreen modes since the SDL window is sized to the display
//     there), compare to the current backbuffer size, and call SwitchSettings
//     only if they differ.
//   * As a safety net, we set the flag every PERIODIC_CHECK_INTERVAL_FRAMES
//     frames so a missed signal converges within ~1s @60fps.
//
// The reverse edge (backbuffer → window) is severed in
// MyWindowsRenderModeChangePatches: ModeChangeForwarder.Forward only acts on
// MODE transitions (Window ↔ Fullscreen ↔ FullscreenWindow), never on
// same-mode size changes. User-driven resolution picks are routed to the SDL
// window by MyVideoSettingsManagerApplyPatch before the original Apply runs.
//
// All callers of Request() and ProcessIfRequested run on the main thread,
// so no synchronization is needed.
internal static class BackbufferResizeRequest
{
    // Safety-net interval. With the SDL event-driven Request() calls, the
    // typical case is single-frame latency; this is purely a backstop for
    // any signal we might miss (e.g. a mode change applied without going
    // through ApplyModeChange).
    private const int PERIODIC_CHECK_INTERVAL_FRAMES = 60;

    private static bool s_requested;
    private static int s_frameCounter;

    // Set the resize-request flag. Called from SDL window event handlers,
    // post-mode-change, and the periodic tick. Cheap and idempotent.
    public static void Request()
    {
        s_requested = true;
    }

    // Called once per frame on the main thread, before rendering work for
    // the frame begins. Bumps the periodic counter; processes the request
    // flag if set or if the periodic interval elapsed.
    public static void ProcessIfRequested(MySandboxGame game)
    {
        if (++s_frameCounter >= PERIODIC_CHECK_INTERVAL_FRAMES)
        {
            s_frameCounter = 0;
            s_requested = true;
        }

        if (!s_requested)
            return;
        s_requested = false;

        if (Sandbox.Engine.Platform.Game.IsDedicated)
            return;

        var sdl = SdlInput2Provider.Instance;
        if (sdl == null)
            return;

        // It is best that we do not run this in fullscreen mode.
        // Ideally, it would have no effect. In practice there are timings issues
        // when entering / exiting fullscreen where it thinks we're still a window
        // and tries to undo the mode change.
        if (!sdl.IsWindowed)
            return;

        var render = game?.GameRenderComponent?.RenderThread;
        if (render == null)
            return;

        Vector2I target = sdl.ClientSizePixels;
        if (target.X <= 0 || target.Y <= 0)
            return;

        // The actual backbuffer size as last reported by the render thread
        // (MyRenderProxy.BackBufferResolution is updated when the swapchain
        // is recreated). The settings struct may briefly disagree while a
        // SwitchSettings request is in flight; using the swapchain's reported
        // resolution avoids issuing a redundant SwitchSettings during that
        // window.
        Vector2I backbuffer = MyRenderProxy.BackBufferResolution;
        if (backbuffer == target)
            return;

        MyRenderDeviceSettings current = render.CurrentSettings;
        current.BackBufferWidth = target.X;
        current.BackBufferHeight = target.Y;
        MyLog.Default.WriteLine(
            $"Backbuffer resize: {backbuffer.X}x{backbuffer.Y} -> {target.X}x{target.Y} (mode={current.WindowMode})");

        // No need to mark this as "internal": ModeChangeForwarder skips
        // same-mode size changes universally, so this SwitchSettings will
        // not feed back into a window resize.
        game.SwitchSettings(current);
    }
}

// Run the resize processor at the start of every game tick, on the main
// thread, before the frame's rendering work begins.
[HarmonyPatch(typeof(MySandboxGame), "Update")]
[HarmonyPatchCategory("Finish")]
static class MySandboxGameUpdatePatch
{
    static void Prefix(MySandboxGame __instance)
    {
        BackbufferResizeRequest.ProcessIfRequested(__instance);
    }
}
