"""Audit Verdant run C atlas geometry, loops and gameplay readability."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
ATLASES = ("smoke", "wildfire", "firesmoke", "leaks", "lights")
COUNTS = {"smoke":16,"wildfire":16,"firesmoke":16,"leaks":16,"lights":20}
LOOPS = {
    "smoke_a":8,"smoke_b":8,"flame_front":8,"flame_patch":8,
    "wildsmoke_a":8,"wildsmoke_b":8,"steam_vent":4,"leak_sap":4,
    "ember_rain":4,"spore_burst":4,"beacon_lime":4,
    "beacon_magenta":4,"window_lights":4,"strobe_white":4,"fireflies":4,
}


def brightness(a: np.ndarray) -> int:
    return round((a[:,:,:3].astype(float).mean(2)*a[:,:,3]/65025).sum())


def main() -> None:
    images: dict[str,np.ndarray] = {}
    for atlas in ATLASES:
        image = Image.open(ROOT/f"{atlas}.png")
        assert image.size == (1024,1024) and image.mode == "RGBA", atlas
        rects = json.loads((ROOT/f"{atlas}.json").read_text())["sprites"]
        assert len(rects) == COUNTS[atlas], (atlas,len(rects))
        occupied = np.zeros((1024,1024),bool)
        for r in rects:
            n,x,y,w,h = (r[k] for k in ("n","x","y","w","h"))
            assert n not in images and 0<=x and 0<=y and x+w<=1024 and y+h<=1024
            assert (w,h)==((128,128) if n.startswith(("strobe_white_","fireflies_")) else (256,256))
            top = 1024-y-h
            assert not occupied[top:top+h,x:x+w].any(), n
            occupied[top:top+h,x:x+w] = True
            a = np.asarray(image.crop((x,top,x+w,top+h))).copy()
            ys,xs = np.where(a[:,:,3]>0)
            assert len(xs)>0, n
            margin = min(xs.min(),ys.min(),w-1-xs.max(),h-1-ys.max())
            assert margin>=6,(n,margin)
            images[n]=a
        print(f"{atlas}: {image.size}, RGBA, {len(rects)} valid nonoverlapping rects")

    run_c = json.loads((ROOT/"manifest.json").read_text())["run_c"]
    assert len(run_c["loops"])==len(LOOPS)
    for name,count in LOOPS.items():
        frames = [images[f"{name}_{i:02d}"] for i in range(count)]
        assert len({f.tobytes() for f in frames}) == count, name
        colors = {tuple(rgb) for f in frames for rgb in
                  np.unique(f[f[:,:,3]>0,:3],axis=0)}
        assert len(colors)<=9,(name,len(colors))
        deltas = [float(np.mean(np.abs(frames[i].astype(float)-
                    frames[(i+1)%count].astype(float)))) for i in range(count)]
        assert min(deltas)>0.02,(name,deltas)
        # The loop seam may be as energetic as another adjacent transition.
        assert deltas[-1]<=max(deltas[:-1])*1.20,(name,deltas)
        assert next(l for l in run_c["loops"] if l["name"]==name)["frames"] == [
            f"{name}_{i:02d}" for i in range(count)]
        if "smoke" in name or name=="steam_vent":
            assert max(int(f[:,:,3].max()) for f in frames)<=179,name
        if name.startswith("beacon_") or name in ("window_lights","strobe_white"):
            scores = [brightness(f) for f in frames]
            ratio = max(scores)/min(scores)
            assert ratio>=(2.2 if name=="window_lights" else 4.0),(name,scores)
            if name.startswith("beacon_") or name=="window_lights":
                assert scores[2]==max(scores),(name,scores)
            print(f"{name}: alpha-weighted brightness {scores}, ratio {ratio:.2f}")
        print(f"{name}: transition deltas {[round(x,2) for x in deltas]}, seam/max {deltas[-1]/max(deltas[:-1]):.2f}")

    all_rgb = np.concatenate([a[:,:,:3].reshape(-1,3) for a in images.values()],0)
    all_alpha = np.concatenate([a[:,:,3].reshape(-1) for a in images.values()],0)
    visible = all_rgb[all_alpha>0]
    player_red = np.array((0xFF,0x3E,0x4E))
    assert not ((visible==player_red).all(1)).any()
    red_dominance = (visible[:,0]>180)&(visible[:,1]<90)&(visible[:,2]>50)&(visible[:,2]<125)
    assert red_dominance.mean()<.005,red_dominance.mean()
    assert Image.open(ROOT/"preview_c.png").size[0]>=1024
    with Image.open(ROOT/"preview_c.gif") as gif:
        assert gif.size==(600,1300) and gif.n_frames>=8
    print(f"player-red pixels: 0; red-like fraction {red_dominance.mean():.4f}; previews valid")


if __name__ == "__main__":
    main()
