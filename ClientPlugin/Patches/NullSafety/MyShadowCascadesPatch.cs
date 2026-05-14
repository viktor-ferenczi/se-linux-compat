using System.Reflection;
using HarmonyLib;

namespace ClientPlugin.Patches.NullSafety;

[HarmonyPatch("VRageRender.MyShadowCascades", "get_CascadeResolution")]
[HarmonyPatchCategory("Finish")]
static class MyShadowCascadesCascadeResolutionPatch
{
    static FieldInfo cascadeShadowmapArrayField;

    static bool Prefix(object __instance, ref int __result)
    {
        cascadeShadowmapArrayField ??= __instance.GetType().GetField(
            "m_cascadeShadowmapArray",
            BindingFlags.Instance | BindingFlags.NonPublic);

        if (cascadeShadowmapArrayField?.GetValue(__instance) == null)
        {
            __result = 0;
            return false;
        }
        return true;
    }
}
