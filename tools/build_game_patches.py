#!/usr/bin/env python3
"""Build runtime-ready binary deltas from private modified game resources."""
import bz2
import gzip
import argparse
import json
import struct
from pathlib import Path

import bsdiff4
from game_patch_format import digest, extracted_file

ROOT = Path(__file__).resolve().parents[1]


def runtime_patch(patch):
    # Keep BSDIFF's control/difference algorithm, using .NET's built-in gzip
    # instead of requiring a bzip2 library or Python on the player's machine.
    def number(data):
        value = int.from_bytes(data, "little")
        return -(value & ((1 << 63) - 1)) if value >> 63 else value
    control_size = number(patch[8:16])
    diff_size = number(patch[16:24])
    new_size = number(patch[24:32])
    control = bz2.decompress(patch[32:32 + control_size])
    diff = bz2.decompress(patch[32 + control_size:32 + control_size + diff_size])
    extra = bz2.decompress(patch[32 + control_size + diff_size:])
    return b"P5APDIF1" + struct.pack("<qqq", len(control), len(diff), new_size) + gzip.compress(control + diff + extra, mtime=0)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assets", type=Path, required=True, help="Private P5REssentials/CPK directory")
    parser.add_argument("--base", type=Path, required=True)
    parser.add_argument("--english", type=Path, required=True)
    args = parser.parse_args()
    output = ROOT / "patches"
    output.mkdir(exist_ok=True)
    entries = []
    for path in sorted(args.assets.rglob("*")):
        if not path.is_file():
            continue
        relative = path.relative_to(args.assets)
        archive, *parts = relative.parts
        original = extracted_file({"base.cpk": args.base, "en.cpk": args.english}[archive],
                                  str(Path(*parts))).read_bytes()
        modified = path.read_bytes()
        bsdiff = bsdiff4.diff(original, modified)
        assert bsdiff4.patch(original, bsdiff) == modified
        patch = runtime_patch(bsdiff)
        name = f"{len(entries):03d}.p5apdiff"
        (output / name).write_bytes(patch)
        entries.append(dict(path=relative.as_posix(), patch=name,
                            original_sha256=digest(original), modified_sha256=digest(modified),
                            patch_sha256=digest(patch)))
    if not entries:
        raise SystemExit("No private game resources found")
    (output / "manifest.json").write_text(json.dumps(dict(format=2, files=entries), indent=2) + "\n")
    for stale in output.glob("*.bsdiff"):
        stale.unlink()
    print(f"Built and round-trip verified {len(entries)} patches in {output}")


if __name__ == "__main__":
    main()
