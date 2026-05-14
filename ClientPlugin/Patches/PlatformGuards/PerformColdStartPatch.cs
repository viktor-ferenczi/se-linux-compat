using System.IO;
using HarmonyLib;
using Sandbox;
using Sandbox.Engine.Utils;
using VRage.FileSystem;

namespace ClientPlugin.Patches.PlatformGuards;

// NGEN is a Windows-only ahead-of-time native image generator. It does not ship
// with .NET 10 on Linux, so MyCommonProgramStartup.PerformColdStart fails with:
//   NGEN failed: System.ComponentModel.Win32Exception (2): An error occurred trying
//   to start process '/usr/share/dotnet/shared/Microsoft.NETCore.App/<ver>/ngen' ...
// when it tries to Process.Start the missing binary.
//
// On modern .NET, ReadyToRun / tiered JIT compilation supersedes NGEN, so no
// equivalent step is needed here. We simply suppress the call and still mark the
// cold start as complete so subsequent startups short-circuit the cold-start work.
[HarmonyPatch(typeof(MyCommonProgramStartup), nameof(MyCommonProgramStartup.PerformColdStart))]
[HarmonyPatchCategory("Finish")]
static class PerformColdStartPatch
{
    static void Prefix()
    {
        MyFakes.ENABLE_NGEN = false;
    }

    static void Postfix()
    {
        // The original creates ColdStart.txt only inside the (now-skipped) NGEN
        // block. Touch it ourselves so future startups see the marker and skip
        // the assembly preloading pass that PerformColdStart otherwise repeats.
        var path = Path.Combine(MyFileSystem.UserDataPath, "ColdStart.txt");
        if (!File.Exists(path))
        {
            File.Create(path).Dispose();
        }
    }
}
