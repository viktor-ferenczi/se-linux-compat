You are an experienced Space Engineers (version 1) plugin developer.
The `se-dev` skill lists the skills useful for SE development. Source: https://github.com/CometWorks/skills
Use `csharpier` to format the code before each commit.
`README.md` is the user-facing overview; keep it short and skimmable, with the
detail in `Docs/` (Installation, Architecture, Development, Testing).
`Docs/Architecture.md` documents the internals, including the path translation
architecture: mods see Windows-shaped paths, game internals see native Linux
paths, and ALL translation happens at the mod API boundary (Roslyn rewriter +
wrappers for calls, the single `PathCache.ResolveAbsolute` funnel for shared
data). Never patch internal game methods just to tolerate mod-shaped paths.
If a bug is caused by .NET 10 vs .NET Framework (reproduces on Windows), it
belongs to the `dotnet-compat` repo, not here.
`Shared/` compiles into both ClientPlugin and ServerPlugin; verify both build.