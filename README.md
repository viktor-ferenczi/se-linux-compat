# Linux compatibility for Space Engineers (version 1)

This plugin contains the compatibility patches required to run Space Engineers
natively on Linux, without Proton or Wine. It covers both the game client
(loaded by Pulsar) and the dedicated server (loaded by Magnetar), so there is
nothing to install from this repository.

- The game runs natively on Linux, on both Wayland and X11.
  Set `SDL_VIDEODRIVER=x11` to force X11.
- The game's window (in windowed mode) can be resized freely and is compatible
  with display scaling.
- Fonts and icons are sharper than on Windows.
- Mods keep Windows semantics (backslash paths, a synthetic `C:\` drive, CRLF)
  while the game's internals use native Linux paths.

The loader compiles the plugin from source and loads it into the game's process
before the game starts. The plugin then replaces the Windows-only parts of the
engine at runtime, without modifying any of the game's own files.

## Documentation

- [Installation](Docs/Installation.md) — prerequisites and the system packages
  your distribution has to provide
- [Architecture](Docs/Architecture.md) — how the plugin works: startup
  sequence, native library shims, SDL, and the path translation architecture
- [Development](Docs/Development.md) — building the plugin, versioning, and the
  diagnostic environment variables
- [Testing](Docs/Testing.md) — the automated test suites

## Bug reports

Please start a support thread on the [Pulsar Discord](https://discord.gg/z8ZczP2YZY)

## Credits

- `SpaceGT` for his 50% contribution in helping to finish this plugin.
- `OwendB` for his relentless testing effort.
- `Linux123123` for the discussions and support in late 2024 (my first attempt on this).

## Legal

Space Engineers is a trademark of Keen Software House s.r.o.
