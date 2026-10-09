"""Check exported Ember death strip geometry, anchoring, and color rules."""

from pathlib import Path

import numpy as np
from PIL import Image

from build import DEATH, ENEMIES, KEYS


def centroid(alpha: np.ndarray) -> tuple[float, float]:
    yy, xx = np.indices(alpha.shape)
    weight = alpha.astype(np.float64)
    return ((xx * weight).sum() / weight.sum(), (yy * weight).sum() / weight.sum())


def main() -> None:
    for key in KEYS:
        idle = Image.open(ENEMIES / f"{key}.png")
        side = idle.height
        assert idle.width == 7 * side, key
        death = Image.open(DEATH / f"{key}.png")
        assert death.mode == "RGBA", key
        assert death.size == (3 * side, side), key
        original = np.array(idle.convert("RGBA").crop((3*side,0,4*side,side)))
        cells = [np.array(death.crop((i*side,0,(i+1)*side,side))) for i in range(3)]
        shift = np.linalg.norm(np.subtract(centroid(cells[0][:,:,3]),centroid(original[:,:,3])))
        assert shift <= 3, (key, shift)
        margins=[]
        for cell in cells:
            rgb=cell[:,:,:3]
            alpha=cell[:,:,3]
            bright=(alpha>=112)&(rgb.max(2)>=160)
            yy,xx=np.where(bright)
            margin=min(xx.min(),yy.min(),side-1-xx.max(),side-1-yy.max())
            assert margin>=6, (key,margin)
            margins.append(int(margin))
            # Strongly saturated color must stay within the Ember orange-yellow band.
            hsv=np.array(Image.fromarray(rgb,"RGB").convert("HSV"))
            foreign=(alpha>=112)&(hsv[:,:,1]>128)&(hsv[:,:,2]>60)&((hsv[:,:,0]<13)|(hsv[:,:,0]>43))
            assert foreign.sum()==0, (key,int(foreign.sum()))
        print(f"{key:20} {death.width}x{side}  centroid {shift:.2f}px  bright margins {margins}")


if __name__ == "__main__":
    main()
