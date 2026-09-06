using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace ClientPlugin.Compatibility;

// Probes every native library the plugin needs before the game touches any of
// them, so a missing package is reported by name instead of surfacing much
// later as a bare DllNotFoundException from some P/Invoke.
internal static class DependencyCheck
{
    // Packages: Debian/Ubuntu | Fedora | Arch
    private sealed record SystemLibrary(string Name, string Purpose, string Packages);

    // Shipped in the plugin directory by the linux-dependencies and
    // linux-native-wrappers releases.
    private static readonly string[] Bundled =
    [
        "libHavok.so",
        "libRecastDetour.so",
        "libVRageNative.so",
#if !MAGNETAR
        "libD3DCompiler.so",
        "libdxvk_dxgi.so",
        "libdxvk_d3d11.so",
        "libSDL3.so",
        "libopenal.so",
        "libavcodec.so",
        "libavformat.so",
        "libavutil.so",
        "libswresample.so",
        "libswscale.so",
        "libEOSSDK-Linux-Shipping.so",
#endif
    ];

#if !MAGNETAR
    private static readonly SystemLibrary Opus = new(
        "libopus.so.0",
        "voice chat codec",
        "libopus0 | opus | opus"
    );

    private static readonly SystemLibrary Vulkan = new(
        "libvulkan.so.1",
        "Vulkan loader for DXVK",
        "libvulkan1 | vulkan-loader | vulkan-icd-loader"
    );

    private static readonly SystemLibrary[] X11 =
    [
        new("libX11.so.6", "X11 client", "libx11-6 | libX11 | libx11"),
        new("libXext.so.6", "X11 extensions", "libxext6 | libXext | libxext"),
    ];

    private static readonly SystemLibrary[] Wayland =
    [
        new(
            "libwayland-client.so.0",
            "Wayland client",
            "libwayland-client0 | libwayland-client | wayland"
        ),
        new(
            "libwayland-cursor.so.0",
            "Wayland cursor",
            "libwayland-cursor0 | libwayland-cursor | wayland"
        ),
        new("libwayland-egl.so.1", "Wayland EGL", "libwayland-egl1 | libwayland-egl | wayland"),
        new(
            "libxkbcommon.so.0",
            "keyboard handling",
            "libxkbcommon0 | libxkbcommon | libxkbcommon"
        ),
    ];

    // OpenAL Soft needs one of these; without any the game runs silent.
    private static readonly SystemLibrary[] Audio =
    [
        new(
            "libpipewire-0.3.so.0",
            "PipeWire audio",
            "libpipewire-0.3-0 | pipewire-libs | libpipewire"
        ),
        new("libpulse.so.0", "PulseAudio", "libpulse0 | pulseaudio-libs | libpulse"),
        new("libasound.so.2", "ALSA", "libasound2 | alsa-lib | alsa-lib"),
    ];
#endif

    internal static void Run() =>
        Run(Path.GetDirectoryName(typeof(DependencyCheck).Assembly.Location));

    internal static void Run(string pluginDirectory)
    {
        var missing = Bundled
            .Where(name => !File.Exists(Path.Combine(pluginDirectory, name)))
            .Select(name => $"{name}: not in {pluginDirectory} (reinstall the plugin)")
            .ToList();

#if !MAGNETAR
        missing.AddRange(Missing([Opus]));
        if (RenderingConfig.AllowRendering)
        {
            missing.AddRange(Missing([Vulkan]));
            missing.AddRange(MissingWindowSystem());
        }

        if (Audio.All(library => !CanLoad(library.Name)))
            Console.WriteLine(
                "[LinuxCompat] WARNING: no audio backend, the game will have no sound; install one of "
                    + string.Join(", ", Audio.Select(Describe))
            );
#endif

        if (missing.Count == 0)
        {
            Console.WriteLine("[LinuxCompat] all native dependencies found");
            return;
        }

        throw new DllNotFoundException(
            "Missing native dependencies (packages: Debian/Ubuntu | Fedora | Arch):\n  "
                + string.Join("\n  ", missing)
                + "\nInstall the missing packages, then start the game again."
        );
    }

#if !MAGNETAR
    private static IEnumerable<string> MissingWindowSystem()
    {
        var x11 = Missing(X11).ToList();
        var wayland = Missing(Wayland).ToList();
        switch (Environment.GetEnvironmentVariable("SDL_VIDEODRIVER")?.ToLowerInvariant())
        {
            case "x11":
                return x11;
            case "wayland":
                return wayland;
            case null or "":
                break;
            default:
                // offscreen, dummy: no display needed
                return [];
        }

        // SDL3 picks whichever driver works, so one complete set is enough.
        if (x11.Count == 0 || wayland.Count == 0)
            return [];

        return
        [
            "no usable window system, install either\n    X11: "
                + string.Join(", ", X11.Select(Describe))
                + "\n    Wayland: "
                + string.Join(", ", Wayland.Select(Describe)),
        ];
    }

    private static IEnumerable<string> Missing(IEnumerable<SystemLibrary> libraries) =>
        libraries
            .Where(library => !CanLoad(library.Name))
            .Select(library => $"{library.Name}: {library.Purpose} ({library.Packages})");

    private static string Describe(SystemLibrary library) => $"{library.Name} ({library.Packages})";

    private static bool CanLoad(string name) => NativeLibrary.TryLoad(name, out _);
#endif
}
