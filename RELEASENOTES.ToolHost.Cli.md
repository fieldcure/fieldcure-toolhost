# Release Notes — FieldCure.ToolHost.Cli (`fcdnx`)

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
