# Prepares a release ("go up one"): raises <Version> in Directory.Build.props by the given
# part and adds the release's entry at the top of CHANGELOG.md. Prints the new tag. The
# release workflow then commits "chore(release): X.Y.Z", tags vX.Y.Z and publishes.
#
#   pwsh .github/scripts/prepare-release.ps1 -Part patch     # 1.0.0 -> 1.0.1
#   pwsh .github/scripts/prepare-release.ps1 -Part minor     # 1.0.1 -> 1.1.0
#   pwsh .github/scripts/prepare-release.ps1 -Part major     # 1.1.0 -> 2.0.0

param(
    [Parameter(Mandatory)][ValidateSet('patch', 'minor', 'major')][string]$Part
)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..' '..')
$props = Join-Path $repo 'Directory.Build.props'

$text = Get-Content $props -Raw
$match = [regex]::Match($text, '<Version>(\d+)\.(\d+)\.(\d+)</Version>')
if (-not $match.Success) { throw 'No <Version>X.Y.Z</Version> in Directory.Build.props.' }
[int]$major = $match.Groups[1].Value
[int]$minor = $match.Groups[2].Value
[int]$patch = $match.Groups[3].Value
switch ($Part) {
    'major' { $major++; $minor = 0; $patch = 0 }
    'minor' { $minor++; $patch = 0 }
    'patch' { $patch++ }
}
$version = "$major.$minor.$patch"
$tag = "v$version"
if (git -C $repo rev-parse -q --verify "refs/tags/$tag") { throw "Tag $tag already exists." }

$text = $text.Remove($match.Index, $match.Length).Insert($match.Index, "<Version>$version</Version>")
[IO.File]::WriteAllText($props, $text)

# The changelog entry: the same sections as the release notes, for the commits since the
# last tag (the tag doesn't exist yet, so release-notes.ps1 works in preview mode).
$entryFile = Join-Path ([IO.Path]::GetTempPath()) "changelog-$tag.md"
& (Join-Path $PSScriptRoot 'release-notes.ps1') -Tag $tag -Out $entryFile -Changelog | Out-Null
$entry = (Get-Content $entryFile -Raw -Encoding utf8).TrimEnd()
Remove-Item $entryFile

$changelog = Join-Path $repo 'CHANGELOG.md'
$header = "# Changelog`n`nWhat changed in each release. Written by the release workflow from the commit`nmessages and the GitHub issues closed in between; see docs/RELEASING.md.`n"
$rest = if (Test-Path $changelog) {
    ((Get-Content $changelog -Raw -Encoding utf8) -replace '(?sm)\A# Changelog.*?(?=^## )', '').TrimStart()
} else { '' }
$new = "$header`n$entry`n"
if ($rest) { $new += "`n$rest" }
[IO.File]::WriteAllText($changelog, $new.TrimEnd() + "`n")

Write-Host "Version $($match.Groups[1].Value).$($match.Groups[2].Value).$($match.Groups[3].Value) -> $version"
$tag
