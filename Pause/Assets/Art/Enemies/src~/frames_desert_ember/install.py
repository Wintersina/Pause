#!/usr/bin/env python3
"""Install the built strips (strips/<name>.png) into Resources/Enemies and rebuild each death strip from the FINAL art.

    python3 install.py [name ...]

Death builders read idle cell 3 of the strip folder: build_desert_death.py (DESERT_IDLE) and build_ember_enemy_death.py (EMBER_IDLE)
are pointed at ./strips; their staged output (desert_death/, ember_refresh_death/) is copied to Resources/Enemies/Death.
"""
import os, shutil, subprocess, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
SRC = HERE.parent
RES = HERE.parents[4] / "Assets/Art/Resources/Enemies"
names = sys.argv[1:] or sorted(p.stem for p in (HERE / "strips").glob("*.png"))
for n in names:
    shutil.copy2(HERE / "strips" / f"{n}.png", RES / f"{n}.png")
    print("installed", n)
for world, script, env, stage in (("desert", "build_desert_death.py", "DESERT_IDLE", "desert_death"),
                                  ("ember", "build_ember_enemy_death.py", "EMBER_IDLE", "ember_refresh_death")):
    keys = [n for n in names if n.startswith(world + "_")]
    if not keys:
        continue
    e = dict(os.environ); e[env] = str(HERE / "strips")
    subprocess.run([sys.executable, str(SRC / script)] + keys, check=True, env=e)
    for k in keys:
        shutil.copy2(SRC / stage / f"{k}.png", RES / "Death" / f"{k}.png")
        print("death", k)
