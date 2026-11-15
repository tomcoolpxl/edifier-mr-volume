"""Try EQ presets on an Edifier MR speaker by ear, then put everything back.

The first run saves the speaker's current Custom EQ curve and sound mode to a JSON file.
`apply <preset>` writes the preset's 9 bands and switches the speaker to Custom so it can be
heard; `restore` writes the saved curve and sound mode back exactly. Every command is built
from what the speaker itself reports (band numbers, frequencies, the 7 sound-mode bytes);
nothing is typed by hand.

  python tools/eq_audition.py --address AA:BB:CC:DD:EE:FF deep
  python tools/eq_audition.py --address AA:BB:CC:DD:EE:FF restore

Close the tray app first: only one device can hold the speaker's control link.
"""

from __future__ import annotations

import argparse
import asyncio
import json
import sys
from pathlib import Path

from bleak import BleakClient

sys.path.insert(0, str(Path(__file__).parent))
from probe import NOTIFY_UUID, WRITE_UUID, build_v2, parse_v2  # noqa: E402

APP_CODE = 0x0C
FLAT = 6  # gain byte for 0 dB (range 0-12)

# Changes from flat, in EQ steps (probably dB), by band frequency in Hz. Sources: TODO.md,
# "Speaker facts for the EQ presets".
PRESETS: dict[str, dict[int, int]] = {
    "flat": {},
    "deep": {62: +3, 125: -1},
    "balanced": {125: -2, 2000: +2},
    "full": {62: +3, 125: -2, 2000: +2},
}


async def run(address: str, action: str, save_path: Path) -> int:
    async with BleakClient(address, timeout=20.0) as client:
        replies: list[bytes] = []

        def on_notify(_sender, data: bytearray) -> None:
            replies.append(bytes(data))

        notify = client.services.get_characteristic(NOTIFY_UUID)
        write = client.services.get_characteristic(WRITE_UUID)
        await client.start_notify(notify, on_notify)
        with_response = "write-without-response" not in write.properties

        async def send(cmd: int, payload: bytes = b"", wait: float = 0.8) -> list[dict]:
            replies.clear()
            await client.write_gatt_char(write, build_v2(APP_CODE, cmd, payload), response=with_response)
            await asyncio.sleep(wait)
            return [f for f in (parse_v2(r) for r in replies) if f and f["checksum_ok"]]

        def find(frames: list[dict], cmd: int) -> dict | None:
            return next((f for f in frames if f["command"] == cmd), None)

        eq = find(await send(0x43), 0x43)
        mode = find(await send(0xD5), 0xD5)
        if not eq or not mode or len(mode["payload"]) != 7:
            print("Could not read the EQ curve or the sound mode; nothing was changed.")
            return 2

        p = eq["payload"]
        flag, count = p[2], p[1]
        if count != 9 or len(p) < 3 + 4 * count:
            print(f"Unexpected EQ reply ({p.hex(' ')}); nothing was changed.")
            return 2
        bands = [(p[3 + 4 * i], (p[4 + 4 * i] << 8) | p[5 + 4 * i], p[6 + 4 * i]) for i in range(count)]

        if not save_path.exists():
            save_path.write_text(json.dumps({
                "flag": flag,
                "bands": [[index, freq, gain] for index, freq, gain in bands],
                "mode": mode["payload"].hex(),
            }, indent=2))
            print(f"Saved the current curve and sound mode to {save_path}")
        saved = json.loads(save_path.read_text())

        if action == "restore":
            gains = [gain for _, _, gain in saved["bands"]]
            mode_payload = bytes.fromhex(saved["mode"])
        else:
            changes = PRESETS[action]
            gains = [max(0, min(12, FLAT + changes.get(freq, 0))) for _, freq, _ in bands]
            mode_payload = bytes([2]) + mode["payload"][1:]  # Custom, tuning bytes echoed as reported

        for (index, freq, _), gain in zip(bands, gains):
            ack = find(await send(0x44, bytes([flag, index, freq >> 8, freq & 0xFF, gain]), 0.3), 0x44)
            if not ack or ack["payload"] != b"\x01":
                print(f"Band {freq} Hz not acknowledged; stopping. Run 'restore' next.")
                return 3
        if find(await send(0xC4, mode_payload), 0xC4) is None:
            print("Sound mode not acknowledged. Run 'restore' next.")
            return 3

        eq = find(await send(0x43), 0x43)
        mode = find(await send(0xD5), 0xD5)
        p = eq["payload"]
        now = [p[6 + 4 * i] for i in range(count)]
        names = ["Monitor", "Music", "Custom"]
        print(f"\n{action}: sound mode now {names[mode['payload'][0]] if mode['payload'][0] < 3 else mode['payload'][0]}")
        for (_, freq, _), gain in zip(bands, now):
            label = f"{freq / 1000:g} kHz" if freq >= 1000 else f"{freq} Hz"
            print(f"  {label:>7}: {gain - FLAT:+d}")
        ok = now == gains and mode["payload"] == mode_payload
        print("Read back: " + ("matches." if ok else "DOES NOT MATCH, run 'restore'."))
        return 0 if ok else 4


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--address", required=True)
    ap.add_argument("--save", default=str(Path.home() / ".edifier-eq-backup.json"),
                    help="where the original curve is kept (default: ~/.edifier-eq-backup.json)")
    ap.add_argument("action", choices=[*PRESETS, "restore"])
    args = ap.parse_args()
    return asyncio.run(run(args.address, args.action, Path(args.save)))


if __name__ == "__main__":
    sys.exit(main())
