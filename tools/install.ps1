<#
.SYNOPSIS
    Publishes a self-contained Release build and installs it for the current user.

.DESCRIPTION
    - Publishes to artifacts\publish\<rid> (.NET and the Windows App SDK runtime included,
      so nothing else needs to be installed).
    - Closes a running copy, mirrors the build into the install folder
      (default %LOCALAPPDATA%\Programs\EdifierMrVolume) and adds a Start menu shortcut.
    - Migrates an install from before the rename (Mr45Volume): closes it, removes its folder,
      shortcut and log, and carries its Start with Windows setting over.
    - Starts the installed copy unless -NoStart is given.
    Rerun it after pulling changes to update the installed app in place.

.EXAMPLE
    .\tools\install.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }),
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\EdifierMrVolume'),
    [switch]$NoStart
)
$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent
$rid = if ($Platform -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
$publishDir = Join-Path $repo "artifacts\publish\$rid"
$project = Join-Path $repo 'src\EdifierMrVolume\EdifierMrVolume.csproj'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startMenu = [Environment]::GetFolderPath('Programs')

# Asks a running copy to exit cleanly so it releases the speaker's Bluetooth link right away
# (a killed process leaves Windows holding the link for ~20 s); kills it if it doesn't.
function Close-App([string]$ProcessName) {
    $running = @(Get-Process $ProcessName -ErrorAction SilentlyContinue)
    if ($running.Count -eq 0) { return }
    Write-Host "Closing the running $ProcessName..."
    try {
        $signal = [System.Threading.EventWaitHandle]::OpenExisting("Local\$ProcessName.Exit")
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

# Native AOT links with Visual Studio's C++ tools and finds them with vswhere.exe, which isn't
# always on PATH.
$vswhereDir = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (-not (Get-Command vswhere.exe -ErrorAction SilentlyContinue) -and (Test-Path (Join-Path $vswhereDir 'vswhere.exe'))) {
    $env:PATH = "$env:PATH;$vswhereDir"
}
Write-Host "Publishing $rid (Release, self-contained)..."
dotnet publish $project -c Release -p:Platform=$Platform -r $rid --self-contained true -o $publishDir -nologo -v:q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

Close-App 'EdifierMrVolume'

# --- Migrate the install from before the rename (Mr45Volume / "MR4.5 Volume") ---
Close-App 'Mr45Volume'
$oldRun = (Get-ItemProperty $runKey -Name Mr45Volume -ErrorAction SilentlyContinue).Mr45Volume
$keepAutostart = [bool]$oldRun
if ($oldRun) { Remove-ItemProperty $runKey -Name Mr45Volume }
foreach ($old in (Join-Path $env:LOCALAPPDATA 'Programs\Mr45Volume'),
                 (Join-Path $env:LOCALAPPDATA 'Mr45Volume'),
                 (Join-Path $startMenu 'MR4.5 Volume.lnk')) {
    if (Test-Path $old) {
        Remove-Item $old -Recurse -Force
        Write-Host "Removed old install item: $old"
    }
}

Write-Host "Installing to $InstallDir..."
New-Item -ItemType Directory -Force $InstallDir | Out-Null
robocopy $publishDir $InstallDir /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copying files failed (robocopy exit $LASTEXITCODE)." }
$exe = Join-Path $InstallDir 'EdifierMrVolume.exe'

$shortcut = Join-Path $startMenu 'Edifier MR Volume.lnk'
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.WorkingDirectory = $InstallDir
$link.Description = 'Edifier MR studio monitor volume in the notification area'
$link.Save()
Write-Host "Start menu shortcut: $shortcut"

if ($keepAutostart) {
    Set-ItemProperty $runKey -Name EdifierMrVolume -Value "`"$exe`""
    Write-Host 'Start with Windows carried over from the old install.'
}
$runValue = (Get-ItemProperty $runKey -Name EdifierMrVolume -ErrorAction SilentlyContinue).EdifierMrVolume
if ($runValue -and $runValue -notlike "*$exe*") {
    Write-Warning "Start with Windows points at $runValue. Untick and tick it again in the tray menu to use the installed copy."
}

if (-not $NoStart) {
    Start-Process -FilePath $exe -WorkingDirectory $InstallDir
    Write-Host 'Started.'
}
Write-Host 'Done.'
exit 0
