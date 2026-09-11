# Mod API boundary test suite

Automated verification that the mod API boundary of this plugin gives mods
Windows semantics (backslash paths, synthetic `C:\` drive, CRLF, Windows
Stopwatch frequency) while the game internals stay native, with translation
happening only at the boundary: the Roslyn rewriter in `Shared/Rewriter/`, the
wrappers in `Shared/Patches/PathHandling/ModApiWrappers/`, and the
`PathCache.ResolveAbsolute` ingress funnel.

## Layout

- `LinuxCompatDiagnostics/`: the test mod, and the source of truth. The harness
  deploys it to `~/.config/SpaceEngineers/Mods/`. A session component runs ~410
  expected-vs-actual probes during world load and writes
  `Storage/LinuxCompatDiagnostics_LinuxCompatDiagnostics/LinuxCompatDiagnostics.log`.
- `run.sh`: client harness. Builds the plugin, deploys the mod, clears the
  compiled-mods cache, starts the game headless, loads the diagnostics world,
  waits for the suite and parses the results. Exit 0 = green.
- `drive_client.py`: Remote-API driver used by `run.sh` (needs the `se-remote`
  skill checkout, `SE_REMOTE_DIR`).
- `run-server.sh`: dedicated server harness. Builds the plugin, prepares the
  local Magnetar DS instance, clears the compiled-mods cache, launches the DS
  offline with `-noimplicitmod`, waits for the suite and parses the results.
  Exit 0 = green.
- `prepare_ds.py`: DS preparation used by `run-server.sh`. It does the offline
  fake-Workshop registration of the mod (the DS rejects local mods in
  multiplayer and cannot download Workshop items offline), the scratch world
  that references it, and the `<LoadWorld>` pointer. Idempotent; run it only
  while the DS is stopped.
- `parse_results.py`: log parser, usable standalone on a suite log. It reports
  the security probes as their own section and enforces the manifest.
- `security-probes.txt`: the security probe manifest (see below).

## Ownership tags

The production stack runs the game on .NET 10 with two plugins: `dotnet-compat`
(applied first, owns .NET-10-vs-Framework differences that reproduce on
Windows) and `linux-compat` (owns Linux-only differences). Every probe carries
the owner:

- `[LINUX]`: the expected value is what the mod observes on Windows (backslash
  separators, drive letters, case-insensitive lookups, CRLF on disk, 10 MHz
  Stopwatch). A FAIL is a linux-compat bug.
- `[DOTNET]`: the expected value is what raw .NET 10 on Windows gives
  (`Encoding.Default` = UTF-8, no codepage 1252, ICU collation, culture
  formatting). A FAIL is filed against the `dotnet-compat` repo, not here. It
  either shows a dotnet-compat shim in action (verify its intent there) or a
  genuine dotnet-compat gap.

The dotnet-compat mod rewriter only activates for mods that fail to compile.
This mod compiles cleanly, so only the linux-compat rewriter transforms it. A
rewriter-detection probe checks exactly that: `typeof(Stopwatch).FullName`
must be the `WindowsStopwatch` shim.

## Security probes

Every path-containment probe is named `security: ...` and comes in matched
halves, so neither direction can regress unnoticed:

- `security: refused, <op>: <case>` is an access the boundary must reject.
  Traversal (`../`, backslash, mid-path, deep-to-root), bare `..`, absolute
  native paths, `%2e%2e`, `\\?\` and UNC prefixes, invented drive letters
  (`C:`, `Z:`, `Q:`), separators in storage filenames, and the engine's
  protected `Data/Scripts` location, across game content, mod location and all
  three storage scopes. Every one of these is refused on Windows too, so a FAIL
  means the Linux port is **wider** than Windows.
- `security: allowed, <op>: <case>` is an access a real mod is entitled to and
  which an over-tightened containment check would break. Relative, backslash
  and wrong-case paths, `..` that stays *inside* the root
  (`Data/../Data/...`), leading `./`, absolute paths built from the egress
  values (`IMyGamePaths.ContentPath`, `IMyModContext.ModPath`), and a
  write/exists/read/delete round trip in local, world and global storage.

The audit these cover is
`~/ws/se/pulsar-tests/se1/reports/security/mod-path-audit.md`.

`security-probes.txt` lists every security probe name a green run reports.
`parse_results.py --security-manifest` fails the run when one of them is
missing, not only when one of them fails. A probe deleted along with the check
it guards, or a suite that died before reaching that section, is a regression
too. After adding or renaming probes, regenerate and commit it:

```
python3 tests/mod-api/parse_results.py <suite.log> \
    --update-security-manifest tests/mod-api/security-probes.txt
```

## Log format

```
PASS [LINUX] name | expected=... | actual=...
FAIL [DOTNET] name | expected=... | actual=...
INFO name = value
=== SUMMARY pass=N fail=N linux_fail=N dotnet_fail=N info=N ===
=== END LinuxCompatDiagnostics ===
```

The `END` line is the terminator; without it the run died mid-suite (parser
exit code 2). All FAILs and the summary are mirrored to `SpaceEngineers.log`,
so results survive a broken storage API.

## Notes

- Both harnesses hold an exclusive `flock` on `~/.cache/se-game.lock` while a
  game instance runs. It is the machine-wide advisory lock shared with the
  other automation sessions, and it is auto-released if the holder dies. They
  wait up to 15 minutes for it.
- DS provenance has two requirements, and both fail silently when unmet. The
  Magnetar local plugin source must be named `linux-compat`, because only a
  name matching the plugin id overrides the implicit core plugin; otherwise the
  GitHub release is loaded. Its `File` must be `ServerPlugin/ServerPlugin.xml`.
  Both live in `~/.config/Magnetar/Magnetar/Sources/sources.xml`.
- The compiled-mods cache clear in `run.sh` is not optional. Dev builds
  randomize the plugin assembly identity, and cached mods pin the stale
  assembly, so world load then fails with
  `FileNotFoundException: LinuxCompat_...`.
- Both harnesses verify that a randomized `LinuxCompat*` assembly name appears,
  which proves the working tree was compiled rather than a shipped plugin
  build. Pulsar reports it in the game log (`LinuxCompat_<random>.<random>`);
  Magnetar reports it only on the launcher's stdout, which `run-server.sh`
  captures, and includes the dev-folder hash
  (`LinuxCompat-<hash>_<random>.<random>`).
- `TestData/CaseSensitivity/casepair.txt` (lowercase sibling of
  `CasePair.txt`) is generated at deploy time; a file pair differing only in
  case cannot be committed without breaking Windows checkouts.
- The mod ships a model and texture (copied from vanilla `GratedCatwalk`)
  referenced from `Data/DiagBlocks.sbc` with backslashes and deliberately
  wrong casing, to exercise the definition/asset pipeline. A second definition
  is mutated at runtime to a Windows-shaped absolute path, to exercise the
  `PathCache.ResolveAbsolute` ingress funnel.
- Dedicated server: the mod runs unchanged there and writes the same log
  format, so `run-server.sh` parses the results with the same parser. It
  passes no security manifest; the manifest is enforced on the client run.
