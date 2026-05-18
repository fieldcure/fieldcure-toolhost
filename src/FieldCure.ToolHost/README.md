# FieldCure.ToolHost

**Run .NET tools from NuGet without the .NET SDK** — a `dnx`-compatible bridge library for environments where only the .NET runtime is available (MS Store apps, MSIX, runtime-only containers, CI bootstrappers).

Bridge until Microsoft ships standalone `dnx` ([dotnet/sdk#49796](https://github.com/dotnet/sdk/issues/49796)).

## Install

```bash
dotnet add package FieldCure.ToolHost
```

## Quick Start

```csharp
using FieldCure.ToolHost;
using System.Diagnostics;

DotnetEnvironment env = await DotnetEnvironment.DetectAsync();
DnxLiteRunner runner = new(env);

using Process tool = await runner.StartAsync(new ToolInvocationRequest
{
    PackageId = "dotnetsay",
    ToolArguments = new[] { "Hello!" },
    Policy = ToolVersionPolicy.CachedWithRefresh,
});

tool.StandardInput.Close();
Console.Write(await tool.StandardOutput.ReadToEndAsync());
await tool.WaitForExitAsync();
```

## Version Policy

| Policy | Behavior |
|---|---|
| `AlwaysLatest` | Query NuGet every call (dnx semantics) |
| `CachedWithRefresh` | Use cached if within TTL (default 24h); else query |
| `CachedOnly` | Use cached unconditionally (offline) |

## Requirements

- .NET 8 or .NET 10 runtime (no SDK needed)

## See Also

- [GitHub](https://github.com/fieldcure/fieldcure-toolhost) — docs, source, issues
- [`fcdnx`](https://www.nuget.org/packages/FieldCure.ToolHost.Cli) — drop-in `dnx` CLI built on this library
