using HarmonyLib;
using Sandbox.Game.Screens.Helpers;

namespace ClientPlugin.Patches.NullSafety;

[HarmonyPatch(typeof(MyAsyncSaving), "OnSnapshotDone")]
[HarmonyPatchCategory("Finish")]
static class MyAsyncSavingOnSnapshotDonePatch
{
    static void Postfix()
    {
        MyAsyncSaving.m_screenshotTaken = true;
    }
}
