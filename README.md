# Edifier MR Volume

An unofficial Windows tray app for **Edifier MR studio monitors** (MR4.5, MR3, MR5). It shows and
sets the speakers' own hardware volume and follows the physical volume knob live. It talks to
the speaker's Bluetooth LE control interface (the one the EDIFIER ConneX phone app uses), so it
doesn't depend on the Windows volume slider. Control runs over its own Bluetooth LE link,
separate from the audio input (tested with the speakers fed by a USB audio interface).

The volume in the tray (the **13** on the left):

![Taskbar with the speaker volume shown as a tray icon](docs/images/tray.png)

Click it for the panel:

![Panel with the model name, a volume slider and the current level](docs/images/flyout.png)

## Supported speakers

| Model | Status |
| --- | --- |
| **MR4.5** | Tested with this app: reading, setting and following the knob. |
| **MR3** | Supported. Same protocol and volume reply as the MR4.5, with its own control service, verified on an MR3 by [omarchy-edifier-mr3](https://github.com/Saikomantisu/omarchy-edifier-mr3) and [EdifierControl](https://github.com/D4n13l3k00/EdifierControl). Not yet tested with this app; reports welcome. |
| **MR5** | Supported. MR5 captures in [Edifier-IR-remote](https://github.com/68776877/Edifier-IR-remote) show volume frames byte-identical to the MR4.5's; only the control service differs. Not yet tested with this app; reports welcome. |
| MR4.5 / MR5 (8625 DSP revisions) | Supported using EdifierControl's IDs; not tested. |

The app finds the speaker by its Bluetooth LE advertisement and then picks the model from the
control service it exposes.

## Features

- The tray icon *is* the current volume (0–30), drawn at the taskbar's pixel size.
- Click it for a panel with a slider; the mouse wheel and arrow keys work too, and Esc closes it.
- **Auto-open panel** (off by default): turning the knob on the speaker opens the panel for a
  moment without taking focus, and it closes 1, 2, 3 or 5 s after the last change. It stays
  away while a full-screen game or presentation runs.
- **Diagnostics** behind the gear: connection status, speaker model and firmware, Bluetooth
  address, reconnect count, last error and app version, with **Reconnect** and **Open log**.
- Right-click for: **Show volume**, **Connected to speaker** (untick to free the link for the
  phone app), **Start with Windows**, **Exit**.
- Reconnects on its own when the speaker is switched off and on, and straight away after the
  PC wakes from sleep. Light: about 0.02 % CPU while connected.

![Settings and diagnostics behind the gear button](docs/images/settings.png)

Built with WinUI 3 (Windows App SDK 2.6), .NET 10 and [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon).

## Install

Windows 10 2004 or later, x64. No pairing in Windows Bluetooth settings is needed.

**Installer** (from v1.0.1 on): download `edifier-mr-volume-vX.Y.Z-win-x64-setup.exe` from
[Releases](https://github.com/tomcoolpxl/edifier-mr-volume/releases) and run it. Per user, no
admin: it installs to `%LOCALAPPDATA%\Programs\EdifierMrVolume`, adds an **Edifier MR Volume**
Start menu shortcut, can start the app when you sign in, upgrades an installed copy in place
(closing it cleanly first) and uninstalls from Windows' **Installed apps**.

**Portable**: download `edifier-mr-volume-vX.Y.Z-win-x64.zip`, unpack it anywhere and run
`EdifierMrVolume.exe`. Self-contained (.NET and the Windows App SDK runtime included).

**From source** (needs the .NET 10 SDK):

```powershell
.\tools\install.ps1
```

This publishes a self-contained Release build (about 240 MB), installs it to
`%LOCALAPPDATA%\Programs\EdifierMrVolume`, adds an **Edifier MR Volume** Start menu shortcut and
starts it. Rerun it after pulling changes: it closes the running copy cleanly, so the speaker
reconnects within seconds. Then tick **Start with Windows** in the tray menu.

Settings live in `%LOCALAPPDATA%\EdifierMrVolume\settings.json`, the log next to it in `log.txt`.
To uninstall an installer copy, use Windows' **Installed apps**. For a portable or
`install.ps1` copy: untick **Start with Windows**, choose **Exit**, then delete
`%LOCALAPPDATA%\Programs\EdifierMrVolume`, `%LOCALAPPDATA%\EdifierMrVolume` and the Start menu shortcut.

Command-line switches: `--show` opens the panel (launching it again while it runs does the
same), `--exit` closes the running copy.

### Tray icon not visible?

Windows 11 hides new tray icons. Turn it on under **Settings › Personalization › Taskbar ›
Other system tray icons › EdifierMrVolume**, or drag it from the hidden-icons area onto the taskbar.

### Phone app can't connect?

The speaker accepts one BLE control client at a time. Right-click the tray icon and untick
**Connected to speaker** while you use EDIFIER ConneX; tick it again afterwards.

## Develop

```powershell
dotnet test tests/EdifierMrVolume.Core.Tests
dotnet build src/EdifierMrVolume/EdifierMrVolume.csproj -p:Platform=x64
src\EdifierMrVolume\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\EdifierMrVolume.exe --show
```

Only one copy runs at a time, so exit the installed one first.

Commits follow Conventional Commits (`feat(scope): …`, `fix(scope): …`) because the release notes
and [CHANGELOG.md](CHANGELOG.md) are built from them. CI builds, tests and smoke-launches the app,
and builds, installs and uninstalls the installer, on every push and pull request. A release is
one click: **Actions › Release › Run workflow** (patch/minor/major); see
[docs/RELEASING.md](docs/RELEASING.md). `.\tools\package.ps1` builds the release files locally.

| Path | What |
| --- | --- |
| `src/EdifierMrVolume.Core` | Protocol (`EdifierProtocol`) and the BLE connection loop (`SpeakerConnection`) |
| `src/EdifierMrVolume` | WinUI 3 tray app: tray icon, panel, settings, autostart, log |
| `tests/EdifierMrVolume.Core.Tests` | Protocol tests using bytes captured from real speakers |
| `tools/install.ps1` | Publish and install for the current user (development) |
| `tools/package.ps1` | Release files: zip, installer, checksums |
| `packaging/inno` | Inno Setup script for the per-user installer |
| `assets/icon` | App icon masters (SVG); `tools/make-icon.ps1` builds `app.ico` from them |
| `tools/probe.py` | Python/Bleak probe used to verify the protocol |
| `.github/workflows` | CI (`ci.yml`) and tag-driven releases (`release.yml`) |
| `docs/RELEASING.md` | Commit convention and release routine |

## Protocol, verified on an MR4.5 (firmware 1.0.0, 2026-11-15)

| Item | Value |
| --- | --- |
| Advertised name | `EDIFIER BLE`, manufacturer id `0x07E0` |
| Advertised search UUID | `00006303-0000-1000-8000-00805f9b34fb` |
| Control service | `48096303-1a48-11e9-ab14-d663bd873d93` (MR3: `4809f301-…`, MR5: `48093a01-…`) |
| Notify characteristic | `48090001-1a48-11e9-ab14-d663bd873d93` |
| Write characteristic | `48090002-1a48-11e9-ab14-d663bd873d93` (write / write-without-response) |

Packet frame: `AA <appCode> <cmd> <lenHi> <lenLo> <payload…> <checksum>`, checksum = sum of all
previous bytes & 0xFF. Replies start with `BB` and carry app code `EC`. The MR4.5 with the 8625
DSP revision reportedly uses `…7c03…` instead of `…6303…`; the app tries both.

| Command | Request | Reply on the MR4.5 | Used by the app |
| --- | --- | --- | --- |
| `0x66` get volume | `AA 0C 66 00 00 1C` | `BB EC 66 00 02 1E 13 40`: max 30, current 19. Polled every 0.5 s; follows every knob step | yes |
| `0x67` set volume | `AA 0C 67 00 01 <level> <sum>` | ack `BB EC 67 00 01 01 10`, then a pushed `0x66` reply | yes |
| `0xC6` firmware | `AA 0C C6 00 00 7C` | `01 00 00` = 1.0.0 | Diagnostics |
| `0xC9` model name | `AA 0C C9 00 00 7F` | `EDIFIER MR4.5` (ASCII) | Diagnostics |
| `0xD5` sound mode | `AA 0C D5 00 00 8B` | `00 02 00 1E 03 00 01`: mode 0 (Monitor), index 2, low cut 30 Hz, slope −24 dB/oct, acoustic space 0 dB, desktop mode on | no |
| `0x43` custom EQ | `AA 0C 43 00 00 F9` | 56 bytes: 9 bands (62 Hz … 16 kHz) as `[index][freqHi][freqLo][gain]`, gain 6 = flat (here 62/125/250 Hz boosted), then a timestamp and the name "Sound Effects" | no |
| `0x61` input | `AA 0C 61 00 00 17` | `04 01`: input 1 of 4 | no |
| `0xD8` features | `AA 0C D8 00 00 8E` | 29 bytes, not decoded | no |
| `0xCE` power off | `AA 0C CE 00 00 84` | no reply; the speakers switch off (switch them back on by hand) | no |
| `0xD7` auto-standby, `0xF2` device state, `0xF7` LED | | no reply on the MR4.5 | no |

### Probe script

```powershell
python -m venv .venv
.venv\Scripts\python.exe -m pip install bleak
.venv\Scripts\python.exe tools\probe.py                       # find the speaker, read the volume
.venv\Scripts\python.exe tools\probe.py --watch 30            # print every knob change for 30 s
.venv\Scripts\python.exe tools\probe.py --read D5 --read 43   # send query commands, print the replies
.venv\Scripts\python.exe tools\probe.py --set-test            # one step down and back (writes!)
.venv\Scripts\python.exe tools\probe.py --power-off           # switches the speakers off (writes!)
```

Quit the tray app (or untick **Connected to speaker**) first, since only one client can connect.

## Credits

The protocol details come from these MIT-licensed reverse-engineering projects:

- [D4n13l3k00/EdifierControl](https://github.com/D4n13l3k00/EdifierControl): MR4.5 and MR3 UUIDs, the two-byte volume reply, command list
- [Saikomantisu/omarchy-edifier-mr3](https://github.com/Saikomantisu/omarchy-edifier-mr3): MR3 service and discovery, sound mode and EQ layout
- [Devis99/omarchy-edifier-mr5](https://github.com/Devis99/omarchy-edifier-mr5): packet framing and command set
- [68776877/Edifier-IR-remote](https://github.com/68776877/Edifier-IR-remote): MR5 packet captures, one client at a time, 0–30 range

The release process follows [cash](https://github.com/tomcoolpxl/cash).

Not affiliated with or endorsed by Edifier.

## License

[MIT](LICENSE)
