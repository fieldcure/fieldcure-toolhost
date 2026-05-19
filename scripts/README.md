# Scripts — Maintainers Only

## Lock-step versioning rule

`FieldCure.ToolHost.Cli` is `PackAsTool=true`. It embeds the library DLL inside
the tool nupkg and **does not advertise `FieldCure.ToolHost` as a NuGet
dependency** — to NuGet, the two packages look independent. To keep them
trivially auditable, every release bumps both `<Version>` properties to the
same number, even when only one project has code changes.

`publish-nuget.ps1` (the default path) builds and pushes both together, so the
lock-step is automatic. Use `publish-toolhost.ps1` / `publish-cli.ps1` (single
publish) only for prereleases or hotfixes — and bump the partner csproj
`<Version>` in the same commit so the next regular release stays aligned.

See `CONTRIBUTING.md` → "Versioning policy" for the rationale.

## Publish Scripts

| Script | Packages |
|--------|----------|
| `publish-toolhost.ps1` | `FieldCure.ToolHost` only |
| `publish-cli.ps1` | `FieldCure.ToolHost.Cli` only |
| `publish-nuget.ps1` | Both (library + CLI) |

```powershell
# Single package
.\scripts\publish-toolhost.ps1                # pack → sign → push
.\scripts\publish-cli.ps1                     # pack → sign → push

# All at once
.\scripts\publish-nuget.ps1                   # pack → sign → push
.\scripts\publish-nuget.ps1 -SkipPush         # pack → sign only
.\scripts\publish-nuget.ps1 -SkipSign -SkipPush  # pack only (testing)
```

All scripts accept `-SkipSign`, `-SkipPush`, and `-NuGetApiKey` parameters.

## Prereleases

`publish-toolhost.ps1` and `publish-cli.ps1` accept `-PackageVersion` for one-off
prerelease nupkgs without committing a csproj `<Version>` change:

```powershell
.\scripts\publish-toolhost.ps1 -PackageVersion '0.2.0-preview.1'
.\scripts\publish-cli.ps1     -PackageVersion '0.2.0-preview.1'
```

`publish-cli.ps1` automatically pins the `FieldCure.ToolHost` nuspec dep back to
the latest published library version (currently `0.1.0`) — otherwise the
`/p:PackageVersion` override would propagate through the ProjectReference and
the resulting Cli prerelease nupkg would reference a `0.2.0-preview.1` library
that nuget.org doesn't have. Update the override in `publish-cli.ps1`
(`$depOverrides`) whenever the published library version changes.

## Prerequisites

- GlobalSign EV code signing USB dongle connected
- NuGet.org API Key ([nuget.org/account/apikeys](https://www.nuget.org/account/apikeys))
- Alternatively, set `$env:NUGET_API_KEY` instead of passing `-NuGetApiKey`

## Signing Certificate

- **Issuer**: GlobalSign
- **Subject**: Fieldcure Co., Ltd.
- **Method**: USB token (EV Code Signing)
- **Timestamp**: GlobalSign TSA
- **Fingerprint** (hardcoded in `nuget-common.ps1`): `FB343073EF0D477E64595A66FFB87AC631278C4B43D2CC89C56BCDF3B5BF8826`

## Output

Built `.nupkg` files are placed in the `artifacts/` folder.
