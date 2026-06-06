# Flag compatibility matrix

`fcdnx` aims for drop-in compatibility with `dnx` / `dotnet tool execute`. This
page lists every supported flag, its semantics, and its mapping to the library
API.

## Flags

| Flag | Type | v0.1 | Library equivalent |
|---|---|:---:|---|
| `<PACKAGE_ID>` (positional, required) | string | ✓ | `ToolInvocationRequest.PackageId` |
| `<PACKAGE_ID>@<VERSION>` | inline | ✓ | `ExplicitVersion` |
| `--tool-package-version <VERSION>` | string | ✓ | `ExplicitVersion` (alias for `@VERSION`) |
| `-y`, `--yes` | bool | ✓ | Auto-accept download confirmation (no-op when no prompt would appear) |
| `--prerelease` | bool | ✓ | `AllowPrerelease = true` |
| `--source <URI>` | string | ✓ | `NuGetPackageResolverOptions.RestrictToSource` |
| `--add-source <URI>` | string (repeatable) | ✓ | `NuGetPackageResolverOptions.AdditionalSources` |
| `--configfile <PATH>` | string | ✓ | `NuGetPackageResolverOptions.NuGetConfigFile` |
| `--ignore-failed-sources` | bool | ✓ | `NuGetPackageResolverOptions.IgnoreFailedSources` |
| `--no-cache` | bool | ✓ | Sets `SourceCacheContext.NoCache = true` |
| `--no-http-cache` | bool | ✓ | Sets `SourceCacheContext.DirectDownload = true` |
| `--interactive` | bool | ✓ | `CredentialProviderSetup.Register(interactive: true)` |
| `--allow-roll-forward` | bool | ✓ | Sets `DOTNET_ROLL_FORWARD=LatestMajor` on child process |
| `--no-inherit-env` (ToolHost extension) | bool | ✓ | `ToolInvocationRequest.InheritEnvironmentVariables = false`; seeds the child with `ToolEnvironment.GetDefaultEnvironmentVariables()` |
| `--env <KEY=VALUE>` (ToolHost extension) | string (repeatable) | ✓ | Adds or overwrites entries in `ToolInvocationRequest.AdditionalEnvironment` |
| `--unset-env <KEY>` (ToolHost extension) | string (repeatable) | ✓ | Adds a null entry to `ToolInvocationRequest.AdditionalEnvironment` so the child process does not receive that variable |
| `--verbosity <LEVEL>` | enum: `q`/`m`/`n`/`d`/`diag` | ✓ | `LogLevel` for `ILogger` factory |
| `--framework <TFM>` | string | ✓ | Overrides extractor TFM selection |
| `--arch <ARCH>` | enum | △ v0.2 | RID architecture override (best-effort in v0.1) |
| `--os <OS>` | enum | △ v0.2 | RID OS override (best-effort in v0.1) |
| `--policy <POLICY>` (ToolHost extension) | enum | ✓ | `ToolInvocationRequest.Policy`; CLI defaults to `AlwaysLatest` |
| `--help`, `-h`, `-?` | bool | ✓ | Print help, exit 0 |
| `--version` | bool | ✓ | Print version, exit 0 |
| `--` (separator) | — | ✓ | Everything after goes to `ToolArguments` |

## Exit codes

`fcdnx` follows the `sysexits.h` convention for failures.

| Code | Meaning |
|---:|---|
| 0 | Tool exited with code 0 |
| (tool's) | Tool exited with a non-zero code (passed through verbatim) |
| 64 | Usage error (bad flags) |
| 65 | Package not found |
| 66 | Package found but no compatible `tools/{tfm}/{rid}` folder |
| 67 | Network or authentication error during resolution / download |
| 68 | Extraction failed (corrupt package, disk full, etc.) |
| 69 | Tool process failed to start |
| 70 | Internal error (unhandled exception in ToolHost) |

## Default policy

- CLI (`fcdnx`): `AlwaysLatest` — matches `dnx` semantics (every invocation is
  a fresh resolve).
- Library (`DnxLiteRunner`): `CachedWithRefresh` (24h TTL) — optimized for apps
  with cold-start sensitivity that embed the runner.

Override per call via `ToolInvocationRequest.Policy`, or via `--policy
CachedWithRefresh|CachedOnly|AlwaysLatest` on the CLI.

## Environment inheritance

By default, `fcdnx` follows normal process and `dnx` behavior: the launched tool
inherits the ambient environment of the `fcdnx` process, and any `--env` /
`--unset-env` entries are applied on top. This keeps NuGet tools compatible with
existing shells, proxy settings, and runtime discovery.

Use `--no-inherit-env` when running a tool that should not see unrelated
credentials or host process settings. In that mode, `fcdnx` starts the tool with
the curated baseline from `ToolEnvironment.GetDefaultEnvironmentVariables()`
(`PATH`, home/profile, temp, and system-directory variables) plus values
provided through `--env`; `--unset-env` can remove even those baseline entries.

## What we explicitly do *not* support

These features exist in `dotnet tool …` but not in `dnx`, so they are out of
scope here too:

- `dotnet tool install`, `update`, `uninstall`
- `dotnet tool list`, `search`
- `dotnet pack`, `restore`, `build`, or any MSBuild-driven workflow
- Replacing or wrapping the `dotnet` muxer

If you need these, use the .NET SDK directly.
