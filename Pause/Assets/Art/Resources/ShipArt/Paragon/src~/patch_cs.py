#!/usr/bin/env python3
"""Writes Paragon's measured rows into the C# tables, between the
`BEGIN PARAGON ...` / `END PARAGON ...` markers (kept outside the generated
blocks of build.py so the vector pipeline never overwrites them).

    python3 patch_cs.py        # reads data.json written by build_paragon.py
"""
import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPTS = os.path.normpath(os.path.join(HERE, "..", "..", "..", "..", "..", "..", "Scripts", "Ship"))
data = json.load(open(os.path.join(HERE, "data.json")))
HUE = ("#2E9BE6", "#174C8C", "#8FD3FF")   # Paragon's identity (neon blue); red is its armour


def patch(name, tag, text):
    path = os.path.join(SCRIPTS, name)
    src = open(path).read()
    pat = re.compile(r"(// BEGIN PARAGON " + tag + r"\n).*?(        // END PARAGON " + tag + r")", re.S)
    assert pat.search(src), (name, tag)
    src = pat.sub(lambda m: m.group(1) + text + m.group(2), src)
    open(path, "w").write(src)


x, y, w, h = data["rect"]
patch("ShipHullArt.cs", "RECT",
      f'        /* 16 Paragon      */ new HullRect({x}, {y}, {w}, {h}, {data["ppu"]:.4f}f, "{HUE[0]}", "{HUE[1]}", "{HUE[2]}"),\n')

noz = []
for nx, ny, k in data["nozzles"]:
    noz.append(f"N({nx:.1f}f, {ny:.1f}f" + ("" if k == 1.0 else f", {k}f") + ")")
patch("ShipNozzles.cs", "NOZZLES", f'        /* 16 Paragon      {w}x{h} */ new[] {{ {", ".join(noz)} }},\n')

KIND = {"sparks": "Sparks", "arc": "Arc", "smoke": "Smoke", "flame": "Flame", "leak": "Leak", "smolder": "Smolder"}
parts = [f"E({st}, {KIND[k]}, {u:.1f}f, {v:.1f}f)" for st, k, u, v in data["emitters"]]
patch("ShipDamageTable.cs", "EMITTERS",
      "        // 16 Paragon: 1) left wing-pod panel blown, wiring sparking; scorch on the inner pod; 2) right inner pod "
      "panel open, right wing-tip pod torn and burning, hull and canopy cracked\n"
      f"        new[] {{ {', '.join(parts)} }},\n")

lines = [f'S("Stock", ShipSkinKind.Stock, "{HUE[0]}", "{HUE[1]}", "{HUE[2]}")']
for sk in data["skins"]:
    lines.append(f'S("{sk["name"]}", ShipSkinKind.Palette, "{sk["hue"]}", "{sk["shadow"]}", "{sk["highlight"]}")')
rows = ["        /* 16 Paragon      */ new[] {\n"]
for i, ln in enumerate(lines):
    rows.append("            " + ln + (",\n" if i < len(lines) - 1 else " },\n"))
patch("ShipSkins.cs", "SKINS", "".join(rows))
print("patched Paragon rows")
