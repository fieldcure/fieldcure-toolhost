<#
.SYNOPSIS
    Pack, sign, and push FieldCure.ToolHost (library) NuGet package.
.EXAMPLE
    .\publish-toolhost.ps1                                       # full: pack → sign → push
    .\publish-toolhost.ps1 -SkipPush                             # pack → sign only
    .\publish-toolhost.ps1 -SkipSign -SkipPush                   # pack only (testing)
    .\publish-toolhost.ps1 -PackageVersion '0.2.0-preview.1'     # publish prerelease nupkg
                                                                 # (csproj <Version> stays as-is)
#>
param(
    [switch]$SkipSign,
    [switch]$SkipPush,
    [string]$NuGetApiKey,
    [string]$PackageVersion
)

. "$PSScriptRoot\nuget-common.ps1"

Invoke-NuGetPublish `
    -Projects @(
        'src\FieldCure.ToolHost\FieldCure.ToolHost.csproj'
    ) `
    -SkipSign:$SkipSign `
    -SkipPush:$SkipPush `
    -NuGetApiKey $NuGetApiKey `
    -PackageVersion $PackageVersion
