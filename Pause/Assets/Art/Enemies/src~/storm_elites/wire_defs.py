#!/usr/bin/env python3
"""Measure muzzles / nozzles on the built hover cell with the project's points tool (seeds = master-px spots mapped through the build
transform), keep every muzzle within 156 cell px of the cell centre (StormEliteTest: 0.42 x cellWorldSize), and write them plus the
new cellWorldSize (same drawn hull size in the world as the old art, hullRadius unchanged) and engine colours into the five EliteDefs.
Run build_strips.py first.   python3 wire_defs.py"""
import json, math, re, subprocess, sys
from pathlib import Path
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import build_strips as B

ART = B.ART
REPO = HERE.parents[5]
TOOL = REPO / ".claude/skills/add-elite-ship/scripts/measure_elite_points.py"
DEFS = ART / "Resources/Elites/Defs"
# name: (cellWorldSize, engineColor, muzzles [(name, master x, y, dir)], nozzle names, radius)
DATA = {
    "skyhook_harrier": (2.07, "#FFC346", [("hook", 575, 60, None)], "lr", 6),
    "refinery_tender": (1.90, "#5FCDFF", [("port", 390, 640, 270), ("star", 975, 640, 270), ("canister", 680, 860, 270)], "lcr", 6),
    "rod_dancer": (2.08, "#78F0EB", [("fan", 540, 560, None), ("portrod", 260, 100, None), ("starrod", 820, 100, None)], "lcr", 5),
    "anvil_cloud": (2.28, "#FFD77A", [("cannon", 628, 85, None)], "lr", 6),
    "static_bastion": (2.31, "#96EBFF", [("core", 620, 450, 270), ("portcap", 240, 190, 270), ("starcap", 1000, 190, 270)], "abcde", 6),
}
CELLC, MAXD = 192, 156


def run(name):
    cws, eng, muz, nn, rad = DATA[name]
    cfg, hull, bb, s = B.load(name)
    w, h = hull.size
    ox = (B.CELL - w) // 2; oy = 34 + (280 - h) // 2
    tf = lambda x, y: (ox + round((x - bb[0]) * s), oy + round((y - bb[1]) * s))
    cmd = [sys.executable, str(TOOL), str(ART / f"Resources/Elites/Storm/storm_elite_{name}.png"), "--layout", "flight", "--radius", str(rad)]
    for n, x, y, d in muz:
        px, py = tf(x, y)
        cmd += ["--muzzle", f"{n}:{px},{py}" + (f":{d}" if d else "")]
    for i, (x, y, bw) in enumerate(B.SHIPS[name]["noz"]):
        px, py = tf(x, y)
        cmd += ["--nozzle", f"{nn[i]}:{px},{py - 8}"]
    out = subprocess.run(cmd, capture_output=True, text=True, cwd=REPO).stdout
    m = json.loads(re.search(r"^muzzles (.*)$", out, re.M).group(1))
    z = json.loads(re.search(r"^nozzles (.*)$", out, re.M).group(1))
    for p in m:                                   # keep inside the StormEliteTest radius: slide toward the cell centre
        dx, dy = p["x"] - CELLC, p["y"] - CELLC
        dist = math.hypot(dx, dy)
        if dist > MAXD:
            k = MAXD / dist
            p["x"], p["y"] = round(CELLC + dx * k), round(CELLC + dy * k)
    p = DEFS / f"storm_elite_{name}.json"
    d = json.loads(p.read_text())
    d["cellWorldSize"] = cws; d["engineColor"] = eng; d["muzzles"] = m; d["nozzles"] = z
    p.write_text(json.dumps(d, indent=2) + "\n")
    print(name, "muzzles", [(q["name"], q["x"], q["y"]) for q in m], "nozzles", [(q["name"], q["x"], q["y"]) for q in z])


if __name__ == "__main__":
    for n in (sys.argv[1:] or list(DATA)):
        run(n)
