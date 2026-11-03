#!/usr/bin/env python3
"""Prepare and verify a reusable P5R bootstrap save for AP sessions."""

from __future__ import annotations

import argparse
import hashlib
from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parents[1]
CHANGE_OF_HEART = ROOT.parent / "local-tools/p5r-ap/references/change-of-heart"
sys.path.insert(0, str(CHANGE_OF_HEART))

from core.editor import CONFIDANT_ARCANA_MAP, SaveEditor  # noqa: E402


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--empty-compendium", action="store_true")
    parser.add_argument("--name", default=None)
    parser.add_argument("--arsene-only", action="store_true")
    parser.add_argument("--reset-confidants", action="store_true")
    args = parser.parse_args()

    original = args.source.read_bytes()
    editor = SaveEditor(original)
    before_integrity = editor.integrity_report()
    if not before_integrity.get("ok"):
        raise SystemExit(f"source save failed integrity validation: {before_integrity}")

    before = editor.get_compendium()
    before_stock = editor.get_persona_stock(0)
    before_confidants = editor.get_confidant_ranks()

    if args.name:
        group = editor.parser.player_names.group_name_utf8 or "Phantom Thieves"
        result = editor.set_player_names(args.name, "", group)
        if result.get("status") != "ok":
            raise SystemExit(f"could not rename player: {result}")

    if args.empty_compendium:
        for persona_id in before["registered"]:
            result = editor.set_compendium_registration(persona_id, False)
            if result.get("status") != "success":
                raise SystemExit(f"could not unregister Persona {persona_id}: {result}")

    if args.arsene_only:
        arsene = next((entry for entry in before_stock if entry["persona_id"] == 201), None)
        if arsene is None:
            raise SystemExit("source save has no Arsene stock entry to preserve")
        result = editor.set_persona_stock_slot(
            0, 0, persona_id=arsene["persona_id"], level=arsene["level"],
            trait_id=arsene["trait_id"], exp=arsene["exp"], skills=arsene["skills"],
            stats=arsene["stats"], flags=arsene["flags"],
        )
        if result.get("status") != "success":
            raise SystemExit(f"could not put Arsene in Joker's first slot: {result}")
        for stock_slot in range(1, editor.PC31_STOCK_SLOTS):
            result = editor.set_persona_stock_slot(0, stock_slot, persona_id=0)
            if result.get("status") != "success":
                raise SystemExit(f"could not clear Joker stock slot {stock_slot}: {result}")

    if args.reset_confidants:
        for arcana_id in CONFIDANT_ARCANA_MAP.values():
            result = editor.set_confidant_rank(arcana_id, 0)
            if result.get("status") not in {"success", "noop"}:
                raise SystemExit(f"could not reset Confidant {arcana_id}: {result}")

    prepared = editor.save_to_bytes()
    verify = SaveEditor(prepared)
    after_integrity = verify.integrity_report()
    after = verify.get_compendium()
    after_stock = verify.get_persona_stock(0)
    after_confidants = verify.get_confidant_ranks()
    if not after_integrity.get("ok"):
        raise SystemExit(f"prepared save failed integrity validation: {after_integrity}")
    if args.empty_compendium and after["count"] != 0:
        raise SystemExit(f"prepared compendium is not empty: {after['count']} registrations")
    if args.arsene_only:
        occupied = [entry for entry in after_stock if not entry["empty"]]
        if len(occupied) != 1 or occupied[0]["persona_id"] != 201:
            raise SystemExit(f"prepared Joker stock is not Arsene-only: {occupied}")
    if args.reset_confidants and any(entry.get("rank", 0) for entry in after_confidants.values()):
        raise SystemExit("prepared save still has nonzero Confidant ranks")

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(prepared)
    print(f"Source SHA256:   {hashlib.sha256(original).hexdigest()}")
    print(f"Prepared SHA256: {hashlib.sha256(prepared).hexdigest()}")
    print(f"Compendium: {before['count']} -> {after['count']} registered")
    print(f"Joker stock: {sum(not x['empty'] for x in before_stock)} -> {sum(not x['empty'] for x in after_stock)}")
    print(f"Confidants with ranks: {sum(v.get('rank', 0) > 0 for v in before_confidants.values())} -> {sum(v.get('rank', 0) > 0 for v in after_confidants.values())}")
    print(f"Name: {verify.parser.header.fname} {verify.parser.header.lname}".rstrip())
    print(f"Integrity: {after_integrity}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
