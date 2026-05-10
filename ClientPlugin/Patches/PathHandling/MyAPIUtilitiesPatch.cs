using System.IO;
using HarmonyLib;
using Sandbox.ModAPI;
using VRage.FileSystem;
using VRage.Game;
using VRage.Private;
using VRage.Utils;

namespace ClientPlugin.Patches.PathHandling;

// Ports Topic 4.6 (Mod API path compatibility) from dotnet-game-local.
//
// Mods are authored against Windows and expect backslash-separated paths
// when doing substring / Split work on IMyGamePaths values. Convert the
// four exposed path properties via ToWindowsPath so mod code continues
// to behave as it does on Windows, even though the underlying Linux
// paths use forward slashes.
//
// For file access methods, normalize the caller-supplied path (so mods
// can pass either separator) and resolve case-insensitively against the
// mod or content root before opening.

static class MyAPIUtilitiesPathHelper
{
    internal const string IMyGamePaths = "VRage.Game.ModAPI.IMyGamePaths.";
    internal const string IMyUtilities = "VRage.Game.ModAPI.IMyUtilities.";
}

[HarmonyPatch(typeof(MyAPIUtilities), MyAPIUtilitiesPathHelper.IMyGamePaths + "get_ContentPath")]
[HarmonyPatchCategory("Finish")]
static class MyAPIUtilitiesContentPathPatch
{
    static bool Prefix(ref string __result)
    {
        __result = PathHelpers.ToWindowsPath(MyFileSystem.ContentPath);
        return false;
    }
}

[HarmonyPatch(typeof(MyAPIUtilities), MyAPIUtilitiesPathHelper.IMyGamePaths + "get_ModsPath")]
[HarmonyPatchCategory("Finish")]
static class MyAPIUtilitiesModsPathPatch
{
    static bool Prefix(ref string __result)
    {
        __result = PathHelpers.ToWindowsPath(MyFileSystem.ModsPath);
        return false;
    }
}

[HarmonyPatch(typeof(MyAPIUtilities), MyAPIUtilitiesPathHelper.IMyGamePaths + "get_UserDataPath")]
[HarmonyPatchCategory("Finish")]
static class MyAPIUtilitiesUserDataPathPatch
{
    static bool Prefix(ref string __result)
    {
        __result = PathHelpers.ToWindowsPath(MyFileSystem.UserDataPath);
        return false;
    }
}

[HarmonyPatch(typeof(MyAPIUtilities), MyAPIUtilitiesPathHelper.IMyGamePaths + "get_SavesPath")]
[HarmonyPatchCategory("Finish")]
static class MyAPIUtilitiesSavesPathPatch
{
    static bool Prefix(ref string __result)
    {
        __result = PathHelpers.ToWindowsPath(MyFileSystem.SavesPath);
        return false;
    }
}

// The file-access methods below normalize the caller-supplied relative
// path and resolve it case-insensitively before handing off to the real
// filesystem API. This makes mod code that hardcodes backslash or the
// wrong casing continue to work on Linux.

[HarmonyPatch(typeof(MyAPIUtilities), MyAPIUtilitiesPathHelper.IMyUtilities + "ReadFileInModLocation")]
[HarmonyPatchCategory("Finish")]
static class MyAPIUtilitiesReadFileInModLocationPatch
{
    static bool Prefix(ref System.IO.TextReader __result, ref string file, MyObjectBuilder_Checkpoint.ModItem modItem)
    {
        if (file.IndexOfAny(MyKeenUtils.GetFixedInvalidPathChars()) != -1)
            throw new FileNotFoundException();

        file = PathHelpers.Normalize(file);
        var modPath = modItem.GetPath();
        var fullPath = Path.GetFullPath(Path.Combine(modPath, file));
        if (fullPath.StartsWith(modPath))
        {
            var protectedDir = Path.Combine(modPath, "Data", "Scripts");
            if (fullPath.StartsWith(protectedDir))
                throw new FileNotFoundException("Access to protected location '" + protectedDir + "' not allowed.", fullPath);

            var resolved = CaseInsensitivePathResolver.Resolve(file, modPath);
            var stream = MyFileSystem.OpenRead(resolved);
            if (stream != null)
            {
                __result = new StreamReader(stream);
                return false;
            }
        }
        throw new FileNotFoundException();
    }
}

[HarmonyPatch(typeof(MyAPIUtilities), MyAPIUtilitiesPathHelper.IMyUtilities + "ReadFileInGameContent")]
[HarmonyPatchCategory("Finish")]
static class MyAPIUtilitiesReadFileInGameContentPatch
{
    static bool Prefix(ref System.IO.TextReader __result, ref string file)
    {
        if (file.IndexOfAny(MyKeenUtils.GetFixedInvalidPathChars()) != -1)
            throw new FileNotFoundException();

        file = PathHelpers.Normalize(file);
        var resolved = PathHelpers.ResolveContentFilePath(file, MyFileSystem.ContentPath);
        if (resolved.StartsWith(MyFileSystem.ContentPath))
        {
            var stream = MyFileSystem.OpenRead(resolved);
            if (stream != null)
            {
                __result = new StreamReader(stream);
                return false;
            }
        }
        throw new FileNotFoundException();
    }
}

[HarmonyPatch(typeof(MyAPIUtilities), MyAPIUtilitiesPathHelper.IMyUtilities + "FileExistsInModLocation")]
[HarmonyPatchCategory("Finish")]
static class MyAPIUtilitiesFileExistsInModLocationPatch
{
    static bool Prefix(ref bool __result, ref string file, MyObjectBuilder_Checkpoint.ModItem modItem)
    {
        if (file.IndexOfAny(MyKeenUtils.GetFixedInvalidPathChars()) != -1)
        {
            __result = false;
            return false;
        }

        file = PathHelpers.Normalize(file);
        var modPath = modItem.GetPath();
        var fullPath = Path.GetFullPath(Path.Combine(modPath, file));
        if (fullPath.StartsWith(modPath))
        {
            var protectedDir = Path.Combine(modPath, "Data", "Scripts");
            if (fullPath.StartsWith(protectedDir))
            {
                __result = false;
                return false;
            }
            var resolved = CaseInsensitivePathResolver.Resolve(file, modPath);
            __result = File.Exists(resolved);
            return false;
        }
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(MyAPIUtilities), MyAPIUtilitiesPathHelper.IMyUtilities + "FileExistsInGameContent")]
[HarmonyPatchCategory("Finish")]
static class MyAPIUtilitiesFileExistsInGameContentPatch
{
    static bool Prefix(ref bool __result, ref string file)
    {
        if (file.IndexOfAny(MyKeenUtils.GetFixedInvalidPathChars()) != -1)
        {
            __result = false;
            return false;
        }

        file = PathHelpers.Normalize(file);
        var resolved = PathHelpers.ResolveContentFilePath(file, MyFileSystem.ContentPath);
        __result = resolved.StartsWith(MyFileSystem.ContentPath) && File.Exists(resolved);
        return false;
    }
}
