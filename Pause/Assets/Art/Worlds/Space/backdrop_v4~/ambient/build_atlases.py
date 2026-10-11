"""Pack image-generated Space ambient art into Unity atlases and audit it.

The only source artwork is the three imagegen outputs below. This script crops,
resizes with nearest-neighbour, applies the specified background brightness/alpha
limits, and packs the generated pixels; it does not draw sprite shapes.
"""

from pathlib import Path
import json
import numpy as np
from PIL import Image, ImageDraw, ImageFont


HERE = Path(__file__).resolve().parent
SKY = Path(__file__).resolve().parents[4] / "Backgrounds/Resources/Worlds/Space/Backdrop/sky_01.png"
SOURCE = HERE / "source~"
NEB_SOURCE = SOURCE / "nebula_imagegen.png"
DBR_SOURCE = SOURCE / "debris_imagegen.png"
TWN_SOURCE = SOURCE / "twinkle_imagegen.png"
RESAMPLE = Image.Resampling.NEAREST


def rgba(path):
    return Image.open(path).convert("RGBA")


def crop_visible(im, threshold=12):
    a = np.asarray(im.getchannel("A"))
    yy, xx = np.where(a > threshold)
    if not len(xx):
        raise ValueError("Empty generated cell")
    return im.crop((int(xx.min()), int(yy.min()), int(xx.max()) + 1, int(yy.max()) + 1))


def brightness_limit(im, factor, cap=153, alpha_factor=1):
    a = np.array(im, dtype=np.uint8, copy=True)
    a[:, :, :3] = np.minimum(np.rint(a[:, :, :3].astype(float) * factor), cap).astype(np.uint8)
    a[:, :, 3] = np.rint(a[:, :, 3].astype(float) * alpha_factor).astype(np.uint8)
    return Image.fromarray(a, "RGBA")


def place(sheet, sprite, idx, cols, cell, name, entries):
    row, col = divmod(idx, cols)
    left = col * cell + (cell - sprite.width) // 2
    top = row * cell + (cell - sprite.height) // 2
    sheet.alpha_composite(sprite, (left, top))
    box = sprite.getchannel("A").getbbox()
    if box is None:
        raise ValueError(name)
    l, t, r, b = box
    x, ytop, w, h = left+l, top+t, r-l, b-t
    entries.append({"n": name, "x": x, "y": sheet.height - ytop - h, "w": w, "h": h})


def nebula():
    source = rgba(NEB_SOURCE)
    out = Image.new("RGBA", (2048, 2048))
    entries = []
    sprites = []
    for i in range(12):
        row, col = divmod(i, 4)
        x0, x1 = round(col * source.width / 4), round((col+1) * source.width / 4)
        y0, y1 = round(row * source.height / 4), round((row+1) * source.height / 4)
        im = source.crop((x0, y0, x1, y1))
        # The generated sheet has occasional spill at a source cell seam. A
        # short alpha taper keeps each isolated cloud's edge transparent.
        a = np.array(im, dtype=np.uint8, copy=True)
        hh, ww = a.shape[:2]
        yy, xx = np.indices((hh, ww))
        edge = np.minimum.reduce([xx, yy, ww-1-xx, hh-1-yy])
        taper = np.clip(edge / 16.0, 0, 1)
        a[:, :, 3] = np.rint(a[:, :, 3] * taper).astype(np.uint8)
        im = Image.fromarray(a, "RGBA")
        im = im.resize((430, 448), RESAMPLE)
        im = brightness_limit(im, .55, 153, .55)
        sprites.append(im)
        place(out, im, i, 4, 512, f"neb_{i:02d}", entries)
    for j in range(4):
        # Breathing frame: same generated silhouette and alpha, filaments dim.
        im = brightness_limit(sprites[j], .87, 153)
        place(out, im, 12+j, 4, 512, f"neb_{12+j:02d}", entries)
    return out, entries


def debris():
    source = rgba(DBR_SOURCE)
    out = Image.new("RGBA", (2048, 1024))
    entries = []
    rows = [0, 267, 480, 678, source.height]
    for i in range(32):
        row, col = divmod(i, 8)
        x0, x1 = round(col*source.width/8), round((col+1)*source.width/8)
        y0, y1 = rows[row], rows[row+1]
        im = source.crop((x0+6, y0+6, x1-6, y1-6))
        im = crop_visible(im, 24)
        scale = min(1.0, 198/max(im.size))
        size = (max(1, round(im.width*scale)), max(1, round(im.height*scale)))
        im = im.resize(size, RESAMPLE)
        im = brightness_limit(im, .65, 153)
        place(out, im, i, 8, 256, f"dbr_{i:02d}", entries)
    return out, entries


def twinkle():
    source = rgba(TWN_SOURCE)
    out = Image.new("RGBA", (1024, 512))
    entries = []
    target = [4, 6, 9, 14, 9, 6, 5, 4]
    for i in range(32):
        row, col = divmod(i, 8)
        x0, x1 = round(col*source.width/8), round((col+1)*source.width/8)
        y0, y1 = round(row*source.height/4), round((row+1)*source.height/4)
        im = crop_visible(source.crop((x0+15, y0+12, x1-15, y1-12)), 48)
        # The frame art itself is generated; shrinking preserves its pixel
        # clusters and keeps tiny stars subordinate to gameplay sprites.
        side = target[col]
        im = im.resize((side, side), RESAMPLE)
        place(out, im, i, 8, 128, f"twinkle_{row}_{col}", entries)
    return out, entries


def audit(sheet, entries, cell, cols, neb=False):
    a = np.asarray(sheet)
    records = []
    rects = []
    for i, e in enumerate(entries):
        x, y, w, h = (e[k] for k in ("x", "y", "w", "h"))
        top = sheet.height-y-h
        rect = (x, top, x+w, top+h)
        assert 0 <= x and 0 <= top and x+w <= sheet.width and top+h <= sheet.height
        for prior in rects:
            assert rect[2] <= prior[0] or prior[2] <= rect[0] or rect[3] <= prior[1] or prior[3] <= rect[1]
        rects.append(rect)
        row, col = divmod(i, cols)
        margin = min(x-col*cell, (col+1)*cell-(x+w), top-row*cell, (row+1)*cell-(top+h))
        assert margin >= 14, (e["n"], margin)
        part = a[top:top+h, x:x+w]
        visible = part[:, :, 3] > 8
        p90 = float(np.percentile(part[:, :, :3].max(axis=2)[visible], 90)/255) if visible.any() else 0
        alpha = int(part[:, :, 3].max())
        edge_alpha = 0
        if neb:
            c = a[row*cell:(row+1)*cell, col*cell:(col+1)*cell, 3]
            edge_alpha = max(int(c[:14].max()), int(c[-14:].max()), int(c[:, :14].max()), int(c[:, -14:].max()))
            assert alpha <= 140 and p90 <= .5 and edge_alpha == 0, (e["n"], alpha, p90, edge_alpha)
        elif cell == 256:
            assert p90 <= .5, (e["n"], p90)
        records.append((e["n"], x, y, w, h, margin, p90, alpha, edge_alpha))
    return records


def preview(sheets):
    bg = (10, 8, 32, 255)
    canvas = Image.new("RGBA", (1770, 1430), bg)
    draw = ImageDraw.Draw(canvas)
    font = ImageFont.load_default()
    labels = [("NEBULA: 16 / 512", 36, 23), ("DEBRIS: 32 / 256", 36, 620),
              ("TWINKLE: 4 TYPES x 8 FRAMES", 36, 1110), ("PHONE COMPOSITE", 1280, 23)]
    for label, x, y in labels:
        draw.text((x, y), label, fill=(174, 177, 217), font=font)
    neb, tw, dbr = sheets
    for i in range(16):
        row, col = divmod(i, 4)
        thumb = neb.crop((col*512, row*512, (col+1)*512, (row+1)*512)).resize((133, 133), RESAMPLE)
        canvas.alpha_composite(thumb, (38+col*151, 55+row*137))
    for i in range(32):
        row, col = divmod(i, 8)
        thumb = dbr.crop((col*256, row*256, (col+1)*256, (row+1)*256)).resize((122, 122), RESAMPLE)
        canvas.alpha_composite(thumb, (38+col*150, 650+row*105))
    for i in range(32):
        row, col = divmod(i, 8)
        thumb = tw.crop((col*128, row*128, (col+1)*128, (row+1)*128)).resize((92, 92), RESAMPLE)
        canvas.alpha_composite(thumb, (48+col*146, 1150+row*65))
    sky = rgba(SKY).resize((410, 820), RESAMPLE)
    phone = Image.new("RGBA", sky.size, bg)
    phone.alpha_composite(sky)
    for idx, pos in [(0,(-85,55)), (5,(70,210)), (9,(-110,440))]:
        row, col = divmod(idx, 4)
        cloud = neb.crop((col*512, row*512, (col+1)*512, (row+1)*512))
        layer = Image.new("RGBA", phone.size)
        layer.paste(cloud, pos)
        phone = Image.alpha_composite(phone, layer)
    for idx, pos in [(1,(22,216)), (10,(217,488)), (26,(54,646))]:
        row, col = divmod(idx, 8)
        frag = dbr.crop((col*256, row*256, (col+1)*256, (row+1)*256)).resize((112, 112), RESAMPLE)
        layer = Image.new("RGBA", phone.size)
        layer.paste(frag, pos)
        phone = Image.alpha_composite(phone, layer)
    for idx, pos in [(3,(100,70)), (11,(330,307)), (19,(76,580)), (27,(304,710))]:
        row, col = divmod(idx, 8)
        star = tw.crop((col*128, row*128, (col+1)*128, (row+1)*128))
        layer = Image.new("RGBA", phone.size)
        layer.paste(star, pos)
        phone = Image.alpha_composite(phone, layer)
    canvas.alpha_composite(phone, (1280, 55))
    draw.rectangle((1279,54,1690,875), outline=(94,92,138))
    canvas.convert("RGB").save(HERE / "preview.png")


def main():
    sheets = {}
    records = {}
    for key, build, size, cell, cols in [
        ("nebula", nebula, (2048,2048), 512, 4),
        ("twinkle", twinkle, (1024,512), 128, 8),
        ("debris", debris, (2048,1024), 256, 8),
    ]:
        sheet, entries = build()
        assert sheet.size == size
        sheet.save(HERE / f"{key}.png")
        data = {"pixelScale": 2, "sheetW": size[0], "sheetH": size[1], "sprites": entries}
        (HERE / f"{key}.json").write_text(json.dumps(data, indent=2) + "\n")
        sheets[key] = sheet
        records[key] = audit(sheet, entries, cell, cols, key == "nebula")
    preview((sheets["nebula"], sheets["twinkle"], sheets["debris"]))
    lines = ["# Space ambient atlas v4", "", "Image-generated source art packed as transparent Unity sprites. The concept render and existing atlases guided the visual style. Existing Space assets were read only.", "", "| Atlas | Size | Grid | Sprites | Pixel scale |", "|---|---:|---:|---:|---:|", "| nebula.png | 2048×2048 | 4×4, 512 px cells | 16 | 2 |", "| twinkle.png | 1024×512 | 8×4, 128 px cells | 32 | 2 |", "| debris.png | 2048×1024 | 8×4, 256 px cells | 32 | 2 |", "", "JSON rectangles use bottom-origin y. All rectangles are in bounds, pairwise non-overlapping, and centered in their cells with at least 14 px transparent margin. p90 value is HSV V (maximum RGB channel / 255) among pixels with alpha > 8; peak alpha and edge alpha are /255. The edge band is 14 px on each side of a nebula cell. Source art was resized only with nearest-neighbour; technical packing normalization caps nebula opacity at 140/255 and dims ambient RGB to meet the requested background limits.", "", "Nebula frames 12–15 reuse the exact silhouettes and alpha of frames 00–03 with dimmer filaments for two-frame breathing. Twinkle rows are cool white, cyan-blue, orange, and violet-pink; columns are sequential frames.", ""]
    for key in ("nebula", "twinkle", "debris"):
        lines += [f"## {key.title()} measurements", "", "| Sprite | x | y bottom | w | h | Min margin | p90 V | Peak α | Edge α |", "|---|---:|---:|---:|---:|---:|---:|---:|---:|"]
        for n,x,y,w,h,m,p,a,e in records[key]:
            lines.append(f"| {n} | {x} | {y} | {w} | {h} | {m} | {p:.3f} | {a/255:.3f} | {e/255:.3f} |")
        lines.append("")
    (HERE / "manifest.md").write_text("\n".join(lines))
    print("Wrote three atlases, JSON manifests, preview.png and manifest.md")


if __name__ == "__main__":
    main()
