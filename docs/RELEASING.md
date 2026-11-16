# Releasing Edifier MR Volume

The process follows [cash](https://github.com/tomcoolpxl/cash): commits follow Conventional
Commits, a release is one version-bump commit plus a tag, and the tag's workflow builds the
app, writes the notes from the commits and publishes the GitHub release.

Versions follow [Semantic Versioning](https://semver.org/): **major** for something that
breaks how the app is used, **minor** for a new feature or supported speaker, **patch** for
fixes.

## Commit messages

Commit subjects follow [Conventional Commits](https://www.conventionalcommits.org/), because
the release notes are built from them:

```text
type(scope): description
```

| Type | Use it for | Release notes section |
| --- | --- | --- |
| `feat` | a new behaviour, setting or supported speaker | Features |
| `fix` | a bug fix | Bug fixes |
| `perf` | faster or lighter, no change in behaviour | Performance |
| `docs` | README, protocol notes, this file | Documentation |
| `refactor` | restructuring with no change in behaviour | Refactoring |
| `test`, `ci`, `build`, `chore`, `style` | tests, workflows, the build, releases, formatting | Maintenance |

- The **scope** is optional and names the area: `ble`, `protocol`, `tray`, `flyout`,
  `settings`, `install`, `probe`. It shows in bold in the notes.
- The **description** is written for someone reading the release notes, and kept exactly
  as written, so start it lowercase: `fix(ble): ride out short disconnect reports`.
- `!` before the colon (`feat(settings)!: ...`) also lists the commit under **Breaking
  changes**; say what breaks in the body.
- A subject without a known type still appears, under **Other**.

A commit taken back by a later one before a release would still be listed; put its hash in
[`.github/release-notes-skip.txt`](../.github/release-notes-skip.txt) with `#` and why.
A release that needs a few words of its own goes in `.github/release-notes/vX.Y.Z.md`; the
notes open with it.

Preview the next release's notes before tagging:

```powershell
pwsh .github/scripts/release-notes.ps1 -Tag vNEXT -Out release-notes.md
```

## Making a release: one click

On GitHub: **Actions > Release > Run workflow**, on `main`, and pick which part of the version
goes up one: **patch** (fixes), **minor** (features, a new speaker), **major** (breaking).
[`.github/workflows/release.yml`](../.github/workflows/release.yml) then:

1. Runs CI on the commit `main` is at: tests, app build, smoke launch, installer
   install/uninstall.
2. Raises `<Version>` in [`Directory.Build.props`](../Directory.Build.props) (the only place
   the version lives) and adds the release's entry at the top of [`CHANGELOG.md`](../CHANGELOG.md)
   ([`prepare-release.ps1`](../.github/scripts/prepare-release.ps1)).
3. Commits that as `chore(release): X.Y.Z` to `main`, tags `vX.Y.Z` and pushes both. If
   `main` moved on while it ran, the push fails and nothing is published.
4. Builds the assets with [`tools/package.ps1`](../tools/package.ps1): the self-contained zip,
   the per-user installer (Inno Setup, [`packaging/inno`](../packaging/inno)) and a `.sha256`
   for each.
5. Writes the notes: the commits since the previous tag by type, the GitHub issues closed (as
   completed) since then, and the install instructions; GitHub appends its list of merged
   pull requests.
6. Publishes the GitHub release with the four files attached.

Preview the next release's notes and changelog entry locally first:

```powershell
pwsh .github/scripts/release-notes.ps1 -Tag vNEXT -Out release-notes.md
```

### By hand

Pushing a `vX.Y.Z` tag yourself also works (bump `<Version>` first, it must match); it builds
and publishes that tag but writes no changelog entry. A tag with a `-` (`v1.1.0-rc.1`) makes
a pre-release.

### Locally

`.\tools\package.ps1` builds the same four files into `artifacts\` (needs Inno Setup 6:
`scoop install inno-setup` or the official installer).

Bug reports and feature requests from users come in through the GitHub issue forms; their
closed issues show up in the release notes.
