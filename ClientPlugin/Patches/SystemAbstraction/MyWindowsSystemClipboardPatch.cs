// Replace the COM/WinForms-based Clipboard property on MyWindowsSystem
// (which routes through System.Windows.Forms.Clipboard on a spun-up STA
// thread) with an SDL3-backed implementation. The stock getter/setter call
// VRage.Platform.Windows.Forms.MyClipboardHelper, which references types
// that do not load on the .NET 9 Linux runtime and ultimately P/Invoke into
// Win32 OLE/user32. The recompiled Linux build of the game replaced the
// helper with an in-process string cache; this plugin goes a step further
// and integrates with the actual desktop clipboard via SDL3 so copy/paste
// interoperates with other applications.
//
// SDL3 clipboard calls must run on SdlRenderThread (the dedicated thread
// that owns every SDL3 call in this plugin). SdlClipboard handles the
// dispatch: sets from off-thread callers are queued and drained by
// SdlClipboard.PumpRenderThread, which the render thread invokes once per
// loop iteration; reads from off-thread callers are served from an
// in-process cache that mirrors the last value the render thread observed
// or wrote. See SdlClipboard for details.

using ClientPlugin.Compatibility;
using HarmonyLib;

namespace ClientPlugin.Patches.SystemAbstraction;

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "Clipboard", MethodType.Getter)]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemClipboardGetterPatch
{
    static bool Prefix(ref string __result)
    {
        __result = SdlClipboard.GetText();
        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "Clipboard", MethodType.Setter)]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemClipboardSetterPatch
{
    static bool Prefix(string value)
    {
        SdlClipboard.SetText(value);
        return false;
    }
}
