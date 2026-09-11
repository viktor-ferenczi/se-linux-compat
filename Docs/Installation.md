# Installation

What you need before the plugin can run. See the [README](../README.md) for
what the plugin is.

## Prerequisites

- [Space Engineers](https://store.steampowered.com/app/244850/Space_Engineers/)
  installed by Steam for Linux (the standard installation for Proton)
- [Pulsar](https://github.com/SpaceGT/Pulsar/), which downloads and applies
  this plugin for you. On a dedicated server,
  [Magnetar](https://github.com/CometWorks/magnetar/) does that instead.

There is nothing to install from this repository.

## System packages

The plugin ships its own DXVK, SDL3, FFmpeg, OpenAL Soft and the PE-loader
wrappers, but a few libraries have to come from your distribution. On startup
the plugin checks for them and names the missing ones, along with the package
names below.

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
