#!/usr/bin/env python3
"""Synchronize or verify the client's versioned APWorld catalog snapshot."""
import argparse
import ast
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
NAMES = ("chests.json", "palaces.json", "confidant_abilities.json", "personas.json", "tv_bundles.json")
MODULES = {
    "chests.json": "locations.py",
    "palaces.json": "palaces.py",
    "confidant_abilities.json": "confidant_abilities.py",
    "personas.json": "personas.py",
    "tv_bundles.json": "items.py",
}


def encoded_catalog(world, name):
    path = world / MODULES[name]
    tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
    for node in tree.body:
        if isinstance(node, ast.Assign) and any(isinstance(t, ast.Name) and t.id == "CATALOG" for t in node.targets):
            document = ast.literal_eval(node.value)
            return (json.dumps(document, indent=2) + "\n").encode("utf-8")
    raise ValueError(f"No literal CATALOG table found: {path}")


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify():
    manifest = json.loads((ROOT / "data/catalogs.json").read_text())
    if manifest["game"] != "Persona 5 Royal (luigiarc)" or manifest["source_modules"] != MODULES:
        raise ValueError("Catalog snapshot game or source-module mapping differs from this client")
    if set(manifest["files"]) != set(NAMES):
        raise ValueError("Catalog snapshot file list differs from the expected protocol catalogs")
    for name, expected in manifest["files"].items():
        if digest(ROOT / "data" / name) != expected:
            raise ValueError(f"Catalog snapshot checksum mismatch: {name}")
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archipelago", type=Path, nargs="?", help="Optional Archipelago fork checkout")
    parser.add_argument("--check", action="store_true", help="Verify without changing files")
    args = parser.parse_args()
    if args.check:
        manifest = verify()
        if args.archipelago:
            for name in NAMES:
                if hashlib.sha256(encoded_catalog(args.archipelago / "worlds/p5r-luigiarc", name)).hexdigest() != manifest["files"][name]:
                    raise ValueError(f"Client and APWorld catalogs differ: {name}")
        print("Client catalog snapshot verified.")
        return
    if not args.archipelago:
        parser.error("Supply the Archipelago fork checkout, or use --check")
    world = args.archipelago / "worlds/p5r-luigiarc"
    contents = {name: encoded_catalog(world, name) for name in NAMES}
    metadata = json.loads((world / "archipelago.json").read_text())
    output = ROOT / "data"
    output.mkdir(exist_ok=True)
    for name, content in contents.items():
        (output / name).write_bytes(content)
    manifest = {"game": metadata["game"], "apworld_version": metadata["world_version"],
                "source_path": "worlds/p5r-luigiarc", "source_modules": MODULES,
                "files": {name: digest(output / name) for name in NAMES}}
    (output / "catalogs.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print("Updated five client catalogs from the APWorld fork.")


if __name__ == "__main__":
    main()
