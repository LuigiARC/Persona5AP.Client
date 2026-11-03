#!/usr/bin/env python3
"""Package only mod code, documentation and patches; never local game assets."""

from __future__ import annotations

from pathlib import Path
import hashlib
import json
import xml.etree.ElementTree as ET
import shutil
import zipfile
from sync_catalogs import verify


ROOT = Path(__file__).resolve().parents[1]
MOD_DIRECTORY = ROOT / "dist" / "P5RArchipelago"
OUTPUT = ROOT / "dist" / "Persona 5 AP.zip"
BUILD_FILES = {
    "ModConfig.json", "P5RArchipelago.dll", "P5RArchipelago.deps.json",
    "Archipelago.MultiClient.Net.dll", "Newtonsoft.Json.dll",
    "Reloaded.Hooks.Definitions.dll", "Reloaded.Hooks.ReloadedII.Interfaces.dll",
    "Reloaded.Memory.Sigscan.Definitions.dll", "Reloaded.Memory.Sigscan.dll",
    "Reloaded.Memory.dll", "NOTICE.md", "Config.example.json",
    "CriFs.V2.Hook.Interfaces.dll", "CriFsV2Lib.Definitions.dll",
}


MIT_COPYRIGHTS = {
    "Daphoa-MIT.txt": "Copyright (c) 2025 Daphoa",
    "CRIT-MAGNET-MIT.txt": "Copyright (c) 2025 CRIT MAGNET",
    "ChangeOfHeart-MIT.txt": "Copyright (c) 2026 j0nny DiGITAL",
    "Archipelago-MIT.txt": "Copyright (c) 2017 LLCoolDave\nCopyright (c) 2025 Berserker66\nCopyright (c) 2022 CaitSith2\nCopyright (c) 2021 LegendaryLinux",
    "Archipelago.MultiClient.Net-MIT.txt": "Copyright (c) 2022 Hussein Farran, Jarno Westhof",
    "Newtonsoft.Json-MIT.txt": "Copyright (c) 2007 James Newton-King",
}


def package_licenses(destination):
    mit = (ROOT / "data/LICENSE").read_text(encoding="utf-8")
    permission = "Permission is hereby granted" + mit.split("Permission is hereby granted", 1)[1]
    for name, copyright_notice in MIT_COPYRIGHTS.items():
        (destination / name).write_text("MIT License\n\n" + copyright_notice + "\n\n" + permission, encoding="utf-8")
    assets_path = ROOT / "src/P5RArchipelago/obj/project.assets.json"
    if not assets_path.exists():
        raise SystemExit("Build the client first; NuGet restore metadata is required to collect library licenses.")
    assets = json.loads(assets_path.read_text())
    for key, library in assets["libraries"].items():
        if library.get("type") != "package":
            continue
        package = next((Path(folder) / library["path"] for folder in assets["packageFolders"]
                        if (Path(folder) / library["path"]).is_dir()), None)
        if package is None:
            raise SystemExit(f"Cannot locate restored package: {key}")
        nuspec = next(package.glob("*.nuspec"))
        license_node = next((node for node in ET.parse(nuspec).iter()
                             if node.tag.rsplit("}", 1)[-1] == "license"), None)
        if license_node is not None and license_node.get("type") == "file":
            shutil.copy2(package / license_node.text, destination / (key.lower().replace("/", "-") + ".txt"))
        elif license_node is None or license_node.text != "MIT":
            raise SystemExit(f"No supported license text found for {key}")


def main() -> None:
    verify()
    required = {"ModConfig.json", "P5RArchipelago.dll", "Archipelago.MultiClient.Net.dll"}
    present = {path.name for path in MOD_DIRECTORY.iterdir() if path.is_file()}
    missing = required - present
    if missing:
        raise SystemExit(f"Build the mod first; missing: {', '.join(sorted(missing))}")

    # Refuse polluted staging directories, including locally reconstructed overrides.
    unexpected = {path.name for path in MOD_DIRECTORY.iterdir()} - BUILD_FILES - {
        "P5RArchipelago.pdb", "CriFs.V2.Hook.Interfaces.pdb", "CriFsV2Lib.Definitions.pdb", "patches", "README.md",
        "LGPL-3.0.txt", "GPL-3.0.txt",
        "licenses", "LICENSE",
    }
    if unexpected:
        raise SystemExit(f"Remove local-only files from the package stage: {', '.join(sorted(unexpected))}")
    manifest = json.loads((ROOT / "patches/manifest.json").read_text(encoding="utf-8"))
    if manifest.get("format") != 2 or not manifest["files"]:
        raise SystemExit("Build game patches first")
    for entry in manifest["files"]:
        name = entry["patch"]
        if Path(name).name != name or not name.endswith(".p5apdiff"):
            raise SystemExit(f"Invalid patch filename: {name}")
        digest = hashlib.sha256((ROOT / "patches" / name).read_bytes()).hexdigest()
        if digest != entry["patch_sha256"]:
            raise SystemExit(f"Patch checksum mismatch: {name}")
    patches = MOD_DIRECTORY / "patches"
    if patches.exists():
        shutil.rmtree(patches)
    patches.mkdir()
    patch_names = {entry["patch"] for entry in manifest["files"]}
    for name in patch_names | {"manifest.json"}:
        if Path(name).name != name or (name != "manifest.json" and not name.endswith(".p5apdiff")):
            raise SystemExit(f"Invalid patch filename: {name}")
        shutil.copy2(ROOT / "patches" / name, patches / name)
    shutil.copy2(ROOT / "README.md", MOD_DIRECTORY / "README.md")
    shutil.copy2(ROOT / "LICENSE", MOD_DIRECTORY / "LICENSE")
    shutil.copy2(ROOT / "THIRD_PARTY_NOTICES.md", MOD_DIRECTORY / "NOTICE.md")
    license_directory = MOD_DIRECTORY / "licenses"
    if license_directory.exists():
        shutil.rmtree(license_directory)
    license_directory.mkdir()
    shutil.copy2(ROOT / "LICENSE", MOD_DIRECTORY / "GPL-3.0.txt")
    shutil.copy2(ROOT / "third_party/CriFsV2Lib.Definitions/LICENSE", MOD_DIRECTORY / "LGPL-3.0.txt")
    package_licenses(license_directory)
    files = [MOD_DIRECTORY / name for name in sorted(BUILD_FILES) if (MOD_DIRECTORY / name).is_file()]
    files += [MOD_DIRECTORY / "README.md"]
    files += [MOD_DIRECTORY / "LGPL-3.0.txt", MOD_DIRECTORY / "GPL-3.0.txt"]
    files += [MOD_DIRECTORY / "LICENSE"]
    files += [p for p in sorted(license_directory.rglob("*")) if p.is_file()]
    files += [patches / name for name in sorted(patch_names | {"manifest.json"})]
    with zipfile.ZipFile(OUTPUT, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in files:
            archive.write(path, path.relative_to(MOD_DIRECTORY))

    print(OUTPUT)


if __name__ == "__main__":
    main()
