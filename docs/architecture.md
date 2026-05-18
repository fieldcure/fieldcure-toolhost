# Architecture

This document summarizes the runtime architecture of `FieldCure.ToolHost`. For
behavioral specifications (algorithms, exit codes, flag semantics), see the other
docs in this folder.

## Component map

```
┌──────────────────────────────────────────────────────────────────────┐
│                          DnxLiteRunner                               │
│  Public entry point. Orchestrates the three steps below.             │
└────────────┬───────────────────┬──────────────────┬──────────────────┘
             │                   │                  │
             ▼                   ▼                  ▼
   ┌──────────────────┐  ┌──────────────────┐ ┌────────────────────┐
   │ IPackageResolver │  │ IToolExtractor   │ │ IToolLauncher      │
   │   ↓ default      │  │   ↓ default      │ │   ↓ default        │
   │ NuGetPackage     │  │ NuGetTool        │ │ ToolLauncher       │
   │ Resolver         │  │ Extractor        │ │ (Process.Start)    │
   └─────┬────────────┘  └────────┬─────────┘ └────────────────────┘
         │                        │
         │ uses                   │ writes to
         ▼                        ▼
   ┌──────────────┐       ┌──────────────────────┐
   │ ToolCache    │       │ NuGet global         │
   │ IndexStore   │       │ packages folder      │
   │ (our meta)   │       │ (~/.nuget/packages)  │
   └──────────────┘       └──────────────────────┘
```

## Sequence — first run

```
caller            DnxLiteRunner       Resolver      Extractor       Launcher
  │ StartAsync ▶      │                  │              │              │
  │                   │ ResolveAsync ▶   │              │              │
  │                   │                  │ HTTP nuget.org              │
  │                   │                  │ → 3.0.3                     │
  │                   │ ◀ resolution ◀   │              │              │
  │                   │ EnsureExtracted ▶               │              │
  │                   │                  │ DownloadResource             │
  │                   │                  │ + extract to global folder   │
  │                   │ ◀ layout ◀───────────────────────              │
  │                   │ Start ▶          │              │              │
  │                   │                  │              │ Process.Start │
  │                   │ ◀ Process ◀──────────────────────────────────  │
  │ ◀ Process ◀───────│                  │              │              │
  │ stdio I/O…        │                  │              │              │
```

## Sequence — warm cache (within TTL, `CachedWithRefresh`)

```
caller            DnxLiteRunner       Resolver      Extractor       Launcher
  │ StartAsync ▶      │                  │              │              │
  │                   │ ResolveAsync ▶   │              │              │
  │                   │                  │ (no HTTP — within TTL)       │
  │                   │ ◀ resolution ◀   │              │              │
  │                   │ EnsureExtracted ▶               │              │
  │                   │                  │ (no download — .nupkg.metadata exists)
  │                   │ ◀ layout ◀───────────────────────              │
  │                   │ Start ▶          │              │              │
```

## Single execution path

There is **one** execution path regardless of whether a .NET 10 SDK with `dnx`
is installed. `HasSdk10OrLater` on `DotnetEnvironment` is for diagnostics only.

Rationale: `dnx` is stateless and one-shot; it cannot honor our version policy,
TTL, or pinning. `global.json` muxer routing can also send `dnx` to the wrong
SDK ([dotnet/sdk#51085](https://github.com/dotnet/sdk/issues/51085)). Two paths
would create "works on my machine" divergence.

## Cache layout

| Artifact | Location | Shared with |
|---|---|---|
| Extracted package binaries | NuGet global packages folder | `dotnet`, `dnx`, `nuget` |
| `_index.json` (our metadata) | `%LOCALAPPDATA%/FieldCure/ToolHost/` (or platform equivalent) | nobody |
| Execution logs | `…/FieldCure/ToolHost/logs/` | nobody |
| In-flight downloads | `…/FieldCure/ToolHost/tmp/` | nobody |

We do **not** create a parallel package cache. `dotnet nuget locals all --clear`
works as expected.

## Resolution algorithm

See §8.1 of the implementation spec. Summary:

```
if explicitVersion is not null: return explicitVersion

switch policy:
  CachedOnly:        require cache, else throw
  CachedWithRefresh: cache if pinned-present AND within TTL, else fall through
  AlwaysLatest:      query NuGet metadata, update index
```

TTL default: 24 hours.

## Index integrity

`_index.json` writes go through `ToolCacheIndexStore.SaveAsync`, which:

1. Serializes to a sibling `_index.json.tmp`.
2. `File.Move(_index.json.tmp, _index.json, overwrite: true)` — atomic on all
   supported platforms.

Corrupt or schema-mismatched files are treated as empty and overwritten on next
save.
