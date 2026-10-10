"""Finish run C from the existing image-generated keys and partial flipbooks.

Only run C pages are written. The painted keys remain the source for the
lighting and the rotationally repeating whirlpool; all rotations use NEAREST.
"""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

import build_c as source

ROOT = Path(__file__).resolve().parent.parent
doc = json.loads((ROOT / "manifest.json").read_text())
run = doc["run_c"]
frames: dict[str, Image.Image] = {}
for stems in run["pages"].values():
    for filename in stems:
        sheet = Image.open(ROOT / filename).convert("RGBA")
        for spr in json.loads((ROOT / filename.replace(".png", ".json")).read_text())["sprites"]:
            x, y = spr["x"], 1024 - spr["y"] - 256
            frames[spr["n"]] = sheet.crop((x, y, x + 256, y + 256))


def light_frames(name: str) -> list[Image.Image]:
    """Reveal painted lamp glass in four pixel-stepped exposure keys."""
    spec = next(s for s in source.specs if s["name"] == name)
    painted = np.asarray(source.on_canvas(spec["key"])).copy()
    value = painted[:, :, :3].max(axis=2)
    yy, xx = np.indices((256, 256))
    if name == "window_lights":
        mask = (value > 92) & (painted[:, :, 3] > 30)
        factors = [0.10, 0.38, 1.0, 0.20]
    elif name == "beacon_pink":
        mask = (value > 94) & (painted[:, :, 3] > 0) & (yy < 143) & (xx > 105) & (xx < 149)
        factors = [0.045, 0.25, 1.0, 0.13]
    else:
        mask = (value > (120 if name == "strobe_white" else 94)) & (painted[:, :, 3] > 0) & (yy < 145)
        factors = [0.035, 0.22, 1.0, 0.12] if name == "strobe_white" else [0.055, 0.28, 1.0, 0.15]
    result = []
    for i, factor in enumerate(factors):
        a = painted.copy()
        # Only the glass is animated. The painted metal already exists in run B.
        a[:, :, 3] = np.where(mask, np.rint(painted[:, :, 3] * factor), 0).astype(np.uint8)
        img = Image.fromarray(a, "RGBA")
        if name != "window_lights":
            halo = Image.new("RGBA", (256, 256))
            d = ImageDraw.Draw(halo)
            if name == "beacon_pink":
                color, rings = (218, 139, 168), [(12, 12), (8, 28)]
            elif name == "strobe_white":
                color, rings = (215, 236, 225), [(23, 12), (16, 27), (10, 50)]
            else:
                color, rings = (124, 242, 192), [(23, 15), (16, 32), (10, 55)]
            rings = [(r, max(1, round(alpha * factor))) for r, alpha in rings]
            for radius, alpha in rings:
                # Square steps avoid smooth antialiasing and keep the bloom compact.
                d.rectangle((128 - radius, 116 - radius, 128 + radius, 116 + radius),
                            outline=(*color, alpha), width=2)
            halo.alpha_composite(img)
            img = halo
        result.append(img)
    return result


for name in ("beacon_mint", "beacon_pink", "window_lights", "strobe_white"):
    for i, im in enumerate(light_frames(name)):
        frames[f"{name}_{i:02d}"] = im

# A four-arm version of the generated spiral has exact quarter-turn symmetry.
# Its eighth-to-first step is therefore the same size as its other rotations.
whirl_spec = next(s for s in source.specs if s["name"] == "whirlpool")
whirl_key = source.on_canvas(whirl_spec["key"])
variants = [np.asarray(whirl_key.rotate(a, Image.Resampling.NEAREST)).copy()
            for a in (0, 90, 180, 270)]
whirl = Image.fromarray(np.maximum.reduce(variants), "RGBA")
for i in range(8):
    frames[f"whirlpool_{i:02d}"] = whirl.rotate(round(90 * i / 8), Image.Resampling.NEAREST)

# Put the smallest painted in-between at each loop seam, preserving cyclic order.
# Four-stage warning lights keep the semantic OFF > ignite > ON > fade order.
for loop in run["loops"]:
    names = loop["frames"]
    if loop["name"] in ("beacon_mint", "beacon_pink", "window_lights", "strobe_white"):
        continue
    seq = [frames[n] for n in names]
    premult = []
    for im in seq:
        arr = np.asarray(im).astype(np.float32)
        premult.append(np.concatenate((arr[:, :, :3] * arr[:, :, 3:4] / 255,
                                       arr[:, :, 3:4]), axis=2))
    costs = [np.abs(premult[i] - premult[(i + 1) % len(seq)]).sum()
             for i in range(len(seq))]
    cut = (int(np.argmin(costs)) + 1) % len(seq)
    seq = seq[cut:] + seq[:cut]
    for name, im in zip(names, seq):
        frames[name] = im
    loop["phase_start"] = cut

# Move the handful of generated water/smoke ramp steps that fall in the shield
# cyan band into the approved blue-green Tide range. White foam stays neutral.
tide_shift = {
    (20, 66, 69): (20, 69, 59),
    (36, 99, 100): (36, 100, 84),
    (81, 149, 145): (81, 149, 128),
    (17, 75, 73): (17, 75, 61),
    (9, 47, 46): (9, 47, 39),
    (53, 88, 87): (53, 88, 76),
    (59, 94, 94): (59, 94, 83),
    (85, 128, 125): (85, 128, 113),
}
for name, img in list(frames.items()):
    pixels = np.asarray(img).copy()
    original = pixels.copy()
    for old, new in tide_shift.items():
        mask = np.all(original[:, :, :3] == old, axis=2)
        pixels[mask, :3] = new
    if name.startswith(("smoke_a_", "smoke_c_")):
        # Lift the black plume one dark ramp step so it survives the Tide water.
        lifted = {
            (7, 24, 29): (16, 38, 35),
            (13, 35, 40): (24, 51, 45),
            (22, 49, 53): (37, 66, 57),
            (35, 66, 68): (52, 84, 72),
        }
        current = pixels.copy()
        for old, new in lifted.items():
            pixels[np.all(current[:, :, :3] == old, axis=2), :3] = new
    frames[name] = Image.fromarray(pixels, "RGBA")

for group, names in run["pages"].items():
    for filename in names:
        atlas = Image.new("RGBA", (1024, 1024))
        sprites = json.loads((ROOT / filename.replace(".png", ".json")).read_text())["sprites"]
        for spr in sprites:
            atlas.alpha_composite(frames[spr["n"]], (spr["x"], 1024 - spr["y"] - 256))
        atlas.save(ROOT / filename)

run["source"] = "Built-in image generation painted key sheets in src~; nearest-neighbour art-directed cyclic in-betweens, lighting exposure keys, and a fourfold painted whirlpool"
run["pixel_filter"] = "nearest-neighbour only"
(ROOT / "manifest.json").write_text(json.dumps(doc, indent=2) + "\n")
print("Finished", len(run["loops"]), "painted-key loops on", sum(map(len, run["pages"].values())), "pages")
