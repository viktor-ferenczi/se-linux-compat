using System;
using HarmonyLib;
using VRage.Render11.Resources;

namespace ClientPlugin.Patches.PathHandling;

// Fix for the Colorful Icons family of mods (and any other mod that
// composes absolute icon paths off ModContext.ModPath). Those mods
// rewrite definition.Icons[i] to strings shaped like
//   C:\users\steamuser\.steam\debian-installation\...\Battery.dds
// because ModPath now returns a Windows-shape path via the mod-facing
// translation in PathHelpers.ToWindowsPath / PathTranslation.Translate.
//
// The GUI texture pipeline routes those strings through
// MyResourceUtils.NormalizeFileTextureName *before* anything reaches
// the patched MyFileTextureImageCache.LoadImage / MyFileSystem.Open:
//
//   MyGuiControlGrid.DrawItem
//     -> MyGuiManager.DrawSpriteBatch
//     -> MyRenderProxy.DrawSprite (enqueues MyRenderMessageDrawSprite)
//     -> MySpritesRenderer.GetTexture
//     -> MyTextureStreamingManager.GetOrMakeTexture
//          -> MyResourceUtils.NormalizeFileTextureName  ← path is mangled here
//     -> MyFileTextureManager.GetTexture
//          -> MyResourceUtils.NormalizeFileTextureName (second pass, cache hit)
//     -> MyFileTexture.Init / Load
//     -> MyFileTextureImageCache.LoadImage
//
// NormalizeFileTextureName's body does (paraphrased):
//   if (Uri.TryCreate(text, UriKind.Absolute, out uri)) name = MakeRelativePath(uri);
//   name = name.ToLowerInvariant().Replace('/', '\\').Replace("\\\\", "\\");
//   uri = new Uri(Path.Combine(MyFileSystem.ContentPath, text));
//
// On Linux for a "C:\users\steamuser\..." input:
//   1. Uri.TryCreate with UriKind.Absolute false-negatives — "C:\..." is
//      not a recognized absolute URI on a non-Windows BCL.
//   2. name gets lowercased + slash-flipped → "c:\users\steamuser\...\battery.dds"
//      and becomes the m_textures cache key.
//   3. Path.Combine(ContentPath, "C:\\users\\...") on Linux concatenates
//      naively because IsPathRooted("C:\\...") is false on Linux, so the
//      Uri body resolves to "<ContentPath>/C:\\users\\..." — nonsense
//      that fails the file open further down.
//
// Result: the icon doesn't load, the toolbar slot renders empty, and the
// Prefix on MyFileTextureImageCache.LoadImage never sees the original
// drive-prefixed string because the corruption has already happened.
//
// The fix is to reverse-translate before the body runs, so the cache key
// AND the URI computation work from the real Linux path. The other
// patched entrypoints (FileTextureImageCachePatch, MyFileSystemPatch.*)
// still need their Untranslate calls for the code paths that bypass
// NormalizeFileTextureName.
[HarmonyPatch(typeof(MyResourceUtils), nameof(MyResourceUtils.NormalizeFileTextureName),
    new[] { typeof(string), typeof(Uri) },
    new[] { ArgumentType.Ref, ArgumentType.Out })]
[HarmonyPatchCategory("Finish")]
static class MyResourceUtilsNormalizeFileTextureNamePatch
{
    static void Prefix(ref string name)
    {
        if (string.IsNullOrEmpty(name))
            return;

        // Only act on inputs that carry a synthetic drive prefix. Relative
        // texture paths ("Textures/GUI/Icons/cubes/Battery.dds") must pass
        // through unchanged so the body's lowercase + '/'→'\' normalization
        // produces the cache key shape the rest of the engine expects.
        // Untranslate is a no-op for inputs without a drive prefix and
        // returns the same reference, so the identity check below skips
        // the assignment in the common case.
        var forward = name.Replace('\\', '/');
        var untranslated = PathTranslation.Untranslate(forward);
        if (!ReferenceEquals(untranslated, forward))
            name = untranslated;
    }
}
