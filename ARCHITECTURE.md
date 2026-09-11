# Architecture

How the plugin makes a Windows-only game run natively on Linux. The
[README](README.md) covers installation and use; this document covers the
internals and is aimed at people changing the plugin.

## Projects

| Project | Assembly | Loader | Build defines |
| --- | --- | --- | --- |
| `ClientPlugin/` | `LinuxCompat` | Pulsar | `PULSAR` |
| `ServerPlugin/` | `LinuxCompatServer` | Magnetar | `MAGNETAR` |
| `Shared/` | compiled into both | — | — |

`Shared/` is a shared project (`Shared.shproj`), so every file in it compiles
into both assemblies; a change there has to build on both sides. Everything in
`Shared/` keeps the `ClientPlugin.*` namespace, including the parts the server
uses.

`LOCAL_BUILD` is defined by both csproj files and by neither loader. The
assembly version attributes in `Plugin.cs` are guarded with `#if !LOCAL_BUILD`,
so a csproj build takes the version from `Directory.Build.props` and a
loader-compiled build takes it from the attributes. See
[Versioning](#versioning).

## Startup sequence

The loader compiles the plugin from source and drives it through four entry
points, in this order:

1. **`Preloader.Initialize()`** — `DependencyCheck.Run()` probes every native
   library the plugin needs and throws a `DllNotFoundException` naming the
   missing ones and their distribution packages, instead of letting a bare
   `DllNotFoundException` surface much later from some P/Invoke.
   `NativeLibraries.Initialize()` then installs the DLL import resolver.
2. **`Preloader.Patch(asmDef)`** — Cecil prepatches, applied to the assemblies
   listed in `Preloader.TargetDLLs` before they load. This is for what Harmony
   cannot reach: the Windows platform layer, the audio backend, the Steam
   layer, SharpDX, and the `MyFileSystem.Open` prologue.
3. **`Preloader.Finish()`** — applies the Harmony `"Finish"` category, which is
   the bulk of the plugin (~125 patch classes; how many apply depends on the
   build, since much of `ClientPlugin/` is compiled out of the server). On the server this is also where
   `PathTranslation.Init()` and `ShimRegistration.Register()` run, because the
   server's `Plugin.Init` runs after the auto-loaded world's mods have already
   compiled.
4. **`Plugin.Init`** — on the client: `PathTranslation.Init()` (the Cecil-injected
   mod path getters need translation before any mod runs),
   `ShimRegistration.Register()` (mod compilation needs the rewriter shims
   whitelisted and referenced), then the Harmony `"Init"` category.

`Plugin.Update` runs every frame: it pumps `MainThreadDispatcher` so
render-thread continuations execute on the game thread, and re-asserts the
Havok scheduling override (`MyFakes` can be rewritten by a multiplayer event).

No game file is ever modified; every change is made to the loaded image.

## Native libraries

`NativeLibraries` hooks `AssemblyLoadContext.Default.ResolvingUnmanagedDll` to map the
Windows library names the engine asks for onto their Linux counterparts:

| Engine asks for | Resolves to |
| --- | --- |
| `d3d11.dll`, `dxgi.dll` | DXVK (`libdxvk_d3d11.so`, `libdxvk_dxgi.so`) |
| `OpenAL32.dll`, `soft_oal.dll` | `libopenal.so` |
| `steam_api64.dll` | `libsteam_api.so` |
| `EOSSDK-Shipping.dll` | `libEOSSDK-Linux-Shipping.so` |
| `Havok.dll`, `RecastDetour.dll`, `VRage.Native.dll` | the PE-loader wrappers |

DirectX 11 therefore runs on DXVK over Vulkan, and the OpenAL, Steamworks and
Epic SDKs come from their native Linux builds.

The game's own three native libraries have no Linux build at all. The original
Windows binaries keep running behind the shim libraries from
[linux-native-wrappers](https://github.com/CometWorks/linux-native-wrappers),
which load the PE image and convert between the Windows and Linux ABIs at every
call. `HavokLinux`, `RecastDetourLinux` and `VRageNativeLinux` in
`Shared/Compatibility/` bind the managed side to those shims.

DXVK reads its configuration with `getenv`, which on Unix does not see
`Environment.SetEnvironmentVariable`, so `NativeLibraries` sets those variables
through `libc`'s `setenv` instead, without overwriting anything the user
exported.

## Windowing, input, audio and video

`SdlGameWindow` replaces the Win32 window with SDL3, and with it the cursor,
clipboard (`SdlClipboard`), joystick (`SdlJoystick`), icon (`SdlIconHelper`)
and splash screen (`MySdlSplashScreen`). `SdlRenderThread` keeps the render
loop on the thread SDL expects.

Steam's overlay has no native Wayland input hooks, so `SteamOverlayInput`
feeds Wayland input to Steam's existing Xlib hooks through a hidden 1×1
XWayland proxy window. The game window and Vulkan surface stay on Wayland. On
X11 no bridge is created; those games already pass through the hooks. See
[tests/steam-overlay/README.md](tests/steam-overlay/README.md).

Audio goes through OpenAL Soft (`SdlAudio`, `MySdlAudioInterop`, and
`XAudio2Shim` for the XAudio2 surface the engine expects). Video playback runs
through FFmpeg (`FfmpegBindings`, `MyLinuxVideoPlayer`) instead of DirectShow.

Shader compilation uses `D3DCompilerLinux`; `SE_SHADER_OVERRIDE` lets a local
directory shadow the game's shader sources.

## Paths and mod semantics

This is the part most likely to be got wrong, so the rule is absolute:

> Mods see Windows-shaped paths. Game internals see native Linux paths. **All**
> translation happens at the mod API boundary. Never patch an internal game
> method just to make it tolerate a mod-shaped path.

Linux filesystems are case-sensitive and use a different separator, and mods
compiled for Windows assume neither. Three mechanisms carry that difference.

### 1. The compilation rewriter — `Shared/Rewriter/`

`CompilationRewriter` runs over every mod compilation (`MyApiTarget.Mod` only)
and `WindowsSemanticsRewriter` rewrites the syntax trees:

- `System.IO.Path` → `WindowsPath` (backslashes, a synthetic `C:\` drive)
- `TextWriter.WriteLine` → `WindowsTextWriter.WriteLine` (CRLF)
- `Stopwatch` → `WindowsStopwatch` (the 10 MHz Windows frequency)

Matching is symbol-based, so a mod's own type named `Path` is left alone, and
only `Path` members the shim actually implements are rewritten — an unknown
member keeps its original binding rather than becoming a compile error.

Mod API members that return filesystem paths (`IMyGamePaths.ContentPath`,
`IMySession.CurrentPath`, `IMyModContext.ModPath`, …) are wrapped at the mod's
call site in `WindowsPath.FromGame`, and values flowing the other way in
`ToGame`. Translation therefore lives in the mod's own compiled code: plugins
and engine code calling the same members keep receiving native paths.

`ShimRegistration` plumbs the shim types into the script compiler before any
mod compiles. It uses the raw in-memory metadata image rather than
`Assembly.Location`, because the loader renames the plugin assembly and only
the in-memory image carries the identity the whitelist was populated from. It
cannot be a Harmony patch on the whitelist: the game runs the whitelist batch
from `MySandboxGame.LoadData`, before `IPlugin.Init` is called.

### 2. Mod API wrappers — `Shared/Patches/PathHandling/ModApiWrappers/`

`WrappedUtilities` wraps `MyAPIGateway.Utilities` after
`MyModAPIHelper.Initialize` and keeps the Windows behaviour the rewriter cannot
express: storage filename validation, CRLF XML, and case handling.
`StorageSharing` emulates the Windows file-sharing semantics of the storage
API: Linux has no mandatory sharing, so a read that Windows would refuse while
a writer handle is open otherwise succeeds and yields a half-written (commonly
empty) file. Path *mapping* is not done here — that belongs to the rewriter.

### 3. The ingress funnel — `PathCache.ResolveAbsolute`

Paths still arrive from mods through shared mutable data (definitions, saved
object builders, a mod writing a path into a field the engine later reads).
`PathCache.ResolveAbsolute` in `Shared/Patches/PathHandling/PathHelpers.cs` is
the single entry point that turns such a path — Windows-shaped, wrong-cased, or
both — into the real path on disk. It is backed by a flat cache of the
immutable `Content` and `Bin64` trees plus mtime-validated per-directory caches
for mutable roots, so resolution does not walk the filesystem per call.

`MyFileSystemOpenPrepatch` injects a call to it as the prologue of
`MyFileSystem.Open`, which is why `VRage.Library.dll` is in `TargetDLLs`.

`IngressTrace` records every internal site that still receives a
drive-prefixed mod path, together with the stack that delivered it. Debug
builds always flag conversions made outside the sanctioned funnel; release
builds need `SE_LINUX_COMPAT_TRACE_INGRESS=1`. A new report there means a
missing wrapper, not a reason to add another translation site.

### Root mapping — `PathTranslation`

`PathTranslation.Init()` builds the longest-prefix mapping between the native
roots and the Proton-shaped paths mods expect: the install root, user data,
temp and home. The install root comes from `MyFileSystem.RootPath`, which the
launcher sets before any plugin runs, so the plugin knows nothing about Steam
or where the game was installed. Matching accepts paths with or without the
synthetic drive prefix, and the mapping is asymmetric — both directions are
tested in [tests/path-translation](tests/path-translation/README.md).

## Scope

Differences that are not Linux-specific belong elsewhere. The game runs on
.NET 10, and anything that also reproduces on Windows — `Encoding.Default`,
missing codepages, ICU collation, culture formatting — is owned by the
`dotnet-compat` plugin, which both loaders apply first. The mod API test suite
tags every probe with the owning plugin for exactly this reason; see
[tests/mod-api/README.md](tests/mod-api/README.md).

## Versioning

The plugin version appears in three places that must stay in step:

- `AssemblyVersion` / `FileVersion` in `Directory.Build.props` — used by the
  csproj builds, for both projects.
- `[assembly: AssemblyVersion]` in `ClientPlugin/Plugin.cs` — used when Pulsar
  compiles the client plugin from source.
- `[assembly: AssemblyVersion]` in `ServerPlugin/Plugin.cs` — used when
  Magnetar compiles the server plugin from source.

`<Commit>TODO</Commit>` in the plugin manifests is a placeholder; PluginHub and
MagnetarHub carry the real commit in their own copy of the manifest.

## Diagnostic environment variables

None of these are needed for normal play; they exist for isolating defects.

| Variable | Effect |
| --- | --- |
| `SDL_VIDEODRIVER` | Read by SDL3 itself; also restricts the startup dependency check to that driver (`x11`, `wayland`, or a headless driver such as `offscreen`). |
| `PULSAR_NO_RENDER` | Set in-process by Pulsar, not on the command line: disables the DXVK and SDL window paths. Plain `--headless` keeps offscreen rendering and does not set it. |
| `SE_HAVOK_SEQUENTIAL=1` | Step Havok clusters sequentially on the main thread instead of draining the job queue from many threads. |
| `SE_HAVOK_SINGLETHREAD=1` | Additionally step each world single-threaded (implies sequential). |
| `SE_HAVOK_NO_FINALIZE=1` | Suppress the `HkHandle` finalizer, leaking the native objects instead of releasing them from the finalizer thread. |
| `SE_SHADER_OVERRIDE` | Directory mirroring the game's shader tree; a shader source found there is compiled instead of the game's own. |
| `SE_LINUX_COMPAT_TRACE_INGRESS=1` | Report every internal site that still receives a drive-prefixed mod path, with the stack that delivered it. |
| `SE_LINUX_COMPAT_FORCE_HKWORLD_PREFIX=1` | Force every `HkWorld` through the patch's replacement creation path (used by the physics border test). |
