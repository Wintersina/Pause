"""Audit the staged Desert elite art. Run from the repository root."""
from __future__ import annotations

from pathlib import Path
import json

import numpy as np
from PIL import Image

from build import SHIPS, ROOT, CELL

BACKDROP = Path("/Users/sina/Developer/Pause/.claude/worktrees/codex-desert-worm/Pause/Assets/Art/Worlds/Desert/backdrop_v3~")
REPORT = ROOT / "src~" / "verification.json"


def stats(im: Image.Image) -> dict:
    assert im.mode == "RGBA" and im.size == (CELL, CELL)
    a = np.asarray(im)
    solid = a[:, :, 3] >= 128
    yy, xx = np.nonzero(solid)
    if not len(xx):
        raise AssertionError("empty cell")
    x0, x1, y0, y1 = int(xx.min()), int(xx.max()), int(yy.min()), int(yy.max())
    h = np.asarray(im.convert("RGB").convert("HSV"))[:, :, 0].astype(float) * 360 / 255
    s = np.asarray(im.convert("RGB").convert("HSV"))[:, :, 1].astype(float) / 255
    v = np.asarray(im.convert("RGB").convert("HSV"))[:, :, 2].astype(float) / 255
    red = ((h <= 15) | (h >= 345)) & (s > .5)
    amber = (h >= 30) & (h <= 48) & (s > .45)
    violet = (h >= 250) & (h <= 300) & (s > .2)
    pink = (h >= 312) & (h <= 326) & (s > .2)
    rgb = a[solid][:, :3]
    return {
        "bbox": [x0, y0, x1, y1],
        "size": [x1-x0+1, y1-y0+1],
        "centroid": [round(float(xx.mean()), 2), round(float(yy.mean()), 2)],
        "margin": min(x0, y0, CELL-1-x1, CELL-1-y1),
        "border_cuts": int(np.count_nonzero(solid[:, 0]) + np.count_nonzero(solid[:, -1]) + np.count_nonzero(solid[0]) + np.count_nonzero(solid[-1])),
        "colors": int(len(np.unique(rgb, axis=0))),
        "pixels": int(len(rgb)),
        "red_pct": round(float(np.mean(red[solid]) * 100), 4),
        "amber_pct": round(float(np.mean(amber[solid]) * 100), 4),
        "violet_pct": round(float(np.mean(violet[solid]) * 100), 4),
        "pink_pct": round(float(np.mean(pink[solid]) * 100), 4),
        "value_p50": round(float(np.median(v[solid])), 3),
        "value_p90": round(float(np.percentile(v[solid], 90)), 3),
    }


def tile_values() -> dict:
    out = {}
    for i in range(1, 5):
        path = BACKDROP / f"v{i}" / "mid.png"
        if not path.exists():
            continue
        im = Image.open(path).convert("RGB")
        a = np.asarray(im.convert("HSV"))[:, :, 2].astype(float) / 255
        out[f"v{i}"] = {"mean": float(a.mean()), "p90": float(np.percentile(a, 90))}
    return out


def main() -> None:
    tiles = tile_values()
    result = {"tiles": tiles, "ships": {}}
    masks = []
    print("ship                 hover WxH  bank L/R WxH       drift L/R  margin colors  motion L/R  red% amber% cuts  tile contrast v1..v4")
    for ship in SHIPS:
        file = ROOT / f"desert_elite_{ship.key}.png"
        strip = Image.open(file)
        assert strip.mode == "RGBA" and strip.size == (1344, 192), (file, strip.mode, strip.size)
        cells = [strip.crop((i*CELL, 0, (i+1)*CELL, CELL)) for i in range(7)]
        st = [stats(c) for c in cells]
        hover = st[3]
        drifts = [round(float(np.hypot(st[i]["centroid"][0]-hover["centroid"][0], st[i]["centroid"][1]-hover["centroid"][1])), 2) for i in (4, 5)]
        scales = [max(abs(st[i]["size"][j]-hover["size"][j]) for j in (0,1)) for i in (4,5)]
        min_margin = min(s["margin"] for s in st)
        red = max(s["red_pct"] for s in st)
        amber = max(s["amber_pct"] for s in st)
        border = sum(s["border_cuts"] for s in st)
        hv = np.asarray(cells[3].convert("HSV"))[:, :, 2].astype(float)/255
        hm = np.asarray(cells[3].getchannel("A")) >= 128
        # Compare the actual hover pixels with a central, same-size crop of
        # each real Desert mid tile; dark silhouettes and bright facets count.
        standout = {}
        for key in tiles:
            bg = Image.open(BACKDROP/key/"mid.png").convert("RGB")
            w,h=bg.size
            bg=bg.crop(((w-CELL)//2,(h-CELL)//2,(w+CELL)//2,(h+CELL)//2))
            bv=np.asarray(bg.convert("HSV"))[:,:,2].astype(float)/255
            standout[key]=round(float(np.mean((np.abs(hv-bv)>=.12)[hm]))*100,1)
        motion=[]
        ah=np.asarray(cells[3])
        for i in (4,5):
            other=np.asarray(cells[i]);union=(ah[:,:,3]>=128)|(other[:,:,3]>=128)
            motion.append(round(float(np.mean(np.any(ah!=other,axis=2)[union]))*100,1))
        death = Image.open(ROOT/f"desert_elite_{ship.key}_death.png")
        assert death.mode == "RGBA" and death.size == (576, 192)
        deaths = [stats(death.crop((i*CELL,0,(i+1)*CELL,CELL))) for i in range(3)]
        assert all(s["margin"] >= 16 for s in st), ship.key
        assert all(s["border_cuts"] == 0 for s in st), ship.key
        assert all(d <= 4 for d in drifts), (ship.key,drifts)
        assert all(d <= 6 for d in scales), (ship.key,scales)
        assert hover["colors"] >= 300, ship.key
        assert red <= 1 and amber <= .3, (ship.key,red,amber)
        assert max(s["violet_pct"] for s in st) == 0, ship.key
        assert max(s["pink_pct"] for s in st) == 0, ship.key
        assert all(s["border_cuts"] == 0 for s in deaths), ship.key
        assert max(s["red_pct"] for s in deaths) <= 1, ship.key
        assert max(s["amber_pct"] for s in deaths) <= .3, ship.key
        assert min(standout.values()) >= 60, (ship.key,standout)
        assert min(motion) >= 3, (ship.key,motion)
        result["ships"][ship.key] = {"flight": st, "death": deaths, "drift": drifts, "bank_size_delta": scales, "motion_pct": motion, "tile_contrast_pct": standout}
        masks.append(np.asarray(cells[3].getchannel("A"))>=128)
        print(f"{ship.title:21} {hover['size'][0]:3}x{hover['size'][1]:<3}  {st[4]['size'][0]:3}x{st[4]['size'][1]:<3}/{st[5]['size'][0]:3}x{st[5]['size'][1]:<3}  {drifts[0]:4.2f}/{drifts[1]:4.2f}  {min_margin:3}   {hover['colors']:5}  {motion[0]:4.1f}/{motion[1]:4.1f}   {red:4.3f} {amber:4.3f}  {border:3}   {'/'.join(str(x) for x in standout.values())}")
    # Binary intersection/union gives a second silhouette distinction check.
    pairs = []
    for i in range(len(masks)):
        for j in range(i+1, len(masks)):
            intersection = np.count_nonzero(masks[i]&masks[j]);union=np.count_nonzero(masks[i]|masks[j])
            pairs.append((SHIPS[i].key, SHIPS[j].key, round(intersection/union,3)))
    result["silhouette_iou"] = pairs
    print("Maximum pairwise hover silhouette IoU:", max(pairs,key=lambda p:p[2]))
    print("Backdrop mid tile mean values:", {k:round(v["mean"],3) for k,v in tiles.items()})
    REPORT.write_text(json.dumps(result, indent=2)+"\n")
    print("PASS; detailed report:", REPORT)


if __name__ == "__main__":
    main()
