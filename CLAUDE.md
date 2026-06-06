# CLAUDE.md

Guidance for AI agents working in this repo. Keep it short — this complements the
docs, it does not replace them. When something here and a linked doc disagree, the
doc wins; fix this file.

## What this is

`fieldcure-toolhost` ships two NuGet packages that run .NET tools from NuGet
**without the .NET SDK** — a `dnx`-compatible bridge for runtime-only hosts
(MS Store / MSIX apps, containers, CI bootstrappers).

- `FieldCure.ToolHost` — the library (`src/FieldCure.ToolHost`)
- `FieldCure.ToolHost.Cli` — the `fcdnx` CLI (`src/FieldCure.ToolHost.Cli`, `PackAsTool=true`)

Architecture and per-component behavior live in [docs/architecture.md](docs/architecture.md);
flag semantics and their library mapping in [docs/flag-compatibility.md](docs/flag-compatibility.md).

## Scope — read before adding anything

The hard rule: **`dnx` parity only.** Features that `dnx` does not have are out of
scope and PRs/requests for them get politely closed (see [README.md](README.md) →
"In scope / Out of scope" and [CONTRIBUTING.md](CONTRIBUTING.md)). The single
sanctioned exception is narrow host-safety controls (e.g. environment-inheritance
isolation). When in doubt, the answer is "no, that belongs in `dotnet/sdk`."

Defaults must match `dnx` behavior. Any safety/isolation feature is **opt-in** and
leaves the default path byte-for-byte `dnx`-compatible.

## Build, test, run

```bash
dotnet build FieldCure.ToolHost.slnx -c Release
dotnet test  FieldCure.ToolHost.slnx -c Release --filter "Category!=Integration"
```

- Multi-targets **net8.0 + net10.0**; both must build and pass.
- `TreatWarningsAsErrors=true` and `EnforceCodeStyleInBuild=true` (see
  `Directory.Build.props`) — a warning fails the build. Don't suppress; fix, or add
  a justified `NoWarn` with a comment like the existing ones.
- Integration tests (network + nuget.org) run only with `RUN_INTEGRATION=1`.

## Code conventions

- **XML doc on every method**, including `private`/`internal` — `GenerateDocumentationFile`
  is on and this is a FieldCure-portfolio house rule, not just public-API hygiene.
- Nullable enabled, implicit usings, `LangVersion=latest`.
- Match the surrounding style (records for requests, `LoggerMessage`/`ILogger` patterns,
  curated platform branches keyed off `OperatingSystem.IsWindows()` / RID).

## Releasing (lock-step) — important workflow

The two packages ship in **lock-step**: every release bumps **both** `<Version>`
properties to the same number even if only one changed. Rationale (the CLI embeds
the library DLL and does *not* declare it as a NuGet dependency) is in
[CONTRIBUTING.md](CONTRIBUTING.md) → "Versioning policy" and [scripts/README.md](scripts/README.md).

A release touches exactly these files (see any `Release vX.Y.Z` commit for the shape):

1. `RELEASENOTES.ToolHost.md` and `RELEASENOTES.ToolHost.Cli.md` — rename the
   `## Unreleased` section to `## vX.Y.Z (YYYY-MM-DD)`.
2. Both `.csproj` — bump `<Version>` and refresh `<PackageReleaseNotes>`.

Then:

- **Stop before committing.** The maintainer makes the `Release vX.Y.Z` commit
  themselves. Do the edits + verify build/test green, then hand off.
- **Tags are automatic.** GitHub Actions creates the `vX.Y.Z` git tag — never tag
  manually.
- **Publish is local**, via `scripts/publish-nuget.ps1` (pack → sign with the
  GlobalSign EV USB dongle → push). One script publishes both packages and keeps the
  lock-step automatic. `publish-toolhost.ps1` / `publish-cli.ps1` are for
  prereleases/hotfixes only. Requires the dongle plugged in and `NUGET_API_KEY` set.

Version line: stay on the `v0.1.x` patch line for additive work; `v0.2.0` is reserved
for the roadmap items in [ROADMAP.md](ROADMAP.md) (RID-graph traversal, NativeAOT, etc.).
