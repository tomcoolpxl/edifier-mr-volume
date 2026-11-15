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
- `!` before the colon (`feat(settings)!: …`) also lists the commit under **Breaking
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

## Making a release

1. Make sure `main` is green in CI.
2. Raise `<Version>` in [`Directory.Build.props`](../Directory.Build.props), the only place
   the version lives.
3. Commit, tag and push:

   ```powershell
   git commit -am "chore(release): 1.1.0"
   git tag -a v1.1.0 -m "Edifier MR Volume 1.1.0"
   git push origin main
   git push origin v1.1.0
   ```

The tag starts [`.github/workflows/release.yml`](../.github/workflows/release.yml):

1. CI runs on the tagged commit: tests, app build, smoke launch.
2. The tag must match `<Version>`; otherwise the release stops.
3. `dotnet publish` builds the self-contained app; it is zipped as
   `edifier-mr-volume-vX.Y.Z-win-x64.zip`, with a `.sha256` file beside it.
4. The notes are written from the commits since the previous tag, and GitHub adds its list
   of merged pull requests.
5. The GitHub release is published with the zip and checksum attached.

A tag with a `-` (`v1.1.0-rc.1`) makes a pre-release. To rerun a release, start the
workflow by hand under **Actions › Release › Run workflow** and name the tag.

Feature ideas and bugs are tracked as GitHub issues (the **Feature request** and
**Bug report** forms).
