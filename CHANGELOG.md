# Changelog

What changed in each release. Written by the release workflow from the commit
messages and the GitHub issues closed in between; see docs/RELEASING.md.

## v1.2.0 (2026-11-17)

### Features

- **flyout:** a light in the speaker's mode colour next to the sound modes; click it for the next mode (d94aa52)

### Bug fixes

- **flyout:** close the panel on a click outside, also when Windows keeps another app in front (883b581)
- **flyout:** the panel gets the keyboard focus when opened from the number (07737e1)

### Documentation

- README with new screenshots, an independent-project note and why Native AOT (2b05292)
- a shorter README in plain words, with the .NET 11 SDK and Scoop (f09a888)
- plain characters instead of long dashes, minus signs and ellipses (0310ad4)
- architecture, protocol, contributing and security documents (a707e6b)
- changelog with the commit hashes after the history rewrite (de73795)
- release notes name the Arm files and Scoop (a2299a8)

### Refactoring

- one file per area for the window, one per class for the support code, plain comments (a366dd7)

### Maintenance

- check formatting with .editorconfig (ef01655)
- **install:** third-party notices in the release files, and no debug symbols (225c49a)
- keep the working to-do list out of the repository (ca293dc)

## v1.1.0 (2026-11-16)

### Features

- **about:** flat MR4.5-style app icon, shown in an About row in settings (ed54524)
- **install:** per-user installer (Inno Setup) and one packaging script (2eae122)
- **settings:** Windows 11 settings cards, volume limit, start volume, mute, first-run hint (cb80cfe)
- **flyout:** show and switch the sound mode (Monitor, Music, Custom) (40b6aa4)
- **settings:** switch the speakers off when the PC sleeps (dbb3b0a)
- **probe:** --step for ordered reads and writes, eq_audition.py to try EQ presets by ear (33e9759)
- equalizer with three presets in settings; switch the speakers off from the panel or menu (6bf74bf)
- **tray:** scroll wheel over the number changes the volume, one step per notch (f2ffd4d)
- **settings:** power saving switch for the speakers' 15-minute standby (7e1ec91)
- **about:** new app icon drawn from the black MR4.5 (808e4b8)
- **protocol:** read and set the room tuning, echoing the speaker's own bytes and checking every value (328dd49)
- **settings:** room tuning in everyday words, and the fixes from the UX review (2e94695)
- **install:** ARM64 releases, built natively with Native AOT and tested on Windows on Arm (2dcb481)

### Bug fixes

- **probe:** eq_audition waits for each reply instead of a fixed 0.3 s (daf6c7f)
- **protocol:** the first byte of the EQ reply is the layout, not the maximum gain (a55c87f)
- **tray:** keep settings and log in the real AppData when started from a packaged app (a2c7e50)
- **settings:** much less text in the settings, and start volume 7 by default (73f50eb)
- **settings:** a wider settings view, and room labels that no longer overlap (a079d43)
- **flyout:** open above the notification-area icon, and always on the volume view (9f1a347)
- **settings:** the volume limit as a slider, out of 30 (d12b692)
- **settings:** start volume as a slider, out of 30, like the limit (5177d37)
- **settings:** opening the panel crashed the app since the volume limit became a slider (1fba657)
- **settings:** a larger app icon in About, and "the speaker knob is not limited" (5c22827)

### Performance

- **ble:** listen for the speaker's pushes instead of asking for the volume every 0.5 s (bccd03e)
- **install:** build as Native AOT: starts twice as fast, a third less memory, 64 MB smaller (2a76b09)

### Documentation

- installer, one-click releases and the changelog (3487c5a)
- complete to-do list with every decision; hide the speaker address in the screenshot (c70000f)
- README with the equalizer, power saving, power-off, wheel and room-tuning protocol (a19cd8b)
- full UX and UI review report (23 findings, nothing changed yet) (0f4d529)
- README and installer wording for the room tuning and the UX fixes (307c99a)
- README screenshots of the panel, the number and the settings (9c3fd0c)
- the owner's newest screenshots of the panel and the settings (5ced83b)

### Refactoring

- **settings:** the equalizer's choice and Custom-curve logic in Core, with tests (5eb99a3)

### Maintenance

- one-click release that bumps the version, CHANGELOG.md, installer in every build (6779473)
- Dependabot for NuGet, the .NET SDK and the GitHub Actions, weekly (0dfbd42)
- merge green patch and minor Dependabot updates by themselves, as in cash (35bcd7b)
- **deps:** Bump softprops/action-gh-release (#15) (82134c8)
- **deps:** Bump dotnet-sdk from 10.0.100 to 11.0.100 (#16) (ad90c08)
- **deps:** bump Microsoft.NET.Test.Sdk from 17.14.2 to 18.12.0 (2a81d7f)
- smoke-launch the published Native AOT app, and fail if it ships the .NET runtime (bdcbc3b)

### Other

- Bump Microsoft.NET.Test.Sdk and xunit.runner.visualstudio (#17) (a7af4db)
- Bump coverlet.collector from 6.0.4 to 10.1.1 (#18) (0bb0cab)
- Bump xunit.runner.visualstudio from 3.1.5 to 4.0.1 (#20) (5d80e68)

## v1.0.0 (2026-11-15)

### Features

- **panel:** settings and diagnostics behind a gear button, optional auto-open panel (54ce72f)
- **probe:** --read to send query commands and --power-off (50ab702)

### Documentation

- settings, diagnostics, verified commands and the release routine (3db4616)
- **release:** opening lines for v1.0.0 (4a3500e)

### Maintenance

- pin the .NET SDK, keep the version in Directory.Build.props, store text with LF (0f9617c)
- build, test and smoke-launch on every push; tag-driven GitHub releases (0591d43)

### Other

- Add read-only BLE probe for Edifier MR4.5 (503be66)
- Add WinUI 3 tray app for the MR4.5 hardware volume (631f34e)
- Add install script, app icon and single-instance signals (c7dba72)
- Support the MR3, add MIT license and README screenshots (91078ee)
- Support the MR5 (6aeb5a6)
- Rename to Edifier MR Volume (edifier-mr-volume) (90051d3)
- Show models as "Edifier MR4.5/MR3/MR5"; retake screenshots (e66ec44)
