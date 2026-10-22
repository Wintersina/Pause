#!/usr/bin/env python3
"""Composite generated J1c paint into the approved Saboteur without silhouette drift."""

from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
TOOLS = ROOT / "tools"
GREEN = (0, 255, 0)
BG = (11, 11, 26)
SIZE = (1024, 1024)


def poly_mask(polygons):
    mask = Image.new("L", SIZE, 0)
    draw = ImageDraw.Draw(mask)
    for polygon in polygons:
        draw.polygon(polygon, fill=255)
    return mask


def composite(raw_name, polygons):
    base = Image.open(ROOT / "key_state0.png").convert("RGB")
    raw = Image.open(TOOLS / raw_name).convert("RGB").resize(SIZE, Image.Resampling.NEAREST)
    mask = poly_mask(polygons)
    out = base.copy()
    bp = base.load()
    rp = raw.load()
    op = out.load()
    mp = mask.load()
    for y in range(SIZE[1]):
        for x in range(SIZE[0]):
            if mp[x, y] and bp[x, y] != GREEN and rp[x, y] != GREEN:
                op[x, y] = rp[x, y]
    return out


STATE1 = [
    # Left horn mid-panel breach, kept inside the existing panel seam.
    [(230, 184), (318, 190), (353, 250), (368, 384), (357, 500),
     (321, 535), (272, 498), (245, 432), (228, 326)],
    # Right lower shoulder plate beside the diamond light.
    [(758, 628), (836, 637), (886, 705), (876, 795), (825, 851),
     (764, 818), (748, 742)],
    # Hairline crack region on the hub plating beside the core.
    [(640, 520), (716, 536), (760, 594), (761, 654), (718, 704),
     (662, 686), (632, 610)],
]

STATE2 = [
    # State 1 locations, opened a little farther for the wreck state.
    [(218, 170), (331, 176), (375, 247), (391, 402), (368, 536),
     (324, 557), (267, 516), (235, 449), (215, 326)],
    [(742, 610), (848, 620), (914, 702), (899, 825), (833, 881),
     (764, 842), (730, 748)],
    [(631, 510), (735, 526), (789, 594), (790, 674), (742, 726),
     (649, 702), (616, 600)],
    # Right horn tip torn open.
    [(720, 64), (815, 65), (856, 118), (862, 208), (823, 285),
     (766, 264), (730, 193)],
    # Hub lower-right ring machinery and arc.
    [(655, 632), (765, 646), (815, 724), (799, 795), (735, 831),
     (668, 788), (642, 710)],
    # Cracked core glass; retain the glowing core outside this local mask.
    [(470, 482), (594, 476), (646, 548), (649, 635), (604, 690),
     (492, 678), (457, 606)],
    # Char and scorch on the left horn tip.
    [(190, 58), (274, 58), (313, 112), (310, 172), (269, 205),
     (208, 165)],
]


def make_preview():
    preview = Image.new("RGB", (1536, 512), BG)
    for index, name in enumerate(("key_state0.png", "key_state1.png", "key_state2.png")):
        im = Image.open(ROOT / name).convert("RGB").resize((512, 512), Image.Resampling.NEAREST)
        pix = im.load()
        for y in range(512):
            for x in range(512):
                if pix[x, y] == GREEN:
                    pix[x, y] = BG
        preview.paste(im, (index * 512, 0))
    preview.save(ROOT / "preview_j1c.png")


def main():
    composite("generated_j1_raw.png", STATE1).save(ROOT / "key_state1.png")
    composite("generated_j2_raw.png", STATE2).save(ROOT / "key_state2.png")
    make_preview()


if __name__ == "__main__":
    main()
