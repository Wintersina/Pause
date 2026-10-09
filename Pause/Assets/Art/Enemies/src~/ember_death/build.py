"""Build Ember death strips from selected painted candidates and idle cell 3.

Run from the repository root: python3 Pause/Assets/Art/Enemies/src~/ember_death/build.py
Only new death PNGs and previews are written. Resampling is nearest-neighbour.
"""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


HERE = Path(__file__).resolve().parent
ENEMIES = HERE.parents[2] / "Resources" / "Enemies"
DEATH = ENEMIES / "Death"
KEYS = [
    "ember_alien", "ember_chaser", "ember_fighter_1", "ember_fighter_2",
    "ember_fighter_3", "ember_fighter_4", "ember_rock_cinder",
    "ember_rock_islet", "ember_rock_magma", "ember_rock_obsidian", "ember_big",
]
CORE = {
    "ember_alien": (158, 112), "ember_chaser": (96, 125),
    "ember_fighter_1": (96, 94), "ember_fighter_2": (96, 92),
    "ember_fighter_3": (96, 117), "ember_fighter_4": (96, 98),
    "ember_rock_cinder": (96, 102), "ember_rock_islet": (96, 103),
    "ember_rock_magma": (96, 96), "ember_rock_obsidian": (96, 96),
    "ember_big": (128, 137),
}
PALETTE_HEX = [
    "0e0808", "1d1212", "2e1d1a", "47302b", "6a4a40", "957060",
    "171518", "292528", "403a3b", "5d5353", "827573", "b0a29a",
    "342014", "5b3921", "8c5929", "b87835", "e0a252",
    "6a2404", "c24a06", "f77a0a", "fcb809", "fcee09", "fdfa92", "fffbe0",
    "1b1819", "302a2b", "484045", "655756",
]
PAL = np.array([tuple(bytes.fromhex(h)) for h in PALETTE_HEX], dtype=np.uint8)
BG = (18, 14, 13)


def clean_palette(im: Image.Image, *, min_alpha: int = 25) -> Image.Image:
    a = np.array(im.convert("RGBA"), dtype=np.uint8).copy()
    rgb = a[:, :, :3]
    alpha = a[:, :, 3]
    # Reference eyes/cores use pink. Rotate only those saturated hues to amber.
    hsv = np.array(Image.fromarray(rgb, "RGB").convert("HSV"), dtype=np.uint8)
    hue = hsv[:, :, 0]
    sat = hsv[:, :, 1]
    val = hsv[:, :, 2]
    foreign = ((hue >= 45) | (hue < 9)) & (sat > 100) & (val > 30)
    hsv[:, :, 0][foreign] = 23  # Pillow hue units: 32 degrees, molten amber.
    hsv[:, :, 1][foreign] = np.maximum(sat[foreign], 170)
    rgb[:] = np.array(Image.fromarray(hsv, "HSV").convert("RGB"))
    flat = rgb.reshape(-1, 3).astype(np.int32)
    out = np.empty_like(flat, dtype=np.uint8)
    # Modest chunks avoid a large temporary Nx28x3 matrix for generated art.
    for start in range(0, len(flat), 20000):
        v = flat[start:start + 20000]
        dist = ((v[:, None, :] - PAL[None, :, :].astype(np.int32)) ** 2).sum(axis=2)
        out[start:start + len(v)] = PAL[dist.argmin(axis=1)]
    rgb[:] = out.reshape(rgb.shape)
    alpha[alpha < min_alpha] = 0
    alpha[(alpha >= min_alpha) & (alpha < 70)] = 64
    alpha[(alpha >= 70) & (alpha < 130)] = 112
    alpha[(alpha >= 130) & (alpha < 210)] = 176
    alpha[alpha >= 210] = 255
    return Image.fromarray(a, "RGBA")


def strong_bbox(im: Image.Image, threshold: int = 210) -> tuple[int, int, int, int]:
    arr = np.asarray(im.getchannel("A"))
    ys, xs = np.where(arr >= threshold)
    if len(xs) == 0:
        return (0, 0, im.width, im.height)
    # Outlying particles do not set the painted form's scale.
    return tuple(map(int, (np.percentile(xs, 1), np.percentile(ys, 1),
                           np.percentile(xs, 99) + 1, np.percentile(ys, 99) + 1)))


def placed_candidate(panel: Image.Image, idle: Image.Image, side: int,
                     stage: int, key: str) -> Image.Image:
    ib = strong_bbox(idle, 180)
    cb = strong_bbox(panel)
    iw, ih = ib[2] - ib[0], ib[3] - ib[1]
    cw, ch = cb[2] - cb[0], cb[3] - cb[1]
    # Rupture is a little larger; final drift is wider but mostly transparent.
    factor = 1.13 if stage == 1 else 0.95
    if key == "ember_big":
        factor = 0.99 if stage == 1 else 0.82
    if key == "ember_rock_islet" and stage == 1:
        factor = 1.07
    scale = min(factor * iw / max(cw, 1), factor * ih / max(ch, 1),
                (side - 16) / panel.width, (side - 16) / panel.height)
    nw = max(1, round(panel.width * scale)); nh = max(1, round(panel.height * scale))
    panel = panel.resize((nw, nh), Image.Resampling.NEAREST)
    # Align the strong-art center to the idle silhouette center, not the canvas.
    cx = (cb[0] + cb[2]) * 0.5 * scale
    cy = (cb[1] + cb[3]) * 0.5 * scale
    tx = round((ib[0] + ib[2]) * 0.5 - cx)
    ty = round((ib[1] + ib[3]) * 0.5 - cy)
    tx = max(6, min(side - 6 - nw, tx)) if nw <= side - 12 else tx
    ty = max(6, min(side - 6 - nh, ty)) if nh <= side - 12 else ty
    out = Image.new("RGBA", (side, side))
    out.alpha_composite(panel, (tx, ty))
    return out


def starburst(idle: Image.Image, key: str) -> Image.Image:
    side = idle.height
    x, y = CORE[key]
    if side == 256:
        x, y = round(x), round(y)
    out = idle.copy()
    # Small hard-stepped amber halo and eight jagged arms. The underlying hull
    # pixels remain intact and at exactly the idle cell's register.
    fx = Image.new("RGBA", (side, side))
    d = ImageDraw.Draw(fx)
    r = 13 if side == 256 else 9
    d.ellipse((x-r, y-r, x+r, y+r), fill=(252, 138, 16, 64))
    d.ellipse((x-r//2, y-r//2, x+r//2, y+r//2), fill=(252, 184, 9, 112))
    rays = [(1,0),(-1,0),(0,1),(0,-1),(0.7,0.7),(-0.7,0.7),(0.7,-0.7),(-0.7,-0.7)]
    for idx, (dx, dy) in enumerate(rays):
        length = (23 if side == 256 else 16) - (idx % 3) * 2
        px, py = x+round(dx*length), y+round(dy*length)
        jx, jy = x+round(dx*length*.48-dy*2), y+round(dy*length*.48+dx*2)
        d.line((x,y,jx,jy,px,py), fill=(194,74,6,255), width=3)
        d.line((x,y,jx,jy,px,py), fill=(252,184,9,255), width=1)
    d.ellipse((x-5,y-5,x+5,y+5), fill=(252,238,9,255))
    d.ellipse((x-3,y-3,x+3,y+3), fill=(255,251,224,255))
    out.alpha_composite(fx)
    return out


def save_preview(strips: dict[str, Image.Image]) -> None:
    rowh = 2*256 + 38
    canvas = Image.new("RGB", (4*2*256 + 24, rowh*len(KEYS)), BG)
    d = ImageDraw.Draw(canvas)
    for i, key in enumerate(KEYS):
        s = strips[key].height
        idle = Image.open(ENEMIES / f"{key}.png").convert("RGBA").crop((3*s,0,4*s,s))
        cells = [idle] + [strips[key].crop((j*s,0,(j+1)*s,s)) for j in range(3)]
        y = i*rowh+28
        d.text((12,i*rowh+8),key,fill=(225,195,159))
        for j, cell in enumerate(cells):
            up = cell.resize((2*s,2*s), Image.Resampling.NEAREST)
            canvas.paste(up,(j*2*256+12,y),up)
    canvas.save(HERE / "preview.png")

    reps = ["ember_alien", "ember_fighter_2", "ember_rock_magma", "ember_big"]
    frames=[]
    for stage in range(3):
        sheet=Image.new("RGB",(4*256,256),BG)
        for i,key in enumerate(reps):
            strip=strips[key]; s=strip.height
            cell=strip.crop((stage*s,0,(stage+1)*s,s))
            sheet.paste(cell,(i*256+(256-s)//2,(256-s)//2),cell)
        frames.append(sheet)
    frames[0].save(HERE/"preview.gif",save_all=True,append_images=frames[1:],duration=[80,110,200],loop=0,disposal=2,optimize=False)


def main() -> None:
    strips={}
    for key in KEYS:
        source=Image.open(ENEMIES/f"{key}.png").convert("RGBA")
        side=source.height
        idle=source.crop((3*side,0,4*side,side))
        first=starburst(clean_palette(idle, min_alpha=1),key)
        cand=Image.open(HERE/"candidates"/f"{key}.png").convert("RGBA")
        panels=[cand.crop((i*cand.height,0,(i+1)*cand.height,cand.height)) for i in range(2)]
        second=placed_candidate(clean_palette(panels[0]),idle,side,1,key)
        third=placed_candidate(clean_palette(panels[1]),idle,side,2,key)
        strip=Image.new("RGBA",(side*3,side))
        for i,cell in enumerate((first,second,third)):
            strip.alpha_composite(cell,(i*side,0))
        strip.save(DEATH/f"{key}.png")
        strips[key]=strip
    save_preview(strips)


if __name__ == "__main__":
    main()
