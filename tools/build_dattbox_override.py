#!/usr/bin/env python3
"""Build a minimal DATTBOX override that removes rewards only from AP chests."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import struct


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_OUTPUT = ROOT.parent / "private-backups/persona-5-ap-local/src/P5RArchipelago/P5REssentials/CPK/base.cpk/field/ftd/DATTBOX.FTD"


def rows(pack: bytes, flags: bytes):
    table_offsets = [struct.unpack_from(">I", pack, 0x10 + i * 4)[0] for i in range(13)]
    for table_index, table_offset in enumerate(table_offsets):
        _, count = struct.unpack_from(">II", pack, table_offset + 4)
        for minor in range(count):
            record = struct.unpack_from(">H", pack, table_offset + 0x10 + minor * 16 + 2)[0]
            for slot in range(10):
                offset = 0x30 + record * 160 + slot * 16
                flag = struct.unpack_from(">I", flags, offset)[0]
                dattbox_index = struct.unpack_from(">H", flags, offset + 4)[0]
                if flag:
                    yield flag, f"F{150 + table_index}_{minor:03}", slot + 1, dattbox_index


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("dump", type=Path, help="Extracted base CPK root containing FIELD/FTD")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()

    ftd = args.dump / "FIELD/FTD"
    pack = (ftd / "FLDDNGPACK.FTD").read_bytes()
    flags = (ftd / "FLDOBJFLAG.FTD").read_bytes()
    dattbox = bytearray((ftd / "DATTBOX.FTD").read_bytes())
    catalog = json.loads((ROOT / "data/chests.json").read_text())["locations"]
    table = {flag: (field, slot, index) for flag, field, slot, index in rows(pack, flags)}

    indices: set[int] = set()
    catalog_flags = {entry["flag"] for entry in catalog}
    for entry in catalog:
        actual = table.get(entry["flag"])
        if actual is None or actual[:2] != (entry["field"], entry["slot"]):
            raise SystemExit(f"catalog/table mismatch for 0x{entry['flag']:08X}: {actual}")
        indices.add(actual[2])

    count = struct.unpack_from(">I", dattbox, 0x28)[0]
    if len(dattbox) != 0x34 + count * 4:
        raise SystemExit(f"unexpected DATTBOX shape: {len(dattbox)} bytes, {count} records")
    for index in indices:
        if not 0 <= index < count:
            raise SystemExit(f"DATTBOX index {index} is out of range")
        dattbox[0x34 + index * 4:0x38 + index * 4] = bytes.fromhex("40 00 01 00")

    collateral = sorted(
        (flag, field, slot, index) for flag, field, slot, index in rows(pack, flags)
        if flag not in catalog_flags and index in indices
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(dattbox)
    print(f"Patched {len(indices)} unique DATTBOX records for {len(catalog)} AP locations.")
    if collateral:
        print(f"Shared-record collateral ({len(collateral)} unverified table rows):")
        for flag, field, slot, index in collateral:
            print(f"  0x{flag:08X} {field} slot {slot}, DATTBOX {index}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
