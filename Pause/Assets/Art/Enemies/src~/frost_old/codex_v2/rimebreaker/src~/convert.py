"""Build the Rimebreaker flight strip from generated candidate B.

Every source-image resize and bank rotation uses nearest-neighbour sampling.
The new structure is assembled from painted source regions; only rim pixels,
nozzle light, jets, and damage marks are drawn after sampling.
"""
from pathlib import Path
import colorsys
from PIL import Image, ImageDraw, ImageFilter, ImageChops

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "rimebreaker_candidate_b.png"
OUT = ROOT / "frost_elite_rimebreaker.png"
N = Image.Resampling.NEAREST
INK = (6, 12, 25, 255)
COOL = (157, 230, 249, 255)
CORE = (240, 253, 255, 255)


def prepared(im):
    im = im.convert("RGBA")
    px = im.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px[x, y]
            if a < 128:
                px[x, y] = (0, 0, 0, 0)
            else:
                h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
                deg = h * 360
                # Candidate B has a few warm brown pixels. Keep only copper's
                # yellow-orange band; remove stray red, pink, and green noise.
                if s > .25 and ((deg < 19 or deg > 340) or 290 < deg < 340 or 65 < deg < 180):
                    if r > b and g > b * .76 and deg < 30:
                        g = max(g, int(r * .48))
                        b = min(b, int(r * .42))
                    else:
                        avg = (r + g + b) // 3
                        r, g, b = int(avg * .72), int(avg * .90), min(255, int(avg * 1.12))
                px[x, y] = (r, g, b, 255)
    return im


def alpha_paste(dst, src, xy):
    dst.alpha_composite(src, xy)


def painted_hull(source):
    canvas = Image.new("RGBA", (192, 192))
    # Rear stabilisers keep the original 125-pixel height while the painted
    # engine mouths remain on the existing (85,133)/(109,133) anchors.
    patch = source.crop((101, 965, 333, 1290)).resize((18, 53), N)
    left_mask = Image.new("L", patch.size)
    d = ImageDraw.Draw(left_mask)
    d.polygon([(1, 0), (16, 5), (16, 19), (13, 37), (10, 52), (7, 52), (4, 34), (2, 16)], fill=255)
    patch.putalpha(Image.composite(patch.getchannel("A"), Image.new("L", patch.size), left_mask))
    alpha_paste(canvas, patch, (59, 113))
    alpha_paste(canvas, patch.transpose(Image.Transpose.FLIP_LEFT_RIGHT), (115, 113))
    # Source-painted ice shoulders put the ram's visual mass near the cell
    # pivot without moving its nose or nozzle coordinates.
    shoulder = source.crop((374, 57, 540, 566)).resize((22, 39), N)
    shoulder_mask = Image.new("L", shoulder.size)
    ImageDraw.Draw(shoulder_mask).polygon([(21, 0), (21, 38), (0, 38), (1, 25), (12, 4)], fill=255)
    shoulder.putalpha(Image.composite(shoulder.getchannel("A"), Image.new("L", shoulder.size), shoulder_mask))
    alpha_paste(canvas, shoulder, (56, 55))
    alpha_paste(canvas, shoulder.transpose(Image.Transpose.FLIP_LEFT_RIGHT), (114, 55))
    # Main prow, cheeks, clamps, conduits and spine: painted pixels only.
    main = source.crop((95, 10, 931, 1250)).resize((80, 88), N)
    alpha_paste(canvas, main, (56, 42))
    # Bring the source-painted pair of bells forward to the measured anchors.
    for box, x in [((255, 1135, 468, 1483), 75), ((557, 1135, 770, 1483), 99)]:
        bell = source.crop(box).resize((20, 27), N)
        alpha_paste(canvas, bell, (x, 113))
    return canvas


def finish_solid(im):
    # A single opaque dark edge, then a sparse pale edge on the lower/right.
    a = im.getchannel("A").point(lambda v: 255 if v >= 128 else 0)
    er = a.filter(ImageFilter.MinFilter(3))
    edge = ImageChops.subtract(a, er)
    pix = im.load(); ep = edge.load(); ap = a.load()
    for y in range(1, 191):
        for x in range(1, 191):
            if not ap[x, y]:
                pix[x, y] = (0, 0, 0, 0)
            elif ep[x, y]:
                pix[x, y] = INK
    for y in range(43, 166):
        for x in range(55, 137):
            if ep[x, y] and (not ap[x + 1, y] or not ap[x, y + 1]) and (x + 2 * y) % 5 != 0:
                pix[x, y] = (121, 188, 212, 255)
    return im


def nozzle_and_flame(im, state):
    d = ImageDraw.Draw(im)
    for cx in (85, 109):
        # Recessed painted bell mouths; origin is y=133 in every pose.
        d.ellipse((cx - 5, 126, cx + 5, 137), fill=(7, 18, 33, 255), outline=(83, 121, 146, 255), width=1)
        d.ellipse((cx - 3, 129, cx + 3, 135), fill=(12, 43, 67, 255))
        if state == 0:
            continue
        if state == 1:
            d.line((cx - 1, 132, cx + 1, 132), fill=(85, 178, 211, 255), width=1)
            d.point((cx, 132), fill=COOL)
            continue
        if state == 2:
            # Ignition: longest, hard-edged double jet and white hot mouth.
            d.polygon([(cx - 5, 134), (cx + 5, 134), (cx + 4, 153), (cx + 1, 170), (cx - 1, 170), (cx - 4, 153)], fill=(20, 91, 184, 255))
            d.polygon([(cx - 3, 134), (cx + 3, 134), (cx + 2, 151), (cx, 166), (cx - 2, 151)], fill=(54, 164, 245, 255))
            d.polygon([(cx - 2, 134), (cx + 2, 134), (cx + 1, 146), (cx, 158), (cx - 1, 146)], fill=(154, 233, 253, 255))
            d.rectangle((cx - 1, 131, cx + 1, 139), fill=CORE)
        elif state == 6 and cx == 109:
            d.polygon([(cx - 3, 134), (cx + 3, 134), (cx + 1, 145), (cx - 1, 147)], fill=(39, 130, 217, 255))
            d.line((cx + 3, 137, cx + 9, 145), fill=CORE, width=1)
            d.line((cx + 5, 143, cx + 12, 141), fill=(95, 213, 248, 255), width=1)
            d.point((cx + 11, 148), fill=CORE)
        else:
            d.polygon([(cx - 4, 134), (cx + 4, 134), (cx + 2, 145), (cx, 153), (cx - 2, 145)], fill=(30, 107, 207, 255))
            d.polygon([(cx - 2, 134), (cx + 2, 134), (cx + 1, 143), (cx, 149), (cx - 1, 143)], fill=(120, 216, 252, 255))
            d.rectangle((cx - 1, 131, cx + 1, 136), fill=CORE)
    return im


def damage(im):
    d = ImageDraw.Draw(im)
    # Cracks follow the ice plate; the whole hull remains intact.
    for points in [((96, 50), (92, 59), (99, 68), (94, 75)),
                   ((74, 75), (80, 82), (76, 89), (82, 94)),
                   ((108, 66), (113, 73), (108, 80), (116, 87)),
                   ((99, 94), (95, 102), (101, 109))]:
        d.line(points, fill=(9, 28, 51, 255), width=1)
        shifted = tuple((x + 1, y) for x, y in points)
        d.line(shifted, fill=(139, 216, 241, 255), width=1)
    return im


def main():
    source = prepared(Image.open(SRC))
    hull = finish_solid(painted_hull(source))
    # The muzzle is on the pointed prow, with a tiny white-cyan live pixel.
    d = ImageDraw.Draw(hull)
    d.point((98, 43), fill=CORE)
    cells = []
    for i in range(7):
        cell = hull.copy()
        if i in (4, 5):
            # Rotate hull and exhaust together for true rolled flight poses.
            cell = nozzle_and_flame(cell, 3)
            cell = cell.rotate(8 if i == 4 else -8, resample=N, center=(96, 96))
        else:
            if i == 6:
                cell = damage(cell)
            cell = nozzle_and_flame(cell, i)
        cells.append(cell)
    strip = Image.new("RGBA", (1344, 192))
    for i, cell in enumerate(cells):
        strip.alpha_composite(cell, (192 * i, 0))
    strip.save(OUT)


if __name__ == "__main__":
    main()
