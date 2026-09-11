# External-browser launch checks

Run `dotnet run --project tests/browser-launch/BrowserLaunchTests.csproj`.

The tests compile the actual URL patch with small Harmony/logging stubs. A
test-only Process alias directly launches `/bin/true` or a missing executable,
exercising success and the prefix's Win32Exception handler without handing the
test path to a desktop opener. They distinguish the prefix's skip-original return
from the game-visible result and check clearing of `LD_PRELOAD`, restoration of
`LD_LIBRARY_PATH` and `PATH` from Steam's `SYSTEM_*` backups, preservation of
unrelated environment values, an unchanged parent environment, direct non-shell
`xdg-open` invocation, and failure results/logging for a missing executable or
invalid URL. Environment changes in the test program are fixtures restored in
`finally`; production only changes `ProcessStartInfo.Environment`.

Live regression: launch Pulsar through Steam with the per-game overlay disabled,
open Plugins → More Info, and confirm the external-browser prompt. Verify the
page opens in the default browser, not merely that xdg-open exits successfully.
Repeat with the overlay enabled to check the unchanged Steam-overlay path.

On Nobara 44/Plasma 6.7.4 with native Brave, the original code reproduced a
browser-child SIGSEGV in Steam's `gameoverlayrenderer.so` during loader startup.
The selective filter was deployed and the real game's More Info link opened in
Brave successfully. Flatpak browsers and a browser cold start remain untested.

Startup exceptions, including `Win32Exception`, return false to the game's
existing failed-to-start-browser popup. An external launcher returning success
does not guarantee the browser subsequently succeeded; this patch does not
claim to detect downstream failures after that handoff.
