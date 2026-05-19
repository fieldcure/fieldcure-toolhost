# Contributing to FieldCure.ToolHost

Thanks for your interest. Please read this short doc before opening an issue or
PR — it will save us all some round-trips.

## Scope discipline

This is a **time-bounded bridge** until Microsoft ships standalone `dnx`
([dotnet/sdk#49796](https://github.com/dotnet/sdk/issues/49796)). Scope is
intentionally narrow:

- We implement features that `dnx` / `dotnet tool execute` has.
- We do **not** implement features that `dnx` does not have (tool installation,
  registry, search, project commands, etc.).

If you want a feature outside `dnx` parity, the right place is `dotnet/sdk`, not
here. We will politely close such PRs.

## Versioning policy

`FieldCure.ToolHost` and `FieldCure.ToolHost.Cli` ship in **lock-step** — every
release bumps both `<Version>` properties to the same number, even when only
one of them has code changes.

The reason isn't NuGet metadata: `FieldCure.ToolHost.Cli` is `PackAsTool=true`,
which embeds the library DLL inside the tool nupkg and **does not declare
`FieldCure.ToolHost` as a dependency**. NuGet treats the two packages as
independent — but the CLI's bytes are always built against one specific
library version, so the two are coupled at compile time.

Without the lock-step rule a maintainer could push a `0.3.x` CLI built against
a `0.2.x` library and nothing on the NuGet side would warn the user that the
combination is incoherent. Keeping the versions aligned makes provenance
trivially auditable: "the library and CLI named `0.3.0` were built from the
same commit." See `scripts/README.md` for the operational checklist.

## Building from source

```bash
git clone https://github.com/fieldcure/fieldcure-toolhost.git
cd fieldcure-toolhost
dotnet restore FieldCure.ToolHost.slnx
dotnet build FieldCure.ToolHost.slnx -c Release
dotnet test FieldCure.ToolHost.slnx -c Release --filter "Category!=Integration"
```

Integration tests (network + nuget.org) run when `RUN_INTEGRATION=1` is set:

```bash
RUN_INTEGRATION=1 dotnet test FieldCure.ToolHost.slnx -c Release
```

## Pull request guidelines

- One logical unit per PR. e.g., `DotnetEnvironment` detection is one PR; the
  resolver is another. Don't bundle.
- Test-first for parser code (XML parsing, version-constraint parsing).
- XML doc on every public member — `TreatWarningsAsErrors` + `GenerateDocumentationFile`
  will catch omissions in CI.
- Async methods end in `Async` and accept a `CancellationToken` (even if currently unused).
- No `var` for non-obvious types. Reviewers shouldn't need to hover to read code.
- Cross-platform paths only — `Path.Combine`, `Path.DirectorySeparatorChar`. Never
  hardcode `\` or `/`.
- Commit messages: imperative mood with scoped prefix
  (e.g., `resolver: support prerelease constraints`,
  `cli: implement --add-source`).

## Reporting bugs

Please include:

1. .NET runtime version (`dotnet --info`).
2. OS and architecture (RID).
3. The exact command (`fcdnx ...`) or code snippet.
4. The full log at `--verbosity diag` if relevant.
5. The package id + version (or "any package") of the tool you were running.
