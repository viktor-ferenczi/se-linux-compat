using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ClientPlugin.Tools;
using HarmonyLib;
using VRage.FileSystem;
using VRage.Render11.Resources;
using VRage.Utils;

namespace ClientPlugin.Patches.PathHandling;

// Regression detector for atlas-path resolution on Linux. The
// MyTextureAtlas(string, string) ctor is invoked once from
// MyBillboardRenderer.Init during CreateDeviceInternal, well before
// MySandboxGame.Constructor() runs -- the earliest point that MyFileSystem
// path-handling Prefixes are exercised under a Windows-style argument
// ("Textures\\Particles\\ParticlesAtlas.tai"). If ParseAtlasDescription's
// broad catch ever silently swallows OpenRead returning null again, every
// later FindElement(material.Texture) call from MyBillboardRenderer
// .GatherInternal throws KeyNotFoundException -- crash observed firing the
// flare gun.
//
// Expected healthy log on Linux (each game start, single line):
//   existsRaw=False           // backslash path is not a real file
//   existsNormalized=True     // PathHelpers.Normalize finds it
//   existsResolved=True       // PathCache.ResolveAbsolute finds it
//   ContentPath populated, no getter exception
// Any deviation (existsNormalized=False, ContentPath getter throws, etc.)
// points at the layer that regressed -- MyFileSystem.Init ordering, the Open
// Prefix, or PathCache.
//
// Cost: one Path.Combine, three File.Exists syscalls, one PathCache lookup,
// one log line per render-device init. Not on any frame-time hot path.
[HarmonyPatch(typeof(MyTextureAtlas), MethodType.Constructor, typeof(string), typeof(string))]
[HarmonyPatchCategory("Finish")]
// ReSharper disable once UnusedType.Global
static class MyTextureAtlasCtorRegressionPatch
{
    // ReSharper disable once UnusedMember.Local
    static void Prefix(string textureDir, string atlasFile)
    {
        try
        {
            string contentPath;
            string contentPathError = null;
            try { contentPath = MyFileSystem.ContentPath; }
            catch (Exception ex) { contentPath = null; contentPathError = ex.GetType().Name + ": " + ex.Message; }

            string combined = contentPath != null ? Path.Combine(contentPath, atlasFile ?? "") : null;
            string normalized = combined != null ? combined.Replace('\\', '/') : null;
            string resolved = (normalized != null && Path.IsPathRooted(normalized))
                ? PathCache.ResolveAbsolute(normalized) : normalized;

            bool existsRaw       = combined   != null && File.Exists(combined);
            bool existsNormalized = normalized != null && File.Exists(normalized);
            bool existsResolved  = resolved   != null && File.Exists(resolved);

            MyLog.Default.WriteLine(
                "[LinuxCompat] MyTextureAtlas..ctor regression check: " +
                "textureDir=" + (textureDir ?? "<null>") +
                ", atlasFile=" + (atlasFile ?? "<null>") +
                ", ContentPath=" + (contentPath ?? "<null>") +
                (contentPathError != null ? " (getter threw: " + contentPathError + ")" : "") +
                ", combined=" + (combined ?? "<null>") +
                ", normalized=" + (normalized ?? "<null>") +
                ", resolved=" + (resolved ?? "<null>") +
                ", existsRaw=" + existsRaw +
                ", existsNormalized=" + existsNormalized +
                ", existsResolved=" + existsResolved);
        }
        catch
        {
            // Diagnostic only; must never break game startup.
        }
    }
}

// MyTextureAtlas.ParseAtlasDescription builds dictionary keys as
//   textureDir + Path.GetFileName(array[0])
// where `array[0]` is the first column of a .tai manifest. The stock SE
// manifests use Windows-style paths, e.g.
//   "Textures\MuzzleFlashMachineGunFront.dds  ParticlesAtlas0.dds  ..."
// On Linux `Path.GetFileName` does not recognize `\` as a separator, so the
// returned "leaf" is the whole string, producing a doubled-prefix key like
// "Textures\Particles\Textures\MuzzleFlashMachineGunFront.dds". The lookup
// in MyBillboardRenderer.GatherInternal (m_atlas.FindElement(material.Texture))
// uses the SBC-side string "Textures\Particles\<file>", so the key isn't
// found and the render thread throws KeyNotFoundException — observed as a
// crash when the flare gun (and any other weapon whose particle muzzle flash
// references a sub-folder in the atlas) fires.
//
// Fix: transpile the single Path.GetFileName(string) call inside
// ParseAtlasDescription to PathHelpers.GetFileName, which normalizes `\` to
// `/` before delegating. Mod-API surface is unchanged — MyTextureAtlas is
// internal, and MyTransparentMaterialDefinition.Texture (the lookup side)
// stays in Windows form for mods that Split('\\') against it.
[HarmonyPatch(typeof(MyTextureAtlas))]
[HarmonyPatchCategory("Finish")]
// ReSharper disable once UnusedType.Global
static class MyTextureAtlasParseAtlasDescriptionPatch
{
    // ReSharper disable once UnusedMember.Local
    [HarmonyTranspiler]
    [HarmonyPatch("ParseAtlasDescription")]
    static IEnumerable<CodeInstruction> ParseAtlasDescriptionTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase patchedMethod)
    {
        var il = instructions.ToList();
        il.RecordOriginalCode(patchedMethod);

        var target = typeof(Path).GetMethod(nameof(Path.GetFileName), new[] { typeof(string) });
        var replacement = typeof(PathHelpers).GetMethod(nameof(PathHelpers.GetFileName), new[] { typeof(string) });

        // Mutate the operand in place so any branch labels or exception
        // blocks attached to the call instruction stay anchored to it.
        foreach (var instr in il)
        {
            if (instr.opcode == OpCodes.Call && instr.operand is MethodInfo mi && mi == target)
                instr.operand = replacement;
        }

        il.RecordPatchedCode(patchedMethod);
        return il;
    }
}
