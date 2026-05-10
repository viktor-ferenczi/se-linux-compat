using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Sandbox.Definitions;
using VRage.FileSystem;
using VRage.Game;
using VRage.Utils;

namespace ClientPlugin.Patches.PathHandling;

// Ports Topic 4.9 (MyDefinitionManager path fixes) from dotnet-game-local.
//
// Two changes are ported:
//
// 1) LoadDefinitions(List<MyModContext>, List<DefinitionSet>) gains a
//    null check around list[k] before entering the foreach that iterates
//    it. On Linux a mod's Data directory may fail DirectoryExists
//    (casing, missing dir), causing list.Add(null) to run above -- the
//    baseline then NREs on foreach. We insert the same warning/continue
//    path used upstream.
//
// 2) ProcessContentFilePath gains path normalization, case-insensitive
//    extension comparison, case-insensitive content file resolution
//    under the mod root, and the error-model literal is switched to
//    forward slashes ("Models/Debug/Error.mwm").

[HarmonyPatch(typeof(MyDefinitionManager), "LoadDefinitions",
    new[] { typeof(List<MyModContext>), typeof(List<MyDefinitionManager.DefinitionSet>) })]
[HarmonyPatchCategory("Finish")]
static class MyDefinitionManagerLoadDefinitionsPatch
{
    static bool Prefix(MyDefinitionManager __instance,
        List<MyModContext> contexts,
        List<MyDefinitionManager.DefinitionSet> definitionSets)
    {
        var list = new List<List<Tuple<MyObjectBuilder_Definitions, string>>>();
        for (int i = 0; i < contexts.Count; i++)
        {
            var ctx = contexts[i];
            if (!MyFileSystem.DirectoryExists(ctx.ModPathData))
            {
                list.Add(null);
                continue;
            }
            definitionSets[i].Context = ctx;
            __instance.m_transparentMaterialsInitialized = false;
            var preloadSet = ctx.IsBaseGame ? __instance.m_mainMenuPreloadSet : null;
            var builders = __instance.GetDefinitionBuilders(ctx, preloadSet, ctx.IsBaseGame);
            list.Add(builders);
            if (builders == null)
                return false;
        }

        Action<MyObjectBuilder_Definitions, MyModContext, MyDefinitionManager.DefinitionSet, bool>[] phases =
        {
            __instance.CompatPhase,
            __instance.LoadPhase1,
            __instance.LoadPhase2,
            __instance.LoadPhase3,
            __instance.LoadPhase4,
            __instance.LoadPhase5,
        };

        for (int j = 0; j < phases.Length; j++)
        {
            for (int k = 0; k < contexts.Count; k++)
            {
                __instance.m_currentLoadingSet = definitionSets[k];
                if (list[k] == null)
                {
                    MyLog.Default.Warning($"Missing definition {k}; Look for a Linux path conversation issue.");
                    continue;
                }

                try
                {
                    foreach (var item in list[k])
                    {
                        contexts[k].CurrentFile = item.Item2;
                        phases[j](item.Item1, contexts[k], definitionSets[k], true);
                    }
                }
                catch (Exception innerException)
                {
                    MyDefinitionManager.FailModLoading(contexts[k], j, phases.Length, innerException);
                    continue;
                }
                __instance.MergeDefinitions();
            }
        }

        for (int l = 0; l < contexts.Count; l++)
            __instance.AfterLoad(contexts[l], definitionSets[l]);

        MyDefinitionManager.m_directoryExistCache.Clear();
        return false;
    }
}

[HarmonyPatch(typeof(MyDefinitionManager), "ProcessContentFilePath")]
[HarmonyPatchCategory("Finish")]
static class MyDefinitionManagerProcessContentFilePathPatch
{
    static bool Prefix(MyModContext context, ref string contentFile, object[] extensions, bool logNoExtensions)
    {
        if (string.IsNullOrEmpty(contentFile))
            return false;

        contentFile = PathHelpers.Normalize(contentFile);
        string extension = Path.GetExtension(contentFile);

        if (extensions == null || extensions.Length == 0)
        {
            if (logNoExtensions)
                MyDefinitionErrors.Add(context, "List of supported file extensions not found. (Internal error)", TErrorSeverity.Warning);
            return false;
        }

        if (string.IsNullOrEmpty(extension))
        {
            MyDefinitionErrors.Add(context, "File does not have a proper extension: " + contentFile, TErrorSeverity.Warning);
            return false;
        }

        bool extensionOk = false;
        foreach (var e in extensions)
        {
            if (string.Equals(e as string, extension, StringComparison.OrdinalIgnoreCase))
            {
                extensionOk = true;
                break;
            }
        }
        if (!extensionOk)
        {
            MyDefinitionErrors.Add(context, "File extension of: " + contentFile + " is not supported.", TErrorSeverity.Warning);
            return false;
        }

        string resolved = CaseInsensitivePathResolver.Resolve(contentFile, context.ModPath);
        if (!MyDefinitionManager.m_directoryExistCache.TryGetValue(resolved, out var exists))
        {
            exists = MyFileSystem.DirectoryExists(Path.GetDirectoryName(resolved))
                  && System.Linq.Enumerable.Any(MyFileSystem.GetFiles(
                        Path.GetDirectoryName(resolved),
                        Path.GetFileName(resolved),
                        MySearchOption.TopDirectoryOnly));
            MyDefinitionManager.m_directoryExistCache.Add(resolved, exists);
        }

        if (exists)
        {
            contentFile = resolved;
        }
        else if (!MyFileSystem.FileExists(PathHelpers.ResolveContentFilePath(contentFile, MyFileSystem.ContentPath)))
        {
            if (contentFile.EndsWith(".mwm"))
            {
                MyDefinitionErrors.Add(context, "Resource not found, setting to error model. Resource path: " + resolved, TErrorSeverity.Error);
                contentFile = "Models/Debug/Error.mwm";
            }
            else
            {
                MyDefinitionErrors.Add(context, "Resource not found, setting to null. Resource path: " + resolved, TErrorSeverity.Error);
                contentFile = null;
            }
        }

        return false;
    }
}
