#!/usr/bin/env python3
"""Bundle client source and pinned dependency source for a binary release."""
import argparse
from io import BytesIO
from pathlib import Path
import re
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SOURCES = [{'name': 'Reloaded.Memory',
  'url': 'https://github.com/Reloaded-Project/Reloaded.Memory',
  'revision': '5b0e3867b75421b16099cd86de88a73ef3b2d37a'},
 {'name': 'Reloaded.Memory.SigScan',
  'url': 'https://github.com/Reloaded-Project/Reloaded.Memory.SigScan',
  'revision': '35063d046c975b9832a9ddb3f3d50cebce152c0a'},
 {'name': 'Reloaded.Hooks',
  'url': 'https://github.com/Reloaded-Project/Reloaded.Hooks',
  'revision': '5b6f2904e0d2f59d6a6274a341a41a44b5bd6618'},
 {'name': 'Reloaded.SharedLib.Hooks',
  'url': 'https://github.com/Sewer56/Reloaded.SharedLib.Hooks',
  'revision': 'b8f2118a13ef1846e3bfa5cf466f7df4083b7d52'},
 {'name': 'Archipelago.MultiClient.Net',
  'url': 'https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net',
  'revision': '3113ea01d1b0db22ada93b5f35c6d4272a8002b2'},
 {'name': 'Reloaded.Project.Configurations',
  'url': 'https://github.com/Reloaded-Project/Reloaded.Project.Configurations',
  'revision': 'cb189c6330dbb525f587884f0619c0bdc84ddef9',
  'destination': 'Reloaded.Memory/src/Reloaded.Project.Configurations'}]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, default=ROOT / ".source-cache")
    parser.add_argument("--fetch", action="store_true", help="Fetch public pinned sources into the cache")
    args = parser.parse_args()
    entries = SOURCES
    repositories = []
    for entry in entries:
        if not re.fullmatch(r"[A-Za-z0-9_.-]+", entry["name"]) or not re.fullmatch(r"[0-9a-f]{40}", entry["revision"]):
            raise ValueError("Invalid dependency source pin")
        repository = args.cache / entry["name"]
        if args.fetch:
            if not repository.exists():
                repository.mkdir(parents=True)
                subprocess.run(["git", "init", str(repository)], check=True, capture_output=True)
                subprocess.run(["git", "-C", str(repository), "remote", "add", "origin", entry["url"]], check=True)
            subprocess.run(["git", "-C", str(repository), "fetch", "--depth", "1", "origin", entry["revision"]], check=True)
        subprocess.run(["git", "-C", str(repository), "cat-file", "-e", entry["revision"] + "^{commit}"], check=True)
        repositories.append((entry, repository))

    # Respect the repository's ignore rules; private inputs and build output are excluded.
    files = subprocess.check_output(["git", "-C", str(ROOT), "ls-files", "--cached", "--others", "--exclude-standard", "-z"]).decode().split("\0")
    output = ROOT / "dist" / "Persona 5 AP.sources.zip"
    output.parent.mkdir(exist_ok=True)
    with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as target:
        for name in sorted(set(files) - {""}):
            path = ROOT / name
            if path.is_file():
                target.write(path, "client/" + name)
        for entry, repository in repositories:
            prefix = "dependencies/" + entry.get("destination", entry["name"]) + "/"
            content = subprocess.check_output(["git", "-C", str(repository), "archive", "--format=zip", "--prefix=" + prefix, entry["revision"]])
            with zipfile.ZipFile(BytesIO(content)) as source:
                for info in source.infolist():
                    if info.is_dir():
                        continue
                    target.writestr(info, source.read(info))
    print(output)


if __name__ == "__main__":
    main()
