# TODO

Open requests, in the owner's words where possible. Tick an item only when it is done and
verified, with a note saying how. Before any question to the owner, every unticked item here is
either offered or named as deliberately left out.

## Done

- [x] **App icon**: flat, abstract, vector line-style, in the Edifier MR4.5's own look and colours,
      without the Edifier name. Not in the taskbar (the tray keeps the volume number); shown in
      the settings/About part of the panel. *Done: `assets/icon/*.svg` (graphite cabinet, warm-white
      tweeter, copper woofer), exe icon via `tools/make-icon.ps1`, About row in settings.*
- [x] **Firmware check**: the speaker reports 1.0.0 (`0xC6` → `01 00 00`). *Checked: omarchy-edifier-mr3
      reads the same command the same way (bytes joined by dots) and gets 1.0.7 from an MR3, so this
      is the firmware version; 1.0.0 fits a model released in August 2026. The ConneX app can confirm.*
- [x] **Installer**: per-user `setup.exe` (Inno Setup, like cash) attached to each release.
      *Done: `packaging/inno`, built by `tools/package.ps1`; tested locally (silent install,
      files, Installed-apps entry, uninstall) and in CI on every push (green). First release with
      it: the next one.*
- [x] **One-click release**: Actions › Release › Run workflow, pick patch/minor/major; it raises the
      version, commits `chore(release): X.Y.Z`, tags, builds and publishes ("go up one").
      *Done in `release.yml` + `prepare-release.ps1`; bump and changelog dry-run locally. Not run
      on GitHub yet, because running it publishes a release.*
- [x] **Release notes**: the GitHub issues closed since the previous release, and the notes
      committed to `CHANGELOG.md`. *Done; CHANGELOG.md starts with v1.0.0.*
- [x] **Feature ideas list** for the owner to pick from. *Written and picked from (below).*
- [x] **Full UX review** ("Do UX review. Think. Check online."). *Written with sources (below);
      the owner picked all four fixes.*
- [x] **The 14 GitHub issues I wrongly created**: *closed, then permanently deleted on the
      owner's choice (2026-11-16), with the labels made for them.*

## To build: picked by the owner

UX fixes:
- [ ] Panel sizes to its content (text scaling)
- [ ] Say why it can't connect (reason in the panel; hint after ~15 s of searching)
- [ ] Panel next to the taskbar wherever it is
- [ ] Wording ("notification area"; menu "Show volume" → "Open")

Speaker:
- [ ] Sound mode: show and switch Monitor / Music / Custom (test switching on the speaker first)
- [ ] Input: show and switch (find out which number is which input first)
- [ ] Volume limit (setting; caps what the app sets, not the knob)
- [ ] Power-off button
- [ ] **Switch the speakers off when the PC goes to sleep** (owner's own idea; setting, off by
      default; must be tested with a real sleep)
- [ ] **Equalizer**: first test writing one band on the speaker (change one step, put it back),
      then propose an EQ design for the owner to approve before building it
- [ ] **Auto-standby**: investigate the protocol (decode the `0xD8` feature table, try the other
      known commands read-only) to find where the MR4.5 keeps it

Convenience:
- [ ] Mute (click the speaker symbol; click again restores)
- [ ] Start volume (setting; set a level when the app connects; off by default)
- [ ] First-run hint (where to switch the notification-area icon on; once)

Project:
- [ ] Make the repo public, then add a Scoop manifest to tomcoolpxl/scoop-bucket (like cash's)
- [ ] About links: GitHub repository and "Report a problem" in the About row
- [ ] Dependabot for NuGet packages and GitHub Actions
- [ ] README, screenshots (settings view with the About row) and commits for all of the above
- [ ] **Verify**: phone-app handover, wake from sleep, a real reboot with Start with Windows, the
      release zip in a clean folder; light theme and high contrast

## Not picked (2026-11-16)

Offered and not chosen; listed so they don't silently disappear:
- Keyboard shortcuts for the speaker volume
- winget package (needs the installer submitted publicly to microsoft/winget-pkgs)
- Label-based pull-request sections in the release notes
- MSIX package assets

## Notes from the UX review (2026-11-15)

- **Fixed panel heights clip with larger text.** Windows' "Text size" scales WinUI text; Microsoft's
  guidance says containers must resize and reflow
  ([text scaling](https://learn.microsoft.com/windows/apps/develop/input/text-scaling)).
- **Silent failure while searching.** When the phone app holds the speaker, or Bluetooth is off,
  the panel only says "Looking for speaker…" / "Retrying…" and hides the reason in a tooltip.
- **Panel always bottom-right**, covering a taskbar on the top or a side (Windows 10).
- **Wording.** Microsoft's style guide says "notification area", not "system tray"
  ([style guide](https://learn.microsoft.com/style-guide/a-z-word-list-term-collections/n/notification-area)).
- Fine as is: the icon shows status (what the notification area is for, per
  [Microsoft's guidelines](https://learn.microsoft.com/windows/win32/uxguide/winenv-notification));
  settings in the right-click menu; nothing optional on by default; the menu closes properly;
  controls have accessible names.

## Protocol facts for the speaker features (MR4.5, 2026-11-15)

- Sound mode `0xD5` → `00 02 00 1E 03 00 01`: mode 0 (Monitor), index 2, low cut 30 Hz,
  slope −24 dB/oct, acoustic space 0 dB, desktop on. Switch with `0xC4` + `[mode][rest echoed]`
  (EdifierControl `makeSetEq`); untested.
- Input `0x61` → `04 01`: input 1 of 4; set with `0x62`; mapping unknown.
- Power off `0xCE`: works; no reply.
- Custom EQ `0x43`: 9 bands `[index][freqHi][freqLo][gain]`, gain 6 = flat; set one band with
  `0x44`; untested.
- Auto-standby `0xD7`: no reply. `0xD8`: 29-byte feature table, not decoded.
