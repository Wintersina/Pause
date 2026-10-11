"""Transfer image-generated Verdant paint into the installed sprite geometry.

The selected generated candidates supply material texture and tonal placement.
The installed atlas supplies exact native-resolution silhouettes and poses.
"""
from pathlib import Path
import colorsys
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
OUT = HERE.parent
OLD = OUT.parent
NEAREST = Image.Resampling.NEAREST

GREEN = np.array([tuple(bytes.fromhex(h)) for h in (
    "101D15", "192C1C", "223C25", "2A4B2D", "345C36", "406D40", "4A7A44")], dtype=np.uint8)
BARK = np.array([tuple(bytes.fromhex(h)) for h in (
    "211A14", "30251A", "3A2A1A", "493620", "5A3F26", "6A4D2E", "7A5A36")], dtype=np.uint8)
PINK = np.array([tuple(bytes.fromhex(h)) for h in (
    "8F2279", "BD329D", "FF4FD8", "FF8AE6", "FFE0F8")], dtype=np.uint8)
INK = np.array([11, 11, 26], dtype=np.uint8)


def gray(rgb):
    a = rgb.astype(np.float32)
    return (a[..., 0] * .2126 + a[..., 1] * .7152 + a[..., 2] * .0722) / 255


def generated_cell(source, index, kind):
    """Locate a painted pose in the generated contact sheet by its alpha."""
    width, height = source.size
    columns = 8 if kind == "lash" else 4
    rows = 1 if kind == "lash" else 2
    x, y = index % columns, index // columns
    x0, x1 = round(x * width / columns), round((x + 1) * width / columns)
    y0, y1 = round(y * height / rows), round((y + 1) * height / rows)
    zone = np.asarray(source.crop((x0, y0, x1, y1)))
    yy, xx = np.where(zone[..., 3] > 80)
    return source.crop((x0 + int(xx.min()), y0 + int(yy.min()),
                        x0 + int(xx.max()) + 1, y0 + int(yy.max()) + 1))


def cell_box(alpha):
    yy, xx = np.where(alpha > 24)
    return int(xx.min()), int(yy.min()), int(xx.max()) + 1, int(yy.max()) + 1


def edge(mask):
    inside = mask.copy()
    inside[1:] &= mask[:-1]
    inside[:-1] &= mask[1:]
    inside[:, 1:] &= mask[:, :-1]
    inside[:, :-1] &= mask[:, 1:]
    return mask & ~inside


def paint_cell(old, source, kind, index):
    old = np.asarray(old.convert("RGBA"))
    h, w = old.shape[:2]
    opaque = old[..., 3] > 24
    x0, y0, x1, y1 = cell_box(old[..., 3])
    painted = generated_cell(source, index, kind).resize((x1-x0, y1-y0), NEAREST)
    src = np.zeros_like(old)
    src[y0:y1, x0:x1] = np.asarray(painted)
    # Existing interior grain and the painted candidate both contribute to value.
    old_luma = gray(old[..., :3])
    src_luma = gray(src[..., :3])
    source_valid = src[..., 3] > 80
    value = np.where(source_valid, .78 * src_luma + .22 * old_luma,
                     .48 * old_luma + .12)
    value = np.clip(value, .025, .58)
    level = np.clip(np.floor(value * 11.5).astype(int), 0, 6)

    # A green majority is selected from the generated moss and vine paint.
    r, g, b = [src[..., k].astype(float) for k in range(3)]
    green_score = (g-r) / 255 + .48*(g-b)/255 + .35*value
    green_score += .10*np.sin((np.arange(h)[:, None]*11 + np.arange(w)[None, :]*17 + index*19)*.41)
    green_score += .10*(old[..., 1].astype(float)-old[..., 0].astype(float))/255
    green_score[~source_valid] -= .1

    rr, gg, bb = [old[..., k].astype(float) / 255 for k in range(3)]
    old_pink = (rr > gg*1.18) & (bb > gg*.92) & (rr > .27)
    sr, sg, sb = [src[..., k].astype(float) / 255 for k in range(3)]
    src_pink = (sr > sg*1.18) & (sb > sg*.92) & (sr > .36)
    border = edge(opaque)
    pink_score = 2.0*src_pink + 1.4*old_pink + .52*border + .48*old_luma
    pink_score += .2*np.sin((np.arange(h)[:, None]*7 + np.arange(w)[None, :]*13 + index*5)*.32)
    if kind == "log":
        xx = np.arange(w)[None, :]
        # The two cut ends are the only emissive regions of a rolling log.
        ends = (xx < 20) | (xx >= w-20)
        pink_score += 1.7*ends
        pink_score[~np.broadcast_to(ends, (h, w))] = -100
        pink_fraction = .115
    elif index >= 6:
        pink_fraction = .88  # Preview dots are a tell, not the vine body.
    else:
        pink_fraction = .115

    coords = np.flatnonzero(opaque)
    count = max(1, round(len(coords)*pink_fraction))
    order = coords[np.argsort(pink_score.flat[coords], kind="stable")[-count:]]
    pink = np.zeros((h,w), bool); pink.flat[order] = True

    result = np.zeros_like(old)
    available = opaque & ~pink
    green_count = round(np.count_nonzero(opaque)*(.70 if kind == "log" else .72))
    if kind == "lash" and index >= 6:
        green_count = round(np.count_nonzero(opaque)*.07)
    avail_coords = np.flatnonzero(available)
    chosen = avail_coords[np.argsort(green_score.flat[avail_coords], kind="stable")[-green_count:]]
    green = np.zeros((h,w), bool); green.flat[chosen] = True
    result[..., :3] = BARK[level]
    # Lift moss one ramp step so its form survives bright canopy tiles.
    result[green, :3] = GREEN[np.minimum(level[green] + 1, 6)]
    # Sparse ink for a cut, shadow-side outline; retain moss at most borders.
    ink = available & ~green & border & (old_luma < .17)
    result[ink, :3] = INK
    pink_value = np.clip(np.floor((.55*old_luma + .45*src_luma)*6.8).astype(int), 0, 4)
    pink_value[pink & (old_luma > .75)] = 4
    result[pink, :3] = PINK[pink_value[pink]]
    result[..., 3] = old[..., 3]
    result[~(old[..., 3] > 0), :3] = 0
    return Image.fromarray(result, "RGBA")


def main():
    for kind, cell_w, cell_h, selected in (
        ("lash", 64, 64, "lash_candidate_b.png"),
        ("log", 128, 64, "log_candidate_a.png"),
    ):
        original = Image.open(OLD / f"verdant_attack_{kind}.png").convert("RGBA")
        native = original.resize((original.width//2, original.height//2), NEAREST)
        source = Image.open(HERE / selected).convert("RGBA")
        result = Image.new("RGBA", native.size)
        for i in range(8):
            x, y = (i, 0) if kind == "lash" else (i%4, i//4)
            box = (x*cell_w, y*cell_h, (x+1)*cell_w, (y+1)*cell_h)
            result.paste(paint_cell(native.crop(box), source, kind, i), box[:2])
        result.resize(original.size, NEAREST).save(OUT / f"verdant_attack_{kind}.png")


if __name__ == "__main__":
    main()
