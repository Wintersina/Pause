#!/usr/bin/env python3
"""Synthetic quiet/drop fixture plus source-integrity checks for baked arrangements.
Run with the same Python environment as scripts/tools/boss_beatmap.py.
"""
import hashlib
import importlib.util
import json
from pathlib import Path
import numpy as np

root = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("boss_beatmap", root / "scripts/tools/boss_beatmap.py")
mapper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mapper)

# Known 32-beat signal: quiet, rising, loud bass drop, quiet again.
sr = mapper.SR
t = np.arange(int(12.8 * sr)) / sr
amplitude = np.select([t < 3.2, t < 6.4, t < 9.6], [.02, .18, .8], default=.02)
y = (amplitude * np.sin(2 * np.pi * 100 * t)).astype(np.float32)
original_load = mapper.load
try:
    mapper.load = lambda path: y
    result = mapper.arrangement("Space", {"steady": True, "firstBeat": 0, "duration": 12.8, "bpm": 150})
finally:
    mapper.load = original_load
assert np.mean(result["energy"][16:24]) > np.mean(result["energy"][:8]) + .7
assert any(s["kind"] == "drop" and s["beat"] == 16 for s in result["sections"])
assert all(0 <= x <= 1 for key in ("energy", "bass", "accents") for x in result[key])
print("PASS synthetic quiet/drop analysis")

for song in json.loads((root / "Pause/Assets/Resources/BossBeatMaps.json").read_text())["bosses"]:
    path = root / "Pause/Assets/Audio/Resources" / (song["clip"] + ".mp3")
    assert hashlib.sha256(path.read_bytes()).hexdigest() == song["sourceSha256"], song["name"]
    count = len(song["energy"])
    assert len(song["bass"]) == len(song["accents"]) == count
    expected = len(song["beats"]) if not song["steady"] else len(np.arange(song["firstBeat"], song["duration"], 60 / song["bpm"]))
    assert count == expected, song["name"]
    print("PASS", song["name"], "source hash and beat indexing")
