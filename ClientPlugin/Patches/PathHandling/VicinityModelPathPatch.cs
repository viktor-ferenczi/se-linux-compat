using System.Collections.Generic;
using HarmonyLib;
using Sandbox.Game.World;
using VRage.FileSystem;
using VRage.Render11.GeometryStage2.Model;
using VRage.Utils;

namespace ClientPlugin.Patches.PathHandling;

/// <summary>
/// The server answers a vicinity-cache request with its own <c>MyModel.AssetName</c>
/// strings. Vanilla assets are Content-relative and resolve on any client; mod assets
/// are the server's absolute on-disk paths, which name nothing on this machine.
/// <para>
/// This is a network ingress, not the mod API boundary. The paths are neither
/// mod-shaped nor local, so neither the rewriter nor <see cref="PathCache"/> can make
/// sense of them: a Windows server's <c>G:\</c> reaches
/// <see cref="PathTranslation.Untranslate"/> with no mapping, loses its drive and
/// arrives at the render thread as a missing mesh asset.
/// </para>
/// <para>
/// Rewriting by published file id restores the prewarm for modded blocks, which is the
/// whole point of the vicinity cache, and dropping what stays unreachable keeps the
/// failed loads and their ingress reports out of the log.
/// </para>
/// </summary>
static class VicinityModelPaths
{
    /// <summary>Rewrites the received list in place, dropping what this client cannot load.</summary>
    public static void Remap(List<string> models)
    {
        if (models == null || models.Count == 0)
            return;

        var modFolders = ModFoldersById();
        var total = models.Count;
        var remapped = 0;
        var kept = 0;

        for (int i = 0; i < models.Count; i++)
        {
            var model = models[i];
            if (string.IsNullOrEmpty(model))
                continue;

            var fromMod = VicinityModelPathRemap.TryRemapToLocalMod(
                model,
                modFolders,
                out var local
            );
            if (fromMod)
            {
                model = local;
            }
            else if (!VicinityModelPathRemap.IsRooted(model))
            {
                // Content-relative vanilla asset, valid on every client as sent.
                models[kept++] = model;
                continue;
            }

            if (!Exists(model))
                continue;

            if (fromMod)
                remapped++;
            models[kept++] = model;
        }

        var dropped = models.Count - kept;
        models.RemoveRange(kept, dropped);

        if (remapped > 0 || dropped > 0)
            MyLog.Default.WriteLine(
                $"VicinityModelPaths: of {total} model paths sent by the server, remapped {remapped} onto this client's mods and dropped {dropped} as unreachable"
            );
    }

    private static Dictionary<string, string> ModFoldersById()
    {
        var map = new Dictionary<string, string>();
        var mods = MySession.Static?.Mods;
        if (mods == null)
            return map;

        foreach (var mod in mods)
        {
            // A mod the server deployed outside the workshop cannot be matched by id,
            // and this client has no copy of it to preload anyway.
            if (mod.PublishedFileId == 0)
                continue;

            string folder;
            try
            {
                folder = mod.GetPath();
            }
            catch
            {
                continue;
            }

            if (!string.IsNullOrEmpty(folder))
                map[mod.PublishedFileId.ToString()] = folder.Replace('\\', '/');
        }

        return map;
    }

    // The predicate the render thread itself will apply: the patched mwm path
    // resolution, then the archive-aware existence check.
    private static bool Exists(string model)
    {
        try
        {
            return MyFileSystem.FileExists(MyMwmUtils.GetFullMwmFilepath(model));
        }
        catch
        {
            return false;
        }
    }
}

[HarmonyPatch(typeof(MySession), "PreloadVicinityCache")]
[HarmonyPatchCategory("Finish")]
static class MySessionPreloadVicinityCachePatch
{
    static void Prefix(List<string> models, List<string> armorModels)
    {
        VicinityModelPaths.Remap(models);
        VicinityModelPaths.Remap(armorModels);
    }
}
