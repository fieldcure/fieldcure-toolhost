# Release Notes — FieldCure.ToolHost.Cli (`fcdnx`)

## v0.1.8 (2026-06-09)

### Fixed

- **stdin is now forwarded to the launched tool; stdio is bridged with raw byte copies.** Long-lived stdio servers — most importantly MCP servers — read JSON-RPC requests from stdin for the entire session. Previously `fcdnx` closed the child's stdin immediately after launch (and forwarded stdout line-by-line), so an MCP server saw end-of-input at startup and shut down right after connecting ("Server transport closed unexpectedly… process exiting early"). `fcdnx` now pumps its own stdin into the child and only closes the child's stdin when its own stdin reaches EOF (i.e. the host disconnects). stdout/stderr are copied verbatim as raw bytes rather than re-emitted line-by-line, preserving JSON-RPC framing exactly. Together with the v0.1.7 stderr-logging fix, this makes `fcdnx` able to host stdio MCP servers end-to-end. Verified with a full `initialize` + `tools/list` round-trip.

### Changed

- **Library re-pinned to `FieldCure.ToolHost` 0.1.8.** Lockstep version bump — no functional library change; keeps CLI and library on a single shared version line.

## v0.1.7 (2026-06-09)

### Fixed

- **fcdnx diagnostics no longer corrupt the stdout JSON-RPC channel.** When `fcdnx` hosts a stdio MCP server, the child's stdout is the JSON-RPC transport that `fcdnx` forwards verbatim. Previously `fcdnx`'s own console logging (NuGet resolution messages such as `info: ...NuGetPackageResolver... Resolved <pkg> -> <ver>`) was written to stdout, interleaving with JSON-RPC frames and causing MCP hosts to report "failed to connect." All `fcdnx` log output now goes to stderr at every level, leaving stdout clean for the protocol. The hosted server's own logging is unaffected (it already controls its own streams).

### Changed

- **Library re-pinned to `FieldCure.ToolHost` 0.1.7.** Lockstep version bump — no functional library change; keeps CLI and library on a single shared version line.

## v0.1.6 (2026-06-06)

### Added

- **Environment isolation flags.** `--no-inherit-env` prevents the launched tool from receiving the full ambient environment, while seeding it with ToolHost's curated default baseline. `--env KEY=VALUE` and `--unset-env KEY` apply explicit child-process environment overrides. Default behavior remains unchanged and continues to match `dnx`: tools inherit the caller's environment.

## v0.1.5 (2026-05-23)

### Changed

- **Library re-pinned to `FieldCure.ToolHost` 0.1.5.** Adds `ToolCacheIndexStore.EvictAsync(packageId, ct)` for host UIs that surface a manual "update to latest" action against the `CachedWithRefresh` TTL — bypasses the 24h wait for a single package without flushing the rest of the cache. CLI surface itself unchanged: no new subcommand, no new flag. Version bump exists to keep CLI and library on a single shared version line — same pattern as v0.1.2 (library-only addition, lockstep release).

## v0.1.4 (2026-05-19)

### Fixed

- **Tool arguments are now accepted and forwarded.** `fcdnx dotnetsay hello` no longer fails argument parsing before ToolHost starts. Unmatched tokens after the package id are treated as tool arguments and passed through to the launched process.
- **Library re-pinned to `FieldCure.ToolHost` 0.1.4.** This pulls in runtime-aware tool TFM selection, cache constraint validation, and `--configfile` reuse during package download.

## v0.1.3 (2026-05-19)

### Fixed (via embedded library)

- **Library re-pinned to `FieldCure.ToolHost` 0.1.3.** This release fixes a library bug where `--source` and `--add-source` were silently inoperative: the resolver passed `PackageSource` constructor arguments in reverse, producing `SourceRepository` instances pointing at the literal strings `"restricted"` / `"additional-N"` instead of the supplied URLs, which surfaced as `PackageNotFoundException`. CLI users of either flag on v0.1.0–0.1.2 saw spurious "package not found" failures; the CLI passed the values through correctly, the library was at fault. CLI surface itself unchanged.

## v0.1.2 (2026-05-18)

### Changed

- **Library re-pinned to `FieldCure.ToolHost` 0.1.2.** Adds `VersionConstraint` and `AdditionalEnvironment` pass-through on `ToolInvocationRequest` for library embedders. CLI surface unchanged — no new flags, no behavior change. Version bump exists to keep CLI and library on a single shared version line.

## v0.1.1 (2026-05-18)

### Fixed

- **Package metadata** — `projectUrl` and `repository` URLs now point at the actual GitHub repository (`fieldcure/fieldcure-toolhost`) instead of the spec's draft path (`FieldCure/ToolHost`, which 404s). No code changes; nupkg contents byte-identical to v0.1.0 except the nuspec URLs.

## v0.1.0 (2026-05-18)

Initial release. `fcdnx` is a drop-in `dnx` / `dotnet tool execute` replacement for environments without the .NET 10 SDK.

```bash
dotnet tool install -g FieldCure.ToolHost.Cli
fcdnx dotnetsay
```

### Flag matrix (v0.1)

| Flag | Status |
|---|:---:|
| `<PACKAGE_ID>[@VERSION]` | ✓ |
| `--tool-package-version` | ✓ |
| `--prerelease` | ✓ |
| `--source`, `--add-source` | ✓ |
| `--configfile` | ✓ |
| `--ignore-failed-sources` | ✓ |
| `--no-cache`, `--no-http-cache` | △ parsed, not yet wired |
| `--interactive` | ✓ |
| `--allow-roll-forward` | ✓ |
| `--verbosity` | ✓ |
| `--framework` | △ parsed, not yet wired |
| `--arch`, `--os` | △ best-effort in v0.1 |
| `--policy` (ToolHost extension) | ✓ |
| `-y`/`--yes`, `-h`/`--help`, `--version` | ✓ |
| `--` separator → tool args | ✓ |

### Exit codes (sysexits.h convention)

| Code | Meaning |
|---:|---|
| 0 | Tool exited with code 0 |
| (tool's) | Tool exited non-zero (passed through) |
| 64 | Usage error |
| 65 | Package not found |
| 66 | No compatible `tools/{tfm}/{rid}` folder |
| 67 | Network / auth error |
| 68 | Extraction failed |
| 69 | Tool process failed to start |
| 70 | Internal error |

### Targets

- `net8.0` and `net10.0` (both LTS). Roll-forward enabled, so the tool runs on any installed .NET 8+ runtime.
