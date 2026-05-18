# Release Notes — FieldCure.ToolHost

## v0.1.2 (2026-05-18)

### Added

- **`ToolInvocationRequest.VersionConstraint`** — optional NuGet version range (e.g. `"2.*"`, `"[2.0.0,3.0.0)"`) is now forwarded to the underlying `PackageResolutionRequest`. Previously the field was absent and the runner pinned major versions only by passing a fully-qualified `ExplicitVersion`. Embedders can now express "latest within major 2" without pre-resolving themselves.
- **`ToolInvocationRequest.AdditionalEnvironment`** — optional `IReadOnlyDictionary<string, string?>` of environment variables for the child process is now forwarded to `LaunchRequest.AdditionalEnvironment`. Required for scenarios that hand credentials or other secrets to the launched tool via env vars (the same pattern used by stdio MCP servers).

Both fields are pure pass-through additions — backwards compatible. Existing callers that omit the new fields see identical behavior to v0.1.1.

## v0.1.1 (2026-05-18)

### Fixed

- **Package metadata** — `projectUrl` and `repository` URLs now point at the actual GitHub repository (`fieldcure/fieldcure-toolhost`) instead of the spec's draft path (`FieldCure/ToolHost`, which 404s). No code changes; nupkg contents byte-identical to v0.1.0 except the nuspec URLs.

## v0.1.0 (2026-05-18)

Initial release.

- **`DnxLiteRunner`** — primary orchestrator. Resolves a NuGet package id, ensures it is extracted into the standard NuGet global packages folder, and launches the tool with stdio redirected. Single execution path regardless of whether the .NET 10 SDK with `dnx` is installed.
- **`DotnetEnvironment.DetectAsync`** — probes installed SDKs/runtimes, the NuGet global packages folder, and the host RID. Used for diagnostics and warm-cache hints.
- **Three-tier version policy** — `AlwaysLatest`, `CachedWithRefresh` (24h TTL default), `CachedOnly`. Library default is `CachedWithRefresh` for cold-start-sensitive embedders.
- **Cache shared with `dotnet` / `dnx`** — installs land in the standard NuGet global packages folder (resolved via `SettingsUtility.GetGlobalPackagesFolder`). Our metadata sits in `%LOCALAPPDATA%/FieldCure/ToolHost/_index.json` and is written atomically.
- **`NuGetToolExtractor`** — uses `DownloadResource.GetDownloadResourceResultAsync` against the configured NuGet sources; picks the best-matching `tools/{tfm}/{rid}/` folder via `FrameworkReducer` with host-RID preference and `any`-RID fallback.
- **`ToolLauncher`** — managed (`dotnet exec`) and self-contained (direct invoke) runners; `DOTNET_ROLL_FORWARD=LatestMajor` when `AllowRollForward` is set.
- **`CredentialProviderSetup.Register`** — wires the standard NuGet credential plugin discovery (env vars, `~/.nuget/plugins`, dotnet CLI plugin folder). Idempotent.
- **`DotnetToolSettings.Parse`** — XML parser for the `DotnetToolSettings.xml` schema (managed and `executable` runners).
- **Targets** — `net8.0` and `net10.0` (both LTS).

### Known limitations (v0.1)

- Platform-specific tool packages (non-`any` RIDs) are best-effort — full RID-graph traversal lands in v0.2.
- Self-contained / NativeAOT tools (`Runner = "executable"`) supported by the launcher but extractor RID selection is best-effort.
