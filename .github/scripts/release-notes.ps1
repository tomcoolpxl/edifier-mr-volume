# Writes a release's notes from its commits, grouped by Conventional Commits type.
# Adapted from cash (github.com/tomcoolpxl/cash, .github/scripts/release-notes.ps1).
#
#   pwsh .github/scripts/release-notes.ps1 -Tag v1.0.0 [-Out release-notes.md]
#
# The range runs from the tag before -Tag (or before HEAD, if -Tag does not exist yet,
# which is how to preview the next release) to -Tag. A subject such as
# "fix(ble): ride out short disconnect reports" lands under "Bug fixes" as
# "**ble:** ride out short disconnect reports"; a "!" before the colon also lists it under
# "Breaking changes". Subjects without a known type go under "Other", so history from
# before the convention still appears. A commit a later one took back is left out when its
# hash is in .github/release-notes-skip.txt. A release's own opening lines, when it has any,
# are .github/release-notes/<tag>.md. See docs/RELEASING.md, "Commit messages".

param(
    [Parameter(Mandatory)][string]$Tag,
    [string]$Out = 'release-notes.md'
)

$ErrorActionPreference = 'Stop'

if (git rev-parse -q --verify "refs/tags/$Tag") {
    # A release: from the tag before this one.
    $head = $Tag
    $previous = git describe --tags --abbrev=0 "$Tag^" 2>$null
} else {
    # A preview: everything since the latest tag, which HEAD itself may carry.
    $head = 'HEAD'
    $previous = git describe --tags --abbrev=0 HEAD 2>$null
}
if ($LASTEXITCODE -ne 0 -or -not $previous) {
    $range = $head
    $title = '## Changes'
} else {
    $range = "$previous..$head"
    $title = "## Changes since $previous"
}

# Section order, and the types that feed each section.
$sections = [ordered]@{
    'Features'      = @('feat')
    'Bug fixes'     = @('fix')
    'Performance'   = @('perf')
    'Documentation' = @('docs')
    'Refactoring'   = @('refactor')
    'Maintenance'   = @('chore', 'ci', 'test', 'build', 'style')
}
$sectionOf = @{}
foreach ($name in $sections.Keys) {
    foreach ($type in $sections[$name]) { $sectionOf[$type] = $name }
}

$entries = [ordered]@{ 'Breaking changes' = @() }
foreach ($name in $sections.Keys) { $entries[$name] = @() }
$entries['Other'] = @()

# Commits taken back by a later one: a hash (or its start) per line, then `#` and why.
$skipFile = Join-Path $PSScriptRoot '..' 'release-notes-skip.txt'
$skip = @()
if (Test-Path $skipFile) {
    $skip = Get-Content $skipFile |
        ForEach-Object { ($_ -replace '#.*$', '').Trim() } |
        Where-Object { $_ }
}

$pattern = '^(?<type>[a-z]+)(?:\((?<scope>[^)]+)\))?(?<bang>!)?:\s*(?<desc>.+)$'
$log = git log --no-merges --reverse --format='%H%x09%h%x09%s' $range
foreach ($line in $log) {
    if (-not $line) { continue }
    $full, $hash, $subject = $line -split "`t", 3
    if ($skip | Where-Object { $full.StartsWith($_) }) { continue }
    $match = [regex]::Match($subject, $pattern)
    if ($match.Success -and $sectionOf.ContainsKey($match.Groups['type'].Value)) {
        # Kept as written: a description often starts with a name that capitalising would change.
        $desc = $match.Groups['desc'].Value
        $scope = $match.Groups['scope'].Value
        $text = if ($scope) { "**${scope}:** $desc" } else { $desc }
        $entry = "- $text ($hash)"
        $entries[$sectionOf[$match.Groups['type'].Value]] += $entry
        if ($match.Groups['bang'].Success) { $entries['Breaking changes'] += $entry }
    } else {
        $entries['Other'] += "- $subject ($hash)"
    }
}

# A release may open with a few lines of its own, written by hand: what it is about,
# before the commits. .github/release-notes/<tag>.md, when there is one.
$body = @()
$preface = Join-Path $PSScriptRoot '..' 'release-notes' "$Tag.md"
if (Test-Path $preface) {
    $body += Get-Content $preface -Encoding utf8
    $body += ''
}
$body += @($title, '')
$commitsFrom = $body.Count
foreach ($name in $entries.Keys) {
    if ($entries[$name].Count -eq 0) { continue }
    $body += "### $name"
    $body += ''
    $body += $entries[$name]
    $body += ''
}
if ($body.Count -eq $commitsFrom) { $body += @('- No changes recorded.', '') }

# How to install, under every release's notes.
$body += @(
    '## Install',
    '',
    "- **Portable**: unpack ``edifier-mr-volume-$Tag-win-x64.zip`` anywhere and run ``EdifierMrVolume.exe``. The folder is self-contained: .NET and the Windows App SDK runtime are included.",
    '- **From source**: clone the repository and run `tools\install.ps1`, which installs per user under `%LOCALAPPDATA%\Programs\EdifierMrVolume` and adds a Start menu shortcut.',
    '',
    "Windows 10 2004 or later, x64. Unsigned, so Windows SmartScreen may warn once. The ``.sha256`` file holds the zip's checksum.",
    ''
)

$body | Set-Content -Path $Out -Encoding utf8
Get-Content $Out
