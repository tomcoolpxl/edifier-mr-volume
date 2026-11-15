# TODO

Open requests, in the owner's words where possible. Tick an item only when it is done and
verified; say how in DONE notes or the commit.

## Now

- [x] **App icon**: flat, abstract, vector line-style, in the Edifier MR4.5's own look and colours,
      without the Edifier name. Not in the taskbar (the tray keeps the volume number); shown in
      the settings/About part of the panel. *Done: `assets/icon/*.svg` (graphite cabinet, warm-white
      tweeter, copper woofer), exe icon via `tools/make-icon.ps1`, About row in settings.*
- [x] **Firmware check**: the speaker reports 1.0.0 (`0xC6` → `01 00 00`). *Checked: omarchy-edifier-mr3
      reads the same command the same way (bytes joined by dots) and gets 1.0.7 from an MR3, so this
      is the firmware version; 1.0.0 fits a model released in August 2026. The ConneX app can confirm.*
- [ ] **README screenshot** of the settings view with the new About row (the panel closes when it
      loses focus, so it needs a moment when nobody is using the PC).
- [x] **Installer**: per-user `setup.exe` (Inno Setup, like cash) attached to each release.
      *Done: `packaging/inno`, built by `tools/package.ps1`; tested locally (silent install,
      files, Installed-apps entry, uninstall) and in CI on every push. First release with it: the
      next one.*
- [x] **One-click release**: Actions › Release › Run workflow, pick patch/minor/major; it raises the
      version, commits `chore(release): X.Y.Z`, tags, builds and publishes ("go up one").
      *Done in `release.yml` + `prepare-release.ps1`; the bump and changelog were dry-run
      locally. Not run on GitHub yet; that publishes a release.*
- [x] **Release notes**: add the GitHub issues closed since the previous release, and commit the
      notes to `CHANGELOG.md`. *Done; CHANGELOG.md starts with v1.0.0.*
- [ ] **Feature ideas list** for the owner to pick from ("review it and think of other features").
      Includes the speaker functions: show/switch sound mode (Monitor/Music/Custom), power off,
      auto-sleep. Nothing gets built until picked.
- [ ] **Full UX review** ("Do UX review. Think. Check online."): tray icon, tooltip, menu, panel,
      settings, wording; a report with sources, changes only after approval.
- [ ] **Verify**: phone-app handover, wake from sleep, a real reboot with Start with Windows, the
      release zip in a clean folder.

## Waiting on the owner

- [ ] Scoop and winget: both need a public download, so the repo (or its releases) must be public.
- [ ] CoolWSL items not offered yet: About links (repo, report an issue), label-based release
      notes, Dependabot, MSIX assets.

## On hold

- [ ] Custom EQ editing ("leave that out for now"). Reading the 9 bands works on the MR4.5.
- [ ] Auto-sleep: `0xD7` gets no reply on the MR4.5; needs more protocol work first.
