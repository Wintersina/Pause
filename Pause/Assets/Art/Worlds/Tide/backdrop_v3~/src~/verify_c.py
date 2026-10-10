"""Audit Tide run C and render its still/animated contact sheets.

Run from any working directory: python3 src~/verify_c.py. Frame-change percent
uses the union of the two visible sprite footprints, so tiny warning lamps are
judged against their painted effect rather than an empty 256 px cell.
"""
from __future__ import annotations

import colorsys
import json
from collections import defaultdict
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
RUN = json.loads((ROOT / "manifest.json").read_text())["run_c"]
EXPECTED = {
    "smoke_a": 8, "smoke_b": 8, "smoke_c": 8,
    "flare": 8, "flare_b": 8, "burn": 8,
    "wave_crest_a": 8, "wave_crest_b": 8, "foam_ring": 8,
    "ripple": 8, "wake": 8, "whirlpool": 8, "spray": 8, "caustic": 8,
    "bubble_stream": 4, "steam_vent": 4, "pipe_drip": 4,
    "rain_curtain": 4, "plankton_glow": 4,
    "beacon_mint": 4, "beacon_pink": 4, "window_lights": 4,
    "strobe_white": 4, "searchlight_sweep": 8, "lighthouse_beam": 8,
}
LIGHT_LIMITS = {"beacon_mint": 4.0, "beacon_pink": 4.0,
                "window_lights": 2.2, "strobe_white": 4.0}
errors: list[str] = []
frames: dict[str, np.ndarray] = {}
atlas_stems = []
for group, page_files in RUN["pages"].items():
    for filename in page_files:
        atlas_stems.append(filename)
        image = Image.open(ROOT / filename)
        if image.size != (1024, 1024) or image.mode != "RGBA":
            errors.append(f"{filename}: expected 1024x1024 RGBA")
        atlas = np.asarray(image)
        sprites = json.loads((ROOT / filename.replace(".png", ".json")).read_text())["sprites"]
        if len(sprites) > 16:
            errors.append(f"{filename}: over 16 cells")
        for spr in sprites:
            if (spr["w"], spr["h"]) != (256, 256):
                errors.append(f"{spr['n']}: wrong cell size")
            x, y = spr["x"], 1024 - spr["y"] - 256
            if x % 256 or y % 256 or not (0 <= x <= 768 and 0 <= y <= 768):
                errors.append(f"{spr['n']}: invalid Unity bottom-origin rectangle")
            if spr["n"] in frames:
                errors.append(f"{spr['n']}: duplicate sprite name")
            frames[spr["n"]] = atlas[y:y+256, x:x+256].copy()


def premultiplied(a: np.ndarray) -> np.ndarray:
    f = a.astype(np.float32)
    return np.concatenate((f[:, :, :3] * f[:, :, 3:4] / 255,
                           f[:, :, 3:4]), axis=2)


def brightness(a: np.ndarray) -> float:
    f = a.astype(np.float64)
    lum = f[:, :, 0]*0.2126 + f[:, :, 1]*0.7152 + f[:, :, 2]*0.0722
    return float((lum * f[:, :, 3] / 255).sum())


def delta(a: np.ndarray, b: np.ndarray) -> float:
    return float(np.abs(premultiplied(a)-premultiplied(b)).sum())


loop_names = {v["name"] for v in RUN["loops"]}
if loop_names != set(EXPECTED):
    errors.append(f"loop names differ: missing={sorted(set(EXPECTED)-loop_names)}, extra={sorted(loop_names-set(EXPECTED))}")
if len(frames) != sum(EXPECTED.values()):
    errors.append(f"sprite total {len(frames)} versus expected {sum(EXPECTED.values())}")

metrics = {}
for loop in RUN["loops"]:
    name = loop["name"]
    names = loop["frames"]
    if len(names) != EXPECTED.get(name, -1) or names != [f"{name}_{i:02d}" for i in range(len(names))]:
        errors.append(f"{name}: frame count or frame names")
        continue
    if any(n not in frames for n in names):
        errors.append(f"{name}: missing atlas frame")
        continue
    a = [frames[n] for n in names]
    margins = []
    for i, f in enumerate(a):
        yy, xx = np.where(f[:, :, 3] > 0)
        if not len(xx):
            errors.append(f"{name}_{i:02d}: empty")
            continue
        margins.append(int(min(xx.min(), yy.min(), 255-xx.max(), 255-yy.max())))
    if margins and min(margins) < 6:
        errors.append(f"{name}: margin {min(margins)} < 6 px")
    if loop["anchor"] == [128, 236]:
        patches = [f[234:238, 126:131].tobytes() for f in a]
        if len(set(patches)) != 1 or any(f[235, 128, 3] == 0 for f in a):
            errors.append(f"{name}: bottom-centre attachment wobbles")
        if "no sideways/base wobble" not in loop.get("anchor_rule", ""):
            errors.append(f"{name}: anchor rule absent from manifest")
    elif loop["anchor"] != [128, 128]:
        errors.append(f"{name}: unexpected anchor {loop['anchor']}")
    if name.startswith("smoke") and any(f[:, :, 3].max() > 178 for f in a):
        errors.append(f"{name}: smoke exceeds 70% alpha")
    steps = [delta(a[i], a[(i+1) % len(a)]) for i in range(len(a))]
    seam = steps[-1] / max(1, float(np.mean(steps[:-1])))
    if seam > 1.00001:
        errors.append(f"{name}: seam {seam:.3f} > typical step")
    changes = []
    for i in range(len(a)):
        f, g = a[i], a[(i+1) % len(a)]
        visible = (f[:, :, 3] > 0) | (g[:, :, 3] > 0)
        changed = np.any(np.abs(f.astype(np.int16)-g.astype(np.int16)) > 1, axis=2)
        changes.append(float(changed[visible].mean()))
    change = min(changes)
    if change < .03:
        errors.append(f"{name}: only {change*100:.1f}% of visible footprint changes")
    values = [brightness(f) for f in a]
    ratio = max(values) / max(1e-6, min(values))
    if name in LIGHT_LIMITS and ratio < LIGHT_LIMITS[name]:
        errors.append(f"{name}: brightness ratio {ratio:.2f} < {LIGHT_LIMITS[name]:.1f}")
    metrics[name] = (seam, change, ratio, min(margins) if margins else 0)

# Pixel shares are alpha weighted. Low saturation foam white is not a hue cue.
hue_weights = defaultdict(float)
pink_max = 0.0
for name, a in frames.items():
    rgba, counts = np.unique(a.reshape(-1, 4), axis=0, return_counts=True)
    pink_pixels = 0
    for c, count in zip(rgba, counts):
        if c[3] == 0:
            continue
        h, s, v = colorsys.rgb_to_hsv(*(int(x)/255 for x in c[:3]))
        hue = h*360
        if s >= .2 and v >= .1:
            hue_weights[int(round(hue))] += int(c[3]) * int(count) / 255
        if 295 <= hue <= 345 and s >= .2 and c[3] >= 8:
            pink_pixels += int(count)
    pink_max = max(pink_max, pink_pixels/65536)
total_hue = sum(hue_weights.values()) or 1
def share(lo: int, hi: int) -> float:
    return sum(w for h, w in hue_weights.items() if lo <= h <= hi) / total_hue

shares = {
    "Tide green 145-174": share(145, 174),
    "shield cyan 175-181": share(175, 181),
    "blue violet 205-240": share(205, 240),
    "repair green 77-87": share(77, 87),
    "violet 254-264": share(254, 264),
    "amber 32-42": share(32, 42),
    "hostile pink 312-326": share(312, 326),
    "red 345-360 or 0-15": share(345, 360) + share(0, 15),
}
if shares["shield cyan 175-181"] > .05:
    errors.append(f"shield cyan area {shares['shield cyan 175-181']*100:.2f}% > 5%")
if shares["red 345-360 or 0-15"] > 0:
    errors.append("red hue found")
if pink_max >= .01:
    errors.append(f"pink coverage {pink_max*100:.2f}% >= 1% of cell")


def draw_previews() -> None:
    font = ImageFont.load_default()
    width, row_h = 1040, 104
    still = Image.new("RGB", (width, 44 + row_h*len(RUN["loops"])), (4, 20, 24))
    d = ImageDraw.Draw(still)
    d.text((16, 14), "TIDE / RUN C     25 PAINTED-KEY LOOPS     BRIGHTNESS = ALPHA-WEIGHTED LUMA", font=font, fill=(190, 228, 211))
    for row, loop in enumerate(RUN["loops"]):
        name = loop["name"]
        top = 44 + row*row_h
        d.rectangle((0, top, width, top+row_h-2), fill=(7, 30, 35) if row%2 else (8, 35, 39))
        seam, change, ratio, _ = metrics[name]
        d.text((12, top+17), name, font=font, fill=(219, 241, 226))
        d.text((12, top+40), f"{len(loop['frames'])}f  {loop['fps']}fps", font=font, fill=(145, 192, 175))
        d.text((12, top+62), f"brightness x{ratio:.2f}  seam {seam:.2f}  motion {change*100:.0f}%", font=font, fill=(124, 242, 192))
        for i, n in enumerate(loop["frames"]):
            f = Image.fromarray(frames[n], "RGBA")
            tile = Image.new("RGBA", (256, 256), (5, 28, 33, 255))
            tile.alpha_composite(f)
            tile = tile.convert("RGB").resize((82, 82), Image.Resampling.NEAREST)
            x = 330 + i*88
            still.paste(tile, (x, top+8))
            d.text((x+2, top+91), f"{i:02d}", font=font, fill=(138, 178, 166))
    still.save(ROOT / "preview_c.png")

    gif_frames = []
    for phase in range(8):
        canvas = Image.new("RGB", (780, 710), (4, 20, 24))
        gd = ImageDraw.Draw(canvas)
        gd.text((12, 8), f"TIDE / RUN C     PHASE {phase+1}/8", font=font, fill=(190, 228, 211))
        for k, loop in enumerate(RUN["loops"]):
            col, row = k%5, k//5
            x, y = 6+col*155, 34+row*134
            name = loop["name"]
            n = loop["frames"][phase % len(loop["frames"])]
            tile = Image.new("RGBA", (256, 256), (5, 28, 33, 255))
            tile.alpha_composite(Image.fromarray(frames[n], "RGBA"))
            canvas.paste(tile.convert("RGB").resize((112, 112), Image.Resampling.NEAREST), (x+19, y))
            gd.text((x+4, y+114), name, font=font, fill=(157, 204, 186))
        gif_frames.append(canvas)
    gif_frames[0].save(ROOT / "preview_c.gif", save_all=True, append_images=gif_frames[1:],
                       duration=125, loop=0, optimize=False)


print("loop                 frames seam/typical min_change brightness max_margin")
for loop in RUN["loops"]:
    name = loop["name"]
    s, c, b, m = metrics[name]
    print(f"{name:20s} {len(loop['frames']):2d}       {s:5.2f}        {c*100:5.1f}%      {b:6.2f}x     {m:3d}px")
print("Hue-band shares (alpha weighted, saturated visible pixels):")
for key, value in shares.items():
    print(f"  {key}: {value*100:.2f}%")
print(f"Maximum pink coverage in a 256 px cell: {pink_max*100:.3f}%")
draw_previews()
if errors:
    print("FAIL:")
    for error in errors:
        print(" -", error)
    raise SystemExit(1)
print(f"PASS: {len(RUN['loops'])} loops, {len(frames)} frames, {len(atlas_stems)} RGBA pages")
