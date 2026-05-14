using System;
using System.Reflection;
using HarmonyLib;
using VRage.Audio;
using VRage.Data.Audio;

namespace ClientPlugin.Patches.Audio;

// NRE crash fix for MyCueBank.GetVoice.
//
// Two race conditions cause NRE inside GetVoice:
//
// 1. TOCTOU on m_audioEngine: The public GetVoice checks `m_audioEngine == null`
//    at entry, but GetVoicePool does `lock (m_audioEngine)` without re-checking.
//    Concurrent StopSound can null m_audioEngine between the two.
//
// 2. Double-dispose of MyInMemoryWave: GetVoice → MySourceVoice.Flush →
//    DisposeWaves → MyInMemoryWave.Dereference → Dispose. The Dispose method
//    accesses m_owner.LoadedStreamedWaves, but m_owner is nulled at the end of
//    Dispose. If a wave is disposed concurrently (or double-dereferenced),
//    m_owner is null on the second call.
//
// Symptom: NullReferenceException crash when using a Jukebox or during normal
// gameplay (footstep sounds, character sounds).
//
// Fix: Harmony finalizer swallows NRE and returns null voice, matching the
// defensive pattern already used at the top of GetVoice (returns null when
// m_audioEngine is null). The caller chain (MyEntity3DSoundEmitter.PlaySoundInternal)
// already catches and logs exceptions from PlaySound.
[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
internal static class MyCueBankGetVoicePatch
{
    private static MethodBase TargetMethod()
    {
        // The public overload: GetVoice(MyCueId, out int, MySoundDimensions, int, MyVoicePoolType)
        // "out int" is ref int at the IL level.
        return typeof(MyCueBank).GetMethod(
            "GetVoice",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null,
            new[]
            {
                typeof(MyCueId),
                typeof(int).MakeByRefType(),
                typeof(MySoundDimensions),
                typeof(int),
                typeof(MyVoicePoolType),
            },
            null);
    }

    private static Exception Finalizer(Exception __exception, ref MySourceVoice __result)
    {
        if (__exception is NullReferenceException)
        {
            // Swallow the NRE: return null voice (no sound plays) instead
            // of crashing the game. This matches the intent of the null
            // check at the top of GetVoice.
            __result = null;
            return null;
        }
        return __exception;
    }
}
