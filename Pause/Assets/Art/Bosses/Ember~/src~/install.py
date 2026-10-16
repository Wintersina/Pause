#!/usr/bin/env python3
"""Rebuild and install the Cinder Drake: build.py, build_damage.py, copy to Resources/Bosses, attack-damage drawings,
then re-measure the emitters (Art/BossAttacks/src~/measure_emitters.py).

    python3 Pause/Assets/Art/Bosses/Ember~/src~/install.py
"""
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
ASSETS = HERE.parents[3]
RES = ASSETS / "Art" / "Resources" / "Bosses"


def run(script, cwd):
    subprocess.check_call([sys.executable, str(script)], cwd=str(cwd))


run(HERE / "build.py", HERE)
run(HERE / "build_damage.py", HERE)
for name in ("Ember.png", "Ember_damage.png", "Ember_damage_fx.png", "Ember_death.png"):
    shutil.copyfile(ROOT / name, RES / name)
run(HERE / "build_attack_damage.py", HERE)
run(ASSETS / "Art" / "BossAttacks" / "src~" / "measure_emitters.py", ASSETS / "Art" / "BossAttacks" / "src~")
