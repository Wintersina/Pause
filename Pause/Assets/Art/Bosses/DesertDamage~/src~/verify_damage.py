"""Verify the staged Desert boss art contract and print measured cell metrics."""

from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
CELL = 384
SPECS = {
    "Desert_damage.png": (768, 1536, 2, 4),
    "Desert_damage_fx.png": (2304, 768, 6, 2),
    "Desert_death.png": (2304, 384, 6, 1),
}


def rgba(path, size):
    im = Image.open(path)
    assert im.mode == "RGBA", f"{path.name}: mode {im.mode}"
    assert im.size == size, f"{path.name}: size {im.size}"
    return im


def bbox24(im):
    a = np.asarray(im.getchannel("A"))
    ys, xs = np.where(a > 24)
    assert len(xs), "empty cell"
    return (int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1))


def colors(im):
    arr = np.asarray(im)
    arr = arr[arr[:, :, 3] > 24]
    return len(np.unique(arr[:, :3], axis=0))


def border_count(im):
    a = np.asarray(im.getchannel("A")) > 24
    rim = np.zeros_like(a)
    rim[:6, :] = True
    rim[-6:, :] = True
    rim[:, :6] = True
    rim[:, -6:] = True
    return int(np.count_nonzero(a & rim))


def bands(im):
    rgba_arr = np.asarray(im)
    valid = rgba_arr[:, :, 3] > 24
    hsv = np.asarray(im.convert("RGB").convert("HSV"))
    hue = hsv[:, :, 0].astype(np.float32) * (360 / 255)
    sat = hsv[:, :, 1].astype(np.float32) / 255
    total = max(1, int(valid.sum()))
    red = (((hue < 15) | (hue >= 345)) & (sat > .5) & valid).sum() / total
    amber = ((hue >= 30) & (hue <= 48) & (sat > .45) & valid).sum() / total
    return float(red), float(amber)


reference = rgba(SRC / "pristine_cell.png", (384, 384))
ref_box = bbox24(reference)
allowed = (ref_box[0]-6, ref_box[1]-6, ref_box[2]+6, ref_box[3]+6)
assert ref_box == (22, 22, 361, 362), f"Unexpected approved cell: {ref_box}"

print("file                  cell   alpha bbox          border  RGB colors    red %  amber %")
print("-" * 86)
all_images = {}
for filename, (w, h, cols, rows) in SPECS.items():
    sheet = rgba(ROOT / filename, (w, h))
    all_images[filename] = sheet
    for row in range(rows):
        for col in range(cols):
            cell = sheet.crop((col*CELL, row*CELL, (col+1)*CELL, (row+1)*CELL))
            box = bbox24(cell)
            border = border_count(cell)
            count = colors(cell)
            red, amber = bands(cell)
            print(f"{filename:22} {row},{col:<2}  {str(box):19} {border:6} {count:11} "
                  f"{100*red:7.3f} {100*amber:8.3f}")
            assert red <= .01, f"red-band share in {filename} {row},{col}"
            assert amber <= .003, f"pickup amber share in {filename} {row},{col}"
            # A fully painted body/death cell retains far more than a posterized palette.
            if filename != "Desert_damage_fx.png":
                assert count >= 3000, f"low detail in {filename} {row},{col}"
            elif row == 0:
                assert count >= 1000, f"low smoke detail in cell {col}"
            if not (filename == "Desert_death.png" and col == 3):
                assert border == 0, f"border spill in {filename} {row},{col}"
            if filename == "Desert_damage.png":
                assert (box[0] >= allowed[0] and box[1] >= allowed[1]
                        and box[2] <= allowed[2] and box[3] <= allowed[3]), \
                    f"registration drift in stage {row+1} loop {col}"

ref = np.asarray(reference).astype(np.int16)
sheet = all_images["Desert_damage.png"]
distance = []
print("\nstage  distance from pristine  alpha pixels  A/B changed pixels")
print("-" * 63)
for stage in range(4):
    a = np.asarray(sheet.crop((0, stage*CELL, CELL, (stage+1)*CELL))).astype(np.int16)
    b = np.asarray(sheet.crop((CELL, stage*CELL, 2*CELL, (stage+1)*CELL))).astype(np.int16)
    d = (np.abs(a-ref).mean() + np.abs(b-ref).mean()) / 2
    live = (np.count_nonzero(a[:, :, 3] > 24) + np.count_nonzero(b[:, :, 3] > 24)) // 2
    loop_diff = np.count_nonzero(np.any(a != b, axis=2))
    print(f"{stage+1:<6} {d:21.2f} {live:13} {loop_diff:19}")
    distance.append(d)
    assert loop_diff >= 50, f"idle loop is static at stage {stage+1}"
assert all(distance[i+1] > distance[i] + 1.5 for i in range(3)), \
    f"damage escalation: {distance}"

for name, size in (("preview.png", (3840, 768)),
                   ("Desert_death_preview.png", (4608, 768))):
    assert Image.open(ROOT / name).size == size
gif = Image.open(ROOT / "preview.gif")
assert gif.size == (768, 768) and getattr(gif, "n_frames", 1) == 12
print("\nPASS: atlas geometry, registration, margins, escalation, detail, palette, previews")
