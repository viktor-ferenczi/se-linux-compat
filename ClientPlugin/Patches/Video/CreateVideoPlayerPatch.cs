using System;
using ClientPlugin.Compatibility.Video;
using HarmonyLib;
using VRage;
using VRage.Platform.Windows;
using VRage.Utils;

namespace ClientPlugin.Patches.Video;

/// <summary>
/// Replaces VRage.Platform.Windows.MyVRagePlatform.CreateVideoPlayer with a
/// Linux-compatible implementation that decodes video/audio via FFmpeg and
/// plays audio through SDL3. The stock implementation instantiates a
/// DirectShow-based MyVideoPlayer, whose FilterGraph COM construction throws
/// PlatformNotSupportedException on Linux — causing MyVideoFactory.Play to
/// silently catch the exception and immediately dismiss the intro screen.
/// </summary>
[HarmonyPatch(typeof(MyVRagePlatform), nameof(MyVRagePlatform.CreateVideoPlayer))]
[HarmonyPatchCategory("Finish")]
static class CreateVideoPlayerPatch
{
    static bool Prefix(ref IVideoPlayer __result)
    {
        try
        {
            MyLog.Default.WriteLineAndConsole("[LinuxCompat] CreateVideoPlayer: constructing MyLinuxVideoPlayer");
            __result = new MyLinuxVideoPlayer();
            return false;
        }
        catch (Exception ex)
        {
            MyLog.Default.WriteLineAndConsole($"[LinuxCompat] MyLinuxVideoPlayer construction failed: {ex}");
            __result = null;
            return false;
        }
    }
}
