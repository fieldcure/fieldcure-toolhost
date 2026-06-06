# Release Notes — FieldCure.ToolHost

## v0.1.6 (2026-06-06)

### Added

- **Opt-in child-process environment isolation.** `ToolInvocationRequest.InheritEnvironmentVariables` and `LaunchRequest.InheritEnvironmentVariables` now default to `true` for `dnx` compatibility, but embedders can set them to `false` when launching untrusted tools or MCP servers that should only receive explicit environment values. `ToolEnvironment.GetDefaultEnvironmentVariables()` returns a curated platform baseline (`PATH`, home/profile, temp, and system-directory variables) for hosts that disable full inheritance but still need normal process startup behavior.

## v0.1.5 (2026-05-23)

### Added

- **`ToolCacheIndexStore.EvictAsync(packageId, ct)`** — first-class API for removing one package's pinned state from `_index.json` so the next resolve falls through to a fresh NuGet metadata query regardless of the `CachedWithRefresh` TTL. Idempotent (missing entries return `false` without throwing) and case-insensitive on the package id. Intended for host UIs that expose a manual "update to latest" action — clearing a single pin lets users bypass the 24h cache wait without losing the rest of the cache or flushing the entire index. Previously the same effect required hosts to call `UpdateAsync` with a mutator that knew the schema details (lowercase keys, `OrdinalIgnoreCase` dictionary, `ToolPackageState`, `SchemaVersion`); `EvictAsync` encapsulates that.

## v0.1.4 (2026-05-19)

### Fixed

- **Runtime TFM selection now follows the installed host runtime.** `NuGetToolExtractor` no longer assumes `net10.0` when selecting `tools/{tfm}/{rid}`. It derives the host framework from the highest installed `Microsoft.NETCore.App` runtime reported by `DotnetEnvironment`, falling back to the current process runtime only if runtime detection data is unavailable.
- **Cache hits honor the current version request.** `CachedOnly` and `CachedWithRefresh` no longer return a pinned prerelease when prerelease is disallowed, or a pinned version outside the requested `VersionConstraint`.
- **Downloads reuse the supplied NuGet.Config.** When the CLI is invoked with `--configfile`, the extractor now loads the same config file for package download that the resolver used for metadata resolution.

## v0.1.3 (2026-05-19)

### Fixed

- **`RestrictToSource` and `AdditionalSources` were silently broken.** `NuGetPackageResolver.BuildSources` passed the `PackageSource` constructor's `(string source, string name)` arguments in reverse — the literal strings `"restricted"` and `"additional-N"` ended up in the *URL* slot, producing `SourceRepository` instances backed by invalid endpoints. `MetadataResource.GetVersions(...)` returned empty version lists, surfacing as `PackageNotFoundException` even for packages that exist and are listed on the configured feed. Any caller setting either option in v0.1.0–0.1.2 should upgrade.
- **`NuGetToolExtractor` fallback source** — the same `(name, source)` ordering bug was present on the fallback path used when the resolved source URL is not among the user's configured NuGet sources. This path runs whenever resolution succeeded via `AdditionalSources` or `RestrictToSource` against a feed the extractor cannot see in `NuGet.Config` — common for embedders that bootstrap their own source list on fresh installs without a user-level `NuGet.Config`.

Argument order is now `new PackageSource(url, name)` matching the NuGet API. No public API change; one-line fix on each of three sites.

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
