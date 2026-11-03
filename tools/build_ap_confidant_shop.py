#!/usr/bin/env python3
"""Build the P5R FCLTABLE override containing the native AP Confidant shop."""

from __future__ import annotations

import argparse
from pathlib import Path
import shutil
import struct
import subprocess


ROOT = Path(__file__).resolve().parents[1]
PAKPACK = ROOT / "local-tools/p5r-shop-editor/P5(R) Shop Editor/PAKPack/PAKPack.exe"
SHOP_ID = 11
MARKER_FIRST = 0x0011
MARKER_COUNT = 24  # 23 Confidants plus Return to Mementos Hub.
ROW_SIZE = 40
DATA_OFFSET = 0x30


def be32(data: bytearray, offset: int) -> int:
    return struct.unpack_from(">I", data, offset)[0]


def put_be32(data: bytearray, offset: int, value: int) -> None:
    struct.pack_into(">I", data, offset, value)


def marker_row(item_id: int) -> bytes:
    row = bytearray(ROW_SIZE)
    row[0] = 0xFF
    struct.pack_into(">H", row, 2, item_id)
    row[4] = 1
    row[5:9] = bytes((4, 1, 3, 31))
    row[9:12] = bytes((99, 99, 99))
    row[12:14] = b"\xff\xff"
    struct.pack_into(">i", row, 24, -1)
    struct.pack_into(">i", row, 32, 100)
    return bytes(row)


def patch_items(path: Path) -> None:
    data = bytearray(path.read_bytes())
    entry_count = be32(data, 0x28)
    data_size = be32(data, 0x24)
    if data_size != entry_count * ROW_SIZE or len(data) < DATA_OFFSET + data_size:
        raise RuntimeError("Unexpected fclPublicShopItemTable.ftd layout")

    position = DATA_OFFSET
    shop = 0
    insert_at = None
    item_count = 0
    for _ in range(entry_count):
        if data[position] == 0x9D:
            if shop == SHOP_ID:
                if item_count != 0:
                    raise RuntimeError(f"Reserved shop {SHOP_ID} is no longer empty")
                insert_at = position
                break
            shop += 1
            item_count = 0
        else:
            item_count += 1
        position += ROW_SIZE
    if insert_at is None:
        raise RuntimeError(f"Could not find reserved shop {SHOP_ID}")

    rows = b"".join(marker_row(MARKER_FIRST + index) for index in range(MARKER_COUNT))
    data[insert_at:insert_at] = rows
    put_be32(data, 0x08, be32(data, 0x08) + len(rows))
    put_be32(data, 0x24, data_size + len(rows))
    put_be32(data, 0x28, entry_count + MARKER_COUNT)
    path.write_bytes(data)


def patch_name(path: Path) -> None:
    data = bytearray(path.read_bytes())
    offset = DATA_OFFSET + SHOP_ID * 48
    encoded = "Archipelago".encode("shift_jis")
    data[offset:offset + 48] = encoded + bytes(48 - len(encoded))
    path.write_bytes(data)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path, help="Vanilla EN/INIT/FCLTABLE.BIN")
    parser.add_argument("output", type=Path, help="Output FCLTABLE.BIN")
    args = parser.parse_args()

    work = ROOT / "local-tools/p5r-ap/build/ap-confidant-shop"
    if work.exists():
        shutil.rmtree(work)
    work.mkdir(parents=True)
    subprocess.run(["mono", str(PAKPACK), "unpack", str(args.source), str(work)], check=True)
    patch_items(work / "fclPublicShopItemTable.ftd")
    patch_name(work / "fclPublicShopName.ftd")

    args.output.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(
        ["mono", str(PAKPACK), "replace", str(args.source), str(work), str(args.output)],
        check=True,
    )
    print(args.output)


if __name__ == "__main__":
    main()
