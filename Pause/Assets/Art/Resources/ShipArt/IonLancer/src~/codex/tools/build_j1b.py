from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
GREEN = (0, 255, 0)
NAVY = (12, 24, 52)
INK = (5, 6, 12)
BONE = (255, 251, 224)
CYAN_WHITE = (245, 253, 253)

KEYS = [ROOT / f"key_state{i}.png" for i in range(3)]
GENERATED = Path("/Users/sina/.codex/generated_images/01a1601c-1ac8-7bf3-a2e7-d7a43c38e417")

# The selected candidates are painted redraws generated from the approved keys.
BANKS = [
    (
        GENERATED / "exec-a753bb68-77e4-4e2d-bf90-402da4fc0e01.png",
        GENERATED / "exec-9e420e79-34fc-45d2-8261-32e10b4fe662.png",
    ),
    (
        GENERATED / "exec-eccfeade-8abb-4ab0-89cd-d1a5b8070fa6.png",
        GENERATED / "exec-e8ffb1ca-d143-4f5c-b7b3-1c9bd956467f.png",
    ),
    (
        GENERATED / "exec-352e44f7-1814-4ff6-87df-4a58d80e0c02.png",
        GENERATED / "exec-256c8906-0ad7-43d6-beb3-4a4fb56e73eb.png",
    ),
]


def canopy_mask(size=1024):
    yy, xx = np.mgrid[0:size, 0:size]
    # Outer canopy glass/frame envelope; it deliberately excludes the wings and engines.
    return (((xx - 512.0) / 58.0) ** 2 + ((yy - 337.0) / 132.0) ** 2) <= 1.0


def dim_canopy(im, amount=(0.72, 0.83, 0.86)):
    a = np.array(im, dtype=np.uint8)
    m = canopy_mask()
    src = a[m].astype(np.float32)
    a[m] = np.clip(src * np.array(amount, dtype=np.float32), 0, 255).astype(np.uint8)
    return Image.fromarray(a, "RGB")


def brighten_canopy(im):
    a = np.array(im, dtype=np.uint8)
    m = canopy_mask()
    src = a[m].astype(np.int16)
    a[m] = np.clip(src + np.array([18, 38, 34], dtype=np.int16), 0, 255).astype(np.uint8)
    return Image.fromarray(a, "RGB")


def add_glint(im, side, strong):
    out = im.copy()
    d = ImageDraw.Draw(out)
    if side == "left":
        # A stepped lower-left to upper-right streak over the left glass half.
        pts = [(470 + round(i * 0.25), 414 - i) for i in range(155)]
        d.line(pts, fill=CYAN_WHITE if strong else (175, 225, 230), width=4 if strong else 2)
        if strong:
            d.line([(x - 1, y) for x, y in pts], fill=BONE, width=2)
            tip = (509, 259)
            d.rectangle((tip[0] - 3, tip[1] - 1, tip[0] + 3, tip[1] + 1), fill=BONE)
            d.rectangle((tip[0] - 1, tip[1] - 4, tip[0] + 1, tip[1] + 4), fill=BONE)
            d.point(tip, fill=(255, 255, 255))
    else:
        # The second glint is thinner and deliberately fainter on the right glass half.
        pts = [(520 + round(i * 0.18), 405 - i) for i in range(140)]
        d.line(pts, fill=(145, 205, 215), width=2)
        tip = (545, 268)
        d.point(tip, fill=(220, 235, 220))
    return out


def darken_running_lights(im):
    a = np.array(im, dtype=np.uint8)
    yy, xx = np.mgrid[0:1024, 0:1024]

    # The two long red wing slabs, converted to dark navy panel pixels.
    def polygon_mask(points):
        mask = Image.new("1", (1024, 1024), 0)
        ImageDraw.Draw(mask).polygon(points, fill=1)
        return np.asarray(mask, dtype=bool)

    slab_left = polygon_mask([(249, 695), (294, 668), (302, 721), (258, 750)])
    slab_right = polygon_mask([(722, 669), (774, 695), (766, 749), (720, 721)])
    # The white/red wingtip diamonds, also fully unlit.
    tip_left = polygon_mask([(137, 864), (151, 879), (137, 895), (123, 879)])
    tip_right = polygon_mask([(886, 864), (900, 879), (886, 895), (872, 879)])
    lights = slab_left | slab_right | tip_left | tip_right
    a[lights] = np.array(NAVY, dtype=np.uint8)

    # Reintroduce a few dark panel pixels so the unlit shapes read as installed panels.
    a[slab_left & (((xx + yy) % 17) < 2)] = np.array((24, 42, 78), dtype=np.uint8)
    a[slab_right & (((xx + yy) % 19) < 2)] = np.array((24, 42, 78), dtype=np.uint8)
    a[tip_left & (((xx + yy) % 7) < 2)] = np.array((20, 36, 66), dtype=np.uint8)
    a[tip_right & (((xx + yy) % 7) < 2)] = np.array((20, 36, 66), dtype=np.uint8)

    # Dim the small nose-tip light without repainting the silver nose cone.
    top = (yy >= 84) & (yy <= 125) & (xx >= 497) & (xx <= 527)
    bright = top & (a[:, :, 0] > 145) & (a[:, :, 1] > 145) & (a[:, :, 2] > 145)
    a[bright] = np.clip(a[bright].astype(np.float32) * 0.58, 0, 255).astype(np.uint8)
    return Image.fromarray(a, "RGB")


def exact_green(im):
    a = np.array(im.convert("RGB"), dtype=np.uint8)
    r, g, b = [a[:, :, i].astype(np.int16) for i in range(3)]
    bg = (g > 160) & (g > r * 1.35) & (g > b * 1.35)
    a[bg] = np.array(GREEN, dtype=np.uint8)
    return Image.fromarray(a, "RGB")


def bank_pose(source):
    im = exact_green(Image.open(source))
    a = np.array(im, dtype=np.uint8)
    r, g, b = [a[:, :, i].astype(np.int16) for i in range(3)]
    bg = (g > 160) & (g > r * 1.35) & (g > b * 1.35)
    mask = ~bg
    ys, xs = np.where(mask)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    cropped = im.crop((x0, y0, x1 + 1, y1 + 1))
    target_h = 928
    target_w = round(cropped.width * target_h / cropped.height)
    cropped = cropped.resize((target_w, target_h), Image.Resampling.NEAREST)
    canvas = Image.new("RGB", (1024, 1024), GREEN)
    canvas.paste(cropped, ((1024 - target_w) // 2, 48))
    return exact_green(canvas)


def make_sheet(state):
    key = Image.open(KEYS[state]).convert("RGB")
    cell0 = darken_running_lights(dim_canopy(key))
    cell1 = add_glint(brighten_canopy(key), "left", True)
    cell2 = add_glint(darken_running_lights(dim_canopy(key)), "right", False)
    cell3 = bank_pose(BANKS[state][0])
    cell4 = bank_pose(BANKS[state][1])
    sheet = Image.new("RGB", (3072, 2048), GREEN)
    for index, cell in enumerate((cell0, cell1, cell2, cell3, cell4)):
        x = (index % 3) * 1024
        y = (index // 3) * 1024
        sheet.paste(cell, (x, y))
    out = ROOT / f"hull_redo_state{state}.png"
    sheet.save(out)
    return sheet


def make_preview(sheets):
    thumb_w, thumb_h = 768, 512
    gap = 24
    preview = Image.new("RGB", (thumb_w * 3 + gap * 4, thumb_h + gap * 2), (11, 11, 26))
    for i, sheet in enumerate(sheets):
        thumb = sheet.resize((thumb_w, thumb_h), Image.Resampling.NEAREST)
        preview.paste(thumb, (gap + i * (thumb_w + gap), gap))
    preview.save(ROOT / "preview_j1b.png")


if __name__ == "__main__":
    make_preview([make_sheet(i) for i in range(3)])
