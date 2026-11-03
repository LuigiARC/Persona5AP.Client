#!/usr/bin/env python3
"""Print the exception and AMD64 register context from a Windows minidump."""

from __future__ import annotations

import argparse
from pathlib import Path
import struct


def u32(data: bytes, offset: int) -> int:
    return struct.unpack_from("<I", data, offset)[0]


def u64(data: bytes, offset: int) -> int:
    return struct.unpack_from("<Q", data, offset)[0]


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("dump", type=Path)
    parser.add_argument("--stack", action="store_true", help="Print captured stack qwords for the exception thread")
    parser.add_argument("--memory", nargs="*", type=lambda value: int(value, 0), default=[],
                        help="Print 128 captured bytes at each virtual address")
    args = parser.parse_args()
    data = args.dump.read_bytes()
    if data[:4] != b"MDMP":
        raise SystemExit("Not a minidump")

    stream_count = u32(data, 8)
    directory_rva = u32(data, 12)
    streams: dict[int, tuple[int, int]] = {}
    for index in range(stream_count):
        entry = directory_rva + index * 12
        stream_type = u32(data, entry)
        streams[stream_type] = (u32(data, entry + 4), u32(data, entry + 8))
    print(f"streams={stream_count} types={sorted(streams)}")
    if args.memory:
        ranges = []
        if 5 in streams:
            _, memory_rva = streams[5]
            for index in range(u32(data, memory_rva)):
                entry = memory_rva + 4 + index * 16
                ranges.append((u64(data, entry), u32(data, entry + 8), u32(data, entry + 12)))
        if 9 in streams:
            _, memory_rva = streams[9]
            rva = u64(data, memory_rva + 8)
            for index in range(u64(data, memory_rva)):
                entry = memory_rva + 16 + index * 16
                size = u64(data, entry + 8)
                ranges.append((u64(data, entry), size, rva))
                rva += size
        for address in args.memory:
            for start, size, rva in ranges:
                if start <= address < start + size:
                    for pos in range(address, min(address + 128, start + size - 7), 8):
                        print(f"memory 0x{pos:016X}: 0x{u64(data, rva+pos-start):016X}")
                    break
            else:
                print(f"memory 0x{address:016X}: not captured")

    if 6 not in streams:
        raise SystemExit("No exception stream")
    _, exception_rva = streams[6]
    thread_id = u32(data, exception_rva)
    record = exception_rva + 8
    exception_code = u32(data, record)
    exception_flags = u32(data, record + 4)
    exception_address = u64(data, record + 16)
    parameter_count = u32(data, record + 24)
    parameters = [u64(data, record + 32 + index * 8) for index in range(parameter_count)]
    context_size = u32(data, exception_rva + 160)
    context_rva = u32(data, exception_rva + 164)
    print(
        f"thread={thread_id} code=0x{exception_code:08X} flags=0x{exception_flags:X} "
        f"address=0x{exception_address:016X} parameters={[hex(value) for value in parameters]}"
    )
    print(f"context size={context_size} rva=0x{context_rva:X}")
    if context_size >= 256:
        names = [
            ("rax", 120), ("rcx", 128), ("rdx", 136), ("rbx", 144),
            ("rsp", 152), ("rbp", 160), ("rsi", 168), ("rdi", 176),
            ("r8", 184), ("r9", 192), ("r10", 200), ("r11", 208),
            ("r12", 216), ("r13", 224), ("r14", 232), ("r15", 240),
            ("rip", 248),
        ]
        for name, offset in names:
            print(f"{name}=0x{u64(data, context_rva + offset):016X}")
        if args.stack and 3 in streams:
            _, threads_rva = streams[3]
            for index in range(u32(data, threads_rva)):
                entry = threads_rva + 4 + index * 48
                if u32(data, entry) != thread_id:
                    continue
                start = u64(data, entry + 24)
                size = u32(data, entry + 32)
                rva = u32(data, entry + 36)
                rsp = u64(data, context_rva + 152)
                offset = max(0, rsp - start)
                for pos in range(offset, min(size, offset + 512), 8):
                    print(f"stack+0x{pos-offset:03X} 0x{start+pos:016X}: 0x{u64(data, rva+pos):016X}")


if __name__ == "__main__":
    main()
