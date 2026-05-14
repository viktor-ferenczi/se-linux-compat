using HarmonyLib;
using Sandbox.Game.Gui;

namespace ClientPlugin.Patches.Video;

[HarmonyPatch(typeof(MyGuiScreenIntroVideo), "TryPlayVideo")]
[HarmonyPatchCategory("Finish")]
static class TryPlayVideoDiagPatch
{
    private static readonly System.Reflection.FieldInfo CurrentVideoField =
        AccessTools.Field(typeof(MyGuiScreenIntroVideo), "m_currentVideo");

    static void Prefix(MyGuiScreenIntroVideo __instance)
    {
        // Normalize backslashes to forward slashes BEFORE the stock
        // File.Exists(Path.Combine(ContentPath, m_currentVideo)) check.
        // Stock game hardcodes "Videos\\BackgroundNN.wmv" and "Videos\\KSH.wmv"
        // paths which fail File.Exists on Linux.
        var currentVideo = CurrentVideoField?.GetValue(__instance) as string;
        if (!string.IsNullOrEmpty(currentVideo) && currentVideo.Contains('\\'))
        {
            CurrentVideoField.SetValue(__instance, currentVideo.Replace('\\', '/'));
        }
    }
}
