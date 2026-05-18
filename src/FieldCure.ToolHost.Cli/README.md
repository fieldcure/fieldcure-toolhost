# FieldCure.ToolHost.Cli (`fcdnx`)

**Run .NET tools from NuGet without the .NET SDK** — a drop-in `dnx` / `dotnet tool execute` replacement for environments where only the .NET runtime is available.

Bridge until Microsoft ships standalone `dnx` ([dotnet/sdk#49796](https://github.com/dotnet/sdk/issues/49796)).

## Install

```bash
dotnet tool install -g FieldCure.ToolHost.Cli
```

## Quick Start

```bash
fcdnx dotnetsay
fcdnx dotnetsay@2.1.0
fcdnx --prerelease MyTool.Preview
fcdnx --add-source https://feed.example.com/v3/index.json MyCorp.Tool
fcdnx --interactive YourCorp.Tool
fcdnx dotnetsay -- "Hello from a runtime-only host!"
```

## Common Flags

| Flag | Meaning |
|---|---|
| `<PACKAGE_ID>[@VERSION]` | Tool to run; append `@VERSION` to pin |
| `--prerelease` | Include prerelease versions |
| `--source <URI>` | Restrict to one source |
| `--add-source <URI>` | Add a source (repeatable) |
| `--interactive` | Allow credential plugin prompts |
| `--verbosity <LEVEL>` | `q`/`m`/`n`/`d`/`diag` |
| `--policy <POLICY>` | `AlwaysLatest` (default), `CachedWithRefresh`, `CachedOnly` |
| `--` | Everything after goes to the tool |

Run `fcdnx --help` for the full list.

## Exit Codes

`0` on success; tool's own code if non-zero. ToolHost-specific failures follow `sysexits.h` (`64`–`70`). See the [flag-compatibility doc](https://github.com/fieldcure/fieldcure-toolhost/blob/main/docs/flag-compatibility.md#exit-codes).

## Requirements

- .NET 8 or .NET 10 runtime (no SDK needed)

## See Also

- [GitHub](https://github.com/fieldcure/fieldcure-toolhost)
- [`FieldCure.ToolHost`](https://www.nuget.org/packages/FieldCure.ToolHost) — the embeddable library this CLI wraps
