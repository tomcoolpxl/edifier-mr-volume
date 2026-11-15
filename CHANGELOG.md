# Changelog

What changed in each release. Written by the release workflow from the commit
messages and the GitHub issues closed in between; see docs/RELEASING.md.

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
