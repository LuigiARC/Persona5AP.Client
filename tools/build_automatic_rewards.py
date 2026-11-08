"""Replace the AP-owned reserved procedure34 with a silent reward sync job.

Only original AP code is generated here. Input hub resources remain private.
"""
import argparse
from pathlib import Path
import re

FLOW = '''void grant_connected_confidant_abilities()
{
    if (CMM_CHECK_ENABLE_FUNC(900200) == 0) { return; }
    if (CMM_CHECK_ENABLE_FUNC(900131) == 1)
    {
        reset_ap_location_flags();
        CMM_CHECK_ENABLE_FUNC(900132);
    }
    CMM_CHECK_ENABLE_FUNC(900130);
    apply_ap_party_recruits();
    apply_ap_party_levels();
    CMM_CHECK_ENABLE_FUNC(900201);
}'''


def transform(source):
    pattern = r"void grant_connected_confidant_abilities\(\)\s*\{\s*CMM_CHECK_ENABLE_FUNC\(900138\);\s*\}"
    if FLOW in source:
        return source
    result, count = re.subn(pattern, lambda _: FLOW, source)
    if count != 1:
        raise ValueError("Expected exactly one reserved AP procedure34; no source changed")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    args.output.write_text(transform(args.source.read_text(encoding="utf-8")), encoding="utf-8")


if __name__ == "__main__":
    main()
