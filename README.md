# Linux compatibility for Space Engineers (version 1)

## Overview

This plugin contains the compatibility patches required to run Space Engineers
natively on Linux, without Proton or Wine. It covers both the game client
(loaded by Pulsar) and the dedicated server (loaded by Magnetar), so there is
nothing to install from this repository.

## Features

- The game runs natively on Linux, on both Wayland and X11.
  Set `SDL_VIDEODRIVER=x11` to force X11.
- The game's window (in windowed mode) can be resized freely and is compatible
  with display scaling.
- Fonts and icons are sharper than on Windows.
- Mods keep Windows semantics (backslash paths, a synthetic `C:\` drive, CRLF)
  while the game's internals use native Linux paths. See
  [ARCHITECTURE.md](ARCHITECTURE.md).

## Prerequisites

- [Space Engineers](https://store.steampowered.com/app/244850/Space_Engineers/) installed by Steam for Linux 
  (standard installation for Proton)
- [Pulsar](https://github.com/SpaceGT/Pulsar/),
  which downloads and applies this plugin for you.
  For a dedicated server, [Magnetar](https://github.com/CometWorks/magnetar/)
  takes that role.

## System packages

The plugin ships its own DXVK, SDL3, FFmpeg, OpenAL Soft and the PE-loader
wrappers, but a few libraries have to come from your distribution. The plugin
checks for them on startup and reports the missing ones by name, together with
the package names below.

| Library | Debian/Ubuntu | Fedora | Arch |
| --- | --- | --- | --- |
| `libvulkan.so.1` (plus a Vulkan driver for your GPU) | `libvulkan1`, `mesa-vulkan-drivers` | `vulkan-loader`, `mesa-vulkan-drivers` | `vulkan-icd-loader`, `vulkan-radeon` / `vulkan-intel` / `nvidia-utils` |
| `libopus.so.0` | `libopus0` | `opus` | `opus` |
| X11: `libX11.so.6`, `libXext.so.6` | `libx11-6`, `libxext6` | `libX11`, `libXext` | `libx11`, `libxext` |
| Wayland: `libwayland-client.so.0`, `libwayland-cursor.so.0`, `libwayland-egl.so.1`, `libxkbcommon.so.0` | `libwayland-client0`, `libwayland-cursor0`, `libwayland-egl1`, `libxkbcommon0` | `libwayland-client`, `libwayland-cursor`, `libwayland-egl`, `libxkbcommon` | `wayland`, `libxkbcommon` |
| Audio, one of: `libpipewire-0.3.so.0`, `libpulse.so.0`, `libasound.so.2` | `libpipewire-0.3-0`, `libpulse0`, `libasound2` | `pipewire-libs`, `pulseaudio-libs`, `alsa-lib` | `libpipewire`, `libpulse`, `alsa-lib` |

Either the X11 or the Wayland set is enough. `SDL_VIDEODRIVER` restricts the
check to that driver, and `offscreen` needs neither. A missing audio backend
only logs a warning: the game starts without sound. The dedicated server needs
none of these.

The Flatpak build of Steam should already have all of them in its runtime.

## How it works

The loader compiles the plugin from source and loads it into the game's process
before the game starts. The plugin then replaces the Windows-only parts of the
engine at runtime, without modifying any of the game's own files: DirectX 11
runs on DXVK over Vulkan, SDL takes over the window and input, FFmpeg replaces
DirectShow, and the game's own Windows-only native libraries keep running
behind shim libraries that convert between the Windows and Linux ABIs. Paths
are translated at the mod API boundary, so mods still believe they run on
Windows.

[ARCHITECTURE.md](ARCHITECTURE.md) has the details.

## Development

Local build paths (`Bin64`, `DS64`, `Pulsar`, `Magnetar`, `Steamworks`,
`Dependencies`, `Wrappers`) are empty in `Directory.Build.props`. To override
them, copy its first `PropertyGroup` into `Directory.Build.props.user`
(git-ignored) in the repo root, wrapped in a top-level `<Project>` element, and
fill in your paths.

`Bin64` and `DS64` are auto-detected from Steam if left empty. `Pulsar` and
`Magnetar` enable the post-build deployment into those plugin loader folders;
pass `-p:Pulsar= -p:Magnetar=` to build without deploying.

`Shared/` compiles into both `ClientPlugin` and `ServerPlugin`; verify that both
build. Format the code with `csharpier` before committing.

## Testing

| Suite | Covers |
| --- | --- |
| [tests/mod-api](tests/mod-api/README.md) | The mod API boundary, on the game client and the dedicated server. Needs both. |
| [tests/physics-border](tests/physics-border/README.md) | Bounded-world broad-phase border cleanup, through both `MyPhysics.CreateHkWorld` paths. Needs the game. |
| [tests/path-translation](tests/path-translation/README.md) | The root mapping in `PathTranslation.Init()`. Standalone. |
| [tests/steam-overlay](tests/steam-overlay/README.md) | The Wayland Steam overlay input bridge. Standalone, with an optional live check. |

## Bug reports

Please start a support thread on the [Pulsar Discord](https://discord.gg/z8ZczP2YZY)

## Credits

- `SpaceGT` for his 50% contribution in helping to finish this plugin.
- `OwendB` for his relentless testing effort.
- `Linux123123` for the discussions and support in late 2024 (my first attempt on this).

## Legal

Space Engineers is a trademark of Keen Software House s.r.o.
