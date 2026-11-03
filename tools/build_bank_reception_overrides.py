#!/usr/bin/env python3
"""Apply narrowly scoped AP patches to locally decompiled Bank reception scripts.

Game-derived sources/messages remain in the supplied analysis directory, outside
the repository. Compile the resulting patched flows with Atlus Script Compiler.
"""
from pathlib import Path
import argparse
import re
import shutil

FEATURE = 900171
GUARD_PROCEDURES = (
    "D03_BlockLeftRoot", "D03_BlockRightRoot",
    "D03_02_ANNOUNCE_01", "D03_02_LOCK_01_A", "D03_02_LOCK_01_B",
    "D03_02_ANNOUNCE_02", "D03_02_LOCK_02",
    "D03_02_ANNOUNCE_03", "D03_02_LOCK_03_A", "D03_02_LOCK_03_B",
    "D03_02_BLOCK_BACK_01", "D03_02_BLOCK_BACK_02",
    "D03_02_BLOCK_NPC_TALK_01", "D03_02_BLOCK_NPC_TALK_02",
    "D03_02_BLOCK_NPC_TALK_03",
)
DOOR_PROCEDURES = (
    "FLD_D03_02_3rdENTRY_LOCK_R", "FLD_D03_02_3rdENTRY_LOCK_L",
    "door_0646", "door_0647", "door_0648",
    "Door_EX_0642", "Door_EX_0643", "Door_EX_0645",
)


def insert_guard(source, procedure, action):
    pattern = rf"(void {re.escape(procedure)}\(\)\s*\{{)"
    replacement = (
        rf"\1\n    // AP-only: retain vanilla behavior outside the active Bank session.\n"
        f"    if (CMM_CHECK_ENABLE_FUNC({FEATURE}) == 1)\n"
        "    {\n" + action + "        return;\n    }\n"
    )
    patched, count = re.subn(pattern, replacement, source)
    if count != 1:
        raise ValueError(f"Expected exactly one procedure {procedure}; found {count}")
    return patched


def patch_script(source, name):
    if f"CMM_CHECK_ENABLE_FUNC({FEATURE})" in source:
        raise ValueError("Input must be an unpatched original decompile")
    if name == "FHIT_154_002_00":
        for procedure in GUARD_PROCEDURES:
            source = insert_guard(source, procedure, "")
    elif name == "FDOOR154_002":
        for procedure in DOOR_PROCEDURES:
            source = insert_guard(source, procedure, "        FLD_OPEN_DOOR();\n")
    else:
        raise ValueError(f"Unsupported script: {name}")
    # Keep the compiler's message lookup relative to the local generated flow.
    return source.replace(f'import("/{name}.msg");', f'import("{name}.msg");')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("analysis", type=Path)
    args = parser.parse_args()
    output = args.analysis / "patched"
    output.mkdir(parents=True, exist_ok=True)
    for name in ("FHIT_154_002_00", "FDOOR154_002"):
        patched = patch_script((args.analysis / f"{name}.flow").read_text(), name)
        (output / f"{name}.flow").write_text(patched)
        shutil.copy2(args.analysis / f"{name}.msg", output / f"{name}.msg")
        print(output / f"{name}.flow")


if __name__ == "__main__":
    main()
