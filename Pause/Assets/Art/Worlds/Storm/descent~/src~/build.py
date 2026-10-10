"""Build Storm planetfall sprites from retained image-generated paintings.

Only nearest-neighbour resizing is used. Hue/value steering is per pixel; there
is no blur, spatial smoothing, or reduction to a global colour palette.
"""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
NN = Image.Resampling.NEAREST


def source(name: str) -> Image.Image:
    return Image.open(SRC / name).convert("RGBA")


def grade(im: Image.Image, *, value_scale: float = 1.0, alpha_floor: int = 0) -> Image.Image:
    """Keep painted texture and colour variation while enforcing Storm hues."""
    rgba = np.asarray(im.convert("RGBA")).copy()
    hsv = np.asarray(im.convert("RGB").convert("HSV")).copy()
    h = hsv[:, :, 0].astype(np.float32) * (360 / 255)
    s = hsv[:, :, 1].copy()
    v = hsv[:, :, 2].copy()
    chroma = (s > 29) & (v > 18)
    warm = chroma & ((h < 52) | (h >= 330))
    electric = warm & (v > 145) & (s > 55)
    # Lightning to 55 degrees; structural bronze remains quiet at 34 degrees.
    hsv[:, :, 0][warm & ~electric] = 24
    hsv[:, :, 1][warm & ~electric] = np.minimum(s[warm & ~electric], 72)
    hsv[:, :, 0][electric] = 39
    hsv[:, :, 1][electric] = np.minimum(s[electric], 145)
    yellow = chroma & (h >= 52) & (h < 80)
    hsv[:, :, 0][yellow] = 39
    hsv[:, :, 1][yellow] = np.minimum(s[yellow], 145)
    alien = chroma & (h >= 80) & (h < 205)
    hsv[:, :, 0][alien] = 151  # 213 degrees
    violet = chroma & (h >= 221) & (h < 330)
    hsv[:, :, 0][violet] = 151
    storm_blue = chroma & (h >= 205) & (h <= 221)
    hsv[:, :, 1][storm_blue] = np.minimum(s[storm_blue], 125)
    hsv[:, :, 1][violet] = np.minimum(s[violet], 120)
    hsv[:, :, 1][alien] = np.minimum(s[alien], 110)
    hsv[:, :, 2] = np.minimum(255, np.rint(v.astype(np.float32) * value_scale)).astype(np.uint8)
    out = Image.fromarray(hsv, "HSV").convert("RGB").convert("RGBA")
    alpha = rgba[:, :, 3]
    if alpha_floor:
        alpha = np.where(alpha >= alpha_floor, alpha, 0).astype(np.uint8)
    out.putalpha(Image.fromarray(alpha, "L"))
    return out


def planet() -> None:
    raw = grade(source("planet_candidate_a.png"), value_scale=.81, alpha_floor=26)
    # The source is retained in src~. The 4096 composition has a true safe
    # boundary for the cables and stations as well as the planetary disc.
    master = Image.new("RGBA", (4096, 4096))
    large = raw.resize((3600, 3600), NN)
    master.alpha_composite(large, (248, 248))
    master.save(ROOT / "masters" / "storm_planet_master_4096.png")
    master.resize((1024, 1024), NN).save(ROOT / "storm_planet.png")


def limb() -> None:
    im = grade(source("limb_source.png"), value_scale=.76, alpha_floor=30)
    a = np.asarray(im)
    sh, sw = a.shape[:2]
    tops = np.zeros(sw, dtype=np.int32)
    for x in range(sw):
        pix = np.flatnonzero(a[:, x, 3] > 150)
        tops[x] = int(pix[0]) if len(pix) else sh // 2
    dst = np.zeros((1024, 2048, 4), dtype=np.uint8)
    xs = np.minimum((np.arange(2048) * sw / 2048).astype(np.int32), sw - 1)
    for x in range(2048):
        edge = abs((x - 1023.5) / 1023.5)
        horizon = round(355 + 365 * edge ** 1.75)
        top = tops[xs[x]]
        sy = top + (np.arange(1024 - horizon) * (sh - top - 1) / max(1, 1023 - horizon)).astype(np.int32)
        dst[horizon:, x] = a[np.minimum(sy, sh - 1), xs[x]]
        dst[horizon, x] = (168, 205, 222, 255)
    Image.fromarray(dst, "RGBA").save(ROOT / "storm_planet_limb.png")


def seamless(a: np.ndarray, band: int = 96, vertical_only: bool = False) -> np.ndarray:
    """Match opposite painted borders with a finite edge crossfade, no blur."""
    a = a.copy()
    if not vertical_only:
        left = a[:, :band].astype(np.float32).copy()
        for j in range(band):
            weight = 1 - j / (band - 1)
            a[:, -1-j] = np.rint(a[:, -1-j] * (1-weight) + left[:, j] * weight).astype(np.uint8)
    top = a[:band].astype(np.float32).copy()
    for j in range(band):
        weight = 1 - j / (band - 1)
        a[-1-j] = np.rint(a[-1-j] * (1-weight) + top[j] * weight).astype(np.uint8)
    if not vertical_only:
        a[:, -1] = a[:, 0]
    a[-1] = a[0]
    return a


def decks() -> None:
    for src, name, scale in [
        ("cloud_source.png", "storm_cloud_deck.png", .66),
        ("cloud_dark_source.png", "storm_cloud_deck_dark.png", .48),
    ]:
        im = grade(source(src), value_scale=scale).resize((2048, 1024), NN)
        a = np.asarray(im).copy()
        a[:, :, 3] = 255
        Image.fromarray(seamless(a), "RGBA").save(ROOT / name)


def entry() -> None:
    a = grade(source("entry_source.png"), alpha_floor=35)
    b = grade(source("entry_candidate_b.png"), alpha_floor=80)
    strip = Image.new("RGBA", (3072, 1024))
    holes = []
    for i in range(6):
        frame = Image.new("RGBA", (512, 1024))
        width = [470, 478, 466, 480, 472, 476][i]
        height = [812, 826, 806, 828, 816, 824][i]
        phase = [0, 3, -3, 4, -2, 2][i]
        layer = a.resize((width, height), NN)
        frame.alpha_composite(layer, ((512-width)//2 + phase, 191 + (i % 3 - 1)*4))
        # Secondary painted source adds genuine changing tendrils on alternating
        # frames; its centre is replaced with the common registered opening.
        if i in (1, 3, 5):
            accent = b.resize((420, 780), NN)
            accent.putalpha(accent.getchannel("A").point(lambda v: v // 5))
            frame.alpha_composite(accent, (46-phase, 185))
        arr = np.asarray(frame).copy()
        yy, xx = np.ogrid[:1024, :512]
        cx = 256 + [0, 2, -2, 3, -1, 1][i]
        cy = 338 + [0, 1, -1, 1, 0, -1][i]
        rx = [74, 73, 74, 75, 73, 74][i]
        ry = [83, 84, 82, 84, 83, 83][i]
        radius = np.sqrt(((xx-cx)/rx)**2 + ((yy-cy)/ry)**2)
        # Restore accidental gaps within the generated crown using its nearest
        # solid painted pixels in each row before cutting an exact oval hole.
        for y in range(cy-85, cy+86):
            row = arr[y]
            left = np.flatnonzero(row[95:cx, 3] > 150) + 95
            right = np.flatnonzero(row[cx:417, 3] > 150) + cx
            if len(left) and len(right):
                lo, hi = int(left[-1]), int(right[0])
                if 0 < hi-lo < 255:
                    for x in range(lo+1, hi):
                        if row[x, 3] < 35 and radius[y,x] > 1:
                            row[x] = row[lo if x < cx else hi]
        arr[radius <= 1] = 0
        arr[(radius > 1) & (radius <= 1.038)] = (255, 254, 231, 255)
        arr[(radius > 1.038) & (radius <= 1.075)] = (184, 208, 220, 235)
        arr[:8] = 0; arr[-8:] = 0; arr[:, :8] = 0; arr[:, -8:] = 0
        strip.alpha_composite(Image.fromarray(arr, "RGBA"), (i*512, 0))
        holes.append({"center_px": [cx, cy], "size_px": [2*rx+1, 2*ry+1]})
    strip.save(ROOT / "storm_entry_fx.png")
    (SRC / "entry_registration.json").write_text(json.dumps(holes, indent=2) + "\n")


def breakthrough() -> None:
    base = grade(source("breakthrough_source.png"), value_scale=.85, alpha_floor=48)
    strip = Image.new("RGBA", (5120, 1024))
    for i, side in enumerate((260, 430, 620, 800, 940)):
        frame = Image.new("RGBA", (1024, 1024))
        ring = base.resize((side, side), NN)
        if i == 4:
            ring.putalpha(ring.getchannel("A").point(lambda v: round(v*.62)))
        frame.alpha_composite(ring, ((1024-side)//2, (1024-side)//2))
        strip.alpha_composite(frame, (i*1024, 0))
    strip.save(ROOT / "storm_breakthrough.png")


def streaks() -> None:
    im = grade(source("streaks_source.png"), value_scale=.76, alpha_floor=42).resize((1024,2048), NN)
    a = np.asarray(im).copy()
    a[:,:,3] = (a[:,:,3].astype(np.uint16)*.38).astype(np.uint8)
    out = Image.fromarray(a,"RGBA")
    draw = ImageDraw.Draw(out)
    rng = np.random.default_rng(6089)
    colors = [(120,145,163,62),(184,198,208,78),(229,231,219,90),(231,229,176,94)]
    for _ in range(930):
        x = int(rng.integers(10,1014)); y = int(rng.integers(0,2048))
        length = int(rng.integers(20,210))
        draw.line((x,y,x,min(2047,y+length)),fill=colors[int(rng.choice(4,p=[.38,.38,.20,.04]))],width=1)
    arr = seamless(np.asarray(out).copy(),band=72,vertical_only=True)
    Image.fromarray(arr,"RGBA").save(ROOT / "storm_entry_streaks.png")


def previews() -> None:
    bg = Image.new("RGB",(2048,2048),"#0b0b1a")
    draw = ImageDraw.Draw(bg)
    layout = [
        ("storm_planet.png", (0, 40, 660, 700)),
        ("storm_planet_limb.png", (680,40,2028,715)),
        ("storm_cloud_deck.png",(20,750,1010,1245)),
        ("storm_cloud_deck_dark.png",(1030,750,2020,1245)),
        ("storm_entry_fx.png",(20,1300,990,1900)),
        ("storm_breakthrough.png",(1030,1300,2020,1690)),
        ("storm_entry_streaks.png",(1030,1730,2020,1990)),
    ]
    for name,box in layout:
        im = Image.open(ROOT/name).convert("RGBA")
        if name == "storm_entry_fx.png": im = im.crop((0,0,1536,1024))
        if name == "storm_breakthrough.png": im = im.crop((0,0,3072,1024))
        im.thumbnail((box[2]-box[0],box[3]-box[1]-24),NN)
        bg.paste(im,(box[0],box[1]+20),im)
        draw.text((box[0]+5,box[1]),name,fill=(181,208,223))
    bg.resize((4096,4096),NN).save(ROOT/"preview.png")
    fx = Image.open(ROOT/"storm_entry_fx.png")
    frames=[]
    for i in range(6):
        frame=Image.new("RGBA",(512,1024),"#0b0b1a")
        frame.alpha_composite(fx.crop((i*512,0,(i+1)*512,1024)))
        frames.append(frame.resize((256,512),NN).convert("P",palette=Image.Palette.ADAPTIVE))
    frames[0].save(ROOT/"preview.gif",save_all=True,append_images=frames[1:],duration=105,loop=0,disposal=2)


def main() -> None:
    for job in (planet,limb,decks,entry,breakthrough,streaks,previews):
        job()
        print(job.__name__,"done",flush=True)


if __name__ == "__main__": main()
