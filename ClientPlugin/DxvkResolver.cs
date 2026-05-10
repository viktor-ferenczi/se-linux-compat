using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace ClientPlugin;

internal static class DxvkResolver
{
    private static readonly Dictionary<string, string> NativeLibraryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "d3d11", "libdxvk_d3d11.so" },
        { "d3d11.dll", "libdxvk_d3d11.so" },
        { "dxgi", "libdxvk_dxgi.so" },
        { "dxgi.dll", "libdxvk_dxgi.so" },
        { "EOSSDK-Shipping", "libEOSSDK-Linux-Shipping.so" },
        { "EOSSDK-Shipping.dll", "libEOSSDK-Linux-Shipping.so" }
    };

    private static readonly Dictionary<string, IntPtr> LoadedLibraries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> PendingAssemblies = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> RegisteredAssemblies = new(StringComparer.OrdinalIgnoreCase);

    private const int RTLD_NOW = 0x2;
    private const int RTLD_GLOBAL = 0x100;

    [DllImport("libdl.so.2", EntryPoint = "dlopen")]
    private static extern IntPtr dlopen(string filename, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);

    [DllImport("libc")]
    private static extern IntPtr getenv(string name);

    public static void Initialize()
    {
        setenv("DXVK_WSI_DRIVER", "SDL3", 0);
        Environment.SetEnvironmentVariable("DXVK_WSI_DRIVER",
            Environment.GetEnvironmentVariable("DXVK_WSI_DRIVER") ?? "SDL3");

        RegisterResolver("SharpDX");
        RegisterResolver("SharpDX.DXGI");
        RegisterResolver("SharpDX.Direct3D11");
        RegisterResolver("SharpDX.D3DCompiler");
        RegisterResolver("VRage.EOS");
        RegisterResolver("Epic.OnlineServices");

        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += OnResolvingUnmanagedDll;

        // ClientPlugin is loaded by Pulsar into a custom AssemblyLoadContext,
        // so the Default-ALC event above does NOT fire for our own DllImport
        // sites (MySdlSplashScreen, SdlGameWindow, SdlClipboard, SdlIconHelper,
        // MySdlAudioInterop, MyLinuxVideoPlayer). Hook the ALC that actually
        // loaded us, and register a per-assembly resolver as a belt-and-braces.
        var selfAssembly = typeof(DxvkResolver).Assembly;
        var selfContext = AssemblyLoadContext.GetLoadContext(selfAssembly);
        if (selfContext != null && selfContext != AssemblyLoadContext.Default)
        {
            selfContext.ResolvingUnmanagedDll += OnResolvingUnmanagedDll;
        }
        if (RegisteredAssemblies.Add(selfAssembly.GetName().Name ?? "ClientPlugin"))
        {
            NativeLibrary.SetDllImportResolver(selfAssembly, ResolveLibrary);
        }

        // DXVK 2.7+ resolves its WSI driver lazily via dlopen at first use,
        // and our DXVK_WSI_DRIVER=SDL3 pins that to SDL3. If nothing else has
        // loaded libSDL3 into the process by then, that lazy dlopen reports
        //     err:   SDL3 WSI: Failed to load SDL3 DLL.
        //     err:   Failed to initialize WSI.
        // and MyWindowsRender then logs "DirectX 11 renderer not supported.
        // No renderer to revert back to." and the game exits.
        //
        // In the default flow MySdlSplashScreen.SDL_Init implicitly loads
        // libSDL3 first, masking the issue. With -nosplash (or when only
        // Pulsar's own loading splash runs and our SE-splash path is not
        // taken), SDL3 is still unloaded when DXVK first reaches for WSI.
        // Preload it here with RTLD_GLOBAL so DXVK's dlopen finds it via
        // the existing handle no matter which name variant it asks for.
        PreloadSdl3();
        PreloadLibraries();
        PreloadLibrary("EOSSDK-Shipping", "libEOSSDK-Linux-Shipping.so");
    }

    private static void PreloadSdl3()
    {
        // Try both the unversioned dev symlink and the SONAME. dlopen with
        // RTLD_GLOBAL adds the symbols to the global namespace so DXVK's
        // subsequent dlopen("libSDL3.so" / "libSDL3.so.0") returns the same
        // handle without re-resolving against the filesystem.
        foreach (var name in new[] { "libSDL3.so.0", "libSDL3.so" })
        {
            var handle = dlopen(name, RTLD_NOW | RTLD_GLOBAL);
            if (handle != IntPtr.Zero)
            {
                CacheSdl3Handle(handle);
                Console.WriteLine($"[LinuxCompat] Preloaded SDL3 for DXVK WSI: {name}");
                return;
            }
        }

        foreach (var probePath in GetProbeDirectories())
        {
            foreach (var fileName in new[] { "libSDL3.so.0", "libSDL3.so" })
            {
                var candidate = Path.Combine(probePath, fileName);
                if (!File.Exists(candidate)) continue;

                var handle = dlopen(candidate, RTLD_NOW | RTLD_GLOBAL);
                if (handle != IntPtr.Zero)
                {
                    CacheSdl3Handle(handle);
                    Console.WriteLine($"[LinuxCompat] Preloaded SDL3 for DXVK WSI: {candidate}");
                    return;
                }
            }
        }

        Console.WriteLine("[LinuxCompat] WARNING: Failed to preload libSDL3; DXVK WSI may fail under -nosplash");
    }

    // Cache the SDL3 handle under every name a DllImport site might use so the
    // ResolvingUnmanagedDll fallback can return it. Inside the SLR4 container
    // the unversioned "libSDL3.so" doesn't exist on disk (only the SONAME
    // libSDL3.so.0 is registered), so [DllImport("libSDL3.so")] in
    // MySdlSplashScreen / SdlGameWindow / SdlClipboard / SdlIconHelper /
    // MySdlAudioInterop / MyLinuxVideoPlayer would otherwise throw
    // DllNotFoundException despite the dlopen above succeeding.
    private static void CacheSdl3Handle(IntPtr handle)
    {
        LoadedLibraries["SDL3"] = handle;
        LoadedLibraries["libSDL3.so"] = handle;
        LoadedLibraries["libSDL3.so.0"] = handle;
    }

    private static void RegisterResolver(string assemblyName)
    {
        try
        {
            var assembly = Assembly.Load(new AssemblyName(assemblyName));
            if (RegisteredAssemblies.Add(assemblyName))
            {
                NativeLibrary.SetDllImportResolver(assembly, ResolveLibrary);
            }
        }
        catch
        {
            PendingAssemblies.Add(assemblyName);
        }
    }

    private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
    {
        var name = args.LoadedAssembly.GetName().Name;
        if (name != null && PendingAssemblies.Remove(name) && RegisteredAssemblies.Add(name))
        {
            try
            {
                NativeLibrary.SetDllImportResolver(args.LoadedAssembly, ResolveLibrary);
            }
            catch
            {
            }
        }
    }

    private static IntPtr OnResolvingUnmanagedDll(Assembly assembly, string libraryName)
    {
        return ResolveLibrary(libraryName, assembly, null);
    }

    private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (LoadedLibraries.TryGetValue(libraryName, out var value))
            return value;

        if (!NativeLibraryMap.TryGetValue(libraryName, out var fileName))
            return IntPtr.Zero;

        // Try via LD_LIBRARY_PATH
        var handle = dlopen(fileName, RTLD_NOW | RTLD_GLOBAL);
        if (handle != IntPtr.Zero)
        {
            LoadedLibraries[libraryName] = handle;
            return handle;
        }

        foreach (var probePath in GetProbeDirectories())
        {
            var candidate = Path.Combine(probePath, fileName);
            if (!File.Exists(candidate)) continue;

            handle = dlopen(candidate, RTLD_NOW | RTLD_GLOBAL);
            if (handle != IntPtr.Zero)
            {
                LoadedLibraries[libraryName] = handle;
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    private static void PreloadLibraries()
    {
        PreloadLibrary("dxgi", "libdxvk_dxgi.so");
        PreloadLibrary("d3d11", "libdxvk_d3d11.so");
    }

    private static void PreloadLibrary(string libraryName, string fileName)
    {
        // Try the unversioned name first (works when an unversioned symlink
        // exists, e.g. on hosts with -dev packages installed). Fall back to
        // the SONAME, which is what the SLR4 container actually exposes via
        // ldconfig (no unversioned symlink is shipped there).
        foreach (var name in new[] { fileName, fileName + ".0" })
        {
            var handle = dlopen(name, RTLD_NOW | RTLD_GLOBAL);
            if (handle != IntPtr.Zero)
            {
                CacheLibraryHandle(libraryName, fileName, handle);
                Console.WriteLine($"[LinuxCompat] Loaded DXVK: {name}");
                return;
            }
        }

        foreach (var probePath in GetProbeDirectories())
        {
            foreach (var name in new[] { fileName, fileName + ".0" })
            {
                var candidate = Path.Combine(probePath, name);
                if (!File.Exists(candidate)) continue;

                var handle = dlopen(candidate, RTLD_NOW | RTLD_GLOBAL);
                if (handle != IntPtr.Zero)
                {
                    CacheLibraryHandle(libraryName, fileName, handle);
                    Console.WriteLine($"[LinuxCompat] Loaded DXVK: {candidate}");
                    return;
                }
            }
        }

        Console.WriteLine($"[LinuxCompat] WARNING: Failed to load DXVK: {fileName}");
    }

    // Cache the handle under all the names DllImport sites may ask for: the
    // short alias ("d3d11"), the Windows-style alias ("d3d11.dll"), the Linux
    // file name ("libdxvk_d3d11.so"), and the SONAME ("libdxvk_d3d11.so.0").
    // The SONAME entry is what fixes resolution inside the SLR4 sandbox where
    // the unversioned .so symlink doesn't exist.
    private static void CacheLibraryHandle(string libraryName, string fileName, IntPtr handle)
    {
        LoadedLibraries[libraryName] = handle;
        LoadedLibraries[libraryName + ".dll"] = handle;
        LoadedLibraries[fileName] = handle;
        LoadedLibraries[fileName + ".0"] = handle;
    }

    private static IEnumerable<string> GetProbeDirectories()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "RenderingLibs");
        yield return Path.Combine(Environment.CurrentDirectory, "RenderingLibs");

        foreach (var baseDir in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 6 && dir != null; i++)
            {
                yield return Path.Combine(dir.FullName, "RenderingLibs");
                yield return dir.FullName;
                dir = dir.Parent;
            }
        }
    }
}
