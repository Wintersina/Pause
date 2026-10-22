#!/usr/bin/env python3
"""Writes a restyled hull's measured rows (build_restyle.py -> <key>/data.json) into the C# tables,
replacing that ship's existing row in place (ShipHullArt rect, ShipNozzles, ShipDamageTable emitters).
Colours, skin names and every gameplay row are left untouched.

    python3 patch_restyle.py NeonComet
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPTS = os.path.normpath(os.path.join(HERE, "..", "..", "..", "..", "..", "Scripts", "Ship"))
key = sys.argv[1]
data = json.load(open(os.path.join(HERE, key.lower(), "data.json")))
x, y, w, h = data["rect"]
KIND = {"sparks": "Sparks", "arc": "Arc", "smoke": "Smoke", "flame": "Flame", "leak": "Leak", "smolder": "Smolder"}


def edit(name, fn):
    path = os.path.join(SCRIPTS, name)
    src = open(path).read()
    new = fn(src)
    assert new != src or True
    open(path, "w").write(new)


def hull(src):
    pat = re.compile(r'(/\*\s*\d+ ' + key + r'\s*\*/ new HullRect\()[^,]+, [^,]+, [^,]+, [^,]+, [\d.]+f')
    assert pat.search(src), "rect row"
    return pat.sub(lambda m: m.group(1) + f"{x}, {y}, {w}, {h}, {data['ppu']:.4f}f", src)


def nozzles(src):
    pat = re.compile(r'(/\*\s*\d+ ' + key + r'\s*)\d+x\d+( \*/ new\[\] \{ ).*?( \},\n)')
    assert pat.search(src), "nozzle row"
    parts = []
    for nx, ny, k in data["nozzles"]:
        scale = "" if k == 1.0 else (", Twin" if abs(k - .72) < 1e-6 else f", {k}f")
        parts.append(f"N({nx:.1f}f, {ny:.1f}f{scale})")
    return pat.sub(lambda m: f"{m.group(1)}{w}x{h}{m.group(2)}{', '.join(parts)}{m.group(3)}", src)


def emitters(src):
    pat = re.compile(r'(//\s*\d+ ' + key + r':[^\n]*\n\s*new\[\] \{ ).*?( \},\n)')
    assert pat.search(src), "emitter row"
    parts = [f"E({st}, {KIND[k]}, {u:.1f}f, {v:.1f}f)" for st, k, u, v in data["emitters"]]
    return pat.sub(lambda m: m.group(1) + ", ".join(parts) + m.group(2), src)


edit("ShipHullArt.cs", hull)
edit("ShipNozzles.cs", nozzles)
edit("ShipDamageTable.cs", emitters)
print("patched", key)
