<#
.SYNOPSIS
    Shared NuGet pack/sign/push logic. Dot-sourced by publish scripts.
.DESCRIPTION
    Provides Invoke-NuGetPublish function that handles:
    1. dotnet pack (Release)
    2. nuget sign with GlobalSign EV certificate
    3. nuget push to nuget.org
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Shared Config ---
$script:RepoRoot = Split-Path $PSScriptRoot -Parent
$script:CertFingerprint = 'FB343073EF0D477E64595A66FFB87AC631278C4B43D2CC89C56BCDF3B5BF8826'
$script:TimestampUrl = 'http://timestamp.globalsign.com/tsa/r6advanced1'
$script:NuGetSource = 'https://api.nuget.org/v3/index.json'

function Set-NupkgDependencyVersion {
    <#
    .SYNOPSIS
        Rewrites a single <dependency id="X" version="..."/> entry inside a
        nupkg's top-level .nuspec, in place.
    .DESCRIPTION
        Workaround for an MSBuild quirk: a /p:PackageVersion override at
        `dotnet pack` time propagates to every project in the build, including
        transitive ProjectReferences. So when we pack the Cli with /p:PackageVersion
        =0.2.0-preview.X to publish a prerelease, the resulting Cli.nuspec
        records the ToolHost dep as "0.2.0-preview.X" too — even though we have
        no intention of publishing the library at that version. This helper lets
        a publish script pin the dep back to whatever is actually published on
        nuget.org. Must be called BEFORE Authenticode signing; signing covers
        the modified nuspec and consumers validate against the post-edit content.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$NupkgPath,
        [Parameter(Mandatory)][string]$DependencyId,
        [Parameter(Mandatory)][string]$NewVersion
    )

    Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
    $archive = [System.IO.Compression.ZipFile]::Open($NupkgPath, [System.IO.Compression.ZipArchiveMode]::Update)
    try {
        $nuspecEntry = $archive.Entries |
            Where-Object { $_.FullName -like '*.nuspec' -and -not $_.FullName.Contains('/') } |
            Select-Object -First 1
        if (-not $nuspecEntry) {
            throw "No top-level .nuspec found in $NupkgPath"
        }

        $entryStream = $nuspecEntry.Open()
        $reader = New-Object System.IO.StreamReader($entryStream, [System.Text.Encoding]::UTF8)
        $content = $reader.ReadToEnd()
        $reader.Close()
        $entryStream.Close()

        $idEsc = [regex]::Escape($DependencyId)
        $pattern = "(<dependency\s+id=`"$idEsc`"[^>]*\s+version=`")[^`"]+(`")"
        $modified = [regex]::Replace($content, $pattern, "`${1}$NewVersion`${2}")

        if ($modified -eq $content) {
            return $false  # dep not present in this nupkg — silently skip
        }

        $entryStream = $nuspecEntry.Open()
        $entryStream.SetLength(0)
        $writer = New-Object System.IO.StreamWriter($entryStream, [System.Text.Encoding]::UTF8)
        $writer.Write($modified)
        $writer.Flush()
        $writer.Close()
        $entryStream.Close()
        return $true
    } finally {
        $archive.Dispose()
    }
}

function Invoke-NuGetPublish {
    param(
        [string[]]$Projects,
        [switch]$SkipSign,
        [switch]$SkipPush,
        [string]$NuGetApiKey,
        [string]$PackageVersion,  # Optional override; lets the publish script
                                  # produce a prerelease nupkg (e.g.
                                  # 0.2.0-preview.1) without committing a csproj
                                  # <Version> change. When non-empty, passed to
                                  # dotnet pack as /p:PackageVersion=...
        [hashtable]$DependencyOverrides = @{}  # Optional; { 'PackageId' = 'X.Y.Z' }
                                               # entries are applied in-place to
                                               # each packed nuspec before signing.
                                               # See Set-NupkgDependencyVersion.
    )

    $OutputDir = Join-Path $script:RepoRoot 'artifacts'

    # --- Clean artifacts ---
    if (Test-Path $OutputDir) {
        Remove-Item $OutputDir -Recurse -Force
    }
    New-Item $OutputDir -ItemType Directory | Out-Null

    # --- 1. Clean + Pack ---
    Write-Host "`n=== Packing (clean build) ===" -ForegroundColor Cyan
    foreach ($proj in $Projects) {
        $projPath = Join-Path $script:RepoRoot $proj
        Write-Host "  Cleaning $proj ..."
        dotnet clean $projPath -c Release --nologo -v q
        Write-Host "  Packing $proj ..."
        $packArgs = @($projPath, '-c', 'Release', '-o', $OutputDir)
        if ($PackageVersion) {
            Write-Host "    (PackageVersion override: $PackageVersion)"
            $packArgs += "/p:PackageVersion=$PackageVersion"
        }
        dotnet pack @packArgs
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Pack failed for $proj"
        }
    }

    $packages = Get-ChildItem $OutputDir -Filter '*.nupkg'
    Write-Host "`n  Packages created:" -ForegroundColor Green
    $packages | ForEach-Object { Write-Host "    $_" }

    # --- 1b. Patch dependency versions (must run BEFORE signing) ---
    if ($DependencyOverrides.Count -gt 0) {
        Write-Host "`n=== Patching nuspec dependencies ===" -ForegroundColor Cyan
        foreach ($pkg in $packages) {
            foreach ($depId in $DependencyOverrides.Keys) {
                $newVer = $DependencyOverrides[$depId]
                $patched = Set-NupkgDependencyVersion -NupkgPath $pkg.FullName `
                    -DependencyId $depId -NewVersion $newVer
                if ($patched) {
                    Write-Host "  $($pkg.Name): $depId -> $newVer"
                }
            }
        }
    }

    # --- 2. Sign ---
    if (-not $SkipSign) {
        Write-Host "`n=== Signing (USB dongle must be plugged in) ===" -ForegroundColor Cyan

        $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object {
            $_.Subject -like '*Fieldcure Co., Ltd.*' -and $_.NotAfter -gt (Get-Date)
        }
        if (-not $cert) {
            Write-Error "Fieldcure code signing certificate not found. Is the USB dongle plugged in?"
        }
        Write-Host "  Using certificate: $($cert.Subject)"

        foreach ($pkg in $packages) {
            Write-Host "  Signing $($pkg.Name) ..."
            dotnet nuget sign $pkg.FullName `
                --certificate-fingerprint $script:CertFingerprint `
                --timestamper $script:TimestampUrl `
                --overwrite
            if ($LASTEXITCODE -ne 0) {
                Write-Error "Signing failed for $($pkg.Name)"
            }
        }
        Write-Host "  All packages signed." -ForegroundColor Green
    }

    # --- 3. Push ---
    if (-not $SkipPush) {
        Write-Host "`n=== Pushing to nuget.org ===" -ForegroundColor Cyan

        if (-not $NuGetApiKey) {
            $NuGetApiKey = $env:NUGET_API_KEY
        }
        if (-not $NuGetApiKey) {
            Write-Error "NuGet API key required. Pass -NuGetApiKey or set NUGET_API_KEY env var."
        }

        foreach ($pkg in $packages) {
            Write-Host "  Pushing $($pkg.Name) ..."
            dotnet nuget push $pkg.FullName `
                --api-key $NuGetApiKey `
                --source $script:NuGetSource `
                --skip-duplicate
            if ($LASTEXITCODE -ne 0) {
                Write-Error "Push failed for $($pkg.Name)"
            }
        }
        Write-Host "  All packages pushed." -ForegroundColor Green
    }

    Write-Host "`n=== Done ===" -ForegroundColor Cyan
    Get-ChildItem $OutputDir -Filter '*.nupkg' | ForEach-Object {
        Write-Host "  $($_.Name)  ($([math]::Round($_.Length / 1KB, 1)) KB)"
    }
}
