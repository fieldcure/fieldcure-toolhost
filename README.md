# FieldCure.ToolHost

[![ToolHost](https://img.shields.io/nuget/v/FieldCure.ToolHost?label=ToolHost)](https://www.nuget.org/packages/FieldCure.ToolHost)
[![ToolHost.Cli](https://img.shields.io/nuget/v/FieldCure.ToolHost.Cli?label=ToolHost.Cli)](https://www.nuget.org/packages/FieldCure.ToolHost.Cli)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**Run .NET tools from NuGet without the .NET SDK** — a `dnx`-compatible bridge for
runtime-only environments (MS Store apps, MSIX-distributed software, runtime-only
containers, CI bootstrappers).

This is a **time-bounded bridge** until Microsoft ships standalone `dnx`
(tracking [dotnet/sdk#49796](https://github.com/dotnet/sdk/issues/49796)). When that
ships, this project enters maintenance and eventually deprecation. The API and CLI
intentionally mirror `dnx` so migration is mechanical. See [ROADMAP.md](ROADMAP.md).

## Why

`dnx` is excellent — but it ships only with the .NET 10 SDK. If your software is
distributed through channels where users may have only the .NET *runtime* (the
Microsoft Store, MSIX installers, runtime-only Docker images, etc.), `dnx` is
unavailable. `FieldCure.ToolHost` fills that gap with the same UX.

## Prerequisites

This is a tool **runner**, not a runtime distributor. A .NET 8 or .NET 10
runtime must be present on the host — ToolHost discovers the `dotnet` muxer
through `PATH` or `DOTNET_ROOT` and uses it to launch tools. It deliberately
does not ship a runtime of its own.

If you are shipping software to environments where users may not already have
a runtime (fresh PCs receiving your MS Store app, minimal Docker images, etc.),
you are responsible for bundling one alongside your application or guiding the
user to install it. ToolHost's job ends at "find and use the runtime that's
available."

## Two ways to use it

### As a library (primary)

```csharp
using FieldCure.ToolHost;
using System.Diagnostics;

DotnetEnvironment env = await DotnetEnvironment.DetectAsync();
DnxLiteRunner runner = new(env);

using Process tool = await runner.StartAsync(new ToolInvocationRequest
{
    PackageId = "dotnetsay",
    ToolArguments = new[] { "Hello from a runtime-only host!" },
    Policy = ToolVersionPolicy.CachedWithRefresh,
});

tool.StandardInput.Close();
string output = await tool.StandardOutput.ReadToEndAsync();
await tool.WaitForExitAsync();
Console.WriteLine(output);
```

### As a CLI (`fcdnx`)

```bash
dotnet tool install -g FieldCure.ToolHost.Cli

fcdnx dotnetsay
fcdnx dotnetsay@2.1.0
fcdnx --prerelease MyTool.Preview
fcdnx --add-source https://feed.example.com/v3/index.json MyCorp.Tool
```

See [docs/flag-compatibility.md](docs/flag-compatibility.md) for the full flag matrix.

## Scope

In scope:

- Library + CLI (`fcdnx`) with full `dnx` flag parity for the v0.1 surface
- NuGet global packages folder reuse (interoperates with `dotnet`/`dnx`)
- Three resolution policies: `AlwaysLatest`, `CachedWithRefresh`, `CachedOnly`
- Authenticated feeds via standard NuGet credential providers
- Platform-agnostic .NET tools (`tools/{tfm}/any/`)
- Opt-in child-process environment isolation for hosts that run untrusted tools
  or MCP servers

Out of scope and staying that way:

- Tool installation/uninstallation (`dotnet tool install/update/uninstall`)
- Replacing or wrapping the `dotnet` muxer
- Features outside `dnx` parity, except narrow host-safety controls such as
  environment inheritance isolation

See the **Lifecycle / Sunset Policy** section of [ROADMAP.md](ROADMAP.md). Feature
requests outside `dnx` parity will be politely closed.

## Status

- v0.1 — MVP: resolution + extraction + launch for managed tools, CLI parity for
  the core flag set, NuGet credential plumbing.
- v0.2 — Platform-specific tools (RID graph traversal), self-contained / NativeAOT
  tools, expanded authenticated-feed test matrix.
- v1.0 — API frozen, breaking changes require major bumps.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Please read the scope discipline statement
before opening a feature PR.

## License

MIT — see [LICENSE](LICENSE).
