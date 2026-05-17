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

        // Strip any synthetic Windows drive prefix the mod side may have
        // added — a path like "C:/users/steamuser/.../Textures/foo.dds"
        // (or "C:/mnt/win/.../foo.dds" for a mod stored on a mount that
        // isn't in the translation table) has to become Linux-rooted
        // before the IsPathRooted gate, otherwise the else branch
        // Path.Combine's contentPath onto a drive-prefixed string and
        // produces a path that doesn't exist. No-op for non-drive input.
        filepath = PathTranslation.Untranslate(filepath);

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
