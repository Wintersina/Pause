"""Desert run C loop sheets at TRUE 256 px-per-cell resolution (build_c.py authored every frame on a
128 canvas and doubled it with nearest-neighbour). Same sources, ramps, timing and layout; all
authoring coordinates are x2 and the painted keys are resampled once with Lanczos.

    python3 build_hires_c.py <out dir>   # writes the nine loop atlases (+ json) there
"""
from __future__ import annotations

import json, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
SRC = Path(__file__).resolve().parent
SOURCES = {k: Image.open(SRC / f"candidate_c_{k}.png").convert("RGBA") for k in ("smoke", "sand", "lights")}
R = Image.Resampling.NEAREST
INK = np.array([12, 12, 25], np.uint8)
SAND = np.array([[47, 45, 51], [69, 65, 65], [85, 80, 73], [105, 98, 80], [124, 115, 91], [151, 139, 109], [179, 166, 131], [217, 203, 160], [230, 220, 192]], np.uint8)
SMOKE = np.array([[15, 17, 27], [24, 25, 39], [34, 35, 49], [47, 47, 61], [61, 59, 69], [78, 73, 76], [96, 88, 80], [128, 117, 100], [170, 157, 132]], np.uint8)
METAL = np.array([[11, 12, 22], [27, 27, 39], [39, 38, 46], [54, 50, 51], [69, 59, 55], [86, 68, 59], [108, 81, 65], [129, 95, 71], [150, 111, 78]], np.uint8)
LIGHT = (255, 241, 200)

LOOPS = [
    ("smoke_a", 8, "smoke", 0, "smoke", "smoke", 6, "refinery stacks; buried hull vents", .65, True),
    ("smoke_b", 8, "smoke", 1, "steam", "smoke", 7, "pipe vents; dry wells", .65, True),
    ("dustdevil", 8, "smoke", 2, "sand", "dust", 8, "open dune sea; worm-ridge wake", .60, True),
    ("sandplume", 8, "smoke", 3, "sand", "dust", 8, "dune crests; buried gantries", .60, True),
    ("sandfall", 8, "sand", 0, "sand", "dust_2", 8, "canyon ledges; refinery walls", .68, True),
    ("grit_gust", 8, "sand", 2, "sand", "dust_2", 10, "dune sea; crawler caravan", .43, False),
    ("heat_vent", 8, "lights", 2, "shimmer", "dust_3", 8, "heat vents; glass flats", .28, True),
    ("heat_shimmer", 8, "lights", 2, "shimmer", "shimmer", 7, "dune crests; machine exhaust", .20, True),
    ("mirage_glint", 8, "lights", 2, "shimmer", "shimmer", 6, "salt flats; glass pans", .27, False),
    ("glass_sparkle", 8, "lights", 3, "sparkle", "shimmer_2", 8, "fused glass; dead solar farms", .55, False),
    ("steam_vent", 4, "smoke", 1, "steam", "leaks", 7, "pipe vents; drill heads", .65, True),
    ("pipe_drip_sand", 4, "sand", 3, "sand", "leaks", 8, "broken pipes; refineries", .66, True),
    ("grit_rain", 4, "sand", 0, "rain", "leaks", 10, "gantry underhangs; canyon walls", .45, False),
    ("sand_slide", 4, "sand", 1, "sand", "leaks", 7, "dune lee; worm trail", .68, True),
    ("beacon_white", 4, "lights", 0, "beacon", "lights", 5, "mast beacons; ruined towers", 1.0, True),
    ("beacon_pale_gold", 4, "lights", 0, "beacon", "lights", 5, "crawler masts; dry wells", 1.0, True),
    ("window_lights", 4, "lights", 1, "window", "lights", 5, "refinery windows; buried hulls", 1.0, False),
    ("strobe_white", 4, "lights", 0, "beacon", "lights_2", 7, "drill heads; solar array posts", 1.0, True),
    ("work_lamp_flicker", 4, "lights", 0, "work", "lights_2", 6, "pipe runs; crawler bay", 1.0, True),
]

S = 2
LAN = Image.Resampling.LANCZOS

def source_crop(sheet, row, col):
    im = SOURCES[sheet]
    edge = im.width / 4
    return im.crop((round(col*edge+8), round(row*edge+8), round((col+1)*edge-8), round((row+1)*edge-8)))

def resample(img, size):
    a = np.asarray(img.convert("RGBA")).astype(np.float32)
    a[..., :3] *= a[..., 3:4] / 255
    o = np.stack([np.asarray(Image.fromarray(a[..., i], "F").resize(size, LAN)) for i in range(4)], -1)
    al = np.clip(o[..., 3], 0, 255)
    rgb = np.where(al[..., None] > 0, o[..., :3] * 255 / np.maximum(al[..., None], 1), 0)
    return Image.fromarray(np.dstack((np.clip(rgb, 0, 255), al)).round().astype(np.uint8), "RGBA")

def painted_piece(sheet, row, col, kind):
    image = source_crop(sheet, row, col)
    a = np.asarray(image)[:, :, 3]
    yy, xx = np.where(a > 55)
    if len(xx):
        image = image.crop((max(0, xx.min()-1), max(0, yy.min()-1), min(image.width, xx.max()+2), min(image.height, yy.max()+2)))
    target = {"smoke": (76, 91), "steam": (72, 87), "sand": (84, 94), "rain": (98, 92),
              "shimmer": (96, 50), "sparkle": (102, 60), "beacon": (65, 95), "work": (65, 90), "window": (108, 58)}[kind]
    if sheet == "sand" and row == 3: target=(70,94)
    target = (target[0]*S, target[1]*S)
    image = resample(image, target)
    raw = np.asarray(image).copy()
    rgb = raw[:, :, :3].astype(float)
    alpha = raw[:, :, 3].copy()
    mx, mn = rgb.max(2), rgb.min(2)
    saturation = np.divide(mx-mn, np.maximum(mx, 1))
    bad = ((rgb[:, :, 0] > rgb[:, :, 1]*1.35) & (rgb[:, :, 0] > rgb[:, :, 2]*1.55) & (saturation > .48)) | ((rgb[:, :, 0] > 140) & (rgb[:, :, 1] > 140) & (rgb[:, :, 2] < 60))
    alpha[bad | (alpha < 58)] = 0
    lum = rgb @ np.array([.25, .60, .15])
    ranks = np.clip((lum / 255 * 8).astype(int), 0, 8)
    ramp = SMOKE if kind == "smoke" else METAL if kind in ("beacon", "work", "window") else SAND
    raw[:, :, :3] = ramp[ranks]
    if kind in ("smoke", "steam", "sand", "rain"):
        alpha = np.minimum(alpha, 174)
    if kind == "shimmer":
        alpha = np.minimum(alpha, 65)
    if kind == "sparkle":
        alpha = np.minimum(alpha, 145)
    raw[:, :, 3] = alpha
    return Image.fromarray(raw, "RGBA")

def rect(d, x0, y0, x1, y1, fill):
    """A rectangle given in 128-canvas pixels, drawn x2 (so the same on-screen size)."""
    d.rectangle((x0*S, y0*S, x1*S+S-1, y1*S+S-1), fill=fill)

def twinkle(d, x, y, size=1, value=LIGHT, alpha=190):
    rect(d, x-size, y, x+size, y, (*value, alpha))
    rect(d, x, y-size, x, y+size, (*value, alpha))

def frame_for(spec, index):
    name, count, sheet, row, kind, atlas, fps, use, draw_alpha, attached = spec
    if kind not in ("beacon","work","window") and index%2:
        left=frame_for(spec,index-1)
        right=frame_for(spec,(index+1)%count)
        return Image.blend(left,right,.5)
    key = index//2 if count==8 else (index//2 if kind not in ("beacon","work","window") else index)
    if kind == "beacon": key = 1
    if kind == "work": key = 3
    if kind == "window": key = 1
    piece = painted_piece(sheet, row, key, kind)
    canvas = Image.new("RGBA", (128*S, 128*S))
    if kind in ("beacon", "work", "window"):
        base = np.asarray(piece).copy()
        bright = base[:, :, :3].max(2) > 75
        base[:, :, :3] = np.minimum(base[:, :, :3], 72)
        base[:, :, 3] = np.minimum(base[:, :, 3], 105)
        base[:, :, 3][bright] = np.minimum(base[:, :, 3][bright], 25)
        piece = Image.fromarray(base, "RGBA")
    x = 64*S-piece.width//2
    if attached:
        pa=np.asarray(piece)[:,:,3]
        _,bx=np.where(pa[-8*S:,:]>50)
        if len(bx):x=64*S-round(float(bx.mean()))
    y = (118*S-piece.height) if attached else (64*S-piece.height//2)
    if kind == "shimmer": y = (68 if name == "heat_shimmer" else 56)*S
    canvas.alpha_composite(piece, (x, y))
    d = ImageDraw.Draw(canvas, "RGBA")
    if kind in ("sand", "steam", "smoke", "rain"):
        for j in range(9 if kind == "sand" else 5):
            px = 26 + (j*37 + index*13 + row*11) % 77
            py = 29 + (j*23 - index*8 + row*17) % 77
            c = (202, 188, 152, 115) if kind != "smoke" else (132, 122, 103, 95)
            rect(d, px, py, px+1, py+1, c)
            if j % 3 == 0: rect(d, px-4, py+1, px-1, py+1, (c[0],c[1],c[2],70))
    if kind == "shimmer":
        for j in range(5):
            px = 17 + (j*23+index*7) % 80
            py = 83 + j*3 if name == "heat_shimmer" else 52+j*5
            rect(d, px, py, px+12, py, (220,208,178,25+j*4))
        if name == "heat_vent":
            twinkle(d,64+(index*3)%9-4,37+(index*11)%25,1,alpha=180)
    if kind == "sparkle":
        for j in range(7):
            px = 22+(j*19+index*7)%83; py=42+(j*17+index*11)%45
            twinkle(d,px,py,1,alpha=90+((j+index)%3)*35)
    if kind in ("beacon", "work", "window"):
        levels = [0, .24, 1.0, .42]
        strength = levels[index]
        if name == "window_lights": strength = [0,.38,1,.58][index]
        if name == "strobe_white": strength = [0,.08,1,.06][index]
        if name == "work_lamp_flicker": strength = [.04,.35,1,.20][index]
        core = (250,240,204) if name == "beacon_pale_gold" else LIGHT
        positions = [(64,56)] if kind == "beacon" else ([(88,59)] if kind == "work" else [(37,64),(60,63),(84,62)])
        if strength:
            glow = Image.new("RGBA",(128*S,128*S)); gd=ImageDraw.Draw(glow,"RGBA")
            bloom_steps=((28,25),(19,50),(11,95),(5,220)) if kind != "window" else ((13,20),(9,40),(6,70),(3,150))
            for cx,cy in positions:
                for radius,aa in bloom_steps:
                    gd.ellipse(((cx-radius)*S,(cy-radius)*S,(cx+radius)*S,(cy+radius)*S), fill=(*core,round(aa*strength)))
            canvas = Image.alpha_composite(canvas,glow)
            d=ImageDraw.Draw(canvas,"RGBA")
            for cx,cy in positions:
                if strength > .15:
                    r=1 if strength < .5 else 3
                    rect(d, cx-r,cy-r,cx+r,cy+r, (*core,round(255*strength)))
                if strength > .8: rect(d, cx-1,cy-1,cx+1,cy+1,(*LIGHT,255))
    if count==8 and index>=4:
        d=ImageDraw.Draw(canvas,"RGBA")
        for j in range(5):
            px=38+(j*29+index*13)%52; py=44+(j*17+index*7)%59
            rect(d, px,py,px+1,py+1,(211,199,167,85 if kind not in ("shimmer","sparkle") else 35))
    if attached:
        d=ImageDraw.Draw(canvas,"RGBA")
        foot=(75,67,57,255) if kind in ("beacon","work") else ((159,146,115,55) if kind=="shimmer" else (159,146,115,150))
        rect(d, 64,118,64,118, foot)
    return canvas

def main():
    out = Path(sys.argv[1]); out.mkdir(parents=True, exist_ok=True)
    atlases={}; sprites={}
    for spec in LOOPS:
        name,count,_,_,kind,atlas,fps,use,opacity,attached=spec
        if atlas not in atlases: atlases[atlas]=Image.new("RGBA",(1024,1024)); sprites[atlas]=[]
        start=len(sprites[atlas]); assert start+count<=16
        for i in range(count):
            cell=start+i; col,row=cell%4,cell//4
            atlases[atlas].paste(frame_for(spec,i),(col*256,row*256))
            sprites[atlas].append(1)
    for atlas,im in atlases.items():
        im.save(out/(atlas+".png")); print("built", atlas)

if __name__ == "__main__":
    main()
