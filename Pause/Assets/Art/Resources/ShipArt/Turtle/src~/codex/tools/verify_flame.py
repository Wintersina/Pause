import json
import os
import numpy as np
from PIL import Image

S = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..")
d = json.load(open(os.path.join(S, "Turtle_data.json")))
fl = np.array(Image.open(os.path.join(S, "Turtle_flame.png")).convert("RGBA")).astype(float)
H = fl.shape[0]
res = {}
for st in d["exhaust"]["strips"]:
    top = H - st["y"] - st["h"]
    tot, ln, corner = 0, 0, 0
    for i in range(st["frames"]):
        f = fl[top:top + st["h"], st["x"] + i * st["stride"]: st["x"] + i * st["stride"] + st["w"]]
        a = f[..., 3] > 0
        tot += (f[..., 3] / 255 * f[..., :3].sum(2)).sum()
        ln += np.nonzero(a.any(axis=1))[0].max() + 1
        corner += int(a[0, 0] or a[0, -1] or a[-1, 0] or a[-1, -1])
        # a backdrop rectangle would make most of the frame opaque
    res[st["layer"]] = (tot, ln / st["frames"], corner)
    print(st["layer"], "brightness", round(tot), "mean tail rows", round(ln / st["frames"], 1), "opaque corners", corner)
print("boost/cruise brightness", round(res["PlumeBoost"][0] / res["Plume"][0], 3), "length", round(res["PlumeBoost"][1] / res["Plume"][1], 3))
