using HarmonyLib;
using VRage.Ansel;

namespace ClientPlugin.Patches.PlatformGuards;

[HarmonyPatch(typeof(MyAnsel), nameof(MyAnsel.Init))]
[HarmonyPatchCategory("Finish")]
static class MyAnselInitPatch
{
    static bool Prefix(ref int __result)
    {
        __result = 3;
        return false;
    }
}

[HarmonyPatch(typeof(MyAnsel), "Enable")]
[HarmonyPatchCategory("Finish")]
static class MyAnselEnablePatch
{
    static bool Prefix()
    {
        return false;
    }
}
