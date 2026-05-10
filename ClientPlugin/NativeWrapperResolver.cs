using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ClientPlugin.Compatibility;

namespace ClientPlugin;

internal static class NativeWrapperResolver
{
    private static readonly Dictionary<string, string> NativeLibraryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Havok.dll", "libHavok.so" },
        { "RecastDetour.dll", "libRecastDetour.so" },
        { "VRage.Native.dll", "libVRageNative.so" },
        { "D3DCompiler.dll", "libD3DCompiler.so" },
    };

    private static readonly Dictionary<string, IntPtr> LoadedLibraries = new(StringComparer.OrdinalIgnoreCase);

    public static void Initialize()
    {
        var gameRoot = Environment.GetEnvironmentVariable("SPACE_ENGINEERS_ROOT");
        if (string.IsNullOrEmpty(gameRoot))
        {
            Console.WriteLine("[LinuxCompat] WARNING: SPACE_ENGINEERS_ROOT not set, cannot initialize native wrappers");
            return;
        }

        var binDir = Path.Combine(gameRoot, "Bin64");

        RegisterResolver("HavokWrapper");
        RegisterResolver("RecastDetourWrapper");
        RegisterResolver("VRage.NativeWrapper");

        InitWrapper("Havok", binDir, "Havok.dll", HavokLinux.Init);
        InitWrapper("RecastDetour", binDir, "RecastDetour.dll", RecastDetourLinux.Init);
        InitWrapper("VRageNative", binDir, "VRage.Native.dll", VRageNativeLinux.Init);
    }

    private static void InitWrapper(string name, string binDir, string dllName, Action<string> initFunc)
    {
        var dllPath = Path.Combine(binDir, dllName);
        if (!File.Exists(dllPath))
        {
            Console.WriteLine($"[LinuxCompat] WARNING: {dllName} not found at {dllPath}");
            return;
        }

        initFunc(dllPath);
        Console.WriteLine($"[LinuxCompat] {name} initialized: {dllPath}");
    }

    private static void RegisterResolver(string assemblyName)
    {
        try
        {
            var assembly = Assembly.Load(new AssemblyName(assemblyName));
            NativeLibrary.SetDllImportResolver(assembly, ResolveLibrary);
            Console.WriteLine($"[LinuxCompat] Registered native wrapper resolver on {assemblyName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LinuxCompat] WARNING: Could not register resolver on {assemblyName}: {ex.Message}");
        }
    }

    private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (LoadedLibraries.TryGetValue(libraryName, out var handle))
            return handle;

        if (!NativeLibraryMap.TryGetValue(libraryName, out var soName))
            return IntPtr.Zero;

        if (NativeLibrary.TryLoad(soName, out handle))
        {
            LoadedLibraries[libraryName] = handle;
            return handle;
        }

        return IntPtr.Zero;
    }
}
