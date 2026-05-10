using HarmonyLib;
using Sandbox.Engine.Utils;

namespace ClientPlugin.Patches.WindowManagement;

// On Linux, default to CaptureMouse = false so the OS cursor can leave the
// window edges naturally when no GUI needs it, and so that GUI screens can
// release SDL relative mouse mode instead of keeping the pointer locked.
// Matches the recompiled game's MyConfig.NewConfigWasStarted patch.
[HarmonyPatch(typeof(MyConfig), "NewConfigWasStarted")]
[HarmonyPatchCategory("Finish")]
static class MyConfigCaptureMousePatch
{
    static void Postfix(MyConfig __instance)
    {
        __instance.CaptureMouse = false;
    }
}
