"""Build Desert elite flight/death art from the five image-generated masters.

All source resampling, animation warps, and preview enlargement use nearest
neighbour. The generated paintings supply every hull's textured surfaces.
"""
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import math

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
CELL = 192
BG = (11, 11, 26, 255)
INK = (5, 8, 14)
SUN = (255, 241, 200)
PALE = (230, 220, 192)
SAND = (217, 203, 160)
DUST = (143, 129, 96)
GLASS = (159, 201, 192)


@dataclass(frozen=True)
class Ship:
    key: str
    title: str
    width: int
    height: int
    core: tuple[int, int]
    nozzles: tuple[tuple[int, int], ...]
    muzzles: tuple[tuple[str, int, int], ...]
    site: str
    attack: str
    moving: str


SHIPS = (
    Ship("dune_stalker", "Dune Stalker", 112, 140, (96, 90), ((88, 158), (104, 158)), (("ram lance", 96, 26), ("port grit", 55, 104), ("starboard grit", 137, 104)), "sand hatch in worm-burrow hole", "lance dash through a sand wake; grit needles", "swept fins flex and segmented tail streams"),
    Ship("caravan_hauler", "Caravan Hauler", 115, 109, (96, 86), ((64, 145), (128, 145)), (("canister drop", 96, 134), ("bucket-wheel", 96, 44)), "crawler bay", "sinking quicksand canisters", "side bunks flex and cradle sways"),
    Ship("lens_matriarch", "Lens Matriarch", 103, 143, (96, 83), ((90, 163), (102, 163)), (("lens fan", 96, 54), ("port shard", 62, 67), ("starboard shard", 130, 67)), "glass pad", "blink and mirage fan of real shards among hollow decoys", "mirror petals spread and glass spine glints"),
    Ship("scorpion_walker", "Scorpion Walker", 113, 137, (96, 99), ((88, 160), (104, 160)), (("sting", 106, 36), ("port claw", 60, 132), ("starboard claw", 132, 132)), "ruin dock", "tail venom lash and claw dive", "claws open, folded legs step and stinger curls"),
    Ship("mirror_bastion", "Mirror Bastion", 116, 128, (96, 78), ((82, 158), (96, 158), (110, 158)), (("lens cannon", 96, 51), ("ward gap", 96, 34)), "buried silo", "ward curtain of slow glass shards with one gap", "shield plates breathe around an open gap"),
)


def mask_shift(a: np.ndarray, dx: int, dy: int) -> np.ndarray:
    out = np.zeros_like(a)
    ys = slice(max(0, dy), min(CELL, CELL + dy))
    xs = slice(max(0, dx), min(CELL, CELL + dx))
    fromys = slice(max(0, -dy), min(CELL, CELL - dy))
    fromxs = slice(max(0, -dx), min(CELL, CELL - dx))
    out[ys, xs] = a[fromys, fromxs]
    return out


def palette_clean(im: Image.Image, lift: float = 0) -> Image.Image:
    alpha = np.asarray(im.getchannel("A"))
    hsv = np.asarray(im.convert("RGB").convert("HSV")).copy()
    h = hsv[:, :, 0].astype(float) * 360 / 255
    s = hsv[:, :, 1].astype(float) / 255
    v = hsv[:, :, 2].astype(float) / 255
    neutral = s < .095
    copper = (~neutral) & ((h < 34) | (h >= 325))
    sand = (~neutral) & (h >= 34) & (h < 64)
    glass = (~neutral) & (h >= 64) & (h < 210)
    indigo = (~neutral) & (h >= 210) & (h < 325)
    # HSV changes keep the source painting's value microtexture and rich ramps.
    h[copper] = 26
    s[copper] = np.minimum(s[copper], .58)
    v[copper] = np.minimum(v[copper], .60)
    h[sand] = 44
    s[sand] = np.minimum(s[sand], .325)
    h[glass] = 166
    s[glass] = np.minimum(s[glass], .20)
    h[indigo] = 240
    s[indigo] = np.minimum(s[indigo], .42)
    v[indigo] = np.minimum(v[indigo], .30)
    # The scorpion master is quite dark; lift body values while retaining the
    # matte copper ceiling. Bright sand and glass facets are left untouched.
    dark = (v < .39) & (alpha > 127) & (~indigo)
    v[dark] = np.minimum(.48, v[dark] * 1.16 + .015)
    if lift:
        material = (alpha > 127) & (~indigo)
        v[material] = np.minimum(1, v[material] + lift)
        v[copper] = np.minimum(v[copper], .60)
    bright_neutral = neutral & (v > .80)
    h[bright_neutral] = 44
    s[bright_neutral] = .17
    hsv[:, :, 0] = np.rint(h * 255 / 360).astype(np.uint8)
    hsv[:, :, 1] = np.rint(s * 255).astype(np.uint8)
    hsv[:, :, 2] = np.rint(v * 255).astype(np.uint8)
    rgb = Image.fromarray(hsv, "HSV").convert("RGB")
    rgb.putalpha(Image.fromarray(alpha, "L"))
    return rgb


def make_base(ship: Ship) -> Image.Image:
    original = Image.open(SRC / f"{ship.key}_master.png").convert("RGBA")
    strong = original.getchannel("A").point(lambda x: 255 if x >= 128 else 0)
    box = strong.getbbox()
    assert box, ship.key
    crop = original.crop(box).resize((ship.width, ship.height), Image.Resampling.NEAREST)
    crop = palette_clean(crop, lift=.09 if ship.key == "scorpion_walker" else 0)
    pixels = np.asarray(crop).copy()
    pixels[:, :, 3] = np.where(pixels[:, :, 3] >= 128, 255, 0).astype(np.uint8)
    pixels[pixels[:, :, 3] == 0, :3] = 0
    base = Image.new("RGBA", (CELL, CELL))
    base.alpha_composite(Image.fromarray(pixels, "RGBA"), ((CELL - ship.width) // 2, (CELL - ship.height) // 2))
    arr = np.asarray(base).copy()
    solid = arr[:, :, 3] > 0
    edge = solid & (~mask_shift(solid, 0, 1) | ~mask_shift(solid, 0, -1) | ~mask_shift(solid, 1, 0) | ~mask_shift(solid, -1, 0))
    # Two pale pixels just inside the upper/left dark outer contour.
    inner = solid & ~edge
    rim = inner & (mask_shift(edge, 0, 1) | mask_shift(edge, 1, 0))
    rgb = arr[:, :, :3].astype(float)
    rgb[edge] = rgb[edge] * .22 + np.array(INK) * .78
    rgb[rim] = rgb[rim] * .42 + np.array(PALE) * .58
    arr[:, :, :3] = np.clip(rgb, 0, 255).astype(np.uint8)
    # Very dark source pixels can round across 250 degrees when mixed with
    # outline ink. Snap those few pixels back into the dusty-indigo band.
    rendered = Image.fromarray(arr, "RGBA")
    hsv = np.asarray(rendered.convert("RGB").convert("HSV")).copy()
    hue = hsv[:, :, 0].astype(float) * 360 / 255
    sat = hsv[:, :, 1].astype(float) / 255
    stray = (hue >= 250) & (hue <= 300) & (sat > .2) & solid
    if np.any(stray):
        hsv[:, :, 0][stray] = round(236 / 360 * 255)
        hsv[:, :, 1][stray] = np.minimum(hsv[:, :, 1][stray], round(.38 * 255))
        fixed = np.asarray(Image.fromarray(hsv, "HSV").convert("RGB"))
        arr[stray, :3] = fixed[stray]
    return Image.fromarray(arr, "RGBA")


def moving_parts(base: Image.Image, ship: Ship, state: int) -> Image.Image:
    if state in (1, 6):
        return base.copy()
    a = np.asarray(base)
    yy, xx = np.indices((CELL, CELL))
    outer = np.clip((np.abs(xx - 96) - 12) / 38, 0, 1)
    if ship.key == "lens_matriarch":
        region = (yy < 106) & (np.abs(xx - 96) > 12)
    elif ship.key == "dune_stalker":
        region = (yy > 68) & (np.abs(xx - 96) > 16)
    elif ship.key == "caravan_hauler":
        region = (yy > 72) & (yy < 151) & (np.abs(xx - 96) > 19)
    elif ship.key == "scorpion_walker":
        region = (yy > 76) & (np.abs(xx - 96) > 15)
    else:
        region = (yy < 145) & (np.abs(xx - 96) > 20)
    if state == 4:
        dy = np.where(xx < 96, -5., 4.) * outer
        dx = (-2.0 - np.where(xx < 96, 1.0, 0.0)) * outer
    elif state == 5:
        dy = np.where(xx < 96, 4., -5.) * outer
        dx = (2.0 + np.where(xx > 96, 1.0, 0.0)) * outer
    else:
        phase = {0: -2.0, 2: 1.5, 3: 2.2}.get(state, 0)
        dy = phase * outer
        dx = np.sign(xx - 96) * phase * .75 * outer
    sy = np.clip(np.rint(yy - dy * region).astype(int), 0, CELL - 1)
    sx = np.clip(np.rint(xx - dx * region).astype(int), 0, CELL - 1)
    warped = a[sy, sx].copy()
    if state in (4, 5):
        sign = -1 if state == 4 else 1
        tilt = np.rint(sign * 1.3 * (96 - yy) / 96).astype(int)
        warped = warped[yy, np.clip(xx - tilt, 0, CELL - 1)]
    return Image.fromarray(warped, "RGBA")


def flight_frame(base: Image.Image, ship: Ship, state: int) -> Image.Image:
    frame = moving_parts(base, ship, state)
    pix = np.asarray(frame).copy()
    if state in (0, 1, 6):
        # Parked lights are off; the armoured body keeps the same value ramp.
        lamp = (pix[:, :, :3].max(axis=2) > 210) & (pix[:, :, 3] > 0)
        factor = .73 if state == 0 else .88 if state == 1 else .78
        pix[lamp, :3] = np.rint(pix[lamp, :3] * factor).astype(np.uint8)
        frame = Image.fromarray(pix, "RGBA")
    marks = Image.new("RGBA", (CELL, CELL))
    d = ImageDraw.Draw(marks)
    cx, cy = ship.core
    if state in (1, 2, 3, 4, 5):
        if state == 1:
            d.point((cx, cy), fill=(*SAND, 255))
        else:
            d.ellipse((cx-2, cy-2, cx+2, cy+2), fill=(*PALE, 220))
            d.point((cx, cy), fill=(*SUN, 255))
    if 2 <= state <= 5:
        for i, (x, y) in enumerate(ship.nozzles):
            length = (12 if state == 2 else 6) + (i + state) % 2
            d.polygon(((x-2, y), (x+2, y), (x, min(174, y+length))), fill=(*PALE, 170))
            d.line(((x, y), (x, min(173, y+length-2))), fill=(*SUN, 255), width=1)
        # Motion remains tied to anatomy: highlighted tips track the warped
        # fins, petals, claws, cradle or shield panels.
        m = -2 if state == 4 else 2 if state == 5 else 0
        if ship.key == "dune_stalker":
            for x, y in ((50+m, 117), (142+m, 117)):
                d.line(((x, y), (x+(-3 if x<96 else 3), y+5)), fill=(*PALE, 230), width=1)
        elif ship.key == "caravan_hauler":
            d.arc((81+m, 113, 111+m, 139), 20, 165, fill=(*PALE, 220), width=1)
            d.point((96+m, 135), fill=(*SUN, 255))
        elif ship.key == "lens_matriarch":
            for x, y in ((66+m, 62), (126+m, 62), (73+m, 78), (119+m, 78)):
                d.point((x, y), fill=(*SUN, 255))
        elif ship.key == "scorpion_walker":
            d.line(((62+m, 128), (57+m, 134)), fill=(*PALE, 245), width=1)
            d.line(((130+m, 128), (135+m, 134)), fill=(*PALE, 245), width=1)
            d.point((105+m, 36), fill=(255, 235, 236, 255))
            d.point((106+m, 36), fill=(255, 235, 236, 255))
        else:
            for x, y in ((48+m, 82), (144+m, 82), (62+m, 51), (130+m, 51)):
                d.point((x, y), fill=(*PALE, 245))
    if state == 6:
        crack = [(cx-15, cy-16), (cx-4, cy-8), (cx-9, cy+1), (cx+4, cy+8), (cx+12, cy+21)]
        d.line(crack, fill=(*INK, 255), width=4)
        d.line([(x-2, y-1) for x, y in crack], fill=(*PALE, 255), width=1)
        for ox, oy in ((-17,-16),(18,10),(5,23),(-24,7)):
            d.point((cx+ox,cy+oy),fill=(*SUN,255))
            d.point((cx+ox+1,cy+oy-1),fill=(*SAND,255))
    frame.alpha_composite(marks)
    return frame


def death_frames(ship: Ship, hover: Image.Image, damaged: Image.Image) -> list[Image.Image]:
    cx, cy = ship.core
    flash = hover.copy()
    d = ImageDraw.Draw(flash)
    d.ellipse((cx-8,cy-8,cx+8,cy+8),fill=(*SUN,255))
    for deg in range(0,360,45):
        t=math.radians(deg)
        d.line(((cx+round(8*math.cos(t)),cy+round(8*math.sin(t))), (cx+round(26*math.cos(t)),cy+round(26*math.sin(t)))),fill=(*PALE,235),width=2)
    rupture = Image.new("RGBA",(CELL,CELL))
    rupture.alpha_composite(damaged.crop((0,0,cx,CELL)),(-7,-4))
    rupture.alpha_composite(damaged.crop((cx,0,CELL,CELL)),(cx+7,4))
    d=ImageDraw.Draw(rupture)
    # Missing angled armour sectors make the rupture a structural break,
    # rather than a complete ship with a bright seam painted over it.
    d.polygon(((cx-37,cy-29),(cx-11,cy-20),(cx-23,cy+18),(cx-48,cy+11)),fill=(0,0,0,0))
    d.polygon(((cx+13,cy-5),(cx+40,cy-19),(cx+45,cy+25),(cx+22,cy+31)),fill=(0,0,0,0))
    d.polygon(((cx-7,cy-34),(cx+6,cy-20),(cx+1,cy+25),(cx-10,cy+31)),fill=(0,0,0,0))
    d.polygon(((cx,cy-19),(cx+13,cy),(cx,cy+20),(cx-13,cy)),fill=(*PALE,230))
    d.ellipse((cx-5,cy-5,cx+5,cy+5),fill=(*SUN,255))
    for x,y in ((cx-31,cy-20),(cx+36,cy+12),(cx-18,cy+28),(cx+19,cy-25)):
        d.line(((x-3,y),(x+3,y-2)),fill=(*SUN,220),width=1)
    debris=Image.new("RGBA",(CELL,CELL))
    # Small masked patches of the generated painting drift outwards. There is
    # no intact body silhouette in the last frame.
    arr=np.asarray(hover)
    pieces=((59,57,14,18,-22,-21),(119,62,14,18,22,-18),
            (74,113,13,17,-20,18),(107,111,13,16,21,20),
            (91,77,11,13,0,-27),(51,96,15,16,-27,1),
            (126,96,15,16,27,-2),(81,95,12,15,-10,-15),
            (102,93,12,15,11,-13),(88,126,11,15,-4,17),
            (111,48,10,14,16,-18),(73,49,10,14,-15,-17))
    for i,(bx,by,bw,bh,dx,dy) in enumerate(pieces):
        part=Image.fromarray(arr[by:by+bh,bx:bx+bw].copy(),"RGBA")
        mask=Image.new("L",(bw,bh))
        md=ImageDraw.Draw(mask)
        if i%2:
            md.polygon(((0,0),(bw-1,0),(bw//2,bh-1),(0,bh-4)),fill=255)
        else:
            md.polygon(((bw//2,0),(bw-1,bh//3),(bw-1,bh-1),(0,bh-1)),fill=255)
        from PIL import ImageChops
        part.putalpha(ImageChops.multiply(part.getchannel("A"),mask))
        debris.alpha_composite(part,(bx+dx,by+dy))
    d=ImageDraw.Draw(debris)
    for deg in range(0,360,30):
        t=math.radians(deg); x=cx+round(30*math.cos(t)); y=cy+round(30*math.sin(t))
        d.point((x,y),fill=(*SUN,255));d.point((x+1,y+1),fill=(*SAND,220))
    for x,y,r in ((58,83,4),(139,75,4),(68,133,5),(126,124,5),(80,58,3),(112,145,3),(96,96,6)):
        d.ellipse((x-r,y-r//2,x+r,y+r//2),fill=(*DUST,130))
    return [flash,rupture,debris]


def on_dark(im: Image.Image) -> Image.Image:
    out=Image.new("RGBA",im.size,BG)
    out.alpha_composite(im)
    return out.convert("RGB")


def main() -> None:
    rows=[]
    anim=[[] for _ in range(4)]
    for ship in SHIPS:
        master=Image.open(SRC/f"{ship.key}_master.png").convert("RGBA")
        palette_clean(master, lift=.09 if ship.key == "scorpion_walker" else 0).save(ROOT/f"desert_elite_{ship.key}_concept.png")
        base=make_base(ship)
        frames=[flight_frame(base,ship,n) for n in range(7)]
        strip=Image.new("RGBA",(CELL*7,CELL))
        for n,im in enumerate(frames):strip.alpha_composite(im,(n*CELL,0))
        strip.save(ROOT/f"desert_elite_{ship.key}.png")
        death=Image.new("RGBA",(CELL*3,CELL))
        for n,im in enumerate(death_frames(ship,frames[3],frames[6])):death.alpha_composite(im,(n*CELL,0))
        death.save(ROOT/f"desert_elite_{ship.key}_death.png")
        rows.append(strip)
        for i,idx in enumerate((3,4,3,5)):anim[i].append(frames[idx])
    # The approved Space comparison is read only.
    comp=Image.open(Path("Pause/Assets/Art/Resources/Elites/Space/space_elite_eventide_bastion.png")).convert("RGBA")
    rows.append(comp)
    sheet=Image.new("RGBA",(CELL*7,len(rows)*CELL),BG)
    for i,row in enumerate(rows):sheet.alpha_composite(row,(0,i*CELL))
    on_dark(sheet).resize((sheet.width*2,sheet.height*2),Image.Resampling.NEAREST).save(ROOT/"contact_sheet.png")
    gif_frames=[]
    for group in anim:
        panel=Image.new("RGBA",(CELL,len(group)*CELL),BG)
        for i,im in enumerate(group):panel.alpha_composite(im,(0,i*CELL))
        gif_frames.append(on_dark(panel).resize((CELL*2,len(group)*CELL*2),Image.Resampling.NEAREST))
    gif_frames[0].save(ROOT/"preview.gif",save_all=True,append_images=gif_frames[1:],duration=[180,130,180,130],loop=0,optimize=False)


if __name__ == "__main__":
    main()
