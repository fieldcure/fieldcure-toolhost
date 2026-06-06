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

## Environment Isolation

ToolHost matches `dnx` by default: launched tools inherit the current process
environment, and `AdditionalEnvironment` is applied on top. Hosts that execute
untrusted tools or stdio MCP servers can opt out:

```csharp
var envVars = ToolEnvironment.GetDefaultEnvironmentVariables();
envVars["MY_TOOL_API_KEY"] = apiKey;

using Process tool = await runner.StartAsync(new ToolInvocationRequest
{
    PackageId = "My.Tool",
    ToolArguments = Array.Empty<string>(),
    InheritEnvironmentVariables = false,
    AdditionalEnvironment = envVars,
});
```

`GetDefaultEnvironmentVariables()` keeps platform basics such as `PATH`, home
directories, temp directories, and system roots without forwarding unrelated
tokens from the parent process.

## Version Policy

| Policy | Behavior |
|---|---|
| `AlwaysLatest` | Query NuGet every call (dnx semantics) |
| `CachedWithRefresh` | Use cached if within TTL (default 24h); else query |
| `CachedOnly` | Use cached unconditionally (offline) |

## Requirements

- **A .NET 8 or .NET 10 runtime must be installed on the host machine.**
  ToolHost locates the `dotnet` muxer via `PATH` or `DOTNET_ROOT` and
  invokes it to launch tools — it does **not** ship a runtime of its
  own. Applications distributed to environments where users may not
  have a pre-installed runtime (MS Store, MSIX on fresh PCs, minimal
  containers) need to bundle one with the application or prompt the
  user to install it. The .NET SDK is **not** required.

## See Also

- [GitHub](https://github.com/fieldcure/fieldcure-toolhost) — docs, source, issues
- [`fcdnx`](https://www.nuget.org/packages/FieldCure.ToolHost.Cli) — drop-in `dnx` CLI built on this library
