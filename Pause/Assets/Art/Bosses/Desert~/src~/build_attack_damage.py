#!/usr/bin/env python3
"""Dune-Eater: the 36 damaged attack-pose drawings (9 poses x 4 stages), derived by the impact-v1 transfer.

    python3 Pause/Assets/Art/Bosses/Desert~/src~/build_attack_damage.py

Reads Resources/Bosses/Desert.png and Desert_damage.png (install them first), writes
Resources/BossImpact/attack/desert_<pose>_stage<N>.png and removes the old 20-cell set's desert_fire_* files.
Same transfer as Art/Bosses~/impact-v1/build.py (damage_attack): one drawing per pose and stage (576 px files), the pose's
own silhouette, hull pixels transferred only where the pose still shows the idle hull.
"""
import importlib.util
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
ASSETS = HERE.parents[3]
spec = importlib.util.spec_from_file_location("impact_v1_build", ASSETS / "Art" / "Bosses~" / "impact-v1" / "build.py")
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

OUT = ASSETS / "Art" / "Resources" / "BossImpact" / "attack"
SOURCE = ASSETS / "Art" / "Resources" / "Bosses"


def main():
    body = Image.open(SOURCE / "Desert.png").convert("RGBA")
    damage = Image.open(SOURCE / "Desert_damage.png").convert("RGBA")
    cs = body.width // 5                      # 576: the atlas's own cell size (the impact-v1 helper assumes 384)
    assert body.height == cs * 7

    def cell(atlas, index, columns=5):
        x, y = index % columns * cs, index // columns * cs
        return atlas.crop((x, y, x + cs, y + cs))

    baseline = cell(body, 20)
    for stale in OUT.glob("desert_fire_stage*.png*"):
        stale.unlink()
    n = 0
    for name, index in mod.attacks(True):
        pose = cell(body, index)
        for stage in range(1, 5):
            damaged = cell(damage, (stage - 1) * 2, 2)
            mod.damage_attack(baseline, damaged, pose).save(OUT / f"desert_{name}_stage{stage}.png", optimize=True)
            n += 1
    print("wrote", n, "ember attack-damage drawings")


if __name__ == "__main__":
    main()
