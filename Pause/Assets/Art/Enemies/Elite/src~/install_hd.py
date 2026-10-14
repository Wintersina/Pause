"""Install the HD rebuilds staged in <World>/hd~/ (written by the build_*_hd.py scripts):
 - the strip + death strip replace the 192 px originals in Art/Enemies/Elite/<World>/ (and the Resources copy; the outlined ships are
   re-outlined from the new originals: ship_scale_border~/add_border.py --init --wire after this),
 - the def (cellPixels 384, muzzles / nozzles x 2; hull / hit radius and scale tier untouched) replaces Resources/Elites/Defs/<key>.json.
    python3 install_hd.py
"""
import json, shutil
from pathlib import Path
ELITE = Path(__file__).resolve().parent.parent
ART = ELITE.parents[1]
RES = ART / "Resources/Elites"
BORDER = ART / "Enemies/src~/ship_scale_border~"
lacking = json.load(open(BORDER / "lacking.json"))
for k in ("desert_elite_caravan_hauler", "desert_elite_dune_stalker", "desert_elite_scorpion_walker"):   # their old 192 art carried a full pale ring; the HD ones get the one-sided pass like every other elite
    lacking["elites"].setdefault(k, "desert")
json.dump(lacking, open(BORDER / "lacking.json", "w"), indent=1); open(BORDER / "lacking.json", "a").write("\n")
n = 0
for world in sorted(p for p in ELITE.iterdir() if (p / "hd~").is_dir()):
    for strip in sorted((world / "hd~").glob("*_elite_*.png")):
        name = strip.name
        if name.endswith("_before_after.png"): continue
        key = name.replace("_death.png", "").replace(".png", "")
        shutil.copyfile(strip, world / name)
        shutil.copyfile(strip, RES / world.name / name)
        if not name.endswith("_death.png"):
            (BORDER / "original" / name).unlink(missing_ok=True)   # re-initialised from the new live strip by add_border.py --init
            shutil.copyfile(strip.with_suffix(".json"), RES / "Defs" / f"{key}.json")
            n += 1
print(n, "ships installed")
