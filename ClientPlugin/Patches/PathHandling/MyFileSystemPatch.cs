using System;
using System.IO;
using HarmonyLib;
using Sandbox.Engine.Voxels;
using VRage.FileSystem;

namespace ClientPlugin.Patches.PathHandling;

// Case-insensitive resolution lives in PathCache (PathHelpers.cs). The
// formerly-separate PathNormalizer was a duplicate file-keyed cache; see
// Docs/Fixes.md 2026-04-25 (path resolver cache redesign).

// MyFileSystem.Open is the lowest-level gateway every file open funnels
// through (OpenRead delegates to it). The recompiled game performs both
// NormalizePath + ResolveCaseSensitivePath here; the stock IL omits
// both. Backslash normalization alone is not enough: callers like
// MyTextureStreamingManager / MyResourceUtils.NormalizeFileTextureName
// lowercase texture paths before passing them down (see MyResourceUtils
// .cs line 350 in dotnet-game-local), and any code path that bypasses
// MyFileTextureImageCache.LoadImage (e.g. MyFileTexture.LoadResource,
// MyTextureCache mip-map probes) reaches Open with a lowercased path.
// On Linux this is the failure that produced "Texture does not exist:
// .../Content/fonts/white/fontdatapa-0.dds" while the on-disk file is
// PascalCased.
//
// Implemented as a Cecil prepatch (see MyFileSystemOpenPrepatch.cs) that
// injects `path = PathCache.ResolveAbsolute(path)` into the body of
// MyFileSystem.Open itself, rather than as a Harmony Prefix. A Prefix
// here is silently bypassed by JIT inlining: OpenRead and Open are tiny
// static methods with constant arguments at every call site, the JIT
// inlines them into hot callers (e.g. ParseAtlasDescription_Patch1), and
// the inlined IL skips the Harmony prologue stub. Putting the
// normalization inside Open's own body means inlined copies still carry
// it. The MyTextureAtlas regression detector in MyTextureAtlasPatch.cs
// is the canary for any future regression of this layer.

[HarmonyPatch(typeof(MyStorageBase), nameof(MyStorageBase.LoadFromFile))]
[HarmonyPatchCategory("Finish")]
static class MyStorageBaseLoadFromFilePatch
{
    static void Prefix(ref string absoluteFilePath)
    {
        if (absoluteFilePath != null)
            absoluteFilePath = PathCache.ResolveAbsolute(absoluteFilePath);
    }
}

// OpenWrite mirrors Open: the recompiled game does NormalizePath +
// ResolveCaseSensitivePath here too. The new-file case needs special
// handling because PathCache.ResolveAbsolute returns the input unchanged
// when the leaf doesn't yet exist on disk (typical for a fresh write).
// Without resolving the parent, a write to "Foo/Bar/new.txt" when only
// "foo/bar/" exists creates a duplicate-cased "Foo/Bar/" sibling — which
// is exactly the per-save / per-screenshot folder-duplication bug the
// recompiled game fixes at this layer.
[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.OpenWrite), typeof(string), typeof(FileMode))]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemOpenWritePatch
{
    static void Prefix(ref string path)
    {
        if (path == null)
            return;

        path = path.Replace('\\', '/');

        if (!Path.IsPathRooted(path))
            return;

        var resolved = PathCache.ResolveAbsolute(path);
        if (resolved != path)
        {
            path = resolved;
            return;
        }

        // Full path didn't resolve (new file). Try to resolve the parent
        // directory so the on-disk casing wins.
        var dir = Path.GetDirectoryName(path);
        var leaf = Path.GetFileName(path);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(leaf))
            return;

        var resolvedDir = PathCache.ResolveAbsolute(dir);
        if (resolvedDir != dir)
            path = resolvedDir + "/" + leaf;
    }
}

[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.FileExists))]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemFileExistsPatch
{
    static void Prefix(ref string path)
    {
        if (path == null)
            return;

        path = path.Replace('\\', '/');

        // Resolve case-insensitively so callers like MyMeshes.LoadMwm find
        // mesh assets when on-disk casing differs from the in-game path
        // (e.g. "Models/Cubes/Small/Armor/..." vs "Models/Cubes/small/armor/...").
        if (Path.IsPathRooted(path))
            path = PathCache.ResolveAbsolute(path);
    }
}

[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.DirectoryExists))]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemDirectoryExistsPatch
{
    static void Prefix(ref string path)
    {
        if (path == null)
            return;

        path = path.Replace('\\', '/');

        // Mirror FileExists: a probe for "Mods/Workshop" should see the
        // on-disk "mods/workshop". On miss, ResolveAbsolute returns the
        // input unchanged and Directory.Exists then returns false, which
        // is the same outcome as before — this only fixes false negatives.
        if (Path.IsPathRooted(path))
            path = PathCache.ResolveAbsolute(path);
    }
}

[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.GetFiles), typeof(string))]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemGetFilesPatch
{
    static void Prefix(ref string path)
    {
        if (path != null)
            path = path.Replace('\\', '/');
    }
}

[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.GetFiles), typeof(string), typeof(string))]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemGetFilesFilterPatch
{
    static void Prefix(ref string path)
    {
        if (path != null)
            path = path.Replace('\\', '/');
    }
}

// EnsureDirectoryExists feeds Directory.CreateDirectory on miss. Resolving
// the path first means an existing differently-cased dir is reused; on a
// genuine miss the existing CreateDirectoryRecursive prefix
// (MyFileSystemCreateDirectoryRecursivePatch below) walks segments
// case-insensitively. This belt-and-braces avoids a duplicate-cased dir
// being created when EnsureDirectoryExists is called with a path that
// already exists on disk under different casing.
[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.EnsureDirectoryExists))]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemEnsureDirectoryExistsPatch
{
    static void Prefix(ref string path)
    {
        if (path == null)
            return;

        path = path.Replace('\\', '/');

        if (Path.IsPathRooted(path))
            path = PathCache.ResolveAbsolute(path);
    }
}

// Ports Topic 4.1 CreateDirectoryRecursive change from dotnet-game-local.
// On Linux the baseline recursion creates duplicate directories with the
// wrong case when an ancestor already exists on disk with different
// casing. Walk segments case-insensitively, reusing existing directories
// before creating new ones.
[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.CreateDirectoryRecursive))]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemCreateDirectoryRecursivePatch
{
    static bool Prefix(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        path = PathHelpers.Normalize(path);

        var segments = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var resolved = path.StartsWith(Path.DirectorySeparatorChar)
            ? Path.DirectorySeparatorChar.ToString()
            : "";

        foreach (var segment in segments)
        {
            var candidate = Path.Combine(resolved, segment);
            if (Directory.Exists(candidate))
            {
                resolved = candidate;
                continue;
            }

            if (Directory.Exists(resolved))
            {
                string found = null;
                try
                {
                    foreach (var entry in Directory.EnumerateDirectories(resolved))
                    {
                        if (string.Equals(Path.GetFileName(entry), segment, StringComparison.OrdinalIgnoreCase))
                        {
                            found = entry;
                            break;
                        }
                    }
                }
                catch
                {
                    // Fall through and treat as not found
                }
                resolved = found ?? candidate;
            }
            else
            {
                resolved = candidate;
            }

            if (!Directory.Exists(resolved))
                Directory.CreateDirectory(resolved);
        }

        return false;
    }
}

// Ports Topic 10.7 (commit 67278f4c): Fix StackOverflowException in
// MyFileSystem.IsGameContent on Linux. The original method, after testing
// the input path against ContentPath, falls back to recursing on
// path.Replace('/', '\\'). On Linux that recursion ping-pongs forever
// because (a) NormalizePath in our other MyFileSystem patches converts all
// separators back to '/', and (b) backslash isn't a path separator on
// Linux anyway -- the Windows-style fallback can never succeed.
//
// Strategy: Prefix replacement matching the upstream Linux behavior --
// return based on the rooted/ContentPath check only, never recurse.
[HarmonyPatch(typeof(MyFileSystem), nameof(MyFileSystem.IsGameContent))]
[HarmonyPatchCategory("Finish")]
static class MyFileSystemIsGameContentPatch
{
    static bool Prefix(string path, ref bool __result)
    {
        if (!Path.IsPathRooted(path))
        {
            __result = true;
            return false;
        }

        __result = path.StartsWith(MyFileSystem.ContentPath, StringComparison.OrdinalIgnoreCase);
        return false;
    }
}
