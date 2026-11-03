#!/usr/bin/env python3
"""Regenerate the compact legacy-offer stubs used beside the native F9 shop.

The public-shop controller gets row names, prices, recipients, and items from managed hooks.
Embedding the older dialogue implementation's 157 messages made the BF exceed the native
hotkey runner's safe size, even though those messages were no longer reachable.
"""

from __future__ import annotations

import json
from collections import defaultdict
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
CATALOG = ROOT / "data/confidant_abilities.json"
FLOW = ROOT.parent / "private-backups/persona-5-ap-local/src/P5RArchipelago/Hub/FSCR8000_002_000.flow"
MESSAGE = ROOT.parent / "private-backups/persona-5-ap-local/src/P5RArchipelago/Hub/FSCR8000_002_000.msg"
SELECTABLE_CMM_IDS = set(range(1, 22)) | {33, 35}


def replace_between(text: str, begin: str, end: str, replacement: str) -> str:
    start = text.index(begin)
    finish = text.index(end, start) + len(end)
    return text[:start] + replacement + text[finish:]


def main() -> None:
    document = json.loads(CATALOG.read_text(encoding="utf-8"))
    offers: dict[tuple[int, int], list[str]] = defaultdict(list)
    for location in document["locations"]:
        for cmm_id in location["trigger_cmm_ids"]:
            if cmm_id in SELECTABLE_CMM_IDS:
                offers[(cmm_id, location["rank"])].append(location["name"])

    # Procedure 31 must remain present so all later procedure indices stay stable, but the
    # old dialogue offer table is no longer reachable from the native public-shop UI.
    flow_lines = [
        "// BEGIN GENERATED CONFIDANT SHOP OFFERS",
        "void show_confidant_offer(int target_rank, int cmm_id)",
        "{",
        "    MSG_WND_DSP();",
        "    MSG(MSG_CONFIDANT_OFFER_UNKNOWN, 0);",
        "    MSG_WND_CLS();",
        "}",
        "// END GENERATED CONFIDANT SHOP OFFERS",
    ]

    message_lines = [
        "// BEGIN GENERATED CONFIDANT SHOP MESSAGES",
        "// Native shop rows are supplied by managed hooks; no per-offer dialogue is embedded.",
        "// END GENERATED CONFIDANT SHOP MESSAGES",
    ]

    flow = replace_between(
        FLOW.read_text(encoding="utf-8"),
        "// BEGIN GENERATED CONFIDANT SHOP OFFERS",
        "// END GENERATED CONFIDANT SHOP OFFERS",
        "\n".join(flow_lines),
    )
    message = replace_between(
        MESSAGE.read_text(encoding="utf-8"),
        "// BEGIN GENERATED CONFIDANT SHOP MESSAGES",
        "// END GENERATED CONFIDANT SHOP MESSAGES",
        "\n".join(message_lines),
    )
    FLOW.write_text(flow, encoding="utf-8")
    MESSAGE.write_text(message, encoding="utf-8")
    print(f"Compacted {len(offers)} legacy Confidant offers into one safe fallback message.")


if __name__ == "__main__":
    main()
