# Steam overlay input regression checks

Covers the Wayland input bridge in `SteamOverlayInput`. Run the checks without
opening a window:

```sh
dotnet run --project tests/steam-overlay/SteamOverlayInputTests.csproj
```

The optional integration check uses the actual `SteamOverlayInput` source,
SDL3, a small Vulkan window, Steam's installed overlay, and an SDL virtual
gamepad. It verifies overlay toggling, pointer forwarding, focus, and
controller axis, button and event delivery before, during and after the overlay
opens. It does not start Space Engineers or modify its configuration.

Close the game first. Steam must be running, the overlay must be enabled, and
its shortcut must be the default Shift+Tab. SDL3 (`libSDL3.so.0`), Vulkan and
XWayland must be available. Adjust the Steam installation path if needed.
Build before setting `LD_PRELOAD`, so Steam does not inject into build tools:

```sh
dotnet build tests/steam-overlay/SteamOverlayInputTests.csproj
env SteamAppId=244850 SteamGameId=244850 \
    ENABLE_VK_LAYER_VALVE_steam_overlay_1=1 \
    LD_PRELOAD="$HOME/.steam/steam/ubuntu12_64/gameoverlayrenderer.so" \
    SDL_VIDEODRIVER=wayland \
    dotnet tests/steam-overlay/bin/Debug/net10.0/SteamOverlayInputTests.dll --native
```

Repeat with `SDL_VIDEODRIVER=x11` for the X11 baseline. That run checks that no
bridge is created and that controller input continues normally. These driver
choices apply only to the test process; the plugin never sets an SDL video
driver.

The Wayland bridge passes input through Steam's existing Xlib hooks using a
separate, hidden XWayland window. It does not replace the game's Wayland window
or Vulkan surface. X11, offscreen sessions, disabled overlays, and processes
without Steam injection do not create a bridge. Mouse motion stays on the
existing path while the overlay is closed, and SDL controller handling is
unchanged.

The bridge exists because Steam has no native Wayland input hooks, so the
overlay gets no input from a game running on Wayland. See
[Valve's tracking issue](https://github.com/ValveSoftware/steam-for-linux/issues/8020)
and the similar Xlib input-proxy approach in
[Proton-GE's overlay bridge](https://github.com/GloriousEggroll/proton-ge-custom/tree/master/lsteamclient/overlay_bridge).

Before release, also check the full game: configured overlay shortcut, mouse
clicks and scrolling, keyboard layouts, Alt+Tab, relative mouse capture on
closing, and a physical controller with Steam Input both enabled and disabled.
The virtual gamepad checks do not qualify hardware-specific mappings, rumble,
or Steam Input controller navigation inside the overlay.
