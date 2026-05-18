# Authenticated NuGet feeds

`FieldCure.ToolHost` uses the standard NuGet credential plugin protocol.
Anything that works with `dotnet restore` against your feed works here too — we
don't store credentials, we delegate.

## How it discovers credentials

When `CredentialProviderSetup.Register(...)` runs (called automatically by the
CLI; call it once during startup when embedding the library), NuGet looks in the
following locations, in order:

1. The `NUGET_PLUGIN_PATHS` environment variable.
2. `~/.nuget/plugins/` (per-user).
3. `%USERPROFILE%\.dotnet\tools\.store\…` for plugins installed via `dotnet tool`
   (e.g., the Azure Artifacts Credential Provider).
4. Machine-wide plugin directories per OS conventions.

On a 401/403 from a feed, NuGet invokes plugins in the discovered order until
one returns credentials or all decline.

## Azure DevOps / Azure Artifacts

Install the
[Azure Artifacts Credential Provider](https://github.com/microsoft/artifacts-credprovider):

```bash
# Cross-platform, installs ~/.nuget/plugins/netcore/CredentialProvider.Microsoft/
iex (iwr https://aka.ms/install-artifacts-credprovider.ps1)
```

Non-interactive mode (e.g., CI):

```bash
export VSS_NUGET_EXTERNAL_FEED_ENDPOINTS='{
  "endpointCredentials": [
    {"endpoint":"https://pkgs.dev.azure.com/your-org/_packaging/your-feed/nuget/v3/index.json",
     "username":"VssSessionToken",
     "password":"<PAT>"}
  ]
}'

fcdnx --add-source https://pkgs.dev.azure.com/your-org/_packaging/your-feed/nuget/v3/index.json YourCorp.Tool
```

Interactive mode (developer machines, allows device login):

```bash
fcdnx --interactive --add-source https://pkgs.dev.azure.com/your-org/_packaging/your-feed/nuget/v3/index.json YourCorp.Tool
```

## GitHub Packages

```xml
<!-- ~/.nuget/NuGet/NuGet.Config -->
<configuration>
  <packageSources>
    <add key="github" value="https://nuget.pkg.github.com/your-org/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github>
      <add key="Username" value="your-github-username" />
      <add key="ClearTextPassword" value="ghp_yourPAT" />
    </github>
  </packageSourceCredentials>
</configuration>
```

Then:

```bash
fcdnx --source https://nuget.pkg.github.com/your-org/index.json YourOrg.Tool
```

## Embedding (library) usage

```csharp
using FieldCure.ToolHost.Authentication;

CredentialProviderSetup.Register(interactive: false, logger);

DotnetEnvironment env = await DotnetEnvironment.DetectAsync();
DnxLiteRunner runner = new(env);

ToolInvocationRequest request = new()
{
    PackageId = "YourCorp.Tool",
    ToolArguments = args,
    Policy = ToolVersionPolicy.CachedWithRefresh,
};

using Process tool = await runner.StartAsync(request);
```

`Register` is idempotent — subsequent calls are no-ops with the original
interactivity setting preserved.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `NetworkOrAuthFailure` (exit 67) on first call | Credential plugin not installed, or `NUGET_PLUGIN_PATHS` doesn't include it. |
| Hangs on `--interactive` in CI | CI is non-interactive; remove the flag or pre-seed credentials via env vars. |
| 401 even with a valid PAT | Check token scope. Azure Artifacts requires *Packaging: Read* at minimum. |
| Different result than `dotnet restore` | Confirm `NuGet.Config` discovery is the same. ToolHost honors `--configfile` and the standard discovery chain. |
