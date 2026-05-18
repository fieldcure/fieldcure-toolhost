# Roadmap

## Lifecycle / Sunset Policy

This is a bridge tool until Microsoft ships standalone `dnx` (tracking
[dotnet/sdk#49796](https://github.com/dotnet/sdk/issues/49796)).

When that ships:

- **v1.x**: security fixes for 12 months.
- **v2.0 (final)**: deprecation notice pointing to Microsoft's tool. Every
  invocation prints a deprecation banner linking the official command.
- The API and CLI surface intentionally mirror `dnx` so migration is mechanical.

We will **not** grow scope beyond `dnx` parity. Feature requests outside that
scope will be politely closed.

## Phase plan

### v0.1.0 — MVP (current)

Goal: AssistStudio (and similar apps) can replace `dnx` calls with
`FieldCure.ToolHost` and ship to MS Store users without requiring the .NET SDK.

In scope for v0.1:

- All public types in `FieldCure.ToolHost` implemented for platform-agnostic
  tools (`tools/{tfm}/any/`).
- All flag-matrix entries marked ✓ v0.1 in
  [docs/flag-compatibility.md](docs/flag-compatibility.md).
- nuget.org as the default source; `--add-source` for additional feeds.
- NuGet credential providers wired up.
- 80% unit-test coverage on the library; one integration test against
  `dotnetsay` on nuget.org.
- CI on Windows, Linux, macOS.

Out of scope for v0.1 (deferred to v0.2):

- Full RID-based tool selection (`--arch`, `--os`).
- Self-contained / NativeAOT tools (`Runner = "executable"`).
- Comprehensive authenticated-feed test matrix.
- Performance optimizations (parallel downloads, pre-warming).

### v0.2.0

- Full RID-graph traversal for platform-specific tool packages.
- Self-contained / NativeAOT tool support.
- Authenticated-feed test matrix (Azure DevOps, GitHub Packages, private feeds).
- `--list` extension: show locally cached tools and versions.
- `--prune` extension: clean stale cached versions per policy.

### v1.0.0

- Public API frozen; breaking changes require a major bump.
- All flag-matrix entries fully implemented (no "best-effort" caveats).
- Performance baseline documented (cold cache, warm cache).
- Used by AssistStudio v1.1+ in production.

### Post-1.0 — sunset watch

Monitor [dotnet/sdk#49796](https://github.com/dotnet/sdk/issues/49796). When
standalone `dnx` ships:

1. Add deprecation notice to README and `fcdnx --help`.
2. Document the migration path (`fcdnx X` → official command Y).
3. Continue security patches for 12 months.
4. Release v2.0 with deprecation banner on every invocation.
