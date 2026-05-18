<#
.SYNOPSIS
    Pack, sign, and push FieldCure.ToolHost.Cli (fcdnx) NuGet package.
.EXAMPLE
    .\publish-cli.ps1                                            # full: pack → sign → push
    .\publish-cli.ps1 -SkipPush                                  # pack → sign only
    .\publish-cli.ps1 -SkipSign -SkipPush                        # pack only (testing)
    .\publish-cli.ps1 -PackageVersion '0.2.0-preview.1'          # publish prerelease nupkg
                                                                 # (csproj <Version> stays as-is)
#>
param(
    [switch]$SkipSign,
    [switch]$SkipPush,
    [string]$NuGetApiKey,
    [string]$PackageVersion
)

. "$PSScriptRoot\nuget-common.ps1"

# When publishing a prerelease via -PackageVersion, MSBuild's /p:PackageVersion
# override propagates to the Cli's transitive ProjectReference on FieldCure.ToolHost
# — making the resulting nuspec say ToolHost dep = "<same prerelease>", which
# doesn't exist on nuget.org. Pin the dep back to the published library version
# so the prerelease Cli nupkg's dep chain resolves cleanly on consumer install.
$depOverrides = @{}
if ($PackageVersion) {
    $depOverrides = @{
        'FieldCure.ToolHost' = '0.1.0'
    }
}

Invoke-NuGetPublish `
    -Projects @(
        'src\FieldCure.ToolHost.Cli\FieldCure.ToolHost.Cli.csproj'
    ) `
    -SkipSign:$SkipSign `
    -SkipPush:$SkipPush `
    -NuGetApiKey $NuGetApiKey `
    -PackageVersion $PackageVersion `
    -DependencyOverrides $depOverrides
