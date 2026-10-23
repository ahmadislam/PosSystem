#!/usr/bin/env python3
"""Create/check the complete content manifest of a database-and-images backup."""
import hashlib
import json
import pathlib
import sys
from datetime import datetime, timezone


def inventory(root):
    entries = {}
    for path in sorted(root.rglob("*")):
        if path.is_symlink():
            raise ValueError("Backup bundles must not contain symbolic links")
        if path.relative_to(root) == pathlib.Path("manifest.json"):
            continue
        if path.is_file():
            with path.open("rb") as source:
                entries[path.relative_to(root).as_posix()] = hashlib.file_digest(
                    source, "sha256"
                ).hexdigest()
        elif not path.is_dir():
            raise ValueError("Backup bundles may contain regular files/directories only")
    return entries


def main():
    mode, directory = sys.argv[1:]
    root = pathlib.Path(directory)
    if root.is_symlink() or not root.is_dir():
        raise ValueError("Backup bundle must be a real directory")
    manifest = root / "manifest.json"
    if manifest.is_symlink():
        raise ValueError("Manifest must not be a symbolic link")
    if mode == "create":
        data = {
            "format_version": 1,
            "created_utc": datetime.now(timezone.utc).isoformat(),
            "sha256": inventory(root),
        }
        manifest.write_text(json.dumps(data, indent=2) + "\n")
    elif mode == "verify":
        data = json.loads(manifest.read_text())
        if data.get("format_version") != 1 or data.get("sha256") != inventory(root):
            raise ValueError("Backup integrity verification failed (missing, changed or extra files)")
        if "database.dump" not in data["sha256"] or not (root / "product-images").is_dir():
            raise ValueError("Backup must contain its database dump and product-images directory")
    else:
        raise ValueError("Expected create or verify")


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, KeyError, json.JSONDecodeError) as error:
        print(str(error), file=sys.stderr)
        sys.exit(2)
