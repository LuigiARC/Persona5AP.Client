#!/usr/bin/env python3
"""Bypass Laboratory Examination quiz events in local original FHIT decompiles.

Use the native all-open return layout (MAPOBJ_SET uses local17=2 on positions1/2).
Do not invoke the completed-question lift path: it still requests actor switch
animations and reproduced the crash. Original game sources stay local.
"""
import argparse
from pathlib import Path
import re
import shutil

FEATURE = 900172
SWITCH_PROCEDURES = tuple(f"SeriSwich_{index:02d}" for index in range(1, 11))
QUIZ_PROCEDURES = ("FirstAnnouncement", "CheckFirstDisplay", "CheckSecondDisplay",
                   "CheckThirdDisplay", "AcceptanceEvent", "AcceptanceCheck",
                   "ThirdQuestionNotCheck", "ExtraHuddle01", "ExtraHuddle02",
                   "ExtraHuddle03", "EndAnnouncement_A", "EndAnnouncement_B",
                   "EndAnnouncement_C", "EndAnnouncement_E")


def patch_script(source):
    if f"CMM_CHECK_ENABLE_FUNC({FEATURE})" in source:
        raise ValueError("Use an unpatched original decompile")
    for procedure in (*QUIZ_PROCEDURES, *SWITCH_PROCEDURES, "StartUpConfirmation"):
        action = "        sVar335 = -1;\n" if procedure == "StartUpConfirmation" else ""
        guard = (f"\n    if (CMM_CHECK_ENABLE_FUNC({FEATURE}) == 1)\n    {{\n"
                 + action + "        return;\n    }\n")
        source, count = re.subn(rf"(void {re.escape(procedure)}\(\)\s*\{{)",
                                lambda m: m[1] + guard, source)
        if count != 1:
            raise ValueError(f"Expected exactly one {procedure}; found {count}")
    old = "if ((BIT_CHK((0x20000000 + 68)) == 1) && (sVar316 == 1))"
    if source.count(old) != 1:
        raise ValueError("Expected exactly one previous-lift block")
    source = source.replace(old,
        "if (((BIT_CHK((0x20000000 + 68)) == 1) && (sVar316 == 1)) && "
        f"(CMM_CHECK_ENABLE_FUNC({FEATURE}) != 1))")
    return source.replace('import("/FHIT_162_009_00.msg");', 'import("FHIT_162_009_00.msg");')


def patch_initializer(source):
    if f"CMM_CHECK_ENABLE_FUNC({FEATURE})" in source:
        raise ValueError("Use an unpatched original decompile")
    guard = (
        f"\n    if (CMM_CHECK_ENABLE_FUNC({FEATURE}) == 1)\n    {{\n"
        "        FLD_SET_LOCAL_COUNT((49152 + 17), 2);\n"
        "        InitAllSeriAnim();\n        return;\n    }\n"
    )
    source, count = re.subn(r"(void MAPOBJ_SET\(\)\s*\{)", lambda m: m[1] + guard, source)
    if count != 1:
        raise ValueError(f"Expected exactly one MAPOBJ_SET; found {count}")
    return source.replace('import("/FINI_162_009.msg");', 'import("FINI_162_009.msg");')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("analysis", type=Path)
    parser.add_argument("--initializers", type=Path, required=True)
    args = parser.parse_args()
    output = args.analysis / "patched"
    output.mkdir(parents=True, exist_ok=True)
    name = "FHIT_162_009_00"
    (output / f"{name}.flow").write_text(patch_script((args.analysis / f"{name}.flow").read_text()))
    shutil.copy2(args.analysis / f"{name}.msg", output / f"{name}.msg")
    name = "FINI_162_009"
    (output / f"{name}.flow").write_text(
        patch_initializer((args.initializers / f"{name}.flow").read_text()))
    shutil.copy2(args.initializers / f"{name}.msg", output / f"{name}.msg")


if __name__ == "__main__":
    main()
