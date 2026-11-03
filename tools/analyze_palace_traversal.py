#!/usr/bin/env python3
"""Report Palace minimap-reveal and traversal-related bit usage from decompiled FINI flows."""

from __future__ import annotations

import argparse
import re
from pathlib import Path


PROC_RE = re.compile(r"^void\s+([A-Za-z0-9_]+)\s*\([^)]*\)\s*$", re.MULTILINE)
BIT_RE = re.compile(r"BIT_(ON|OFF|CHK)\s*\(\(0x20000000\s*\+\s*(0x[0-9A-Fa-f]+|\d+)\)\)")
TRAVERSAL_NAME_RE = re.compile(
    r"DOOR|GATE|SHORTCUT|SHUTTER|BRIDGE|ELEVATOR|LIFT|LOCK|STAIR|PASSAGE",
    re.IGNORECASE,
)
MAP_NAME_RE = re.compile(r"MINI_MAP_SET", re.IGNORECASE)
DOOR_PRIMITIVE_RE = re.compile(r"FLD_(?:SET_DOOR_HIT|OPEN_DOOR_NO|HIT_SET_TYPE)")


def procedures(text: str):
    matches = list(PROC_RE.finditer(text))
    for index, match in enumerate(matches):
        start = match.end()
        end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        yield match.group(1), text[start:end]


def bits(body: str):
    return [(op, int(value, 0)) for op, value in BIT_RE.findall(body)]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("flows", type=Path)
    args = parser.parse_args()

    for major in (151, 153, 154, 155, 156, 157, 159, 162):
        print(f"[{major}]")
        for path in sorted(args.flows.glob(f"FINI_{major}_*.flow")):
            for name, body in procedures(path.read_text(errors="replace")):
                if not (
                    MAP_NAME_RE.search(name)
                    or TRAVERSAL_NAME_RE.search(name)
                    or DOOR_PRIMITIVE_RE.search(body)
                ):
                    continue
                found = bits(body)
                if found:
                    rendered = ", ".join(f"{op}:{value}" for op, value in found)
                    print(f"{path.name}:{name}: {rendered}")
        print()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
