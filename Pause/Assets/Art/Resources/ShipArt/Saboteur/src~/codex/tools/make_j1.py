#!/usr/bin/env python3
"""Build the J1 Saboteur key poses from the selected generated intact redraw.

The generated art supplies the painted surface. This helper only performs exact
nearest-neighbour fitting/keying and adds the requested hard-edged damage passes.
"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw


GREEN = (0, 255, 0)
INK = (5, 6, 12)
DEEP = (22, 10, 32)
AUBERGINE = (42, 18, 64)
VIOLET_DARK = (94, 26, 102)
VIOLET = (138, 56, 200)
LAVENDER = (201, 160, 245)
CYAN_DARK = (11, 79, 122)
CYAN = (11, 208, 246)
CYAN_HI = (122, 246, 252)
WHITE = (245, 253, 253)
BONE = (255, 246, 214)
RED = (216, 35, 44)
AMBER_DARK = (194, 74, 6)
AMBER = (252, 122, 8)
AMBER_HI = (255, 203, 80)

OUT = Path(__file__).resolve().parents[1]
TARGET_BBOX = (144, 52, 879, 971)  # matches the old intact silhouette contract


def is_generated_green(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    # The model's backdrop has a dark green fringe at the keyed edge. Remove
    # green-dominant pixels at every brightness so the ship never inherits it.
    return g >= 10 and g > r * 1.08 and g > b * 1.08


def key_and_fit(source: Path) -> tuple[Image.Image, Image.Image]:
    src = Image.open(source).convert("RGB")
    src_px = src.load()
    points = []
    for y in range(src.height):
        for x in range(src.width):
            if not is_generated_green(src_px[x, y]):
                points.append((x, y))
    if not points:
        raise RuntimeError("source has no non-green subject pixels")
    min_x = min(x for x, _ in points)
    min_y = min(y for _, y in points)
    max_x = max(x for x, _ in points)
    max_y = max(y for _, y in points)

    crop = Image.new("RGB", (max_x - min_x + 1, max_y - min_y + 1), GREEN)
    cp = crop.load()
    for y in range(min_y, max_y + 1):
        for x in range(min_x, max_x + 1):
            px = src_px[x, y]
            if not is_generated_green(px):
                cp[x - min_x, y - min_y] = px

    left, top, right, bottom = TARGET_BBOX
    fitted = crop.resize((right - left + 1, bottom - top + 1), Image.Resampling.NEAREST)
    base = Image.new("RGB", (1024, 1024), GREEN)
    base.paste(fitted, (left, top))
    mask = Image.new("1", (1024, 1024), 0)
    mask.paste(fitted.convert("RGB").point(lambda p: 255 if p < 245 else 0).convert("1"), (left, top))
    # The point-based mask above is only a placeholder; derive the exact mask
    # from keyed green so every state shares one identical silhouette.
    mask = Image.new("1", (1024, 1024), 0)
    mp = mask.load()
    bp = base.load()
    for y in range(1024):
        for x in range(1024):
            if bp[x, y] != GREEN:
                mp[x, y] = 1
    return base, mask


def poly(draw: ImageDraw.ImageDraw, pts, fill):
    draw.polygon(pts, fill=fill)


def line(draw: ImageDraw.ImageDraw, pts, fill, width=2):
    draw.line(pts, fill=fill, width=width, joint="curve")


def clipped_damage(base: Image.Image, mask: Image.Image, draw_fn) -> Image.Image:
    out = base.copy()
    layer = out.copy()
    draw = ImageDraw.Draw(layer)
    draw_fn(draw)
    op = out.load()
    lp = layer.load()
    mp = mask.load()
    for y in range(1024):
        for x in range(1024):
            if mp[x, y]:
                op[x, y] = lp[x, y]
            else:
                op[x, y] = GREEN
    return out


def state1_damage(draw: ImageDraw.ImageDraw):
    # Left horn mid-panel blown away: cavity, ribs, amber/cyan wiring, soot.
    poly(draw, [(222, 268), (294, 244), (329, 281), (341, 355),
                (320, 431), (274, 456), (237, 421), (226, 350)], DEEP)
    poly(draw, [(230, 282), (294, 259), (316, 289), (324, 350),
                (306, 414), (274, 432), (248, 405), (239, 347)], INK)
    line(draw, [(246, 293), (274, 335), (265, 393), (287, 425)], CYAN_DARK, 6)
    line(draw, [(306, 281), (286, 330), (299, 371), (282, 419)], AMBER, 5)
    line(draw, [(238, 347), (268, 357), (302, 345)], AMBER_HI, 3)
    line(draw, [(253, 407), (282, 384), (314, 397)], CYAN_HI, 3)
    # Uneven soot streaks around the blown panel.
    poly(draw, [(214, 274), (230, 267), (241, 304), (226, 337), (216, 325)], (32, 19, 29))
    poly(draw, [(318, 249), (337, 259), (347, 313), (335, 342), (326, 303)], (29, 16, 28))
    poly(draw, [(224, 411), (247, 424), (261, 456), (245, 470), (222, 444)], (25, 13, 24))
    line(draw, [(231, 286), (219, 302), (215, 328)], (112, 42, 32), 4)
    line(draw, [(323, 387), (338, 400), (337, 426)], (112, 42, 32), 4)

    # One blown/scorched plate on the right lower shoulder.
    poly(draw, [(711, 664), (771, 681), (804, 728), (783, 779),
                (735, 792), (699, 753)], DEEP)
    poly(draw, [(722, 682), (765, 694), (786, 728), (771, 756),
                (739, 764), (716, 741)], INK)
    poly(draw, [(706, 670), (729, 661), (742, 677), (719, 695)], AMBER_DARK)
    line(draw, [(724, 689), (747, 713), (775, 728)], AMBER, 4)
    line(draw, [(720, 762), (751, 747), (779, 755)], (112, 42, 32), 7)
    line(draw, [(736, 681), (764, 704)], (49, 28, 28), 8)

    # Two/three hairline cracks on the hub plating beside the core.
    line(draw, [(418, 556), (404, 573), (411, 592), (397, 611)], (54, 18, 54), 3)
    line(draw, [(639, 552), (653, 570), (645, 590), (659, 609)], (54, 18, 54), 3)
    line(draw, [(431, 710), (447, 721), (439, 738)], (54, 18, 54), 3)


def state2_damage(draw: ImageDraw.ImageDraw):
    state1_damage(draw)

    # Right horn tip torn/bent internally; keep the original outer silhouette.
    poly(draw, [(744, 75), (814, 65), (841, 108), (834, 177),
                (813, 222), (778, 201), (792, 163), (771, 131)], INK)
    poly(draw, [(766, 86), (808, 79), (825, 111), (819, 146),
                (799, 174), (782, 158), (791, 125)], AMBER_DARK)
    poly(draw, [(720, 70), (761, 57), (812, 62), (842, 96), (842, 143),
                (826, 192), (794, 225), (757, 205), (769, 167), (742, 132)], DEEP)
    poly(draw, [(737, 78), (766, 70), (803, 73), (827, 101), (827, 133),
                (811, 170), (790, 192), (772, 180), (780, 142), (752, 116)], INK)
    line(draw, [(733, 75), (759, 102), (781, 119)], AMBER_DARK, 7)
    line(draw, [(804, 78), (822, 104), (817, 145), (797, 177)], AMBER, 5)
    poly(draw, [(778, 176), (806, 160), (829, 178), (826, 214),
                (805, 243), (779, 228), (790, 204)], INK)
    line(draw, [(791, 181), (812, 196), (817, 224)], AMBER_DARK, 6)
    poly(draw, [(760, 214), (786, 224), (804, 246), (793, 276),
                (769, 287), (751, 263)], DEEP)
    line(draw, [(763, 230), (780, 249), (781, 272)], AMBER, 4)
    poly(draw, [(779, 95), (807, 91), (815, 115), (807, 137),
                (793, 151), (797, 121)], DEEP)
    line(draw, [(749, 81), (776, 103), (790, 136)], AMBER, 5)
    line(draw, [(813, 163), (825, 184), (817, 208)], VIOLET_DARK, 8)
    line(draw, [(800, 108), (825, 122)], AMBER_HI, 3)

    # Hub lower-right ring opened to exposed machinery and a blue-white arc.
    poly(draw, [(525, 679), (603, 671), (665, 700), (699, 749),
                (689, 808), (649, 844), (584, 833), (545, 795)], DEEP)
    poly(draw, [(499, 727), (532, 699), (568, 724), (575, 781),
                (553, 827), (520, 839), (494, 806)], INK)
    line(draw, [(509, 747), (538, 739), (555, 758), (548, 801)], (73, 102, 134), 5)
    line(draw, [(514, 813), (536, 788), (559, 782)], CYAN_DARK, 5)
    poly(draw, [(555, 705), (629, 702), (677, 746), (663, 806),
                (610, 824), (566, 792)], INK)
    poly(draw, [(575, 722), (622, 719), (652, 748), (642, 785),
                (611, 797), (583, 776)], DEEP)
    line(draw, [(583, 742), (605, 733), (628, 746), (623, 777)], (73, 102, 134), 5)
    line(draw, [(599, 789), (614, 765), (637, 751), (653, 759)], CYAN, 5)
    line(draw, [(606, 788), (619, 769), (639, 758)], WHITE, 2)
    poly(draw, [(626, 731), (640, 730), (646, 741), (636, 748)], AMBER_HI)

    # Cracked teal crystal: bone-white, hard pixel cracks across the glass.
    line(draw, [(535, 568), (524, 602), (535, 625), (523, 655), (534, 684)], BONE, 5)
    line(draw, [(526, 606), (548, 615), (568, 612)], BONE, 4)
    line(draw, [(535, 625), (558, 640), (575, 666)], BONE, 4)
    line(draw, [(570, 555), (563, 589), (577, 615), (568, 647), (580, 688)], BONE, 4)
    line(draw, [(486, 596), (501, 610), (497, 631)], BONE, 5)

    # Char/scorch on the left horn tip, contained inside the old edge.
    poly(draw, [(193, 86), (245, 75), (269, 108), (256, 156),
                (221, 175), (199, 145)], DEEP)
    poly(draw, [(206, 91), (239, 88), (251, 110), (240, 137),
                (216, 148), (207, 128)], (39, 20, 28))
    poly(draw, [(182, 66), (226, 55), (267, 71), (286, 106), (275, 153),
                (252, 181), (218, 172), (199, 143)], (28, 15, 25))
    line(draw, [(190, 70), (215, 96), (238, 113)], AMBER_DARK, 6)
    line(draw, [(211, 94), (232, 120), (245, 128)], AMBER_DARK, 5)
    line(draw, [(218, 148), (238, 139)], (112, 42, 32), 4)

    # A few blocky glowing embers, no exhaust or flame.
    for cx, cy in [(205, 188), (345, 430), (694, 645), (818, 235), (676, 824), (284, 468)]:
        poly(draw, [(cx, cy - 5), (cx + 4, cy - 2), (cx + 3, cy + 4),
                    (cx - 3, cy + 6), (cx - 5, cy + 1)], AMBER_DARK)
        poly(draw, [(cx, cy - 2), (cx + 3, cy), (cx + 1, cy + 3),
                    (cx - 2, cy + 2)], AMBER_HI)
    poly(draw, [(690, 809), (698, 804), (707, 810), (711, 821),
                (704, 831), (693, 827), (687, 818)], AMBER_DARK)
    poly(draw, [(697, 812), (703, 811), (706, 819), (701, 824),
                (695, 821)], AMBER_HI)
    poly(draw, [(610, 799), (621, 796), (630, 802), (632, 814),
                (625, 823), (613, 820), (607, 810)], AMBER_DARK)
    poly(draw, [(615, 803), (622, 802), (626, 808), (622, 815),
                (615, 813)], AMBER_HI)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("source", type=Path)
    args = ap.parse_args()
    base, mask = key_and_fit(args.source)
    base.save(OUT / "key_state0.png")
    state1 = clipped_damage(base, mask, state1_damage)
    state2 = clipped_damage(base, mask, state2_damage)
    state1.save(OUT / "key_state1.png")
    state2.save(OUT / "key_state2.png")

    preview = Image.new("RGB", (3072, 1024), (11, 11, 26))
    for i, image in enumerate((base, state1, state2)):
        tile = image.copy()
        tp = tile.load()
        for y in range(1024):
            for x in range(1024):
                if tp[x, y] == GREEN:
                    tp[x, y] = (11, 11, 26)
        preview.paste(tile, (i * 1024, 0))
    preview.save(OUT / "preview_j1.png")


if __name__ == "__main__":
    main()
