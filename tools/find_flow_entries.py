#!/usr/bin/env python3
"""Read-only PE lookup of native flow-table functions by their ASCII names."""
from pathlib import Path
import struct
import argparse

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("exe", type=Path)
    parser.add_argument("names", nargs="+")
    args = parser.parse_args()
    data = args.exe.read_bytes()
    pe = struct.unpack_from("<I", data, 60)[0]
    count, optional_size = struct.unpack_from("<H", data, pe + 6)[0], struct.unpack_from("<H", data, pe + 20)[0]
    base = struct.unpack_from("<Q", data, pe + 24 + 24)[0]
    sections = []
    for i in range(count):
        entry = pe + 24 + optional_size + 40*i
        virtual_size, rva, size, offset = struct.unpack_from("<IIII", data, entry + 8)
        sections.append((offset, size, rva))
    def address(offset):
        for start, size, rva in sections:
            if start <= offset < start + size:
                return base + rva + offset - start
        raise ValueError(offset)
    for name in args.names:
        start = 0
        while (offset := data.find(name.encode("ascii") + b"\0", start)) >= 0:
            addr = address(offset)
            print(f"{name}: string 0x{addr:X}")
            cursor = 0
            while (ref := data.find(struct.pack("<Q", addr), cursor)) >= 0:
                if ref >= 16:
                    function, argc = struct.unpack_from("<QQ", data, ref-16)
                    if base <= function < base + 0x20000000 and argc < 32:
                        print(f"  table 0x{address(ref-16):X} function 0x{function:X} argc {argc}")
                cursor = ref+1
            start = offset+1

if __name__ == "__main__":
    main()
