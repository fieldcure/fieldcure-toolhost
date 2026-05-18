<#
.SYNOPSIS
    Pack, sign, and push BOTH FieldCure.ToolHost packages.
.EXAMPLE
    .\publish-nuget.ps1                      # full: pack → sign → push
    .\publish-nuget.ps1 -SkipPush            # pack → sign only
    .\publish-nuget.ps1 -SkipSign -SkipPush  # pack only (testing)
#>
param(
    [switch]$SkipSign,
    [switch]$SkipPush,
    [string]$NuGetApiKey
)

. "$PSScriptRoot\nuget-common.ps1"

Invoke-NuGetPublish `
    -Projects @(
        'src\FieldCure.ToolHost\FieldCure.ToolHost.csproj',
        'src\FieldCure.ToolHost.Cli\FieldCure.ToolHost.Cli.csproj'
    ) `
    -SkipSign:$SkipSign `
    -SkipPush:$SkipPush `
    -NuGetApiKey $NuGetApiKey
