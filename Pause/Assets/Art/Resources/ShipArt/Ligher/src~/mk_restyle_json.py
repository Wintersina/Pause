import json, os
em = [(1, "sparks", 51.1, 55.2), (1, "smoke", 59.7, 111.1), (1, "smolder", 51.6, 55.2), (2, "arc", 74.8, 86.6),
      (2, "flame", 70.5, 109.2), (2, "leak", 94.2, 99.4), (2, "smolder", 74.8, 86.6), (2, "smolder", 56.7, 75.6),
      (2, "smolder", 64.9, 104.5), (2, "smolder", 82.1, 59.5), (2, "smolder", 75.2, 42.7)]
cfg = {"hull_w": 158, "world_w": round(167 / 835, 6), "nozzles": 1, "noz_scale": [1.0],
       "emitters": [list(e) for e in em],
       "skins": [["Night", "night"], ["Amber", "sodium"], ["Bone", "bone"], ["Flint", "stock"]],
       "fill_holes": True,
       "flame": {"frames": 6, "bands": {"outer": "#C8662E", "mid": "#FFB43C", "core": "#F4EAD4",
                                        "dark": "#6E3014", "accent": "#6EF2EE"}}}
json.dump(cfg, open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "restyle.json"), "w"), indent=1)
