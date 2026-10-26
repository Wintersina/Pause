"""Retint DockGradeTable Ember/<v>/* rows by new_mid_mean / old_mid_mean per channel (clamped <= 1).
usage: retint_docks.py V OLD_MID.png NEW_MID.png"""
import re, sys
import numpy as np
from PIL import Image
v, old, new = sys.argv[1], sys.argv[2], sys.argv[3]
def mean(p): return np.asarray(Image.open(p).convert("RGB")).reshape(-1, 3).mean(0) / 255
ratio = mean(new) / mean(old)
print("old", mean(old).round(3), "new", mean(new).round(3), "ratio", ratio.round(3))
path = __file__.rsplit("/Art/", 1)[0] + "/Scripts/Worlds/Backdrop/DockGradeTable.cs"
lines = open(path).read().split("\n")
start = next(i for i, l in enumerate(lines) if "tableVals" in l)
keys0 = next(i for i, l in enumerate(lines) if "tableKeys" in l)
keys = []
for l in lines[keys0:start]:
    m = re.search(r'"([A-Za-z]+/[^"]+)"', l)
    if m: keys.append(m.group(1))
row = 0
for i in range(start, len(lines)):
    m = re.match(r"(\s*)(\d\.\d+)f, (\d\.\d+)f, (\d\.\d+)f,(.*)$", lines[i])
    if not m: continue
    if keys[row].startswith(f"Ember/{v}/"):
        c = np.clip(np.array([float(m.group(j)) for j in (2, 3, 4)]) * ratio, .05, 1.0)
        out = f"{m.group(1)}{c[0]:.3f}f, {c[1]:.3f}f, {c[2]:.3f}f,{m.group(5)}"
        print(keys[row], lines[i].strip(), "->", out.strip()); lines[i] = out
    row += 1
open(path, "w").write("\n".join(lines))
