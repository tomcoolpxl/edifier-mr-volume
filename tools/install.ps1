<#
.SYNOPSIS
    Publishes a self-contained Release build and installs it for the current user.

.DESCRIPTION
    - Publishes to artifacts\publish\<rid> (.NET and the Windows App SDK runtime included,
      so nothing else needs to be installed).
    - Stops a running Mr45Volume, mirrors the build into the install folder
      (default %LOCALAPPDATA%\Programs\Mr45Volume) and adds a Start menu shortcut.
    - Starts the installed copy unless -NoStart is given.
    Rerun it after pulling changes to update the installed app in place.

.EXAMPLE
    .\tools\install.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }),
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\Mr45Volume'),
    [switch]$NoStart
)
$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent
$rid = if ($Platform -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
$publishDir = Join-Path $repo "artifacts\publish\$rid"
$project = Join-Path $repo 'src\Mr45Volume\Mr45Volume.csproj'

Write-Host "Publishing $rid (Release, self-contained)..."
dotnet publish $project -c Release -p:Platform=$Platform -r $rid --self-contained true -o $publishDir -nologo -v:q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

$running = @(Get-Process Mr45Volume -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    # Ask it to exit cleanly so it releases the speaker's Bluetooth link right away;
    # a killed process leaves Windows holding the link for ~20 s.
    Write-Host 'Closing the running Mr45Volume...'
    try {
        $signal = [System.Threading.EventWaitHandle]::OpenExisting('Local\Mr45Volume.Exit')
        [void]$signal.Set()
        $signal.Dispose()
    }
    catch {
        Write-Host '  (running copy has no exit signal; it will be stopped)'
    }
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
    $left = @($running | Where-Object { -not $_.HasExited })
    if ($left.Count -gt 0) {
        $left | Stop-Process -Force
        $left | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
    }
}

Write-Host "Installing to $InstallDir..."
New-Item -ItemType Directory -Force $InstallDir | Out-Null
robocopy $publishDir $InstallDir /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copying files failed (robocopy exit $LASTEXITCODE)." }
$exe = Join-Path $InstallDir 'Mr45Volume.exe'

$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'MR4.5 Volume.lnk'
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.WorkingDirectory = $InstallDir
$link.Description = 'Edifier MR4.5 hardware volume in the system tray'
$link.Save()
Write-Host "Start menu shortcut: $shortcut"

$runValue = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name Mr45Volume -ErrorAction SilentlyContinue).Mr45Volume
if ($runValue -and $runValue -notlike "*$exe*") {
    Write-Warning "Start with Windows points at $runValue. Untick and tick it again in the tray menu to use the installed copy."
}

if (-not $NoStart) {
    Start-Process -FilePath $exe -WorkingDirectory $InstallDir
    Write-Host 'Started.'
}
Write-Host 'Done.'
exit 0
