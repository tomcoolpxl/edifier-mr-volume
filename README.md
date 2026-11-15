# mr45-volume

A Windows tray app that shows and sets the **hardware volume of Edifier MR4.5 studio monitors**,
and follows the physical volume knob live. It talks to the speaker's Bluetooth LE control
interface (the one the EDIFIER ConneX phone app uses), so it doesn't depend on the Windows volume slider.

Status: protocol verified on real hardware (read volume, live knob tracking).
The WinUI 3 tray app is in progress.

## Verified on an MR4.5 (2026-11-15)

| Item | Value |
| --- | --- |
| Advertised name | `EDIFIER BLE`, manufacturer id `0x07E0` |
| Advertised search UUID | `00006303-0000-1000-8000-00805f9b34fb` |
| Control service | `48096303-1a48-11e9-ab14-d663bd873d93` |
| Notify characteristic | `48090001-1a48-11e9-ab14-d663bd873d93` |
| Write characteristic | `48090002-1a48-11e9-ab14-d663bd873d93` (write / write-without-response) |
| Get volume (cmd `0x66`) | send `AA 0C 66 00 00 1C` |
| Reply | `BB EC 66 00 02 <max> <current> <checksum>`, for example `1E 13` = 19 of 30 |
| Pairing | not needed in Windows settings |
| Knob tracking | polling every 0.5 s picks up every knob step |

Packet frame: `AA <appCode> <cmd> <lenHi> <lenLo> <payload…> <checksum>`, checksum = sum of all
previous bytes & 0xFF. Not yet tested: set volume (cmd `0x67`, payload `[volume]`).

The MR4.5 with the 8625 DSP revision reportedly uses `…7c03…` instead of `…6303…`.

## Read-only probe

`probe_mr45.py` scans for the speaker, lists its GATT services and reads the volume. It only sends
the get-volume query.

```powershell
python -m venv .venv
.venv\Scripts\python.exe -m pip install bleak
.venv\Scripts\python.exe probe_mr45.py              # find the speaker and read the volume
.venv\Scripts\python.exe probe_mr45.py --watch 30   # print every knob change for 30 s
```

Close EDIFIER ConneX on your phone first. The speaker accepts one BLE control client at a time.

## Credits

The protocol details come from these MIT-licensed reverse-engineering projects:

- [D4n13l3k00/EdifierControl](https://github.com/D4n13l3k00/EdifierControl): MR4.5 UUIDs and the two-byte volume reply
- [Devis99/omarchy-edifier-mr5](https://github.com/Devis99/omarchy-edifier-mr5): packet framing and the command set
- [68776877/Edifier-IR-remote](https://github.com/68776877/Edifier-IR-remote): one client at a time, 0–30 range

Not affiliated with or endorsed by Edifier.
