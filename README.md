# mr45-volume

A Windows tray app that shows and sets the **hardware volume of Edifier MR4.5 studio monitors**
and follows the physical volume knob live. It talks to the speaker's Bluetooth LE control
interface (the one the EDIFIER ConneX phone app uses), so it doesn't depend on the Windows volume slider.

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
about 240 MB), installs it to `%LOCALAPPDATA%\Programs\Mr45Volume`, adds a **MR4.5 Volume**
Start menu shortcut and starts it. Rerun it after pulling changes: it closes the running copy
cleanly, so the speaker reconnects within seconds. Then tick **Start with Windows** in the tray menu.

To uninstall: untick **Start with Windows**, choose **Exit**, then delete
`%LOCALAPPDATA%\Programs\Mr45Volume`, the Start menu shortcut and `%LOCALAPPDATA%\Mr45Volume` (log).

Command-line switches: `--show` opens the flyout (launching it again while it runs does the
same), `--exit` closes the running copy.

## Develop

```powershell
dotnet build src/Mr45Volume/Mr45Volume.csproj -p:Platform=x64
src\Mr45Volume\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\Mr45Volume.exe --show
```

Only one copy runs at a time, so exit the installed one first. Run the protocol tests:

```powershell
dotnet test tests/Mr45Volume.Core.Tests
```

### Tray icon not visible?

Windows 11 hides new tray icons. Turn it on under **Settings › Personalization › Taskbar ›
Other system tray icons › Mr45Volume**, or drag it from the hidden-icons area onto the taskbar.

### Phone app can't connect?

The speaker accepts one BLE control client at a time. Right-click the tray icon and untick
**Connected to speaker** while you use EDIFIER ConneX; tick it again afterwards.

The log lives in `%LOCALAPPDATA%\Mr45Volume\log.txt`.

## Layout

| Path | What |
| --- | --- |
| `src/Mr45Volume.Core` | Protocol (`EdifierProtocol`) and the BLE connection loop (`SpeakerConnection`) |
| `src/Mr45Volume` | WinUI 3 tray app: tray icon, flyout, autostart, log |
| `tests/Mr45Volume.Core.Tests` | Protocol tests using bytes captured from a real MR4.5 |
| `tools/install.ps1` | Publish and install for the current user |
| `tools/make-icon.ps1` | Draws the app icon (`src/Mr45Volume/Assets/app.ico`) |
| `tools/probe_mr45.py` | Python/Bleak probe used to verify the protocol |

## Protocol, verified on an MR4.5 (2026-11-15)

| Item | Value |
| --- | --- |
| Advertised name | `EDIFIER BLE`, manufacturer id `0x07E0` |
| Advertised search UUID | `00006303-0000-1000-8000-00805f9b34fb` |
| Control service | `48096303-1a48-11e9-ab14-d663bd873d93` |
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
.venv\Scripts\python.exe tools\probe_mr45.py              # find the speaker, read the volume
.venv\Scripts\python.exe tools\probe_mr45.py --watch 30   # print every knob change for 30 s
.venv\Scripts\python.exe tools\probe_mr45.py --set-test   # one step down and back (writes!)
```

Quit the tray app (or untick **Connected to speaker**) first, since only one client can connect.

## Credits

The protocol details come from these MIT-licensed reverse-engineering projects:

- [D4n13l3k00/EdifierControl](https://github.com/D4n13l3k00/EdifierControl): MR4.5 UUIDs and the two-byte volume reply
- [Devis99/omarchy-edifier-mr5](https://github.com/Devis99/omarchy-edifier-mr5): packet framing and command set
- [68776877/Edifier-IR-remote](https://github.com/68776877/Edifier-IR-remote): one client at a time, 0–30 range

Not affiliated with or endorsed by Edifier.
