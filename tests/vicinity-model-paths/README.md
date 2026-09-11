# Vicinity model path tests

Verifies the rewrite the client applies to the model paths a server sends with
its answer to a vicinity-cache request.

The server takes those paths from its own `MyModel.AssetName` values. Vanilla
assets are Content-relative and resolve on any client; mod assets are the
server's absolute on-disk paths, which name nothing on the client. The client
rewrites them onto its own mod folders by published file id, so the prewarm the
vicinity cache exists for happens for modded blocks too.

The suite is standalone. It compiles
`ClientPlugin/Patches/PathHandling/VicinityModelPathRemap.cs` verbatim against a
fabricated mod set, so it needs neither the game nor Steam, and it touches
nothing on disk. The surrounding patch, which reads the session's mod list and
checks the rewritten paths against the filesystem, is not covered here.

```bash
cd tests/vicinity-model-paths && dotnet run -c Release
```

Exit code 0 means every check passed. Covered:

| Case | What it pins down |
| --- | --- |
| `windows server` | A Torch instance root on a `G:` drive, in both separator styles |
| `linux server` | A Linux host, a layout without the Steam app id, and which id wins when one mod's content names another |
| `non-matching` | Vanilla relative paths, unknown ids, the app id, a numeric file name, and empty input are all left alone |
| `rooted` | Which paths count as the sender's own: Linux absolute, drive-prefixed, UNC |
