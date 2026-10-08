"""Assemble painted Frost rupture candidates into native three-cell death strips.

The *_rupture_candidate.png images are the image-generation source art.  This
script keeps idle cell 3 as the first pose, adds the short cyan overload, and
uses the painted rupture plus recoloured Space vapour for the two later poses.
All scaling uses nearest-neighbour sampling.
"""

from __future__ import annotations

import colorsys
import hashlib
import math
import random
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


HERE = Path(__file__).resolve().parent
ART = HERE.parents[2] / "Resources" / "Enemies"
DEATH = ART / "Death"
KEYS = [
    "frost_alien", "frost_chaser", "frost_fighter_1", "frost_fighter_2",
    "frost_fighter_3", "frost_fighter_4", "frost_big",
    "frost_rock_chunk", "frost_rock_shard",
]
SPACE = {
    "frost_alien": "space_alien", "frost_chaser": "space_chaser",
    "frost_fighter_1": "space_fighter_1", "frost_fighter_2": "space_fighter_2",
    "frost_fighter_3": "space_fighter_3", "frost_fighter_4": "space_fighter_4",
    "frost_big": "space_fighter_4", "frost_rock_chunk": "space_rock_cluster",
    "frost_rock_shard": "space_rock_dark",
}
INK = (5, 6, 12)
ICE = [(10, 23, 52), (28, 60, 104), (42, 98, 174),
       (74, 150, 230), (159, 210, 242), (226, 251, 255)]
METAL = [(12, 16, 32), (30, 38, 56), (51, 64, 92),
         (79, 97, 136), (125, 147, 189)]
CYAN = [(21, 105, 200), (24, 152, 247), (70, 221, 253),
        (170, 242, 251), (248, 254, 252)]
COPPER = [(44, 30, 24), (74, 48, 33), (116, 66, 40),
          (159, 95, 52), (196, 135, 78), (220, 173, 110)]
PALETTE = np.asarray([INK] + ICE + METAL + CYAN + COPPER, dtype=np.int16)
BRIGHT = (248, 254, 252, 255)
GLINT = (70, 221, 253, 255)
MIST = (159, 210, 242, 150)


def rgba(path: Path) -> Image.Image:
    return Image.open(path).convert("RGBA")


def native_idle(key: str) -> Image.Image:
    strip = rgba(ART / f"{key}.png")
    side = strip.height
    assert strip.width == 7 * side, (key, strip.size)
    return strip.crop((3 * side, 0, 4 * side, side))


def fixed_palette(image: Image.Image) -> Image.Image:
    """Map solid candidate colours into the documented Frost material ramps."""
    data = np.array(image, dtype=np.uint8)
    rgb = data[:, :, :3].astype(np.int16)
    alpha = data[:, :, 3]
    # RGB distance is sufficient after the candidates' own blue/copper ranges
    # have been classified; keeping ramps separate prevents purple artifacts.
    flat = rgb.reshape(-1, 3)
    out = np.empty_like(flat, dtype=np.uint8)
    step = 8192
    for start in range(0, len(flat), step):
        chunk = flat[start:start + step]
        red_copper = ((chunk[:, 0] > chunk[:, 2] * 1.20)
                      & (chunk[:, 0] > chunk[:, 1] * 1.09)
                      & (chunk[:, 0] > 48))
        electric = ((chunk[:, 2] > chunk[:, 0] * 1.16)
                    & (chunk[:, 1] > chunk[:, 0] * 1.50)
                    & (chunk[:, 1] > 95))
        masks = [(red_copper, np.asarray(COPPER, dtype=np.int16)),
                 (electric & ~red_copper, np.asarray(CYAN + ICE[3:], dtype=np.int16)),
                 (~red_copper & ~electric, PALETTE[:1 + len(ICE) + len(METAL)])]
        for mask, palette in masks:
            if not np.any(mask):
                continue
            delta = chunk[mask, None, :] - palette[None, :, :]
            dist = (delta * delta).sum(axis=2)
            out[start:start + len(chunk)][mask] = palette[dist.argmin(axis=1)]
    data[:, :, :3] = out.reshape(rgb.shape)
    # The hard alpha levels preserve a small glow without antialiased fringe.
    levels = np.asarray([0, 64, 112, 160, 208, 255], dtype=np.uint8)
    data[:, :, 3] = levels[np.abs(alpha[:, :, None].astype(np.int16)
                                  - levels[None, None, :]).argmin(axis=2)]
    data[data[:, :, 3] == 0, :3] = 0
    return Image.fromarray(data, "RGBA")


def rupture(key: str, side: int) -> Image.Image:
    source = rgba(HERE / f"{key}_rupture_candidate.png")
    a = np.array(source.getchannel("A"))
    ys, xs = np.nonzero(a > 72)
    box = (int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)
    crop = source.crop(box)
    limit = side - (24 if side == 192 else 32)
    scale = min(limit / crop.width, limit / crop.height)
    target = (max(1, round(crop.width * scale)), max(1, round(crop.height * scale)))
    crop = crop.resize(target, Image.Resampling.NEAREST)
    canvas = Image.new("RGBA", (side, side))
    x, y = (side - target[0]) // 2, (side - target[1]) // 2
    canvas.alpha_composite(crop, (x, y))
    canvas = fixed_palette(canvas)
    # The candidates' white ice has more coverage than the shipped Space
    # middle poses. Step the shell and frost down one ramp tone, preserving
    # only the radial core as truly white-hot.
    px = np.array(canvas)
    original = px.copy()
    yy, xx = np.indices((side, side))
    radial = np.hypot(xx-side/2, yy-side/2)
    for old, new, minimum in [
        (ICE[5], ICE[4], side*.08),
        (ICE[4], ICE[3], side*.20),
        (METAL[4], METAL[3], side*.32),
        (CYAN[4], CYAN[3], side*.11),
    ]:
        mask = np.all(original[:,:,:3] == old, axis=2) & (radial > minimum) & (px[:,:,3] > 0)
        px[mask,:3] = new
    canvas = Image.fromarray(px, "RGBA")
    if key == "frost_chaser":
        # The generated lance remains too joined at the center. A short open
        # fracture behind its nose separates the two painted pieces.
        d = ImageDraw.Draw(canvas)
        cx, cy = int(side * .48), int(side * .50)
        d.line([(cx - 5, cy - 30), (cx - 2, cy - 11), (cx + 2, cy + 6),
                (cx - 1, cy + 27)], fill=(0, 0, 0, 0), width=4)
        d.line([(cx - 6, cy - 30), (cx - 3, cy - 11), (cx + 1, cy + 6),
                (cx - 2, cy + 27)], fill=GLINT, width=1)
    if key == "frost_fighter_3":
        d = ImageDraw.Draw(canvas)
        cx, cy = side // 2, side // 2
        pts = [(cx + round(16 * math.cos(i * math.pi / 3)),
                cy + round(16 * math.sin(i * math.pi / 3))) for i in range(6)]
        d.line(pts + [pts[0]], fill=GLINT, width=1)
    return margin_bright(canvas)


def flash(key: str, idle: Image.Image) -> Image.Image:
    side = idle.height
    out = idle.copy()
    core = (side // 2, side // 2)
    if key == "frost_chaser":
        core = (side // 2 - 4, side // 2)
    if key == "frost_big":
        core = (side // 2, side // 2 - 5)
    if key in ("frost_rock_chunk", "frost_rock_shard"):
        core = (side // 2, side // 2 + 4)
    cx, cy = core
    # Quantised halo behind a short eight-point flash.
    halo = Image.new("RGBA", out.size)
    hd = ImageDraw.Draw(halo)
    for radius, a in [(21, 40), (14, 75), (8, 110)]:
        hd.ellipse((cx-radius, cy-radius, cx+radius, cy+radius),
                   fill=(70, 200, 253, a))
    out = Image.alpha_composite(out, halo)
    d = ImageDraw.Draw(out)
    radius = 22 if side == 192 else 30
    for i in range(12):
        t = i * math.pi / 6
        length = radius + (8 if i % 3 == 0 else -4)
        ex = cx + round(math.cos(t) * length)
        ey = cy + round(math.sin(t) * length)
        mx = cx + round(math.cos(t) * (length * .58)) + (1 if i % 2 else -1)
        my = cy + round(math.sin(t) * (length * .58))
        d.line([(cx, cy), (mx, my), (ex, ey)], fill=CYAN[2] + (255,), width=2 if i % 3 == 0 else 1)
        d.point((ex, ey), fill=CYAN[3] + (255,))
    if key.startswith("frost_rock"):
        for end in [(cx-31,cy-42),(cx+37,cy-24),(cx-27,cy+39),(cx+23,cy+43)]:
            mid = ((cx + end[0])//2 + 4, (cy + end[1])//2 - 3)
            d.line([core, mid, end], fill=CYAN[2] + (255,), width=2)
            d.line([core, mid], fill=BRIGHT, width=1)
    else:
        # The glass/visor fogs at the overload, without replacing the hull.
        d.line([(cx-9,cy-3),(cx+8,cy-3)], fill=CYAN[3] + (255,), width=2)
        d.line([(cx-5,cy+3),(cx+4,cy+3)], fill=BRIGHT, width=1)
    d.ellipse((cx-5,cy-5,cx+5,cy+5), fill=CYAN[3] + (255,))
    d.ellipse((cx-2,cy-2,cx+2,cy+2), fill=BRIGHT)
    return margin_bright(out)


def margin_bright(im: Image.Image) -> Image.Image:
    """Dim original edge highlights when a source idle pose reaches y=5."""
    a = np.array(im)
    h, w = a.shape[:2]
    yy, xx = np.indices((h, w))
    edge = (xx < 6) | (yy < 6) | (xx >= w - 6) | (yy >= h - 6)
    bright = (a[:, :, :3].max(axis=2) >= 178) & (a[:, :, 3] >= 96)
    a[edge & bright, :3] = METAL[2]
    return Image.fromarray(a, "RGBA")


def remove_source_red(im: Image.Image) -> Image.Image:
    """Correct stray red/purple rust pixels inherited from the idle art."""
    a = np.array(im)
    rgb = a[:, :, :3].astype(np.float32) / 255
    mx, mn = rgb.max(axis=2), rgb.min(axis=2)
    diff = mx - mn
    sat = diff / np.maximum(mx, 1e-6)
    r, g, b = np.moveaxis(rgb, 2, 0)
    hue = np.zeros(mx.shape, dtype=np.float32)
    nz = diff > 1e-5
    rm = nz & (mx == r)
    gm = nz & (mx == g) & ~rm
    bm = nz & ~rm & ~gm
    hue[rm] = ((g[rm]-b[rm])/diff[rm]) % 6
    hue[gm] = (b[gm]-r[gm])/diff[gm] + 2
    hue[bm] = (r[bm]-g[bm])/diff[bm] + 4
    hue *= 60
    visible = (a[:, :, 3] >= 128) & (sat >= .5) & (mx >= .2)
    warm = ((hue <= 15) | (hue >= 345)) & visible
    purple = (hue >= 285) & (hue < 345) & visible
    luma = .28*a[:,:,0].astype(float)+.59*a[:,:,1]+.13*a[:,:,2]
    a[warm,:3] = np.asarray(COPPER,dtype=np.uint8)[np.digitize(luma[warm],[40,60,87,120,165])]
    a[purple,:3] = np.asarray(METAL,dtype=np.uint8)[np.digitize(luma[purple],[32,54,83,125])]
    return Image.fromarray(a,"RGBA")


def frost_vapour(source: Image.Image) -> Image.Image:
    a = np.array(source, dtype=np.uint8)
    rgb = a[:, :, :3].astype(np.float32)
    r, g, b = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
    lum = .28*r + .59*g + .13*b
    pink = (r > 150) & (r-g > 80) & (b-g > 70)
    # Painted magenta spark clusters become discrete cyan glints. Smoke has a
    # cold blue-grey ramp with increased visibility against the dark preview.
    level = np.digitize(lum, [34, 52, 80, 112, 150])
    smoke = np.asarray([(30,38,56),(51,64,92),(79,97,136),
                        (125,147,189),(159,210,242),(226,251,255)],dtype=np.uint8)
    a[:, :, :3] = smoke[level]
    a[pink, :3] = np.where((lum[pink] > 140)[:, None],
                              np.asarray(CYAN[4],dtype=np.uint8),
                              np.asarray(CYAN[2],dtype=np.uint8))
    alpha = a[:, :, 3]
    alpha[(~pink) & (alpha > 0)] = np.minimum(190, alpha[(~pink) & (alpha > 0)].astype(np.int16) + 12)
    a[:, :, 3] = alpha
    return Image.fromarray(a, "RGBA")


def aftermath(key: str, side: int, burst: Image.Image) -> Image.Image:
    space = rgba(DEATH / f"{SPACE[key]}.png")
    ss = space.height
    mist = frost_vapour(space.crop((ss*2, 0, ss*3, ss)))
    box = mist.getbbox()
    mist = mist.crop(box)
    if key == "frost_chaser":
        tw, th = int(side*.80), int(side*.43)
    elif key == "frost_fighter_4":
        tw, th = int(side*.77), int(side*.68)
    elif key == "frost_big":
        tw, th = int(side*.76), int(side*.65)
    elif key.startswith("frost_rock"):
        tw, th = int(side*.60), int(side*.54)
    else:
        tw, th = int(side*.70), int(side*.59)
    mist = mist.resize((tw, th), Image.Resampling.NEAREST)
    if key.startswith("frost_rock"):
        ma = np.array(mist)
        ma[:, :, 3] = (ma[:, :, 3].astype(np.float32)*.42).astype(np.uint8)
        mist = Image.fromarray(ma, "RGBA")
    out = Image.new("RGBA", (side, side))
    out.alpha_composite(mist, ((side-tw)//2, (side-th)//2))
    # Each aftermath retains tiny pieces sampled from its own painted burst.
    # Local crops, rather than synthetic fill shapes, keep its ice/metal grain.
    rng = random.Random(int(hashlib.sha256(key.encode()).hexdigest()[:8],16))
    arr = np.asarray(burst)
    yy, xx = np.indices((side,side))
    dist = np.hypot(xx-side/2, yy-side/2)
    ice_or_copper = (((arr[:,:,2] > arr[:,:,0]*1.16)
                      & (arr[:,:,1] > 75))
                     | ((arr[:,:,0] > arr[:,:,2]*1.20)
                        & (arr[:,:,0] > 75)))
    ys, xs = np.nonzero((arr[:,:,3] > 200) & ice_or_copper
                        & (dist > side*.25) & (dist < side*.43))
    n = 4 if key.startswith("frost_rock") else (17 if key == "frost_big" else 11)
    for i in range(n):
        if len(xs) == 0:
            break
        q = rng.randrange(len(xs))
        size = rng.choice([3,4,5,6] if side == 192 else [4,5,6,8])
        left = max(0, min(side-size, int(xs[q])-size//2))
        top = max(0, min(side-size, int(ys[q])-size//2))
        tile = burst.crop((left, top, left+size, top+size))
        angle = 2*math.pi*i/max(1,n) + rng.uniform(-.32,.32)
        rad = rng.uniform(side*.24,side*.37)
        px = int(side/2 + math.cos(angle)*rad-size/2)
        py = int(side/2 + math.sin(angle)*rad-size/2)
        out.alpha_composite(tile, (max(7,min(side-size-7,px)),
                                   max(7,min(side-size-7,py))))
    d = ImageDraw.Draw(out)
    if key == "frost_alien":
        # One dangling crystal tentacle survives the vapour.
        d.polygon([(side//2+28,side//2+31),(side//2+34,side//2+30),
                   (side//2+31,side//2+49)], fill=ICE[3]+(230,))
        d.line([(side//2+30,side//2+33),(side//2+31,side//2+45)],
               fill=ICE[5]+(255,),width=1)
    elif key == "frost_fighter_2":
        d.polygon([(side//2+32,side//2+21),(side//2+40,side//2+21),
                   (side//2+35,side//2+46)], fill=ICE[3]+(245,))
        d.line([(side//2+35,side//2+23),(side//2+35,side//2+43)],
               fill=ICE[5]+(255,),width=1)
    elif key == "frost_fighter_4":
        for i in range(11):
            x = rng.randrange(25,side-25)
            y = rng.randrange(side//2-22,side//2+58)
            d.ellipse((x,y,x+2,y+2),fill=ICE[4]+(205,))
    elif key == "frost_big":
        for dx,dy in [(-45,37),(43,31),(-21,52)]:
            x,y=side//2+dx,side//2+dy
            d.polygon([(x-8,y+3),(x-5,y-7),(x+7,y-4),(x+10,y+4)],
                      fill=ICE[2]+(255,))
            d.line([(x-5,y-6),(x+6,y-4)],fill=ICE[4]+(255,),width=2)
    if key.startswith("frost_rock"):
        # Crystal shatter dissipates quickly: only three or four pin glints.
        for dx,dy in [(-29,-18),(23,-23),(37,15),(-18,32)]:
            x,y=side//2+dx,side//2+dy
            d.point((x,y),fill=BRIGHT)
            d.line([(x-2,y),(x+2,y)],fill=GLINT,width=1)
    return margin_bright(out)


def make_preview(strips: dict[str, Image.Image], idles: dict[str, Image.Image]) -> None:
    row_h = 2*256 + 28
    width = 4*2*256 + 150
    height = row_h*len(KEYS)
    bg=(7,16,31,255)
    out=Image.new("RGBA",(width,height),bg)
    d=ImageDraw.Draw(out)
    for i,key in enumerate(KEYS):
        y=i*row_h
        side=idles[key].height
        d.text((12,y+12),key,fill=(217,239,247,255))
        cells=[idles[key]]+[strips[key].crop((j*side,0,(j+1)*side,side)) for j in range(3)]
        for j,cell in enumerate(cells):
            x=150+j*512+(256-side)
            out.alpha_composite(cell.resize((side*2,side*2),Image.Resampling.NEAREST),(x,y+26))
            d.text((x+4,y+5),("idle 3","0 · 0.08s","1 · 0.11s","2 · 0.20s")[j],
                   fill=(115,166,193,255))
    out.convert("RGB").save(HERE/"preview.png")

    reps=["frost_alien","frost_fighter_1","frost_big","frost_rock_shard"]
    frames=[]
    for step in range(3):
        canvas=Image.new("RGBA",(2*512+24,2*512+48),bg)
        dd=ImageDraw.Draw(canvas)
        for i,key in enumerate(reps):
            side=idles[key].height
            cell=strips[key].crop((step*side,0,(step+1)*side,side))
            col,row=i%2,i//2
            x=col*512+(512-side*2)//2+12
            y=row*512+(512-side*2)//2+24
            canvas.alpha_composite(cell.resize((side*2,side*2),Image.Resampling.NEAREST),(x,y))
            dd.text((col*512+24,row*512+24),key,fill=(217,239,247,255))
        frames.append(canvas.convert("RGB"))
    frames[0].save(HERE/"preview.gif",save_all=True,append_images=frames[1:],
                   duration=[80,110,200],loop=0,optimize=False,disposal=2)


def main() -> None:
    strips={}
    idles={}
    for key in KEYS:
        idle=native_idle(key)
        side=idle.height
        cells=[remove_source_red(flash(key,idle)),rupture(key,side)]
        cells.append(aftermath(key,side,cells[1]))
        strip=Image.new("RGBA",(side*3,side))
        for i,cell in enumerate(cells):
            strip.paste(cell,(side*i,0))
        strip.save(DEATH/f"{key}.png")
        strips[key]=strip
        idles[key]=idle
    make_preview(strips,idles)


if __name__=="__main__":
    main()
