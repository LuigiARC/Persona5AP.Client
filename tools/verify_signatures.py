#!/usr/bin/env python3
"""Read-only scanner for every P5R executable signature used by the test mod."""

from __future__ import annotations

import argparse
from pathlib import Path


PATTERNS = {
    "MessageWindowBlocked": "48 83 EC 28 48 83 79 70 00 75 42 48 8D 05 ?? ?? ?? ?? 48 8D 15 ?? ?? ?? ?? 0F 1F 80 00 00 00 00",
    "DrawPlainText": "48 83 EC 68 83 3D 21 85 3A 01 01 0F 28 D8 F3 0F 10 05",
    "DrawItemHelp": "48 8B C4 48 81 EC 98 00 00 00 66 0F 6F 1D EE 2A AB 00 45 8B D1",
    "GameActionInput": "48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 18 48 89 7C 24 20 41 56 48 83 EC 20 41 0F B7 F8 8B F2 48 8B E9 66 45 85 C0",
    "FieldPadLock": "48 83 EC 28 E8 ?? ?? ?? ?? 48 85 C0 0F 84 BA 00 00 00 48 89 5C 24 30 48 8B 58 48 48 85 DB",
    "GenericSelectorInput": "40 57 48 81 EC A0 00 00 00 48 8B F9 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 85 C0 74 15 48 8B 40 48",
    "SetMessageVariable": "48 89 5C 24 10 48 89 6C 24 18 48 89 74 24 20 57 48 83 EC 30 48 8D 35 ?? ?? ?? ?? 49 63 C1 48 63 FA 48 63 D9",
    "StartOpenChest": "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 41 56 41 57 48 83 EC 60 48 8B FA 4C 8B F9",
    "CompleteOpenChest": "40 53 48 83 EC 20 48 8B 59 48 48 8B 4B 20 48 85 C9 74 ?? E8 ?? ?? ?? ?? 48 8B 4B 30 48 85 C9 74 ?? E8 ?? ?? ?? ?? 48 8B CB 48 83 C4 20 5B E9 ?? ?? ?? ??",
    "GetItemName": "4C 8B DC 48 83 EC 78 48 8D 05 42 2E 01 00 BA FF 0F 00 00",
    "GetItemNum": "4C 8B DC 48 83 EC 78 48 8D 05 D2 F3 FF FF BA FF 0F 00 00",
    "GetItemPrice": "4C 8B DC 48 83 EC 78 48 8D 05 92 E5 FF FF 49 C7 43 C8 00 00 00 00",
    "SetItemNum": "4C 8B DC 49 89 5B ?? 57 48 83 EC 70 48 8D 05 ?? ?? ?? ??",
    "FLD_GET_TBOX_FLAG": "48 83 EC 28 E8 ?? ?? ?? ?? 48 85 C0 74 ?? 4C 8B 48 ?? 4D 85 C9 74 ?? 49 8B 91 ?? ?? ?? ??",
    "FlowCommandData reference": "4C 8B 05 ?? ?? ?? ?? 41 8B 50 ?? 29 CA",
    "FlowFunctionGroups": "4C 8D 3D ?? ?? ?? ?? 8B F5",
    "BIT_CHK": "4C 8D 05 ?? ?? ?? ?? 33 C0 49 8B D0 0F 1F 40 00 39 0A 74 ?? FF C0 48 83 C2 08 83 F8 10 72 ?? 8B D1",
    "AppStorageRead": "48 89 5C 24 ?? 57 48 83 EC 60 89 CB",
    "BIT_ON": "40 53 48 83 EC 20 31 C9 E8 ?? ?? ?? ?? B9 01 00 00 00",
    "CMM_CHECK_ENABLED": "40 53 55 56 41 54 41 56 48 83 EC 20",
    "CMM_SET_LV": "66 85 C9 0F 84 ?? ?? ?? ?? 57",
    "CMM_OPEN": "48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 48 8B 35 ?? ?? ?? ?? 33 FF 0F B7 D9",
    "RunFlowProcedure": "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 48 89 7C 24 ?? 41 56 48 83 EC 60 41 89 CE",
    "SequenceRoot": "48 8B 05 ?? ?? ?? ?? 48 8B 48 48 0F B6 01 24 01 C3",
    "EndBattle": "40 53 55 41 57 48 81 EC 80 00 00 00",
    "SET_PERSONA_LV": "48 89 5C 24 ?? 55 48 81 EC E0 00 00 00",
    "JokerGrowthCommit": "48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 18 57 41 56 41 57 48 83 EC 20 8B 1D ?? ?? ?? ?? 48 8B F2 03 59 04",
}


def compile_pattern(text: str) -> tuple[bytes, bytes]:
    values = []
    masks = []
    for token in text.split():
        values.append(0 if token == "??" else int(token, 16))
        masks.append(0 if token == "??" else 0xFF)
    return bytes(values), bytes(masks)


def find_all(data: bytes, values: bytes, masks: bytes) -> list[int]:
    fixed_indexes = [index for index, mask in enumerate(masks) if mask]
    # Anchoring on the rarest fixed byte avoids millions of candidate checks
    # for patterns beginning with common x64 opcodes such as 0x48.
    anchor = min(fixed_indexes, key=lambda index: data.count(values[index]))
    needle = bytes([values[anchor]])
    matches = []
    offset = 0
    while True:
        found = data.find(needle, offset)
        if found < 0:
            return matches
        start = found - anchor
        if start >= 0 and start + len(values) <= len(data):
            if all(not mask or data[start + index] == value for index, (value, mask) in enumerate(zip(values, masks))):
                matches.append(start)
        offset = found + 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("executable", type=Path)
    args = parser.parse_args()
    data = args.executable.read_bytes()

    failed = False
    for name, pattern in PATTERNS.items():
        values, masks = compile_pattern(pattern)
        matches = find_all(data, values, masks)
        rendered = ", ".join(f"file+0x{match:X}" for match in matches) or "none"
        print(f"{name}: {len(matches)} match(es): {rendered}")
        failed |= len(matches) != 1
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
