from __future__ import annotations

from pathlib import Path
from typing import Iterable

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
GREEN = (0, 255, 0)
INK = (5, 6, 12)
NAVY = (10, 17, 32)
DEEP_NAVY = (11, 15, 35)
BLUE_DARK = (13, 55, 112)
BLUE = (15, 118, 207)
BLUE_LIGHT = (52, 168, 237)
CYAN_DARK = (8, 89, 124)
CYAN = (11, 208, 246)
CYAN_HOT = (122, 246, 252)
WHITE_HOT = (245, 253, 253)
BONE = (255, 251, 224)
MAROON = (54, 14, 39)
RED_DARK = (104, 19, 32)
AMBER_DARK = (117, 54, 11)
AMBER = (220, 113, 10)


def mask_poly(size: tuple[int, int], points: Iterable[tuple[int, int]]) -> Image.Image:
    m = Image.new("1", size, 0)
    ImageDraw.Draw(m).polygon(list(points), fill=1)
    return m


def paint_mask(img: Image.Image, mask: Image.Image, fn) -> None:
    px = img.load()
    mm = mask.load()
    for y in range(img.height):
        for x in range(img.width):
            if mm[x, y] and px[x, y] != GREEN:
                px[x, y] = fn(x, y, px[x, y])


def is_cyan(p: tuple[int, int, int]) -> bool:
    r, g, b = p
    return g > 75 and b > 75 and b >= r + 25


def is_damage(p: tuple[int, int, int]) -> bool:
    r, g, b = p
    return (r > 105 and g < 175 and b < 110) or (r > 45 and r > g * 1.35 and r > b * 1.35)


def dim_lights(img: Image.Image) -> Image.Image:
    out = img.copy()
    d = ImageDraw.Draw(out)

    # The bright bolt inserts, leaving their dark 1 px sel-out intact.
    bolt_l = [(151, 523), (181, 490), (278, 500), (245, 531), (221, 563), (176, 589)]
    bolt_r = [(873, 523), (843, 490), (746, 500), (779, 531), (803, 563), (848, 589)]
    for poly in (bolt_l, bolt_r):
        paint_mask(out, mask_poly(out.size, poly), lambda x, y, p: (18, 39, 70) if (x + y) % 11 else (31, 55, 87))

    # Wingtip diamonds become maroon/navy panel pixels, with a small dull inset.
    for poly in (
        [(82, 568), (103, 548), (125, 570), (104, 603)],
        [(942, 568), (921, 548), (899, 570), (920, 603)],
    ):
        paint_mask(out, mask_poly(out.size, poly), lambda x, y, p: MAROON if (x + y) % 5 else RED_DARK)

    # Gold star mark dimmed but still readable as a dormant insignia.
    star = mask_poly(out.size, [(504, 610), (520, 646), (584, 665), (520, 683), (504, 731), (486, 683), (432, 665), (486, 646)])
    paint_mask(out, star, lambda x, y, p: AMBER_DARK if (x + y) % 7 else (153, 75, 10))
    d.point((504, 665), fill=(220, 142, 31))

    # Dim only the glass, preserving its outline and panel frame.
    canopy = mask_poly(out.size, [(511, 237), (587, 296), (574, 420), (512, 443), (446, 405), (443, 296)])
    paint_mask(out, canopy, lambda x, y, p: (8, 111, 133) if is_cyan(p) and (x + y) % 9 else ((8, 74, 111) if is_cyan(p) else p))
    return out


def canopy_glint(img: Image.Image, right: bool, faint: bool = False) -> Image.Image:
    out = img.copy()
    d = ImageDraw.Draw(out)
    canopy = mask_poly(out.size, [(511, 237), (587, 296), (574, 420), (512, 443), (446, 405), (443, 296)])
    cm = canopy.load()
    px = out.load()

    # A broad, stepped reflection is painted into the glass so the glint reads at phone scale.
    if right:
        broad = mask_poly(out.size, [(535, 276), (575, 310), (570, 395), (538, 420), (522, 387)])
        edge = (20, 151, 190) if faint else (32, 190, 220)
    else:
        broad = mask_poly(out.size, [(451, 316), (492, 271), (511, 277), (476, 410), (450, 397)])
        edge = (47, 178, 208) if faint else (55, 214, 234)
    bm = broad.load()
    for y in range(out.height):
        for x in range(out.width):
            if bm[x, y] and cm[x, y] and is_cyan(px[x, y]):
                px[x, y] = edge if (x + 2 * y) % 7 else (93, 231, 241)

    if not right:
        # Enlarge the left glass reflection as a hard-banded painted facet. This keeps
        # the glint legible after the sheet is viewed at phone scale while changing
        # glass pixels only, never the blue hull around the canopy.
        facet = mask_poly(out.size, [(447, 315), (474, 278), (505, 270), (493, 343), (470, 407), (449, 397)])
        fm = facet.load()
        for y in range(out.height):
            for x in range(out.width):
                if fm[x, y] and cm[x, y] and px[x, y] != GREEN and sum(px[x, y]) > 75:
                    px[x, y] = (79, 224, 235) if (x + y) % 5 else (127, 242, 244)
        broad_facet = mask_poly(out.size, [(447, 296), (485, 250), (521, 248), (528, 306), (514, 416), (467, 413), (447, 383)])
        bf = broad_facet.load()
        for y in range(out.height):
            for x in range(out.width):
                if bf[x, y] and cm[x, y] and px[x, y] != GREEN and sum(px[x, y]) > 20:
                    px[x, y] = (56, 199, 224) if (x * 3 + y) % 9 else (105, 235, 239)

    if right:
        a, b = (548, 399), (579, 312)
        width = 4 if faint else 7
        d.line((a, b), fill=(145, 241, 245) if faint else WHITE_HOT, width=width)
        d.line((552, 393, 578, 318), fill=(238, 252, 246) if faint else BONE, width=2 if faint else 4)
        tip = (578, 313)
    else:
        a, b = (450, 397), (511, 270)
        d.line((a, b), fill=WHITE_HOT, width=8)
        d.line((454, 391, 513, 274), fill=BONE, width=4)
        tip = (512, 270)

    # Bone-white stepped sparkle at the leading tip, never a soft glow.
    tx, ty = tip
    radius = 7 if not faint else 4
    d.polygon([(tx, ty - radius), (tx + radius, ty), (tx, ty + radius), (tx - radius, ty)], fill=BONE)
    d.point((tx, ty), fill=WHITE_HOT)
    return out


def damage_snapshot(img: Image.Image) -> dict[tuple[int, int], tuple[int, int, int]]:
    px = img.load()
    snap: dict[tuple[int, int], tuple[int, int, int]] = {}
    for y in range(img.height):
        for x in range(img.width):
            if is_damage(px[x, y]):
                for oy in range(-2, 3):
                    for ox in range(-2, 3):
                        xx, yy = x + ox, y + oy
                        if 0 <= xx < img.width and 0 <= yy < img.height:
                            snap[(xx, yy)] = px[xx, yy]
    return snap


def paint_engine(d: ImageDraw.ImageDraw, left: bool) -> None:
    # Shorter near-side engine leg: retain the original leg and repaint only its
    # lower face/nozzle, so the approved panel texture remains visible.
    if left:
        d.polygon([(354, 830), (444, 830), (444, 864), (428, 878), (371, 872), (350, 851)], fill=INK)
        d.polygon([(365, 834), (433, 834), (435, 856), (420, 868), (378, 864), (362, 849)], fill=(48, 55, 71))
        d.rectangle((380, 849, 428, 862), fill=(92, 104, 111))
        d.polygon([(379, 860), (430, 860), (438, 878), (374, 878)], fill=INK)
        d.rectangle((388, 862, 427, 871), fill=(236, 120, 10))
        d.rectangle((398, 862, 420, 868), fill=(255, 214, 76))
    else:
        d.polygon([(580, 830), (670, 830), (674, 851), (651, 878), (596, 872), (573, 864)], fill=INK)
        d.polygon([(591, 834), (658, 834), (659, 849), (645, 868), (604, 864), (586, 856)], fill=(48, 55, 71))
        d.rectangle((595, 849, 645, 862), fill=(92, 104, 111))
        d.polygon([(594, 860), (642, 860), (651, 878), (583, 878)], fill=INK)
        d.rectangle((589, 862, 628, 871), fill=(236, 120, 10))
        d.rectangle((596, 862, 618, 868), fill=(255, 214, 76))


def paint_wing(d: ImageDraw.ImageDraw, left: bool, wide: bool) -> None:
    if left:
        # Near-side tip is shortened with a new stepped outer face; the original
        # wing panels and their light remain in place behind it.
        d.polygon([(104, 454), (132, 435), (146, 442), (124, 598), (102, 588)], fill=INK)
        d.polygon([(113, 463), (130, 449), (138, 453), (119, 581), (110, 575)], fill=BLUE_DARK)
        d.line([(116, 464), (132, 450)], fill=BLUE_LIGHT, width=4)
        d.polygon([(113, 557), (129, 542), (145, 557), (129, 575)], fill=RED_DARK)
        d.polygon([(121, 557), (129, 549), (137, 557), (129, 565)], fill=BONE)
        if wide:
            d.polygon([(101, 440), (35, 473), (14, 526), (24, 596), (101, 626)], fill=INK)
            d.polygon([(91, 458), (42, 486), (26, 530), (35, 584), (94, 607)], fill=(20, 63, 116))
            d.line([(42, 487), (90, 461)], fill=BLUE_LIGHT, width=5)
    else:
        d.polygon([(920, 454), (892, 435), (878, 442), (900, 598), (922, 588)], fill=INK)
        d.polygon([(911, 463), (894, 449), (886, 453), (905, 581), (914, 575)], fill=BLUE_DARK)
        d.line([(908, 464), (892, 450)], fill=BLUE_LIGHT, width=4)
        d.polygon([(911, 557), (895, 542), (879, 557), (895, 575)], fill=RED_DARK)
        d.polygon([(903, 557), (895, 549), (887, 557), (895, 565)], fill=BONE)
        if wide:
            d.polygon([(923, 440), (989, 473), (1010, 526), (1000, 596), (923, 626)], fill=INK)
            d.polygon([(933, 458), (982, 486), (998, 530), (989, 584), (930, 607)], fill=(20, 63, 116))
            d.line([(982, 487), (934, 461)], fill=BLUE_LIGHT, width=5)


def repaint_bank_planes(out: Image.Image, key: Image.Image, bank_left: bool) -> None:
    """Paint hard-banded perspective facets across the banked wing planes."""
    px = out.load()
    src = key.load()
    if bank_left:
        polys = [
            [(704, 390), (854, 410), (932, 468), (899, 528), (780, 493)],
            [(185, 418), (306, 425), (323, 512), (231, 548), (174, 509)],
            [(742, 505), (900, 535), (884, 610), (790, 579)],
            [(382, 503), (476, 500), (466, 732), (402, 706)],
            [(548, 500), (642, 503), (622, 706), (558, 732)],
        ]
    else:
        polys = [
            [(320, 390), (170, 410), (92, 468), (125, 528), (244, 493)],
            [(839, 418), (718, 425), (701, 512), (793, 548), (850, 509)],
            [(282, 505), (124, 535), (140, 610), (234, 579)],
            [(382, 503), (476, 500), (466, 732), (402, 706)],
            [(548, 500), (642, 503), (622, 706), (558, 732)],
        ]
    for poly_index, poly in enumerate(polys):
        m = mask_poly(out.size, poly).load()
        for y in range(370, 570):
            for x in range(70, 950):
                if not m[x, y] or src[x, y] == GREEN or is_damage(src[x, y]):
                    continue
                r, g, b = src[x, y]
                if sum(src[x, y]) < 40:
                    continue
                band = (x // 13 + y // 9 + poly_index * 3) % 6
                px[x, y] = [
                    (9, 52, 111),
                    (13, 76, 145),
                    (15, 111, 191),
                    (34, 151, 220),
                    (17, 83, 157),
                    (8, 39, 91),
                ][band]


def bank_variant(key: Image.Image, bank_left: bool) -> Image.Image:
    out = key.copy()
    damage = damage_snapshot(key)
    d = ImageDraw.Draw(out)
    # The near side is clipped by the roll; the far side receives a visible outer face.
    if bank_left:
        d.polygon([(42, 522), (70, 456), (105, 430), (126, 603), (86, 647), (42, 631)], fill=GREEN)
        d.polygon([(342, 846), (468, 846), (468, 901), (342, 901)], fill=GREEN)
        paint_wing(d, left=True, wide=False)
        paint_engine(d, left=True)
        paint_wing(d, left=False, wide=True)
        repaint_bank_planes(out, key, bank_left=True)
        # Slight left-shifted centre seam and skewed canopy edge, painted as new panel pixels.
        d.line([(504, 467), (504, 615), (498, 759)], fill=(7, 23, 54), width=5)
        d.line([(585, 288), (594, 352), (583, 425)], fill=(8, 31, 61), width=5)
        d.line([(579, 297), (586, 352)], fill=(31, 128, 177), width=3)
    else:
        d.polygon([(982, 522), (954, 456), (919, 430), (898, 603), (938, 647), (982, 631)], fill=GREEN)
        d.polygon([(556, 846), (682, 846), (682, 901), (556, 901)], fill=GREEN)
        paint_wing(d, left=False, wide=False)
        paint_engine(d, left=False)
        paint_wing(d, left=True, wide=True)
        repaint_bank_planes(out, key, bank_left=False)
        d.line([(520, 467), (520, 615), (526, 759)], fill=(7, 23, 54), width=5)
        d.line([(439, 288), (430, 352), (441, 425)], fill=(8, 31, 61), width=5)
        d.line([(445, 297), (438, 352)], fill=(31, 128, 177), width=3)

    # Restore the same physical damaged panels after the new bank paint.
    px = out.load()
    for (x, y), p in damage.items():
        if p != GREEN and px[x, y] == GREEN:
            px[x, y] = p
    return out


def make_sheet(state: int) -> Image.Image:
    key = Image.open(ROOT / f"key_state{state}.png").convert("RGB")
    cells = [
        dim_lights(key),
        canopy_glint(key, right=False, faint=False),
        canopy_glint(dim_lights(key), right=True, faint=True),
        bank_variant(key, bank_left=True),
        bank_variant(key, bank_left=False),
        Image.new("RGB", key.size, GREEN),
    ]
    sheet = Image.new("RGB", (3072, 2048), GREEN)
    for i, cell in enumerate(cells):
        sheet.paste(cell, ((i % 3) * 1024, (i // 3) * 1024))
    return sheet


def main() -> None:
    for state in range(3):
        make_sheet(state).save(ROOT / f"hull_redo_state{state}.png", format="PNG", optimize=False)

    # Compact nearest-neighbour preview: three complete sheets side by side on dark navy.
    preview = Image.new("RGB", (2304, 512), (11, 11, 26))
    for state in range(3):
        sheet = Image.open(ROOT / f"hull_redo_state{state}.png").convert("RGB")
        thumb = sheet.resize((768, 512), Image.Resampling.NEAREST)
        arr = np.asarray(thumb).copy()
        arr[np.all(arr == GREEN, axis=2)] = (11, 11, 26)
        thumb = Image.fromarray(arr, mode="RGB")
        preview.paste(thumb, (state * 768, 0))
    preview.save(ROOT / "preview_j1b.png", format="PNG", optimize=False)


if __name__ == "__main__":
    main()
