<#
.SYNOPSIS
    Builds the release assets into artifacts\: the portable zip, the per-user installer, and a
    .sha256 file for each.

.DESCRIPTION
    1. dotnet publish: self-contained (.NET and the Windows App SDK runtime included).
    2. edifier-mr-volume-<tag>-win-x64.zip with README.md and LICENSE beside the exe.
    3. edifier-mr-volume-<tag>-win-x64-setup.exe from packaging\inno (Inno Setup 6's ISCC).
    4. "<sha256>  <file>" in <file>.sha256 for each, the format Scoop's autoupdate reads.
    Used by CI and the release workflow; runs the same locally.

.EXAMPLE
    .\tools\package.ps1                 # version from Directory.Build.props
    .\tools\package.ps1 -Tag v1.1.0-rc.1
#>
[CmdletBinding()]
param(
    [string]$Tag,
    [ValidateSet('x64')]
    [string]$Platform = 'x64'
)
$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content (Join-Path $repo 'Directory.Build.props'))).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
if (-not $Tag) { $Tag = "v$version" }
if ((($Tag -replace '^v', '') -split '-')[0] -ne $version) {
    throw "Tag $Tag does not match <Version>$version</Version> in Directory.Build.props."
}

$rid = 'win-x64'
$artifacts = Join-Path $repo 'artifacts'
$publish = Join-Path $artifacts "publish\$rid"
$base = "edifier-mr-volume-$Tag-$rid"

Write-Host "Publishing $rid $version..."
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $repo 'src\EdifierMrVolume\EdifierMrVolume.csproj') -c Release `
    -p:Platform=$Platform -r $rid --self-contained true -o $publish -nologo -v:q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }
Copy-Item (Join-Path $repo 'README.md'), (Join-Path $repo 'LICENSE') $publish

function Write-Checksum([string]$file) {
    $hash = (Get-FileHash -Path $file -Algorithm SHA256).Hash.ToLower()
    "$hash  $(Split-Path $file -Leaf)" | Out-File -FilePath "$file.sha256" -Encoding ascii
    Write-Host "  $(Split-Path $file -Leaf)  $([math]::Round((Get-Item $file).Length / 1MB)) MB  sha256 $hash"
}

$zip = Join-Path $artifacts "$base.zip"
Remove-Item $zip, "$zip.sha256" -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip
Write-Checksum $zip

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { $iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source }
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) is needed for the installer.' }
& $iscc /Q "/DAppVersion=$version" "/DFileTag=$Tag" "/DSourceDir=$publish" "/O$artifacts" `
    (Join-Path $repo 'packaging\inno\edifier-mr-volume.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed (exit $LASTEXITCODE)." }
$setup = Join-Path $artifacts "$base-setup.exe"
if (-not (Test-Path $setup)) { throw "No $setup was written." }
Write-Checksum $setup
