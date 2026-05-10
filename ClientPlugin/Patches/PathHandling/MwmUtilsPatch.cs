using HarmonyLib;
using VRage.FileSystem;
using VRage.Render11.GeometryStage2.Model;

namespace ClientPlugin.Patches.PathHandling;

// Ports Topic 4.3 MyMwmUtils.GetFullMwmFilepath fix from dotnet-game-local.
// The baseline implementation lowercases the combined path, which is
// catastrophic on Linux (case-sensitive filesystem) -- assets stored as
// "Models/Cubes/Small/Foo.mwm" are looked up as "models/cubes/small/foo.mwm"
// and fail to load. Replace with normalize + case-insensitive resolve.
[HarmonyPatch(typeof(MyMwmUtils), nameof(MyMwmUtils.GetFullMwmFilepath))]
[HarmonyPatchCategory("Finish")]
static class MyMwmUtilsGetFullMwmFilepathPatch
{
    static bool Prefix(ref string __result, string mwmFilepath)
    {
        if (!mwmFilepath.EndsWith(".mwm"))
            mwmFilepath += ".mwm";
        __result = PathHelpers.ResolveContentFilePath(mwmFilepath, MyFileSystem.ContentPath);
        return false;
    }
}
