// Prepatch methods whose PerformanceCounter or P/Invoke references prevent Harmony IL parsing.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HarmonyLib;
using VRage.Utils;

namespace ClientPlugin.Patches.SystemAbstraction;

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "GetOsName")]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemGetOsNamePatch
{
    static bool Prefix(ref string __result)
    {
        __result = "Linux";
        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "GetTotalPhysicalMemory")]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemGetTotalPhysicalMemoryPatch
{
    static bool Prefix(ref ulong __result)
    {
        __result = TryReadMemInfo("MemTotal");
        return false;
    }

    static ulong TryReadMemInfo(string key)
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith(key + ":", StringComparison.Ordinal))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && ulong.TryParse(parts[1], out var result))
                        return result * 1024;
                }
            }
        }
        catch { }
        return 0;
    }
}

[HarmonyPatch(
    "VRage.Platform.Windows.Sys.MyWindowsSystem",
    "ThreeLetterISORegionName",
    MethodType.Getter
)]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemThreeLetterISOPatch
{
    static bool Prefix(ref string __result)
    {
        __result = string.Empty;
        return false;
    }
}

[HarmonyPatch(
    "VRage.Platform.Windows.Sys.MyWindowsSystem",
    "TwoLetterISORegionName",
    MethodType.Getter
)]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemTwoLetterISOPatch
{
    static bool Prefix(ref string __result)
    {
        __result = string.Empty;
        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "RegionLatitude", MethodType.Getter)]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemRegionLatitudePatch
{
    static bool Prefix(ref string __result)
    {
        __result = string.Empty;
        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "RegionLongitude", MethodType.Getter)]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemRegionLongitudePatch
{
    static bool Prefix(ref string __result)
    {
        __result = string.Empty;
        return false;
    }
}

[HarmonyPatch(
    "VRage.Platform.Windows.Sys.MyWindowsSystem",
    "HasSwappedMouseButtons",
    MethodType.Getter
)]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemHasSwappedMouseButtonsPatch
{
    static bool Prefix(ref bool __result)
    {
        __result = false;
        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "IsUsingGeforceNow", MethodType.Getter)]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemIsUsingGeforceNowPatch
{
    static bool Prefix(ref bool __result)
    {
        __result = false;
        return false;
    }
}

[HarmonyPatch(
    "VRage.Platform.Windows.Sys.MyWindowsSystem",
    "IsUsingGeforceNowCloud",
    MethodType.Getter
)]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemIsUsingGeforceNowCloudPatch
{
    static bool Prefix(ref bool __result)
    {
        __result = false;
        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "IsSingleInstance", MethodType.Getter)]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemIsSingleInstancePatch
{
    static bool Prefix(ref bool __result)
    {
        __result = true;
        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "LogEnvironmentInformation")]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemLogEnvironmentInformationPatch
{
    static bool Prefix()
    {
        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Sys.MyWindowsSystem", "OpenUrl")]
[HarmonyPatchCategory("Finish")]
static class MyWindowsSystemOpenUrlPatch
{
    static bool Prefix(string url, ref bool __result)
    {
        __result = false;
        try
        {
            var uri = new Uri(url);
            if (uri.Scheme == "https")
            {
                using var process = Process.Start(CreateStartInfo(uri));
                __result = process != null;
            }
        }
        catch (Win32Exception ex)
        {
            MyLog.Default?.WriteLineAndConsole(
                $"[LinuxCompat] Cannot start the default browser. Check that a desktop URL launcher and browser are installed: {ex}"
            );
        }
        catch (Exception ex)
        {
            MyLog.Default?.WriteLineAndConsole($"[LinuxCompat] Cannot open browser URL: {ex}");
        }
        // Skip the Windows implementation; __result controls the game's browser-failure popup.
        return false;
    }

    internal static ProcessStartInfo CreateStartInfo(Uri uri)
    {
        var startInfo = new ProcessStartInfo { FileName = uri.ToString(), UseShellExecute = true };
        // Steam's overlay can crash external browsers. Change only the child's environment.
        if (startInfo.Environment.TryGetValue("LD_PRELOAD", out var preload) && preload != null)
        {
            startInfo.Environment["LD_PRELOAD"] = string.Join(
                ':',
                preload
                    .Split(new[] { ':', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(path => Path.GetFileName(path) != "gameoverlayrenderer.so")
            );
        }
        return startInfo;
    }
}
