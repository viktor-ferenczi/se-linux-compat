using System;
using System.IO;
using ClientPlugin.Compatibility.Rendering;

namespace ClientPlugin;

internal static class D3DCompilerResolver
{
    public static void Initialize()
    {
        var gameRoot = Environment.GetEnvironmentVariable("SPACE_ENGINEERS_ROOT");
        if (string.IsNullOrEmpty(gameRoot))
        {
            Console.WriteLine("[LinuxCompat] WARNING: SPACE_ENGINEERS_ROOT not set, cannot initialize D3DCompiler");
            return;
        }

        var d3dCompilerPath = Path.Combine(gameRoot, "Bin64", "d3dcompiler_47.dll");
        if (!File.Exists(d3dCompilerPath))
        {
            Console.WriteLine($"[LinuxCompat] WARNING: d3dcompiler_47.dll not found at {d3dCompilerPath}");
            return;
        }

        D3DCompilerLinux.Init(d3dCompilerPath);
        Console.WriteLine($"[LinuxCompat] D3DCompiler initialized: {d3dCompilerPath}");
    }
}
