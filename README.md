# Edifier MR Volume

> [!IMPORTANT]
> This is an independent project. It is made for Edifier speakers, but it is not affiliated
> with, endorsed by or supported by Edifier.

A Windows app for Edifier MR studio monitors (MR4.5, MR3, MR5). It shows the speakers' own
volume as a number in the taskbar, and lets you change the volume, sound mode, equalizer, room
tuning and power saving.

It talks to the speakers over Bluetooth LE, like Edifier's ConneX phone app, so it works with
any audio input and doesn't touch the Windows volume.

![The speaker volume, 3, as a number in the taskbar](docs/images/tray.png)

![The panel: sound mode, volume and mute](docs/images/flyout.png)

## Supported speakers

| Model | Status |
| --- | --- |
| MR4.5 | Tested |
| MR3 | Should work: same protocol, verified on an MR3 by other projects (see Credits). Not tested with this app. |
| MR5 | Should work: same volume messages in MR5 captures. Not tested with this app. |
| MR4.5 / MR5 with the 8625 DSP | Should work, not tested |

Reports for the untested models are welcome.

## Features

- **The number in the taskbar** is the volume (0-30). Scroll over it to change it.
- **Click it** for the panel: sound mode, volume, mute and power off. The light next to the
  modes has the speaker's colour (red Monitor, green Music, white Custom); click it for the
  next mode.
- **Right-click it** for the menu: Open, Turn off speakers, Connect to speaker, Start with
  Windows, Exit.
- **Keyboard:** Win+B, arrow keys to the number, then Enter for the panel or Shift+F10 for the
  menu. In the panel the arrow keys change the volume and Esc closes it.
- **Reconnects by itself** after the speakers are switched off and on, and after sleep.

Settings, behind the gear:

| Setting | What it does |
| --- | --- |
| Auto-open panel | Shows the panel for a moment when you turn the knob. Off by default. |
| Volume limit | The app never sets the volume higher (24 by default). The knob isn't limited. |
| Start volume | Sets a volume when the app first connects after it starts. Off by default. |
| Equalizer | Deep bass, Balanced, Deep + balanced, or Your curve (9 bands). Switches to Custom mode. |
| Room | Where the speakers stand, how much bass, and whether they're on a desk. |
| Turn off when the PC sleeps | Off by default. |
| Power saving | The speakers' own standby after 15 minutes without sound. |
| Diagnostics | Connection details, Reconnect and Open log. |

<img src="docs/images/settings.png" alt="Settings" width="320">

## Install

Windows 10 2004 or later, on x64 or Arm. No Bluetooth pairing needed.

- **Installer:** download `edifier-mr-volume-vX.Y.Z-win-x64-setup.exe` (or `-win-arm64-setup.exe`) from
  [Releases](https://github.com/tomcoolpxl/edifier-mr-volume/releases) and run it. No admin
  rights needed. Uninstall it from Windows' Installed apps.
- **Portable:** download `edifier-mr-volume-vX.Y.Z-win-x64.zip` (or `-win-arm64.zip`), unpack it anywhere and run
  `EdifierMrVolume.exe`. Nothing else to install.
- **Scoop:**

  ```powershell
  scoop bucket add tomcoolpxl https://github.com/tomcoolpxl/scoop-bucket
  scoop install edifier-mr-volume
  ```

Settings and the log are in `%LOCALAPPDATA%\EdifierMrVolume`. To remove a portable copy, turn
off Start with Windows, choose Exit, and delete its folder and that one.

`EdifierMrVolume.exe --show` opens the panel; `--exit` closes the running app.

## Problems

**No number in the taskbar?** Windows hides new icons. Turn it on under Settings >
Personalization > Taskbar > Other system tray icons (Windows 10: Select which icons appear on
the taskbar). The app shows this hint on its first start.

**The phone app can't connect?** The speakers accept one control connection at a time. Turn
off **Connect to speaker** while you use ConneX.

## Why Native AOT

The app runs all day, so it should start fast and stay small. It is compiled ahead of time
with Microsoft's [Native AOT](https://learn.microsoft.com/dotnet/core/deploying/native-aot/),
so no .NET runtime ships with it. Measured against a normal .NET build on the same PC:

| | Normal .NET | Native AOT |
| --- | ---: | ---: |
| Start, until the number shows | 0.40 s | 0.19 s |
| CPU in one idle minute | 109 ms | 31 ms |
| Memory (working set) | 117 MB | 76 MB |
| Install size | 237 MB | 173 MB |

A partial C++ version was only a little smaller. Details:
[docs/NATIVE-AOT.md](docs/NATIVE-AOT.md).

## Develop

Needs the .NET 11 SDK and Visual Studio's "Desktop development with C++" workload, which
Native AOT uses to link. The app targets .NET 10 and the Windows 11 SDK (10.0.26100), with
WinUI 3 (Windows App SDK 2.6) and [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon).

```powershell
dotnet test tests/EdifierMrVolume.Core.Tests                  # protocol tests
dotnet build src/EdifierMrVolume/EdifierMrVolume.csproj -p:Platform=x64
.\tools\install.ps1                                           # publish, install and start
```

Only one copy runs at a time, so exit the installed one before starting another.

Commit messages follow Conventional Commits, because the release notes are built from them.
CI builds, tests and installs the app on every push, on x64 and on Windows on Arm. A release
is Actions > Release > Run workflow; see [docs/RELEASING.md](docs/RELEASING.md).

| Path | What |
| --- | --- |
| `src/EdifierMrVolume.Core` | Protocol and Bluetooth connection |
| `src/EdifierMrVolume` | The WinUI 3 app |
| `tests/EdifierMrVolume.Core.Tests` | Protocol tests with bytes from real speakers |
| `tools/install.ps1` | Publish and install for the current user |
| `tools/package.ps1` | Release files: zips, installers, checksums |
| `packaging/inno` | Installer script (Inno Setup) |
| `assets/icon` | App icon sources |
| `tools/probe.py` | Bluetooth probe used to work out the protocol |
| `tools/eq_audition.py` | Plays an EQ preset on the speaker to hear it |
| `.github/workflows` | CI, releases, and merging Dependabot updates |
| `docs` | Architecture, protocol, Native AOT measurements, releasing |

## Documentation

- [Architecture](docs/ARCHITECTURE.md): how the code is laid out
- [Bluetooth protocol](docs/PROTOCOL.md): the commands the speakers understand
- [Why Native AOT](docs/NATIVE-AOT.md): the measurements
- [Releasing](docs/RELEASING.md): commit style and the release routine
- [Contributing](CONTRIBUTING.md), [Security](SECURITY.md) and [Third-party notices](THIRD-PARTY-NOTICES.md)

## Credits

The protocol comes from these MIT-licensed projects:

- [D4n13l3k00/EdifierControl](https://github.com/D4n13l3k00/EdifierControl): MR4.5 and MR3 IDs, the volume reply, commands, EQ layouts, room-tuning values
- [Saikomantisu/omarchy-edifier-mr3](https://github.com/Saikomantisu/omarchy-edifier-mr3): MR3 service, sound mode and EQ
- [Devis99/omarchy-edifier-mr5](https://github.com/Devis99/omarchy-edifier-mr5): packet format, commands, room-tuning layout
- [68776877/Edifier-IR-remote](https://github.com/68776877/Edifier-IR-remote): MR5 captures
- [wh201906/mEDIFIER](https://github.com/wh201906/mEDIFIER): the shutdown-timer commands

The power-saving commands were found with this project's probe. The release process follows
[cash](https://github.com/tomcoolpxl/cash).

## License

[MIT](LICENSE)
