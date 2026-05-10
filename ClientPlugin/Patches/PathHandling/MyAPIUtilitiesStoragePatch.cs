using System;
using System.IO;
using System.Text;
using HarmonyLib;
using Sandbox.ModAPI;
using VRage.Private;
using VRage.Utils;

namespace ClientPlugin.Patches.PathHandling;

// Linux-side fix for the IMyUtilities `*FileIn{Local,World}Storage` family.
//
// Background: the engine's 12 storage methods all begin with
//     if (file.IndexOfAny(MyKeenUtils.GetFixedInvalidFileNameChars()) != -1)
//         throw new FileNotFoundException();
// where `FixedInvalidFileNameChars` is a hardcoded 41-char Windows-fixed
// list that includes ':', '*', '?', '<', '>', '|', '"', '\\', '/' and the
// control chars.
//
// Many mods sanitize their filenames defensively before calling these APIs
// using `Path.GetInvalidFileNameChars()`. That .NET API is platform-
// dependent: on Windows it returns the full Windows-fixed list, but on
// Linux it returns only `{ '\0', '/' }`. The mod's defensive scrub
// therefore lets characters like ':' through on Linux, and the engine
// then rejects the call with FileNotFoundException.
//
// Concrete repro: Sigma Draconis 7.0: Weapons Pack (CoreParts framework)
// calls Log.Init($"{ModContext.ModName}Init.log"), produces the filename
// "Sigma Draconis 7.0: Weapons PackInit.log" containing a colon, and
// fails the engine check on Linux. World load aborts and the rest of the
// loading sequence cascades into NREs.
//
// Fix: a Prefix on each of the 12 methods scrubs the caller-supplied
// filename through MyKeenUtils.GetFixedInvalidFileNameChars(), replacing
// each offending char with '_'. This mirrors the Windows-side behavior
// of `Path.GetInvalidFileNameChars()` that the mod's scrubber assumed,
// without changing the engine's protective logic — the engine's own
// IndexOfAny check still runs after our scrub, so anything we miss still
// gets rejected.
//
// A Finalizer is retained as a regression sentinel: it does nothing on
// success but logs the input when the engine still throws despite our
// scrub (e.g. if a future game update changes the filter list). No-op
// in the common case.
//
// The replacement char '_' matches the convention used by mod templates
// (e.g. CoreParts' Log.Init) and is itself a valid filename character on
// every supported filesystem.

static class MyAPIUtilitiesStorageScrub
{
    internal const string Tag = "[LinuxCompat][Storage]";

    /// <summary>
    /// Replaces every char in <paramref name="file"/> that appears in
    /// <see cref="MyKeenUtils.GetFixedInvalidFileNameChars"/> with '_'.
    /// Returns the input unchanged if it already contains no invalid
    /// chars (the common case), avoiding an allocation.
    /// </summary>
    internal static string Scrub(string file)
    {
        if (string.IsNullOrEmpty(file))
            return file;

        var invalid = MyKeenUtils.GetFixedInvalidFileNameChars();
        if (file.IndexOfAny(invalid) < 0)
            return file;

        var sb = new StringBuilder(file.Length);
        foreach (var c in file)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }

    internal static Exception LogIfThrew(
        string method, string file, Type callingType, Exception __exception)
    {
        if (__exception == null)
            return null;

        try
        {
            MyLog.Default?.WriteLine(
                $"{Tag} {method} threw {__exception.GetType().FullName} after scrub: " +
                $"{__exception.Message} (file='{file ?? "<null>"}', " +
                $"callingType='{callingType?.FullName ?? "<null>"}')");
        }
        catch
        {
            // Sentinel only; never swallow or alter the exception.
        }
        return __exception;
    }
}

[HarmonyPatch(typeof(MyAPIUtilities), MyAPIUtilitiesPathHelper.IMyUtilities + "WriteFileInLocalStorage")]
[HarmonyPatchCategory("Finish")]
static class MyAPIUtilitiesWriteFileInLocalStoragePatch
{
    static void Prefix(ref string file) => file = MyAPIUtilitiesStorageScrub.Scrub(file);
    static Exception Finalizer(string file, Type callingType, Exception __exception)
        => MyAPIUtilitiesStorageScrub.LogIfThrew("WriteFileInLocalStorage", file, callingType, __exception);
}

[HarmonyPatch(typeof(MyAPIUtilities), MyAPIUtilitiesPathHelper.IMyUtilities + "WriteFileInWorldStorage")]
[HarmonyPatchCategory("Finish")]
static class MyAPIUtilitiesWriteFileInWorldStoragePatch
{
    static void Prefix(ref string file) => file = MyAPIUtilitiesStorageScrub.Scrub(file);
    static Exception Finalizer(string file, Type callingType, Exception __exception)
        => MyAPIUtilitiesStorageScrub.LogIfThrew("WriteFileInWorldStorage", file, callingType, __exception);
}

[HarmonyPatch(typeof(MyAPIUtilities), MyAPIUtilitiesPathHelper.IMyUtilities + "ReadFileInLocalStorage")]
[HarmonyPatchCategory("Finish")]
static class MyAPIUtilitiesReadFileInLocalStoragePatch
{
    static void Prefix(ref string file) => file = MyAPIUtilitiesStorageScrub.Scrub(file);
    static Exception Finalizer(string file, Type callingType, Exception __exception)
        => MyAPIUtilitiesStorageScrub.LogIfThrew("ReadFileInLocalStorage", file, callingType, __exception);
}

[HarmonyPatch(typeof(MyAPIUtilities), MyAPIUtilitiesPathHelper.IMyUtilities + "ReadFileInWorldStorage")]
[HarmonyPatchCategory("Finish")]
static class MyAPIUtilitiesReadFileInWorldStoragePatch
{
    static void Prefix(ref string file) => file = MyAPIUtilitiesStorageScrub.Scrub(file);
    static Exception Finalizer(string file, Type callingType, Exception __exception)
        => MyAPIUtilitiesStorageScrub.LogIfThrew("ReadFileInWorldStorage", file, callingType, __exception);
}

[HarmonyPatch(typeof(MyAPIUtilities), MyAPIUtilitiesPathHelper.IMyUtilities + "FileExistsInLocalStorage")]
[HarmonyPatchCategory("Finish")]
static class MyAPIUtilitiesFileExistsInLocalStoragePatch
{
    static void Prefix(ref string file) => file = MyAPIUtilitiesStorageScrub.Scrub(file);
    static Exception Finalizer(string file, Type callingType, Exception __exception)
        => MyAPIUtilitiesStorageScrub.LogIfThrew("FileExistsInLocalStorage", file, callingType, __exception);
}

[HarmonyPatch(typeof(MyAPIUtilities), MyAPIUtilitiesPathHelper.IMyUtilities + "FileExistsInWorldStorage")]
[HarmonyPatchCategory("Finish")]
static class MyAPIUtilitiesFileExistsInWorldStoragePatch
{
    static void Prefix(ref string file) => file = MyAPIUtilitiesStorageScrub.Scrub(file);
    static Exception Finalizer(string file, Type callingType, Exception __exception)
        => MyAPIUtilitiesStorageScrub.LogIfThrew("FileExistsInWorldStorage", file, callingType, __exception);
}

[HarmonyPatch(typeof(MyAPIUtilities), MyAPIUtilitiesPathHelper.IMyUtilities + "DeleteFileInLocalStorage")]
[HarmonyPatchCategory("Finish")]
static class MyAPIUtilitiesDeleteFileInLocalStoragePatch
{
    static void Prefix(ref string file) => file = MyAPIUtilitiesStorageScrub.Scrub(file);
    static Exception Finalizer(string file, Type callingType, Exception __exception)
        => MyAPIUtilitiesStorageScrub.LogIfThrew("DeleteFileInLocalStorage", file, callingType, __exception);
}

[HarmonyPatch(typeof(MyAPIUtilities), MyAPIUtilitiesPathHelper.IMyUtilities + "DeleteFileInWorldStorage")]
[HarmonyPatchCategory("Finish")]
static class MyAPIUtilitiesDeleteFileInWorldStoragePatch
{
    static void Prefix(ref string file) => file = MyAPIUtilitiesStorageScrub.Scrub(file);
    static Exception Finalizer(string file, Type callingType, Exception __exception)
        => MyAPIUtilitiesStorageScrub.LogIfThrew("DeleteFileInWorldStorage", file, callingType, __exception);
}

[HarmonyPatch(typeof(MyAPIUtilities), MyAPIUtilitiesPathHelper.IMyUtilities + "WriteBinaryFileInLocalStorage")]
[HarmonyPatchCategory("Finish")]
static class MyAPIUtilitiesWriteBinaryFileInLocalStoragePatch
{
    static void Prefix(ref string file) => file = MyAPIUtilitiesStorageScrub.Scrub(file);
    static Exception Finalizer(string file, Type callingType, Exception __exception)
        => MyAPIUtilitiesStorageScrub.LogIfThrew("WriteBinaryFileInLocalStorage", file, callingType, __exception);
}

[HarmonyPatch(typeof(MyAPIUtilities), MyAPIUtilitiesPathHelper.IMyUtilities + "WriteBinaryFileInWorldStorage")]
[HarmonyPatchCategory("Finish")]
static class MyAPIUtilitiesWriteBinaryFileInWorldStoragePatch
{
    static void Prefix(ref string file) => file = MyAPIUtilitiesStorageScrub.Scrub(file);
    static Exception Finalizer(string file, Type callingType, Exception __exception)
        => MyAPIUtilitiesStorageScrub.LogIfThrew("WriteBinaryFileInWorldStorage", file, callingType, __exception);
}

[HarmonyPatch(typeof(MyAPIUtilities), MyAPIUtilitiesPathHelper.IMyUtilities + "ReadBinaryFileInLocalStorage")]
[HarmonyPatchCategory("Finish")]
static class MyAPIUtilitiesReadBinaryFileInLocalStoragePatch
{
    static void Prefix(ref string file) => file = MyAPIUtilitiesStorageScrub.Scrub(file);
    static Exception Finalizer(string file, Type callingType, Exception __exception)
        => MyAPIUtilitiesStorageScrub.LogIfThrew("ReadBinaryFileInLocalStorage", file, callingType, __exception);
}

[HarmonyPatch(typeof(MyAPIUtilities), MyAPIUtilitiesPathHelper.IMyUtilities + "ReadBinaryFileInWorldStorage")]
[HarmonyPatchCategory("Finish")]
static class MyAPIUtilitiesReadBinaryFileInWorldStoragePatch
{
    static void Prefix(ref string file) => file = MyAPIUtilitiesStorageScrub.Scrub(file);
    static Exception Finalizer(string file, Type callingType, Exception __exception)
        => MyAPIUtilitiesStorageScrub.LogIfThrew("ReadBinaryFileInWorldStorage", file, callingType, __exception);
}
