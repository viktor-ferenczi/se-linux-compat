// Prepatch methods whose PerformanceCounter or P/Invoke references prevent Harmony IL parsing.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HarmonyLib;

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
    const string SteamOverlayLibrary = "gameoverlayrenderer.so";

    static bool Prefix(string url, ref bool __result)
    {
        try
        {
            var uri = new Uri(url);
            if (uri.Scheme == "https")
            {
                using var process = Process.Start(CreateStartInfo(uri));
                __result = process != null;
                return false;
            }
            __result = true;
        }
        catch
        {
            __result = false;
        }
        return false;
    }

    internal static ProcessStartInfo CreateStartInfo(Uri uri)
    {
        var startInfo = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
        startInfo.ArgumentList.Add(uri.ToString());
        SanitizeEnvironment(startInfo);
        return startInfo;
    }

    internal static void SanitizeEnvironment(ProcessStartInfo startInfo)
    {
        if (startInfo.Environment.TryGetValue("LD_PRELOAD", out var preload))
        {
            string sanitized = string.Join(
                Path.PathSeparator,
                (preload ?? string.Empty)
                    .Split([Path.PathSeparator, ' '], StringSplitOptions.RemoveEmptyEntries)
                    .Where(path =>
                        !string.Equals(
                            Path.GetFileName(path),
                            SteamOverlayLibrary,
                            StringComparison.Ordinal
                        )
                    )
            );

            if (sanitized.Length == 0)
                startInfo.Environment.Remove("LD_PRELOAD");
            else
                startInfo.Environment["LD_PRELOAD"] = sanitized;
        }

        RestoreHostVariable(startInfo, "LD_LIBRARY_PATH", "SYSTEM_LD_LIBRARY_PATH");
        startInfo.Environment.Remove("ENABLE_VK_LAYER_VALVE_steam_overlay_1");
        startInfo.Environment.Remove("ENABLE_VK_LAYER_VALVE_steam_fossilize_1");
        startInfo.Environment["DISABLE_VK_LAYER_VALVE_steam_overlay_1"] = "1";
        startInfo.Environment["DISABLE_VK_LAYER_VALVE_steam_fossilize_1"] = "1";
    }

    static void RestoreHostVariable(
        ProcessStartInfo startInfo,
        string variable,
        string hostVariable
    )
    {
        if (!startInfo.Environment.TryGetValue(hostVariable, out var value))
            return;

        if (string.IsNullOrEmpty(value))
            startInfo.Environment.Remove(variable);
        else
            startInfo.Environment[variable] = value;
    }
}
