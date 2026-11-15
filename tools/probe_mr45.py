"""Read-only BLE probe for Edifier MR-series studio monitors (target: MR4.5).

What it does:
  1. Scans for Edifier BLE control advertisements (manufacturer id 0x07E0,
     name "EDIFIER BLE", or one of the known per-model search UUIDs).
  2. Connects to the chosen device and lists its GATT services.
  3. Sends the "get volume" query (command 0x66) and prints the reply.

By default it is read-only. Only --set-test writes: it lowers the volume one
step and restores it (command 0x67). It never sends EQ, input, LED, shutdown
or reset commands.

Protocol facts are taken from two MIT-licensed reverse-engineering projects:
  - github.com/D4n13l3k00/EdifierControl  (src/protocol/protocol.hpp)
  - github.com/Devis99/omarchy-edifier-mr5 (scripts/edifier_protocol.py)

Usage:
  python probe_mr45.py                 # scan, connect, query volume
  python probe_mr45.py --scan-only     # only list what is advertising
  python probe_mr45.py --address AA:BB:CC:DD:EE:FF
"""

from __future__ import annotations

import argparse
import asyncio
import sys

from bleak import BleakClient, BleakScanner
from bleak.backends.device import BLEDevice
from bleak.backends.scanner import AdvertisementData

EDIFIER_MANUFACTURER_ID = 0x07E0
NOTIFY_UUID = "48090001-1a48-11e9-ab14-d663bd873d93"
WRITE_UUID = "48090002-1a48-11e9-ab14-d663bd873d93"

# name -> (advertised search UUID, GATT service UUID)
PROFILES = {
    "MR3": ("0000f300-0000-1000-8000-00805f9b34fb", "4809f301-1a48-11e9-ab14-d663bd873d93"),
    "MR5": ("00003a01-0000-1000-8000-00805f9b34fb", "48093a01-1a48-11e9-ab14-d663bd873d93"),
    "MR4.5": ("00006303-0000-1000-8000-00805f9b34fb", "48096303-1a48-11e9-ab14-d663bd873d93"),
    "MR4 MKII": ("00006a03-0000-1000-8000-00805f9b34fb", "48096a03-1a48-11e9-ab14-d663bd873d93"),
    "MR4.5 (8625)": ("00007c03-0000-1000-8000-00805f9b34fb", "48097c03-1a48-11e9-ab14-d663bd873d93"),
    "MR4 MKII (8625)": ("00007d03-0000-1000-8000-00805f9b34fb", "48097d03-1a48-11e9-ab14-d663bd873d93"),
    "MR5 (8625)": ("00007e03-0000-1000-8000-00805f9b34fb", "48097e03-1a48-11e9-ab14-d663bd873d93"),
}
SEARCH_TO_MODEL = {s.lower(): m for m, (s, _) in PROFILES.items()}
SERVICE_TO_MODEL = {svc.lower(): m for m, (_, svc) in PROFILES.items()}

CMD_GET_VOLUME = 0x66
CMD_SET_VOLUME = 0x67  # only used by --set-test
# Different clients use different "app code" bytes (EdifierControl 0x0C,
# omarchy-edifier-mr5 0x06). Try both; the speaker should echo the command.
APP_CODES = (0x0C, 0x06)


def build_v2(app_code: int, command: int, payload: bytes = b"") -> bytes:
    body = bytes([0xAA, app_code, command, (len(payload) >> 8) & 0xFF, len(payload) & 0xFF]) + payload
    return body + bytes([sum(body) & 0xFF])


def parse_v2(data: bytes) -> dict | None:
    if len(data) < 6 or data[0] not in (0xAA, 0xBB, 0xCC, 0xDD):
        return None
    length = (data[3] << 8) | data[4]
    payload = data[5:5 + length]
    return {
        "header": data[0],
        "app_code": data[1],
        "command": data[2],
        "payload": payload,
        "checksum_ok": (sum(data[:-1]) & 0xFF) == data[-1],
    }


def model_from_adv(adv: AdvertisementData) -> str | None:
    for u in adv.service_uuids or []:
        if u.lower() in SEARCH_TO_MODEL:
            return SEARCH_TO_MODEL[u.lower()]
    return None


def is_edifier(device: BLEDevice, adv: AdvertisementData) -> bool:
    name = (adv.local_name or device.name or "").upper()
    return (
        EDIFIER_MANUFACTURER_ID in (adv.manufacturer_data or {})
        or "EDIFIER" in name
        or model_from_adv(adv) is not None
    )


async def scan(timeout: float) -> list[tuple[BLEDevice, AdvertisementData]]:
    print(f"Scanning for {timeout:.0f}s ... (speaker on, nothing else holding its BLE control link)")
    found = await BleakScanner.discover(timeout=timeout, return_adv=True)
    hits = [(d, a) for d, a in found.values() if is_edifier(d, a)]
    print(f"Saw {len(found)} BLE devices, {len(hits)} look like Edifier.\n")
    for d, a in hits:
        mfr = a.manufacturer_data.get(EDIFIER_MANUFACTURER_ID)
        print(f"  {d.address}  name={a.local_name or d.name!r}  rssi={a.rssi}")
        print(f"    model guess from advert: {model_from_adv(a) or 'unknown'}")
        print(f"    service uuids: {a.service_uuids}")
        if mfr is not None:
            print(f"    manufacturer 0x07E0 data: {mfr.hex(' ')}")
    return hits


async def probe(address: str, wait: float, watch: float = 0.0, poll: float = 0.5, set_test: bool = False) -> int:
    print(f"\nConnecting to {address} ...")
    async with BleakClient(address, timeout=20.0) as client:
        print("Connected. GATT services:")
        model = None
        for svc in client.services:
            tag = SERVICE_TO_MODEL.get(svc.uuid.lower())
            if tag:
                model = tag
            print(f"  service {svc.uuid}" + (f"   <-- {tag} control service" if tag else ""))
            for ch in svc.characteristics:
                print(f"    char {ch.uuid}  props={','.join(ch.properties)}")
        print(f"\nModel by GATT service: {model or 'NOT a known MR profile'}")

        notify_ch = client.services.get_characteristic(NOTIFY_UUID)
        write_ch = client.services.get_characteristic(WRITE_UUID)
        if notify_ch is None or write_ch is None:
            print("Control characteristics 48090001/48090002 not found; stopping (nothing was sent).")
            return 2

        replies: list[bytes] = []
        got = asyncio.Event()

        def on_notify(_sender, data: bytearray) -> None:
            replies.append(bytes(data))
            got.set()

        await client.start_notify(notify_ch, on_notify)
        use_response = "write-without-response" not in write_ch.properties

        volume = None
        working_code = None
        for app_code in APP_CODES:
            pkt = build_v2(app_code, CMD_GET_VOLUME)
            print(f"\nSending get-volume query (app code 0x{app_code:02X}): {pkt.hex(' ')}")
            got.clear()
            await client.write_gatt_char(write_ch, pkt, response=use_response)
            try:
                await asyncio.wait_for(got.wait(), timeout=wait)
            except asyncio.TimeoutError:
                print("  no reply")
                continue
            await asyncio.sleep(0.3)  # collect any trailing notifications
            for r in replies:
                p = parse_v2(r)
                print(f"  reply: {r.hex(' ')}  parsed={p}")
                v = decode_volume(p)
                if v:
                    volume = v
            replies.clear()
            if volume is not None:
                working_code = app_code
                break

        if volume is not None:
            print(f"\n>>> Speaker hardware volume: {volume[1]} / {volume[0]}")

        if volume is not None and set_test:
            # Step one notch down (or up at 0), read back, then restore the original level.
            original = volume[1]
            target = original - 1 if original > 0 else 1
            for step_to in (target, original):
                pkt = build_v2(working_code, CMD_SET_VOLUME, bytes([step_to]))
                print(f"\nSet-volume {step_to}: {pkt.hex(' ')}")
                replies.clear()
                await client.write_gatt_char(write_ch, pkt, response=use_response)
                await asyncio.sleep(0.5)
                for r in replies:
                    print(f"  reply: {r.hex(' ')}  parsed={parse_v2(r)}")
                replies.clear()
                await client.write_gatt_char(write_ch, build_v2(working_code, CMD_GET_VOLUME), response=use_response)
                await asyncio.sleep(0.5)
                for r in replies:
                    v = decode_volume(parse_v2(r))
                    print(f"  read back: {r.hex(' ')}" + (f"  -> volume {v[1]} / {v[0]}" if v else ""))
                replies.clear()

        if volume is not None and watch > 0:
            print(f"\nWatching for {watch:.0f}s - turn the volume knob now. Polling every {poll:.1f}s.")
            last = volume[1]
            loop = asyncio.get_running_loop()
            end = loop.time() + watch
            while loop.time() < end:
                got.clear()
                await client.write_gatt_char(write_ch, build_v2(working_code, CMD_GET_VOLUME), response=use_response)
                await asyncio.sleep(poll)
                for r in replies:
                    v = decode_volume(parse_v2(r))
                    if v and v[1] != last:
                        print(f"  {loop.time() - (end - watch):5.1f}s  volume {last} -> {v[1]} / {v[0]}")
                        last = v[1]
                    elif not v:
                        print(f"  other notification: {r.hex(' ')}")
                replies.clear()
            print(f"Done watching. Final volume: {last} / {volume[0]}")

        await client.stop_notify(notify_ch)

    if volume is None:
        print("\nNo volume reply decoded. Raw output above is still useful for the next step.")
        return 3
    return 0


def decode_volume(p: dict | None) -> tuple[int, int] | None:
    """Return (max, current). MR4.5 replies [max, current]; older single-byte replies are [current]."""
    if not p or p["command"] != CMD_GET_VOLUME or not p["checksum_ok"] or not p["payload"]:
        return None
    pl = p["payload"]
    return (pl[0], pl[1]) if len(pl) >= 2 else (30, pl[0])


async def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--scan-only", action="store_true")
    ap.add_argument("--address", help="connect to this BLE address, skipping selection")
    ap.add_argument("--scan-time", type=float, default=10.0)
    ap.add_argument("--wait", type=float, default=3.0, help="seconds to wait for each reply")
    ap.add_argument("--watch", type=float, default=0.0, help="keep polling volume for N seconds (turn the knob)")
    ap.add_argument("--poll", type=float, default=0.5, help="poll interval in seconds for --watch")
    ap.add_argument("--set-test", action="store_true",
                    help="WRITES: lower the volume one step, then restore it (tests command 0x67)")
    args = ap.parse_args()

    if args.address:
        return await probe(args.address, args.wait, args.watch, args.poll, args.set_test)

    hits = await scan(args.scan_time)
    if args.scan_only:
        return 0 if hits else 1
    if not hits:
        print("No Edifier advertisement found. Is the speaker on and in range, and is the phone app closed?")
        return 1
    # Prefer a device whose advert names an MR4.5 profile, then strongest signal.
    hits.sort(key=lambda h: ("MR4.5" not in (model_from_adv(h[1]) or ""), -(h[1].rssi or -999)))
    return await probe(hits[0][0].address, args.wait, args.watch, args.poll, args.set_test)


if __name__ == "__main__":
    sys.exit(asyncio.run(main()))
