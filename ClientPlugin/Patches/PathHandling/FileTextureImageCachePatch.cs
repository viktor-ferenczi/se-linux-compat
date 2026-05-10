using System.IO;
using HarmonyLib;
using VRage.FileSystem;
using VRage.Render11.Resources;

namespace ClientPlugin.Patches.PathHandling;

// Ports Topic 4.4 (Texture Path Resolution) from dotnet-game-local:
// MyFileTextureImageCache.LoadImage normalizes backslashes and resolves
// case-insensitively on Linux before opening the texture file.
// Without this, texture paths concatenated with backslashes
// (e.g., "Content/textures\particles\particlesatlas0.dds") fail to open,
// rendering world assets as magenta/purple placeholders.
[HarmonyPatch(typeof(MyFileTextureImageCache), "LoadImage")]
[HarmonyPatchCategory("Finish")]
static class FileTextureImageCacheLoadImagePatch
{
    static void Prefix(ref string filepath)
    {
        if (string.IsNullOrEmpty(filepath))
            return;

        filepath = filepath.Replace('\\', '/');

        // Resolve case-insensitively relative to Content path when possible.
        if (Path.IsPathRooted(filepath))
        {
            filepath = PathCache.ResolveAbsolute(filepath);
            return;
        }

        var contentPath = MyFileSystem.ContentPath;
        if (string.IsNullOrEmpty(contentPath))
            return;

        var full = Path.Combine(contentPath, filepath);
        var resolved = PathCache.ResolveAbsolute(full);
        if (resolved != full && File.Exists(resolved))
            filepath = resolved;
    }
}
