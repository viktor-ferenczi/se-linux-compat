using System;
using System.IO;
using HarmonyLib;
using VRage.FileSystem;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders.Definitions;

namespace ClientPlugin.Patches.PathHandling;

[HarmonyPatch(typeof(MyGuiTextureAtlasDefinition), "Init")]
[HarmonyPatchCategory("Finish")]
static class GuiTextureAtlasDefinitionPatch
{
    static void Prefix(MyObjectBuilder_DefinitionBase builder)
    {
        if (builder is not MyObjectBuilder_GuiTextureAtlasDefinition atlasDef)
            return;

        if (atlasDef.Textures == null)
            return;

        foreach (var texture in atlasDef.Textures)
        {
            if (texture.Path != null)
                texture.Path = FixTexturePath(texture.Path);
        }
    }

    static string FixTexturePath(string path)
    {
        path = path.Replace('\\', '/');

        if (Path.IsPathRooted(path))
            return PathCache.ResolveAbsolute(path);

        var contentPath = MyFileSystem.ContentPath;
        if (string.IsNullOrEmpty(contentPath))
            return path;

        var fullPath = Path.Combine(contentPath, path);
        var resolved = PathCache.ResolveAbsolute(fullPath);
        if (resolved != fullPath)
        {
            if (resolved.StartsWith(contentPath, StringComparison.OrdinalIgnoreCase))
                return resolved.Substring(contentPath.Length).TrimStart('/');
        }

        return path;
    }
}
