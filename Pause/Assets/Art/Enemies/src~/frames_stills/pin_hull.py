"""Pin idle cells 1-3 to cell 0's hull (CalmIdleTest: centroid <= 1.5% of the cell, bounding box within 3% in width and
height). Registered frames drift a few percent in size from the model; this rescales each cell's silhouette (at most a few
percent, per axis) to cell 0's bounding box and re-seats its centroid on cell 0's. Idempotent.

  python3 pin_hull.py space_rock_cluster space_rock_dark ...
"""
import sys
import numpy as np
from PIL import Image
sys.path.insert(0, __file__.rsplit("/", 1)[0])
import jobs


def bbox(a):
    ys, xs = np.where(a[..., 3] > 32)
    return xs.min(), ys.min(), xs.max(), ys.max(), xs.mean(), ys.mean()


def pin(cell, ref):
    cw = cell.shape[0]
    x0, y0, x1, y1, cx, cy = bbox(cell)
    rx0, ry0, rx1, ry1, rcx, rcy = bbox(ref)
    w, h = x1 - x0 + 1, y1 - y0 + 1
    rw, rh = rx1 - rx0 + 1, ry1 - ry0 + 1
    im = Image.fromarray(cell)
    crop = im.crop((x0, y0, x1 + 1, y1 + 1)).resize((rw, rh), Image.LANCZOS)
    out = Image.new("RGBA", (cw, cw), (0, 0, 0, 0))
    out.alpha_composite(crop, (int(rx0), int(ry0)))
    a = np.array(out)
    # centroid re-seat (whole pixels)
    _, _, _, _, ncx, ncy = bbox(a)
    dx, dy = int(round(rcx - ncx)), int(round(rcy - ncy))
    if dx or dy:
        out2 = Image.new("RGBA", (cw, cw), (0, 0, 0, 0))
        out2.alpha_composite(out, (dx, dy))
        a = np.array(out2)
    return a


def main():
    for sprite in sys.argv[1:]:
        p = jobs.RES + sprite + ".png"
        sh = np.array(Image.open(p).convert("RGBA"))
        cw = sh.shape[0]
        ref = sh[:, :cw]
        for k in (1, 2, 3):
            cell = sh[:, k * cw:(k + 1) * cw]
            sh[:, k * cw:(k + 1) * cw] = pin(cell, ref)
        Image.fromarray(sh).save(p)
        print("pinned", sprite)


if __name__ == "__main__":
    main()
