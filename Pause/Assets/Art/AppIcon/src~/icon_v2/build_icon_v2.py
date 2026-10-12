"""Build the Pause app icon set from the approved 4096 master (master_4096.png,
composed by make.py) and its parts. Run from this folder:  python3 build_icon_v2.py
Writes the six OrbitalRail_* PNGs into ../../OrbitalRail/ plus adaptive_preview.png here."""
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
OUT = HERE.parents[1] / "OrbitalRail"
L = Image.Resampling.LANCZOS
LAYER = 1728        # adaptive layers: 108dp x 16
TITLE_FRAC = 0.62   # title crop width / LAYER (letters themselves are ~84% of it)

master = Image.open(HERE / "master_4096.png").convert("RGB")
m1024 = master.resize((1024, 1024), L)
m1024.save(OUT / "OrbitalRail_1024.png", optimize=True)
m1024.save(OUT / "OrbitalRail_ios_1024.png", optimize=True)   # RGB, no alpha, square corners

def squircle_mask(n, r):
    k = 4; big = Image.new("L", (n * k, n * k), 0)
    ImageDraw.Draw(big).rounded_rectangle((0, 0, n * k - 1, n * k - 1), r * k, fill=255)
    return big.resize((n, n), L)
def circle_mask(n):
    k = 4; big = Image.new("L", (n * k, n * k), 0)
    ImageDraw.Draw(big).ellipse((0, 0, n * k - 1, n * k - 1), fill=255)
    return big.resize((n, n), L)
def masked(img, mask):
    o = img.convert("RGBA"); o.putalpha(mask); return o

master.resize((512, 512), L).save(HERE / "playstore_512.png", optimize=True)   # Play Console hi-res icon (not imported)
masked(m1024, squircle_mask(1024, round(1024 * 0.22))).save(OUT / "OrbitalRail_legacy_192.png", optimize=True)
masked(m1024, circle_mask(1024)).save(OUT / "OrbitalRail_round_192.png", optimize=True)

# adaptive background: starfield + moon, full-bleed, no title / ship (same geometry as make.py)
S = 4096
bg = Image.open(HERE / "starfield.png").convert("RGB").resize((S, S), L).convert("RGBA")
moon = Image.open(HERE / "moon.png").convert("RGBA")
mp = np.array(moon)
mr, mg, mb = [mp[:, :, i].astype(np.int16) for i in range(3)]
violet = (mb > mg * 1.18) & (mr > mg * 1.20) & (mb > 45) & (mp[:, :, 3] > 8)
mp[violet, 0] = np.clip(mg[violet] * 0.85, 0, 255)
mp[violet, 1] = np.maximum(mg[violet], mb[violet] * 0.58).clip(0, 255)
moon = Image.fromarray(mp, "RGBA").resize((1910, 1910), L)
bg.alpha_composite(moon, (2510, -275))
bgl = bg.convert("RGB").resize((LAYER, LAYER), L)
bgl.save(OUT / "OrbitalRail_adaptive_bg_432.png", optimize=True)

# adaptive foreground: title + ship on transparent, centred in the 288 px safe zone
logo = Image.open(HERE / "title_logo.png").convert("RGBA").crop((69, 272, 990, 706))
ship = Image.open(HERE / "ship_alone.png").convert("RGBA")
W = round(LAYER * TITLE_FRAC)
t = logo.resize((W, round(W * logo.height / logo.width)), L)
# ship geometry relative to the title in the master (see make.py): centre x .523W, top .417W, width .181W
sw = round(W * 0.181); sh = round(sw * ship.height / ship.width)
s = ship.resize((sw, sh), L)
sx = round(W * 0.523) - sw // 2; sy = round(W * 0.417)
# group bbox of opaque lettering + ship, centred on the canvas
fg = Image.new("RGBA", (LAYER, LAYER), (0, 0, 0, 0))
gh = max(t.height, sy + sh)
lt = np.where(np.array(t)[:, :, 3] > 200)[0]
top = lt.min(); bottom = sy + sh
ox = (LAYER - W) // 2
oy = round(LAYER / 2 - (top + bottom) / 2)
fg.alpha_composite(t, (ox, oy))
fg.alpha_composite(s, (ox + sx, oy + sy))
fg.save(OUT / "OrbitalRail_adaptive_fg_432.png", optimize=True)

# preview: adaptive composite under circle + rounded-square masks (launcher scale 108dp -> 72dp visible)
comp = bgl.convert("RGBA"); comp.alpha_composite(fg)
P = 432  # preview tile
canvas = Image.new("RGB", (P * 2 + 60, P + 40), (28, 28, 32))
for i, mk in enumerate([circle_mask(P), squircle_mask(P, round(P * 0.30))]):
    tile = Image.new("RGBA", (P, P), (0, 0, 0, 0)); tile.alpha_composite(comp.resize((P, P), L))
    # Android shows the central 72/108 of the layer inside the mask
    c = round(P * 72 / 108); o = (P - c) // 2
    crop = tile.crop((o, o, o + c, o + c)).resize((P, P), L)
    crop.putalpha(mk)
    canvas.paste(crop, (20 + i * (P + 20), 20), crop)
canvas.save(HERE / "adaptive_preview.png")
a = np.array(fg)[:, :, 3]
yy, xx = np.mgrid[0:LAYER, 0:LAYER]; r = np.hypot(xx + .5 - LAYER / 2, yy + .5 - LAYER / 2) * 432 / LAYER
print("letters+ship core max radius:", r[a > 200].max(), " any alpha>.2:", r[a > 51].max(), "(safe 132)")
