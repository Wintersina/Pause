#!/usr/bin/env python3
"""Audit the eleven staged Storm strips and build a label-free preview.png.

The stand-out score is the share of opaque key-pose texels whose Rec. 709
luminance is at least three times each Storm cloud-deck tile mean.
"""
from pathlib import Path
import argparse
import math
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[5]
STORM = ROOT / "Pause/Assets/Art/Worlds/Storm/descent~"
NAMES = [
    "storm_alien", "storm_chaser", "storm_big", "storm_fighter_1",
    "storm_fighter_2", "storm_fighter_3", "storm_fighter_4",
    "storm_rock_hail", "storm_rock_ore", "storm_rock_slag",
    "storm_rock_islet",
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


def make_preview(sheets, tile_images, path):
    max_cell = 256
    strip_x = 0
    tile_x = strip_x + 7 * max_cell * 2 + 24
    width = tile_x + 2 * max_cell * 2 + 16
    total_h = sum((256 if n == "storm_big" else 192) * 2 + 12 for n in NAMES)
    out = Image.new("RGB", (width, total_h), BG)
    y = 0
    for name in NAMES:
        sheet = sheets[name]
        cell = sheet.height
        up = sheet.resize((sheet.width*2, sheet.height*2), Image.Resampling.NEAREST)
        out.paste(up, (strip_x,y), up)
        key = sheet.crop((0,0,cell,cell))
        for i, tile in enumerate(tile_images):
            px = (tile.width-cell)//2 + (i*137)
            py = min(tile.height-cell, tile.height//3 + i*91)
            crop = tile.crop((px,py,px+cell,py+cell)).convert("RGB")
            crop.paste(key,(0,0),key)
            crop = crop.resize((cell*2,cell*2),Image.Resampling.NEAREST)
            out.paste(crop,(tile_x+i*max_cell*2,y))
        y += cell*2+12
    out.save(path,optimize=True)


def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--strips-dir',type=Path,default=HERE.parents[1])
    ap.add_argument('--preview',type=Path,default=HERE/'preview.png')
    args=ap.parse_args()
    tiles = [Image.open(STORM/n).convert("RGB") for n in ('storm_cloud_deck_dark.png','storm_cloud_deck.png')]
    tile_means = [float(luminance(np.asarray(t)).mean()) for t in tiles]
    sheets = {}
    failures = []
    print("Storm cloud tile mean luminance:", ", ".join(f"{n}={v:.1f}" for n,v in zip(('dark','light'),tile_means)))
    print("name                 tones bounds cols cut  drift  area-range  motion  margins  red-band  stand-out dark/light")
    for name in NAMES:
        im = Image.open(args.strips_dir/(name+".png")).convert("RGBA")
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
            "tile contrast": all(x >= (15 if name == "storm_alien" else 10) for x in stand),
        }
        failures += [f"{name}: {check}" for check, ok in checks.items() if not ok]
        print(f"{name:21s} {tones:5d} {bounds:6d} {cols:4d} {cut:3d} {drift:5.1f} "
              f"{min(areas):.3f}-{max(areas):.3f} "
              f"{'/'.join(f'{x:.1f}' for x in motions):>19s} "
              f"{'/'.join(str(x) for x in margins):>21s} "
              f"{red:7.2f}% "
              f"{'/'.join(f'{x:.1f}' for x in stand)}%")
    make_preview(sheets,tiles,args.preview)
    print("Preview:", args.preview)
    for problem in failures:
        print("FAIL", problem)
    print("RESULT:", "FAIL" if failures else "PASS", f"({len(failures)} failures)")
    raise SystemExit(bool(failures))

if __name__ == "__main__":main()
