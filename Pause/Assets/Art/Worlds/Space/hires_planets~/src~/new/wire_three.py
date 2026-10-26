"""Replace giant_00/02/07 (Storm/Tide/Verdant, Codex art) in the Space planet sheets; every other cell stays byte-identical.
Run from repo root: python3 <this>.  Uses the same fit as build_hires_planets.py minus its violet/gold hue remap."""
import json
from pathlib import Path
import numpy as np
from PIL import Image
HERE = Path(__file__).resolve().parent
OUT = HERE.parents[1]
REPO = HERE.parents[7]
BD = REPO / "Pause/Assets/Art/Backgrounds/Resources/Worlds/Space/Backdrop"
PAD = 10
MAP = {"giant_00": "giant_00_new.png", "giant_02": "giant_02_new.png", "giant_07": "giant_07_new.png"}

def paint(src, w, h):
    s = Image.open(src).convert("RGBA")
    a = s.getchannel("A").point(lambda v: 255 if v >= 128 else 0)
    body = s.crop(a.getbbox()).resize((w, h), Image.Resampling.NEAREST)
    rgb = Image.new("RGB", body.size, (7, 10, 28)); rgb.paste(body, (0, 0), body.getchannel("A"))
    rgb = rgb.quantize(colors=128, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert("RGB")
    px = np.asarray(rgb, dtype=np.float32).copy()
    yy, xx = np.mgrid[:h, :w]
    nx = (xx + .5 - w / 2) / (w / 2); ny = (yy + .5 - h / 2) / (h / 2)
    r = np.sqrt(nx * nx + ny * ny)
    mask = np.clip((1 - r) / (2 / min(w, h)), 0, 1)
    lit = np.clip((nx + .08) * 1.5, 0, 1)
    rim = np.clip((r - .958) / .025, 0, 1) * np.clip((1 - r) / .018, 0, 1) * lit
    px += rim[..., None] * np.array([6, 38, 48], dtype=np.float32)
    px *= 1 - ((r > .993) & (nx < 0)).astype(np.float32)[..., None] * .35
    out = Image.fromarray(np.uint8(np.clip(px, 0, 255)), "RGB").convert("RGBA")
    out.putalpha(Image.fromarray(np.uint8(np.round(mask * 255)), "L"))
    return out

def cells(path): return {s["n"]: s for s in json.loads(Path(path).read_text())["sprites"]}
def box(sheet, s, scale=1):
    H = sheet.height
    return (s["x"], H - s["y"] - s["h"], s["x"] + s["w"], H - s["y"])

m4 = Image.open(OUT / "anim_hires_4096.png").convert("RGBA")
j4, j2, j1 = cells(OUT / "anim_hires_4096.json"), cells(OUT / "anim_hires.json"), cells(BD / "anim.json")
s2 = Image.open(BD / "anim_hires.png").convert("RGBA")
s1 = Image.open(BD / "anim.png").convert("RGBA")
for n, f in MAP.items():
    e = j4[n]
    disc = paint(HERE / f, e["w"] - 2 * PAD, e["h"] - 2 * PAD)
    cell = Image.new("RGBA", (e["w"], e["h"]), (0, 0, 0, 0)); cell.paste(disc, (PAD, PAD))
    m4.paste(cell, box(m4, e)[:2])
    e2 = j2[n]; c2 = cell.resize((e2["w"], e2["h"]), Image.Resampling.BOX)
    s2.paste(c2, box(s2, e2)[:2])
    e1 = j1[n]; c1 = c2.resize((e1["w"], e1["h"]), Image.Resampling.BOX)
    s1.paste(c1, box(s1, e1)[:2])
m4.save(OUT / "anim_hires_4096.png", optimize=True)
s2.save(BD / "anim_hires.png", optimize=True)
s1.save(BD / "anim.png", optimize=True)
