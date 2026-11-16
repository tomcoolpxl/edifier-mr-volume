# Changelog

What changed in each release. Written by the release workflow from the commit
messages and the GitHub issues closed in between; see docs/RELEASING.md.

## v1.1.0 (2026-11-16)

### Features

- **about:** flat MR4.5-style app icon, shown in an About row in settings (ed54524)
- **install:** per-user installer (Inno Setup) and one packaging script (0ec1f2b)
- **settings:** Windows 11 settings cards, volume limit, start volume, mute, first-run hint (8b7555d)
- **flyout:** show and switch the sound mode (Monitor, Music, Custom) (57b91d2)
- **settings:** switch the speakers off when the PC sleeps (3588f90)
- **probe:** --step for ordered reads and writes, eq_audition.py to try EQ presets by ear (04f1074)
- equalizer with three presets in settings; switch the speakers off from the panel or menu (6732393)
- **tray:** scroll wheel over the number changes the volume, one step per notch (306ee92)
- **settings:** power saving switch for the speakers' 15-minute standby (45ef0b5)
- **about:** new app icon drawn from the black MR4.5 (3d5319e)
- **protocol:** read and set the room tuning, echoing the speaker's own bytes and checking every value (7ca2b0d)
- **settings:** room tuning in everyday words, and the fixes from the UX review (a129956)
- **install:** ARM64 releases, built natively with Native AOT and tested on Windows on Arm (169325e)

### Bug fixes

- **probe:** eq_audition waits for each reply instead of a fixed 0.3 s (623e833)
- **protocol:** the first byte of the EQ reply is the layout, not the maximum gain (a1a4dea)
- **tray:** keep settings and log in the real AppData when started from a packaged app (95ec3a2)
- **settings:** much less text in the settings, and start volume 7 by default (597d002)
- **settings:** a wider settings view, and room labels that no longer overlap (8b6c828)
- **flyout:** open above the notification-area icon, and always on the volume view (21b00d8)
- **settings:** the volume limit as a slider, out of 30 (92c775b)
- **settings:** start volume as a slider, out of 30, like the limit (4ba148e)
- **settings:** opening the panel crashed the app since the volume limit became a slider (cf5559d)
- **settings:** a larger app icon in About, and "the speaker knob is not limited" (e3ed16d)

### Performance

- **ble:** listen for the speaker's pushes instead of asking for the volume every 0.5 s (248cca6)
- **install:** build as Native AOT: starts twice as fast, a third less memory, 64 MB smaller (014793a)

### Documentation

- add TODO.md with the open requests (7982db6)
- installer, one-click releases and the changelog (970429a)
- complete to-do list with every decision; hide the speaker address in the screenshot (2b71082)
- TODO.md with what is tested, built-but-unverified and still open (be9d29e)
- TODO.md with the equalizer presets request, power-off choice and test results (c7490e7)
- TODO.md with the UX/UI review request, the WinUI skill and the speaker facts for EQ presets (6f7ba48)
- TODO.md with the owner's choices on EQ listening, input, timer and rescan (ec0a21b)
- TODO.md with the owner's preset choice after listening (f6c1be9)
- TODO.md with the equalizer, power-off and wheel work, tests and the CPU check (db06c72)
- TODO.md with the timer test: 0xD1 is a one-shot shutdown timer (1da4b38)
- TODO.md with power saving found and built, the room-tuning request, the timer decision (756f369)
- TODO.md with the CPU check after the power saving switch (b81e501)
- TODO.md with the icon redraw (3dc2a8a)
- TODO.md with the owner's choice to leave switch-off-on-sleep off (29a19f0)
- TODO.md with the room tuning (Acoustic Tuning) findings (7d3849f)
- TODO.md with Dependabot done (ebff015)
- README with the equalizer, power saving, power-off, wheel and room-tuning protocol (bd69eac)
- TODO.md with the Open log failure, its cause and the fix (0add94a)
- TODO.md with the input research and the lighter connection, measured (46eefae)
- TODO.md with the release zip checked in a clean folder (304fbb5)
- full UX and UI review report (23 findings, nothing changed yet) (ef09e50)
- TODO.md with what waits for the owner after the night (349e31c)
- TODO.md with the owner's decisions of 2026-11-16 (b55d219)
- TODO.md with the owner's choices for the UX fixes and room tuning (adad16c)
- README and installer wording for the room tuning and the UX fixes (3ff684c)
- TODO.md with the UX fixes and room tuning built and tested (73bba70)
- TODO.md with the major updates merged (61d2e4f)
- TODO.md with the shorter settings texts (02a3ab2)
- TODO.md with the wider settings view (636c777)
- TODO.md with the panel above the icon (ffc513c)
- TODO.md with the volume limit slider (411975f)
- TODO.md with ARM64 releases green in CI (43e16b4)
- README screenshots of the panel, the number and the settings (059c3ab)
- the owner's newest screenshots of the panel and the settings (32c4b62)

### Refactoring

- **settings:** the equalizer's choice and Custom-curve logic in Core, with tests (b15a236)

### Maintenance

- one-click release that bumps the version, CHANGELOG.md, installer in every build (f8bb4ee)
- Dependabot for NuGet, the .NET SDK and the GitHub Actions, weekly (f223160)
- merge green patch and minor Dependabot updates by themselves, as in cash (da62be2)
- **deps:** Bump softprops/action-gh-release (#15) (dc39edd)
- **deps:** Bump dotnet-sdk from 10.0.100 to 11.0.100 (#16) (f192ace)
- **deps:** bump Microsoft.NET.Test.Sdk from 17.14.2 to 18.12.0 (5483dff)
- smoke-launch the published Native AOT app, and fail if it ships the .NET runtime (65c2d6c)

### Other

- Bump Microsoft.NET.Test.Sdk and xunit.runner.visualstudio (#17) (b74f972)
- Bump coverlet.collector from 6.0.4 to 10.1.1 (#18) (54729f1)
- Bump xunit.runner.visualstudio from 3.1.5 to 4.0.1 (#20) (3a0695f)

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
