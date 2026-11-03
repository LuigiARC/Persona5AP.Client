#!/usr/bin/env python3
"""Patch local original Laboratory decompiles; game sources stay outside the repo.

FINI_162_011 distinguishes roads (animation2 present) from obstacles
(animation0 absent). Override their final collision/animation stages only during
the durable AP Laboratory session, preserving switches and vanilla behavior.
"""
import argparse
from pathlib import Path
import re
import shutil

FEATURE = 900172
ROAD_PROCEDURES = ("SetNextAnimDisAppearanceRoad", "SetNextAnimAppearanceRoad",
                   "DisAppearanceAnimSetRoad", "AppearanceAnimSetRoad")
OBSTACLE_PROCEDURES = ("SetObstacleColAndLoopAnim", "DisAppearanceAnimSetObstacle",
                       "AppearanceAnimSetObstacle")


def patch_script(source):
    if f"CMM_CHECK_ENABLE_FUNC({FEATURE})" in source:
        raise ValueError("Use an unpatched original decompile")
    for procedure in ROAD_PROCEDURES + OBSTACLE_PROCEDURES:
        road = procedure in ROAD_PROCEDURES
        action = (
            f"\n    if (CMM_CHECK_ENABLE_FUNC({FEATURE}) == 1)\n    {{\n"
            "        if (sVar29 > 0)\n        {\n"
            f"            FLD_MODEL_ANIM(sVar29, {2 if road else 0}, 1, 0, 1);\n"
            f"            FLD_OBJ_MODEL_COLLIS(sVar29, {1 if road else 0});\n"
            "        }\n        return;\n    }\n"
        )
        source, count = re.subn(rf"(void {re.escape(procedure)}\(\)\s*\{{)",
                                lambda m: m[1] + action, source)
        if count != 1:
            raise ValueError(f"Expected exactly one {procedure}; found {count}")
    return source.replace('import("/FINI_162_011.msg");', 'import("FINI_162_011.msg");')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("analysis", type=Path)
    args = parser.parse_args()
    output = args.analysis / "patched"
    output.mkdir(parents=True, exist_ok=True)
    (output / "FINI_162_011.flow").write_text(
        patch_script((args.analysis / "FINI_162_011.flow").read_text()))
    shutil.copy2(args.analysis / "FINI_162_011.msg", output / "FINI_162_011.msg")


if __name__ == "__main__":
    main()
