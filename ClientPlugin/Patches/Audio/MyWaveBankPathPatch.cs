using ClientPlugin.Patches.PathHandling;
using HarmonyLib;
using VRage.Audio;

namespace ClientPlugin.Patches.Audio;

// Ports Topic 8.4 (MyWaveBank path fixes) from dotnet-game-local.
//
// Stock VRage.Audio.MyWaveBank.FindAudioFile(MySoundData, string, out string)
// receives Windows-style paths from cue definitions, e.g.
//
//   'ARC\\TOOL\\ArcToolLrgWeldIdle3d.xwm '
//
// (note backslashes and trailing space — both come straight from the audio
// SBC). Stock then does:
//
//   fsPath = Path.Combine(MyFileSystem.ContentPath, "Audio", fileName);
//   bool flag = MyFileSystem.FileExists(fsPath);
//
// On Linux the in-tree MyFileSystemFileExistsPatch normalizes backslashes
// and case-resolves, but it does NOT trim trailing whitespace. Combined
// with the embedded backslashes in the input, the on-disk lookup fails:
// the .xwm exists at .../Audio/ARC/TOOL/ArcToolLrgWeldIdle3d.xwm but
// FindAudioFile is asking for '...\\ArcToolLrgWeldIdle3d.xwm '. The .wav
// fallback inside FindAudioFile inherits the same dirty fileName via
// Path.GetDirectoryName/GetFileNameWithoutExtension and fails too. The
// log line that surfaces the bug is the OnSoundError invocation:
//
//   "Unable to find audio file: '<SubtypeId>', '<fileName>'"
//
// The recompiled FindAudioFile starts with `fileName = PathUtils.Normalize(fileName)`.
// Mirror that here as a Harmony prefix so the rest of the (stock) method
// works against a clean path. PathHelpers.Normalize does the same job:
// converts both `\\` and `\` to `/` and Trim()s.
[HarmonyPatch(typeof(MyWaveBank), "FindAudioFile")]
[HarmonyPatchCategory("Finish")]
static class MyWaveBankFindAudioFilePathPatch
{
    static void Prefix(ref string fileName)
    {
        if (fileName != null)
            fileName = PathHelpers.Normalize(fileName);
    }
}
