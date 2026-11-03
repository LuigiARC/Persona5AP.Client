"""Development helpers for locating private original game inputs."""
import hashlib
from pathlib import Path


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def extracted_file(root: Path, relative: str) -> Path:
    path = root
    for part in Path(relative).parts:
        if part in ("..", "/", "\\"):
            raise ValueError("Unsafe asset path")
        matches = [p for p in path.iterdir() if p.name.casefold() == part.casefold()]
        if len(matches) != 1:
            raise ValueError(f"Cannot uniquely locate {relative} below {root}")
        path = matches[0]
    return path
