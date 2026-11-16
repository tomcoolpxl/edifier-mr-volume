# Bluetooth protocol

How the app talks to Edifier MR speakers. Verified on an MR4.5 (firmware 1.0.0) in November 2026.
The sources are listed under Credits in the [README](../README.md#credits).

## Discovery

| Item | Value |
| --- | --- |
| Advertised name | `EDIFIER BLE`, manufacturer id `0x07E0` |
| Advertised search UUID | `00006303-0000-1000-8000-00805f9b34fb` |
| Control service | `48096303-1a48-11e9-ab14-d663bd873d93` (MR3: `4809f301-...`, MR5: `48093a01-...`) |
| Notify characteristic | `48090001-1a48-11e9-ab14-d663bd873d93` |
| Write characteristic | `48090002-1a48-11e9-ab14-d663bd873d93` |

The 8625 DSP revisions of the MR4.5 and MR5 reportedly use `...7c03...` instead of
`...6303...`; the app tries both.

## Packets

`AA <appCode> <cmd> <lenHi> <lenLo> <payload...> <checksum>`, where the checksum is the sum of
the bytes before it, & 0xFF. The app code is `0C`. Replies start with `BB EC`.

## Commands

| Command | Request | Reply on the MR4.5 | Used |
| --- | --- | --- | --- |
| `0x66` get volume | `AA 0C 66 00 00 1C` | `BB EC 66 00 02 1E 13 40`: max 30, now 19. Also sent on every knob step. | yes, every 5 s |
| `0x67` set volume | `AA 0C 67 00 01 <level> <sum>` | ack, then a `0x66` reply | yes |
| `0xC6` firmware | `AA 0C C6 00 00 7C` | `01 00 00` = 1.0.0 | Diagnostics |
| `0xC9` model name | `AA 0C C9 00 00 7F` | `EDIFIER MR4.5` | Diagnostics |
| `0xD5` sound mode and room tuning | `AA 0C D5 00 00 8B` | `00 02 00 1E 03 00 01`: mode 0 (Monitor), then six room-tuning bytes. Also sent when the speaker's button changes the mode. | yes, every 30 s |
| `0xC4` set sound mode | the 7 bytes from `0xD5` with the mode replaced, e.g. `AA 0C C4 00 07 01 02 00 1E 03 00 01 A6` (Music) | ack, then a `0xD5` | yes |
| `0x43` custom EQ | `AA 0C 43 00 00 F9` | 9 bands of `[index][freqHi][freqLo][gain]`, 62 Hz to 16 kHz, gain 0-12 (6 = flat) | yes, once per connection |
| `0x44` set an EQ band | `[slot][index][freqHi][freqLo][gain]`, e.g. `AA 0C 44 00 05 01 00 00 3E 0B 49` | ack | yes |
| `0xB1` power saving | `AA 0C B1 00 00 67` | `00` off, `01` on | yes |
| `0xB2` set power saving | `AA 0C B2 00 01 00 69` (off), `... 01 6A` (on) | the previous value | yes |
| `0xCE` power off | `AA 0C CE 00 00 84` | none; the speakers switch off | yes |
| `0xD1`/`0xD2`/`0xD3` shutdown timer | `AA 0C D3 00 00 89` | `00 00` = off | no |
| `0x61` input | `AA 0C 61 00 00 17` | `04 01`, meaning unknown | no |
| `0xD8` features | `AA 0C D8 00 00 8E` | 29 bytes, not decoded | no |
| `0xD7`, `0xF2`, `0xF7` | | no reply on the MR4.5 | no |

## Room tuning

The six bytes after the mode in `0xD5` and `0xC4`, ConneX's "Acoustic Tuning". Layout from
[omarchy-edifier-mr5](https://github.com/Devis99/omarchy-edifier-mr5), meanings from the MR4.5
manual.

| Byte | Meaning | Values |
| --- | --- | --- |
| index | calibration index | unknown, kept as read |
| byte0 | 1 resets the DSP calibration | kept as read |
| low cut | high-pass frequency | 20-100 Hz in 5 Hz steps |
| slope | low-cut steepness | 0-3 = 6, 12, 18, 24 dB per octave |
| acoustic space | bass cut for walls nearby | 0-4 = 0 to -4 dB |
| desktop | cuts the boost from a desk | 0 off, 1 on |

The app only changes the low cut, slope, acoustic space and desktop bytes, within these ranges,
and keeps the rest as the speaker reported them. The size of one EQ step in dB isn't published,
so the app shows steps.

## Probe script

`tools/probe.py` talks to the speaker directly. Exit the app (or turn off Connect to speaker)
first: only one client can connect.

```powershell
python -m venv .venv
.venv\Scripts\python.exe -m pip install bleak
.venv\Scripts\python.exe tools\probe.py                       # find the speaker, read the volume
.venv\Scripts\python.exe tools\probe.py --watch 30            # print knob changes for 30 s
.venv\Scripts\python.exe tools\probe.py --read D5 --read 43   # send queries, print the replies
.venv\Scripts\python.exe tools\probe.py --set-test            # one step down and back (writes)
.venv\Scripts\python.exe tools\probe.py --power-off           # switch the speakers off (writes)
```
