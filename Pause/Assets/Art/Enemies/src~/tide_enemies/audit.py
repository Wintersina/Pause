#!/usr/bin/env python3
"""Audit the eleven staged Tide strips and build preview.png.

The stand-out score is the share of opaque key-pose texels whose Rec. 709
luminance is at least three times the mean luminance of each Tide mid tile.
"""
from pathlib import Path
import colorsys
import math
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[5]
TIDES = ROOT / "Pause/Assets/Art/Backgrounds/Resources/Worlds/Tide/Backdrop3"
NAMES = [
    "tide_alien", "tide_chaser", "tide_big", "tide_fighter_1",
    "tide_fighter_2", "tide_fighter_3", "tide_fighter_4",
    "tide_rock_brain", "tide_rock_staghorn", "tide_rock_urchin",
    "tide_rock_islet",
]
BG = (11, 11, 26)


def luminance(rgb):
    rgb = rgb.astype(np.float64)
    return .2126 * rgb[..., 0] + .7152 * rgb[..., 1] + .0722 * rgb[..., 2]


def centroid_area(c):
    yy, xx = np.where(c[:, :, 3] > 128)
    return (np.array([xx.mean(), yy.mean()]), len(xx)) if len(xx) else (np.zeros(2), 0)


def margin(c):
    yy, xx = np.where(c[:, :, 3] > 16)
    h, w = c.shape[:2]
    return int(min(xx.min(), yy.min(), w - 1 - xx.max(), h - 1 - yy.max()))


def straight_cut(c):
    opaque = c[:, :, 3] > 128
    worst = 0
    for side in (-1, 1):
        for x in range(c.shape[1]):
            nx = x + side
            edge = opaque[:, x] & (~opaque[:, nx]) if 0 <= nx < c.shape[1] else opaque[:, x]
            run = 0
            for v in edge:
                run = run + 1 if v else 0
                worst = max(worst, run)
    return worst


def motion(a, b):
    oa = a[:, :, 3] > 128; ob = b[:, :, 3] > 128
    body = oa | ob
    diff = np.abs(a[:, :, :3].astype(np.int16)-b[:, :, :3].astype(np.int16)).sum(2) > 24
    return 100 * (((oa ^ ob) | (oa & ob & diff)).sum() / body.sum())


def red_band_share(arr):
    p = arr.reshape(-1, 4)
    p = p[p[:, 3] > 128, :3].astype(np.float32) / 255
    mx = p.max(1); mn = p.min(1); de = mx-mn
    sat = np.divide(de, mx, out=np.zeros_like(de), where=mx > 0)
    hue = np.zeros_like(mx)
    valid = de > 0
    rr = valid & (mx == p[:, 0]); gg = valid & (mx == p[:, 1]); bb = valid & (mx == p[:, 2])
    hue[rr] = (60*(p[:, 1][rr]-p[:, 2][rr])/de[rr]) % 360
    hue[gg] = 60*(p[:, 2][gg]-p[:, 0][gg])/de[gg]+120
    hue[bb] = 60*(p[:, 0][bb]-p[:, 1][bb])/de[bb]+240
    return 100 * np.mean((sat > .45) & ((hue < 15) | (hue >= 345)))


def make_preview(sheets, tile_images):
    max_cell = 256
    strip_x = 130
    tile_x = strip_x + 7 * max_cell * 2 + 28
    width = tile_x + 4 * max_cell * 2 + 20
    total_h = sum((256 if n == "tide_big" else 192) * 2 + 12 for n in NAMES)
    out = Image.new("RGB", (width, total_h), BG)
    draw = ImageDraw.Draw(out)
    y = 0
    for name in NAMES:
        sheet = sheets[name]
        cell = sheet.height
        draw.text((10, y+12), name, fill=(220,255,237))
        draw.text((10, y+35), "idle 0-3", fill=(124,242,192))
        draw.text((10, y+53), "tell 4-5", fill=(124,242,192))
        draw.text((10, y+71), "hit 6", fill=(220,255,237))
        up = sheet.resize((sheet.width*2, sheet.height*2), Image.Resampling.NEAREST)
        out.paste(up, (strip_x,y), up)
        key = sheet.crop((0,0,cell,cell))
        for i, tile in enumerate(tile_images):
            # A distinct square from each ground set, matching the actual key-pose scale.
            px = (tile.width-cell)//2
            py = min(tile.height-cell, tile.height//3 + i*61)
            crop = tile.crop((px,py,px+cell,py+cell)).convert("RGB")
            crop.paste(key,(0,0),key)
            crop = crop.resize((cell*2,cell*2),Image.Resampling.NEAREST)
            out.paste(crop,(tile_x+i*max_cell*2,y))
            draw.text((tile_x+i*max_cell*2+5,y+5),f"v{i+1}",fill=(240,255,245))
        y += cell*2+12
    out.save(HERE/"preview.png",optimize=True)


def main():
    tiles = [Image.open(TIDES/f"v{i}"/"mid.png").convert("RGB") for i in range(1,5)]
    tile_means = [float(luminance(np.asarray(t)).mean()) for t in tiles]
    sheets = {}
    failures = []
    print("Tide mid tile mean luminance:", ", ".join(f"v{i+1}={v:.1f}" for i,v in enumerate(tile_means)))
    print("name                 tones bounds cols cut  drift  area-range  motion  margins  red-band  stand-out v1/v2/v3/v4")
    for name in NAMES:
        im = Image.open(HERE/(name+".png")).convert("RGBA")
        sheets[name] = im
        h = im.height
        a = np.asarray(im)
        cells = [a[:,i*h:(i+1)*h] for i in range(7)]
        key = cells[0]
        op = key[:, :, 3] > 128
        rgb = (key[:,:,0].astype(np.int32)<<16)|(key[:,:,1].astype(np.int32)<<8)|key[:,:,2]
        tones = len(np.unique(rgb[op]))
        r = (rgb[:,:-1]!=rgb[:,1:]) & op[:,:-1] & op[:,1:]
        d = (rgb[:-1,:]!=rgb[1:,:]) & op[:-1,:] & op[1:,:]
        edge=np.zeros_like(op);edge[:,:-1]|=r;edge[:-1,:]|=d
        bounds=int(edge.sum())
        cols=sum(int((c[:,0,3]>128).sum()+(c[:,-1,3]>128).sum()) for c in cells)
        cut=max(straight_cut(c) for c in cells)
        ref, area0=centroid_area(key)
        drift=max(float(np.linalg.norm(centroid_area(c)[0]-ref)) for c in cells[1:4])
        areas=[centroid_area(c)[1]/area0 for c in cells[:4]]
        motions=[motion(cells[i],cells[(i+1)%4]) for i in range(4)]
        margins=[margin(c) for c in cells]
        red=red_band_share(a)
        lum=luminance(key[:,:,:3])[op]
        stand=[100*np.mean(lum>=3*m) for m in tile_means]
        checks = {
            "tones": tones >= 300,
            "boundaries": bounds >= (6000 if h >= 256 else 3000),
            "cell sides": cols == 0,
            "straight cut": cut < math.ceil(.15*h),
            "idle anchor": drift <= 3,
            "idle area": all(.96 <= x <= 1.04 for x in areas),
            "idle motion": all(x >= 3 for x in motions),
            "margins": all(x >= 6 for x in margins),
            "red band": red < 2,
            "tile contrast": all(x >= (15 if name == "tide_alien" else 10) for x in stand),
        }
        failures += [f"{name}: {check}" for check, ok in checks.items() if not ok]
        print(f"{name:21s} {tones:5d} {bounds:6d} {cols:4d} {cut:3d} {drift:5.1f} "
              f"{min(areas):.3f}-{max(areas):.3f} "
              f"{'/'.join(f'{x:.1f}' for x in motions):>19s} "
              f"{'/'.join(str(x) for x in margins):>21s} "
              f"{red:7.2f}% "
              f"{'/'.join(f'{x:.1f}' for x in stand)}%")
    make_preview(sheets,tiles)
    print("Preview:", HERE/"preview.png")
    for problem in failures:
        print("FAIL", problem)
    print("RESULT:", "FAIL" if failures else "PASS", f"({len(failures)} failures)")
    raise SystemExit(bool(failures))

if __name__ == "__main__":main()
