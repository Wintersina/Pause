#!/usr/bin/env python3
"""Whole chain: strips -> copy next to Codex's folder (EliteArtSync source) -> death strips -> hardpoints into the defs -> def text.
  python3 install.py [preview dir]"""
import shutil, subprocess, sys
from pathlib import Path
H = Path(__file__).resolve().parent
ART = H.parents[2]
KEYS = "dune_stalker caravan_hauler glass_cannon lens_matriarch mirror_bastion scorpion_walker".split()
prev = sys.argv[1] if len(sys.argv) > 1 else "/tmp/desert_elite_points"
Path(prev).mkdir(parents=True, exist_ok=True)
subprocess.run([sys.executable, str(H / "build_strips.py")], check=True)
for k in KEYS:
    shutil.copy(ART / f"Resources/Elites/Desert/desert_elite_{k}.png", ART / f"Enemies/Elite/Desert/desert_elite_{k}.png")
subprocess.run([sys.executable, str(H / "death.py")], check=True)
subprocess.run([sys.executable, str(H / "points.py"), prev], check=True)
subprocess.run([sys.executable, str(H / "defs.py")], check=True)
