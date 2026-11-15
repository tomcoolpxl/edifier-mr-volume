# Edifier MR Volume

An unofficial Windows tray app for **Edifier MR studio monitors** (MR4.5, MR3, MR5). It shows and
sets the speakers' own hardware volume and follows the physical volume knob live. It talks to the speaker's Bluetooth LE
control interface (the one the EDIFIER ConneX phone app uses), so it doesn't depend on the
Windows volume slider. Control runs over its own Bluetooth LE link, separate from the audio
input (tested with the speakers fed by a USB audio interface).

The volume in the tray (the **2** on the left):

![Taskbar with the speaker volume shown as a tray icon](docs/images/tray.png)

Click it for the flyout:

![Flyout with the model name, a volume slider and the current level](docs/images/flyout.png)

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
- Click it for a flyout with a slider; the mouse wheel and arrow keys work too, and Esc closes it.
- Right-click for: **Show volume**, **Connected to speaker** (untick to free the link for the
  phone app), **Start with Windows**, **Exit**.
- Reconnects on its own when the speaker is switched off and on, or after sleep.

Built with WinUI 3 (Windows App SDK 2.6), .NET 10 and [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon).

## Install

Requires the .NET 10 SDK to build, on Windows 10 2004 or later (x64 or ARM64). No pairing in
Windows Bluetooth settings is needed.

```powershell
.\tools\install.ps1
```

This publishes a self-contained Release build (.NET and the Windows App SDK runtime included,
about 240 MB), installs it to `%LOCALAPPDATA%\Programs\EdifierMrVolume`, adds a **Edifier MR Volume**
Start menu shortcut and starts it. Rerun it after pulling changes: it closes the running copy
cleanly, so the speaker reconnects within seconds. Then tick **Start with Windows** in the tray menu.

To uninstall: untick **Start with Windows**, choose **Exit**, then delete
`%LOCALAPPDATA%\Programs\EdifierMrVolume`, the Start menu shortcut and `%LOCALAPPDATA%\EdifierMrVolume` (log).

Command-line switches: `--show` opens the flyout (launching it again while it runs does the
same), `--exit` closes the running copy.

## Develop

```powershell
dotnet build src/EdifierMrVolume/EdifierMrVolume.csproj -p:Platform=x64
src\EdifierMrVolume\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\EdifierMrVolume.exe --show
```

Only one copy runs at a time, so exit the installed one first. Run the protocol tests:

```powershell
dotnet test tests/EdifierMrVolume.Core.Tests
```

### Tray icon not visible?

Windows 11 hides new tray icons. Turn it on under **Settings › Personalization › Taskbar ›
Other system tray icons › EdifierMrVolume**, or drag it from the hidden-icons area onto the taskbar.

### Phone app can't connect?

The speaker accepts one BLE control client at a time. Right-click the tray icon and untick
**Connected to speaker** while you use EDIFIER ConneX; tick it again afterwards.

The log lives in `%LOCALAPPDATA%\EdifierMrVolume\log.txt`.

## Layout

| Path | What |
| --- | --- |
| `src/EdifierMrVolume.Core` | Protocol (`EdifierProtocol`) and the BLE connection loop (`SpeakerConnection`) |
| `src/EdifierMrVolume` | WinUI 3 tray app: tray icon, flyout, autostart, log |
| `tests/EdifierMrVolume.Core.Tests` | Protocol tests using bytes captured from a real MR4.5 |
| `tools/install.ps1` | Publish and install for the current user |
| `tools/make-icon.ps1` | Draws the app icon (`src/EdifierMrVolume/Assets/app.ico`) |
| `tools/probe.py` | Python/Bleak probe used to verify the protocol |

## Protocol, verified on an MR4.5 (2026-11-15)

| Item | Value |
| --- | --- |
| Advertised name | `EDIFIER BLE`, manufacturer id `0x07E0` |
| Advertised search UUID | `00006303-0000-1000-8000-00805f9b34fb` |
| Control service | `48096303-1a48-11e9-ab14-d663bd873d93` (MR3: `4809f301-…`, MR5: `48093a01-…`) |
| Notify characteristic | `48090001-1a48-11e9-ab14-d663bd873d93` |
| Write characteristic | `48090002-1a48-11e9-ab14-d663bd873d93` (write / write-without-response) |
| Get volume (cmd `0x66`) | send `AA 0C 66 00 00 1C` |
| Volume reply | `BB EC 66 00 02 <max> <current> <checksum>`, e.g. `1E 13` = 19 of 30 |
| Set volume (cmd `0x67`) | send `AA 0C 67 00 01 <level> <checksum>`; ack `BB EC 67 00 01 01 10`, then the speaker pushes the new volume reply |
| Knob tracking | polling every 0.5 s picks up every knob step |

Packet frame: `AA <appCode> <cmd> <lenHi> <lenLo> <payload…> <checksum>`, checksum = sum of all
previous bytes & 0xFF. The MR4.5 with the 8625 DSP revision reportedly uses `…7c03…` instead of
`…6303…`; the app tries both.

### Probe script

```powershell
python -m venv .venv
.venv\Scripts\python.exe -m pip install bleak
.venv\Scripts\python.exe tools\probe.py              # find the speaker, read the volume
.venv\Scripts\python.exe tools\probe.py --watch 30   # print every knob change for 30 s
.venv\Scripts\python.exe tools\probe.py --set-test   # one step down and back (writes!)
```

Quit the tray app (or untick **Connected to speaker**) first, since only one client can connect.

## Credits

The protocol details come from these MIT-licensed reverse-engineering projects:

- [D4n13l3k00/EdifierControl](https://github.com/D4n13l3k00/EdifierControl): MR4.5 and MR3 UUIDs, the two-byte volume reply
- [Saikomantisu/omarchy-edifier-mr3](https://github.com/Saikomantisu/omarchy-edifier-mr3): MR3 service and discovery, knob push notifications
- [Devis99/omarchy-edifier-mr5](https://github.com/Devis99/omarchy-edifier-mr5): packet framing and command set
- [68776877/Edifier-IR-remote](https://github.com/68776877/Edifier-IR-remote): MR5 packet captures, one client at a time, 0–30 range

Not affiliated with or endorsed by Edifier.

## License

[MIT](LICENSE)
