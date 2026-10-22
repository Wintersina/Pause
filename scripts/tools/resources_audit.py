#!/usr/bin/env python3
"""Inventory Resources against an existing APK; reference hints never prove non-use.

Run from any directory. CSV output defaults to the ignored Pause/Builds folder.
ZIP sizes are from the supplied APK, not predictions for the next build.
"""
import argparse
import csv
import hashlib
import json
import re
import zipfile
from collections import defaultdict
from pathlib import Path


def main():
    root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apk", type=Path, default=root / "Pause/Builds/Android/Pause-dev.apk")
    parser.add_argument("--csv", type=Path, default=root / "Pause/Builds/resources-audit.csv")
    args = parser.parse_args()
    assets = root / "Pause/Assets"
    files = sorted(p for p in assets.rglob("*") if p.is_file()
                   and "Resources" in p.relative_to(assets).parts[:-1]
                   and not any(part.endswith("~") or part in ("Editor", ".git")
                               for part in p.relative_to(assets).parts[:-1])
                   and not p.name.startswith(".")
                   and p.suffix != ".meta")
    # Code strings and data tables help identify likely consumers. They cannot
    # resolve concatenation, formatting, fallback paths, or scene dependencies.
    runtime_text = "\n".join(p.read_text(errors="replace") for p in assets.rglob("*")
                             if p.is_file() and p.suffix in (".cs", ".json", ".csv")
                             and not any(part.endswith("~") or part == "Editor"
                                         for part in p.relative_to(assets).parts[:-1]))
    with zipfile.ZipFile(args.apk) as apk:
        entries = {entry.filename: entry for entry in apk.infolist()}
    rows = []
    hashes = defaultdict(list)
    folders = defaultdict(int)
    keys = defaultdict(list)
    for path in files:
        relative = path.relative_to(root / "Pause").as_posix()
        parts = path.relative_to(assets).parts
        key = Path(*parts[parts.index("Resources") + 1:]).with_suffix("").as_posix()
        keys[key].append(relative)
        meta = path.with_name(path.name + ".meta")
        match = re.search(r"^guid: ([0-9a-f]{32})$", meta.read_text(), re.M) if meta.exists() else None
        base = "assets/bin/Data/" + match[1] if match else ""
        packed = [entries[name] for name in (base, base + ".resource", base + ".resS") if name in entries]
        zipped = sum(entry.compress_size for entry in packed)
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        hashes[digest].append(relative)
        folders["/".join(relative.split("/")[:4])] += zipped
        rows.append(dict(path=relative, resource_key=key, source_bytes=path.stat().st_size,
                         apk_compressed_bytes=zipped, apk_entry_found=bool(packed),
                         literal_name_hint=path.stem in runtime_text, sha256=digest))
    args.csv.parent.mkdir(parents=True, exist_ok=True)
    with args.csv.open("w", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    print(json.dumps(dict(apk=str(args.apk), csv=str(args.csv), resources=len(rows),
                          directly_attributed_apk_bytes=sum(r["apk_compressed_bytes"] for r in rows),
                          largest_folders=sorted(folders.items(), key=lambda item: -item[1])[:20],
                          identical_source_groups=sum(len(group) > 1 for group in hashes.values()),
                          # PNG + JSON atlas pairs intentionally share a key.
                          shared_key_groups=sum(len(group) > 1 for group in keys.values())), indent=2))


if __name__ == "__main__":
    main()
