using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ClientPlugin.Patches.PathHandling;
using ClientPlugin.Tools;
using HarmonyLib;
using SharpDX.Multimedia;
using VRage.Audio;
using VRage.Data.Audio;

namespace ClientPlugin.Patches.Audio;

// Topic 8.3: MyInMemoryWave Linux loading.
//
// Stock VRage.Audio.dll IL loads audio via MyInMemoryWaveDataCache, which
// parses a WAV via SharpDX's SoundStream and hands back a
// SharpDX.DataStream pointing at native PCM memory. That DataStream is
// stored in AudioBuffer.Stream.
//
// Our XAudio2 shim's SourceVoice.PutBuffer reads from AudioBuffer.Data
// (a managed byte[]), not AudioBuffer.Stream. With stock IL every buffer
// has Data == null, so the shim submits zero bytes to SDL3 and the audio
// device plays silence.
//
// This patch replaces MyInMemoryWave..ctor with the Linux branch from the
// recompiled source: decode via MySdlAudioInterop.LoadAudioFile (which
// handles WAV and FFmpeg-supported formats and returns S16LE PCM bytes)
// and populate AudioBuffer.Data + m_waveFormat. The original Dispose path
// then works as-is: m_cacheToDispose and m_stream are left null, so the
// "else m_stream?.Dispose()" branch runs as a no-op and the Streamed
// cleanup still fires.
//
// Reflection is used for AudioBuffer creation/population because
// AudioBuffer at compile time resolves to the real SharpDX.XAudio2
// assembly (which lacks the `byte[] Data` field our shim adds), while
// at runtime the preloader's AssemblyRef redirect makes it resolve to
// our shim type. By going through reflection we stay type-agnostic.
//
// The stock Dispose() accesses m_owner.LoadedStreamedWaves.Remove()
// without a null guard. When the same MyInMemoryWave is shared across
// multiple MySourceVoice instances, the first voice's DisposeWaves
// dereferences it to 0, calling Dispose() which sets m_owner = null.
// A second voice then dereferences the same wave, calling Dispose()
// again and hitting a NullReferenceException on m_owner. Fix: early
// return if m_owner is already null (wave was already disposed).
[HarmonyPatch(typeof(MyInMemoryWave), nameof(MyInMemoryWave.Dispose))]
[HarmonyPatchCategory("Finish")]
static class MyInMemoryWaveDisposePatch
{
    static bool Prefix(MyInMemoryWave __instance)
    {
        // m_owner is set to null at the end of Dispose().
        // If it's already null, the wave was already disposed — skip.
        if (__instance.m_owner == null)
            return false;

        // On Linux our ctor prefix never sets m_cacheToDispose or m_stream,
        // so the stock Dispose's cache/stream cleanup branches are no-ops.
        // Let the stock method run for the m_owner/m_buffer/m_waveFormat
        // null-outs and the LoadedStreamedWaves removal.
        return true;
    }
}

// Category "Finish" (not "Init"): MyAudio.LoadData runs during
// MySandboxGame.Run before MyPlugins.Plugins are initialized, so the
// "Init" harmony category fires too late. Preloader.Finish() applies
// "Finish"-category patches before the game starts loading audio.
[HarmonyPatch(typeof(MyInMemoryWave), MethodType.Constructor,
    new[] { typeof(MySoundData), typeof(string), typeof(MyWaveBank), typeof(bool), typeof(bool) })]
[HarmonyPatchCategory("Finish")]
static class MyInMemoryWaveCtorPatch
{
    // Cached reflection info into the runtime AudioBuffer type (our shim
    // after the preloader AssemblyRef redirect).
    private static Type s_audioBufferType;
    private static FieldInfo s_audioBytesField;
    private static FieldInfo s_flagsField;
    private static FieldInfo s_dataField;
    private static FieldInfo s_loopCountField;

    // Cached MyInMemoryWave private fields.
    private static FieldInfo s_bufferField;

    static bool Prefix(MyInMemoryWave __instance, MySoundData cue, string path,
        MyWaveBank owner, bool streamed, bool cached)
    {
        // Stock IL passes Windows-style paths with `\` separators and
        // casing that doesn't match the on-disk layout. Normalize here —
        // the stock MyFileSystem.OpenRead path happens to work because
        // MyFileSystemOpenPatch rewrites `\` -> `/`, but our own direct
        // File.Exists check inside LoadAudioFile has no such patch.
        path = path.Replace('\\', '/');
        if (Path.IsPathRooted(path))
            path = PathCache.ResolveAbsolute(path);

        byte[] data = MySdlAudioInterop.LoadAudioFile(path, out var waveFormat);

        if (s_audioBufferType == null)
        {
            s_bufferField = AccessTools.Field(typeof(MyInMemoryWave), "m_buffer")
                ?? throw new InvalidOperationException("MyInMemoryWave.m_buffer not found");
            s_audioBufferType = s_bufferField.FieldType;
            s_audioBytesField = s_audioBufferType.GetField("AudioBytes")
                ?? throw new InvalidOperationException("AudioBuffer.AudioBytes not found");
            s_flagsField = s_audioBufferType.GetField("Flags")
                ?? throw new InvalidOperationException("AudioBuffer.Flags not found");
            s_dataField = s_audioBufferType.GetField("Data")
                ?? throw new InvalidOperationException("AudioBuffer.Data not found (shim not active?)");
            s_loopCountField = s_audioBufferType.GetField("LoopCount")
                ?? throw new InvalidOperationException("AudioBuffer.LoopCount not found");
        }

        var buffer = Activator.CreateInstance(s_audioBufferType);
        s_audioBytesField.SetValue(buffer, data.Length);
        s_flagsField.SetValue(buffer, 0); // BufferFlags.None
        s_dataField.SetValue(buffer, data);
        if (cue.Loopable)
        {
            s_loopCountField.SetValue(buffer, 255);
        }

        __instance.m_owner = owner;
        __instance.m_path = path;
        __instance.m_waveFormat = waveFormat;
        s_bufferField.SetValue(__instance, buffer);
        __instance.Streamed = streamed;
        return false;
    }
}

// The stock VRage.Audio.dll IL for MySourceVoice.SubmitSourceBuffer(MyInMemoryWave)
// contains:
//   callvirt instance class SoundStream MyInMemoryWave::get_Stream()
//   callvirt instance uint32[] SoundStream::get_DecodedPacketsInfo()
//
// On Linux, MyInMemoryWavePatch leaves m_stream = null (we load audio differently),
// so this crashes with a NullReferenceException. DecodedPacketsInfo is only non-null
// for WMA streams; for all PCM/WAV sources it is null and our shim ignores it.
// This Transpiler replaces the get_DecodedPacketsInfo() callvirt with (pop; ldnull):
// discards the null stream reference and pushes null for the argument, which is the
// correct value in all cases and does not crash.
[HarmonyPatch(typeof(MySourceVoice))]
[HarmonyPatchCategory("Finish")]
// ReSharper disable once UnusedType.Global
static class MySourceVoiceSubmitBufferTranspiler
{
    // ReSharper disable once UnusedMember.Local
    [HarmonyTranspiler]
    [HarmonyPatch("SubmitSourceBuffer", new[] { typeof(MyInMemoryWave) })]
    static IEnumerable<CodeInstruction> SubmitSourceBufferTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase patchedMethod)
    {
        // Record original IL next to this patch file for review/diffing
        // across game updates. See ClientPlugin/Tools/TranspilerHelpers.cs.
        var il = instructions.ToList();
        il.RecordOriginalCode(patchedMethod);

        var getDecodedPackets = typeof(SoundStream)
            .GetProperty("DecodedPacketsInfo", BindingFlags.Public | BindingFlags.Instance)
            ?.GetGetMethod();

        // Walk backwards so Insert calls don't shift the indexes still to visit.
        for (var i = il.Count - 1; i >= 0 && getDecodedPackets != null; i--)
        {
            var instr = il[i];
            if (instr.opcode != OpCodes.Callvirt) continue;
            if (instr.operand is not MethodInfo mi || mi != getDecodedPackets) continue;

            // Stack before: [..., SoundStream (may be null)]
            // Replace: callvirt get_DecodedPacketsInfo()
            // With:    pop (discard stream) + ldnull (null DecodedPacketsInfo)
            // Mutate the existing instruction in place so any branch labels
            // or exception blocks attached to it stay anchored to the pop.
            instr.opcode = OpCodes.Pop;
            instr.operand = null;
            il.Insert(i + 1, new CodeInstruction(OpCodes.Ldnull));
        }

        // Record modified IL next to the original for side-by-side diffing.
        il.RecordPatchedCode(patchedMethod);
        return il;
    }
}
