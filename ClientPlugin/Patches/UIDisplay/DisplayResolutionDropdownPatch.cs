using System.Text;
using ClientPlugin.Patches.PlatformGuards;
using HarmonyLib;
using Sandbox.Engine.Platform.VideoMode;
using Sandbox.Game.Gui;
using VRage;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Patches.UIDisplay;

// On Linux the X11 window manager can resize the SE window to any pixel
// dimensions (drag-resize, tiling WM tiles, fractional scaling, etc.).
// MySandboxGameWindowResizePatches feeds those dimensions into
// SwitchSettings, so the live drawable size ends up at a non-standard
// value like 1273x891 that is not in the adapter's SupportedDisplayModes
// list.
//
// Stock MyGuiScreenOptionsDisplay.UpdateResolutionComboBox only enumerates
// SupportedDisplayModes, so the actual current backbuffer size never
// appears as a combo entry. SelectResolution then snaps to the closest
// area-match, and ReadSettingsFromControls treats that approximate item
// as "no change" -- so OnOkClick early-exits without applying any
// user-picked standard resolution. The user is effectively locked out of
// the resolution dropdown until they switch to fullscreen and back.
//
// Fix: while in windowed mode, append the live drawable size to the
// resolution combo if absent, and select it as the default. The user can
// still pick any other entry; that picked resolution then differs from
// the current settings, ReadSettingsFromControls reports a change, and
// Apply runs normally.
internal static class DisplayResolutionDropdownHelper
{
    public static long GetResolutionKey(Vector2I resolution)
    {
        // Matches MyGuiScreenOptionsDisplay.GetResolutionKey: ((long)X << 32) | Y.
        return ((long)resolution.X << 32) | (uint)resolution.Y;
    }

    // Prefer the SDL drawable size (matches what
    // MySandboxGameWindowResizePatches feeds into SwitchSettings) and fall
    // back to MyRenderProxy.BackBufferResolution if SDL is unavailable.
    public static Vector2I GetCurrentBackbuffer()
    {
        var sdl = SdlInput2Provider.Instance;
        if (sdl != null)
        {
            var px = sdl.ClientSizePixels;
            if (px.X > 0 && px.Y > 0)
                return px;
        }
        return MyRenderProxy.BackBufferResolution;
    }

    // Append the live backbuffer entry if windowed and not already present.
    // Returns the key if added (or already present and selectable), or 0 if
    // skipped.
    public static long EnsureCurrentBackbufferEntry(MyGuiScreenOptionsDisplay screen)
    {
        if (screen.GetSelectedWindowMode() != MyWindowModeEnum.Window)
            return 0L;

        Vector2I current = GetCurrentBackbuffer();
        if (current.X <= 0 || current.Y <= 0)
            return 0L;

        long key = GetResolutionKey(current);
        var combo = screen.m_comboResolution;
        if (combo.TryGetItemByKey(key) != null)
            return key;

        var aspectEnum = MyVideoSettingsManager.GetClosestAspectRatio((float)current.X / (float)current.Y);
        var aspect = MyVideoSettingsManager.GetAspectRatio(aspectEnum);
        combo.AddItem(key, new StringBuilder($"{current.X} x {current.Y} - {aspect.TextShort}"));
        MyLog.Default.WriteLine(
            $"DisplayResolutionDropdown: added live backbuffer entry {current.X}x{current.Y} (key={key:X16}) to resolution combo");
        return key;
    }
}

[HarmonyPatch(typeof(MyGuiScreenOptionsDisplay), "UpdateResolutionComboBox")]
[HarmonyPatchCategory("Finish")]
static class MyGuiScreenOptionsDisplayUpdateResolutionComboBoxPatch
{
    static void Postfix(MyGuiScreenOptionsDisplay __instance)
    {
        DisplayResolutionDropdownHelper.EnsureCurrentBackbufferEntry(__instance);
    }
}

[HarmonyPatch(typeof(MyGuiScreenOptionsDisplay), "WriteSettingsToControls")]
[HarmonyPatchCategory("Finish")]
static class MyGuiScreenOptionsDisplayWriteSettingsToControlsPatch
{
    // WriteSettingsToControls runs once at dialog open (from RecreateControls)
    // and again on revert. After the original finishes, SelectResolution may
    // have snapped to an approximate match because the live non-standard
    // backbuffer was not in SupportedDisplayModes. Re-add the live entry as
    // a safety net (UpdateResolutionComboBox postfix should already have
    // added it, but the chain via SelectWindowMode -> ItemSelected ->
    // UpdateResolutionComboBox can leave windowMode != Window briefly).
    // Then force-select the live backbuffer entry.
    static void Postfix(MyGuiScreenOptionsDisplay __instance)
    {
        long key = DisplayResolutionDropdownHelper.EnsureCurrentBackbufferEntry(__instance);
        if (key == 0L)
            return;

        var combo = __instance.m_comboResolution;
        if (combo.TryGetItemByKey(key) == null)
            return;
        combo.SelectItemByKey(key);
        MyLog.Default.WriteLine(
            $"DisplayResolutionDropdown: selected live backbuffer entry (key={key:X16})");
    }
}
