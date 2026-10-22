#!/usr/bin/env python3
"""Build the VoltViper J1 poses from the selected generated painted candidate.

The generator supplied the painted hull. This helper only performs nearest-neighbour
normalisation to the legacy silhouette bounds and adds hard-edged, in-silhouette damage.
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


GREEN = np.array([0, 255, 0], dtype=np.uint8)
INK = (5, 6, 12)
NAVY = (10, 17, 35)
DEEP = (16, 25, 48)
STEEL = (55, 69, 93)
BLUE = (23, 76, 140)
AZURE = (46, 155, 230)
CYAN = (11, 208, 246)
PALE_CYAN = (143, 211, 255)
WHITE = (245, 253, 253)
SOOT = (27, 22, 27)
SOOT_MID = (62, 46, 42)
BURNT = (111, 46, 23)
ORANGE = (247, 122, 10)
AMBER = (252, 184, 9)
CREAM = (255, 251, 224)


def green_like(a: np.ndarray) -> np.ndarray:
    r, g, b = a.astype(np.int16).transpose(2, 0, 1)
    return (g > 90) & (g > r * 1.20) & (g > b * 1.08)


def bbox(mask: np.ndarray) -> tuple[int, int, int, int]:
    ys, xs = np.where(mask)
    return int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)


def normalise(source: Path, reference: Path) -> tuple[Image.Image, np.ndarray]:
    src = np.array(Image.open(source).convert("RGB"))
    src_mask = ~green_like(src)
    sx0, sy0, sx1, sy1 = bbox(src_mask)

    ref = np.array(Image.open(reference).convert("RGB"))
    ref_mask = np.any(ref != GREEN, axis=2)
    tx0, ty0, tx1, ty1 = bbox(ref_mask)

    crop = Image.fromarray(src[sy0:sy1, sx0:sx1], "RGB")
    crop_mask = Image.fromarray((src_mask[sy0:sy1, sx0:sx1] * 255).astype(np.uint8), "L")
    size = (tx1 - tx0, ty1 - ty0)
    rgb = crop.resize(size, Image.Resampling.NEAREST)
    mask = crop_mask.resize(size, Image.Resampling.NEAREST)

    canvas = Image.new("RGB", ref.shape[1::-1], tuple(GREEN))
    canvas.paste(rgb, (tx0, ty0), mask)
    canvas_arr = np.array(canvas)
    return canvas, np.any(canvas_arr != GREEN, axis=2)


def clipped_damage(base: Image.Image, mask: np.ndarray, draw_ops) -> Image.Image:
    out = base.copy()
    draw = ImageDraw.Draw(out)
    draw_ops(draw)
    arr = np.array(out)
    arr[~mask] = GREEN
    return Image.fromarray(arr, "RGB")


def state1_ops(draw: ImageDraw.ImageDraw) -> None:
    # Blown panel at the right wing root: burnt lip, deep cavity, ribs and live wires.
    draw.polygon([(594, 382), (681, 392), (735, 438), (728, 523), (668, 552),
                  (610, 526), (590, 464)], fill=BURNT)
    draw.polygon([(606, 400), (677, 407), (716, 445), (708, 507), (669, 531),
                  (620, 508), (605, 460)], fill=INK)
    draw.polygon([(617, 410), (643, 419), (633, 511), (617, 502)], fill=DEEP)
    draw.polygon([(673, 414), (694, 434), (684, 520), (666, 528)], fill=NAVY)
    draw.line([(614, 420), (701, 493)], fill=STEEL, width=6)
    draw.line([(635, 410), (704, 453)], fill=(106, 115, 132), width=4)
    draw.line([(623, 500), (690, 427)], fill=(43, 54, 77), width=5)
    draw.line([(645, 518), (697, 465)], fill=(107, 57, 34), width=4)
    draw.line([(621, 469), (691, 438)], fill=AMBER, width=4)
    draw.line([(630, 489), (703, 474)], fill=CYAN, width=4)
    draw.line([(632, 490), (616, 535)], fill=ORANGE, width=3)
    draw.rectangle((620, 402, 630, 413), fill=AMBER)
    draw.rectangle((700, 496, 712, 509), fill=CYAN)

    # Soot and heat streaks around the opening and down the centre plating.
    draw.polygon([(574, 391), (606, 400), (600, 458), (585, 489), (568, 470)], fill=SOOT)
    draw.polygon([(706, 389), (744, 425), (734, 485), (718, 466)], fill=SOOT_MID)
    draw.polygon([(581, 506), (608, 518), (596, 574), (579, 555)], fill=SOOT_MID)
    draw.line([(548, 331), (568, 365), (558, 402)], fill=SOOT, width=7)
    draw.line([(548, 331), (568, 365), (558, 402)], fill=BURNT, width=3)
    draw.line([(525, 568), (553, 596), (540, 641)], fill=SOOT, width=7)
    draw.line([(525, 568), (553, 596), (540, 641)], fill=SOOT_MID, width=3)

    # Three hairline cracks over otherwise readable armour.
    draw.line([(447, 335), (472, 359), (460, 382), (480, 403)], fill=INK, width=4)
    draw.line([(472, 359), (494, 353)], fill=INK, width=3)
    draw.line([(518, 592), (540, 610), (534, 632), (553, 648)], fill=INK, width=4)
    draw.line([(518, 592), (505, 615)], fill=INK, width=3)
    draw.line([(470, 664), (492, 681), (488, 708)], fill=INK, width=3)

    # Scorched left nozzle and blocky smoke, all kept within the ship mask.
    draw.polygon([(349, 777), (426, 775), (439, 829), (423, 873), (361, 866),
                  (344, 824)], fill=SOOT)
    draw.polygon([(367, 797), (411, 793), (423, 833), (408, 858), (372, 852)], fill=BURNT)
    draw.rectangle((375, 827, 420, 851), fill=INK)
    draw.rectangle((387, 832, 416, 846), fill=AMBER)
    draw.rectangle((337, 756, 362, 790), fill=SOOT_MID)
    draw.rectangle((350, 734, 378, 763), fill=SOOT)
    draw.rectangle((367, 716, 389, 742), fill=SOOT_MID)
    draw.rectangle((380, 697, 395, 723), fill=DEEP)
    draw.rectangle((345, 765, 355, 778), fill=(112, 104, 100))


def state2_ops(draw: ImageDraw.ImageDraw) -> None:
    state1_ops(draw)

    # Left wingtip is torn inside the preserved outer silhouette; amber inner rim.
    draw.polygon([(49, 505), (91, 476), (157, 493), (188, 548), (159, 605),
                  (94, 617), (56, 583)], fill=SOOT)
    draw.polygon([(61, 523), (98, 500), (143, 511), (169, 548), (148, 581),
                  (105, 591), (74, 568)], outline=ORANGE, width=7)
    draw.polygon([(80, 530), (108, 515), (126, 528), (105, 549), (126, 565),
                  (98, 580), (76, 560)], fill=INK)
    draw.rectangle((112, 502, 126, 518), fill=AMBER)
    draw.rectangle((146, 543, 161, 558), fill=ORANGE)

    # Buckled right wing: broad dark folds and offset panel facets.
    draw.polygon([(744, 405), (826, 425), (935, 495), (969, 550), (907, 589),
                  (827, 548), (770, 510)], fill=SOOT_MID)
    draw.polygon([(765, 432), (831, 447), (913, 500), (886, 526), (809, 501)], fill=BLUE)
    draw.polygon([(789, 501), (864, 527), (916, 558), (884, 581), (815, 548)], fill=NAVY)
    draw.line([(758, 420), (810, 510), (896, 566)], fill=INK, width=8)
    draw.line([(808, 443), (785, 512), (828, 559)], fill=BURNT, width=5)
    draw.line([(844, 466), (824, 535), (882, 575)], fill=STEEL, width=6)
    draw.rectangle((868, 492, 893, 508), fill=ORANGE)

    # Right engine leg tear and orange-hot interior.
    draw.polygon([(577, 766), (660, 763), (682, 820), (668, 875), (602, 880),
                  (574, 831)], fill=SOOT)
    draw.polygon([(604, 786), (646, 784), (661, 824), (646, 858), (614, 853),
                  (594, 824)], fill=INK)
    draw.line([(605, 790), (635, 823), (620, 857)], fill=ORANGE, width=9)
    draw.line([(638, 790), (650, 829)], fill=AMBER, width=5)
    draw.rectangle((624, 818, 654, 846), fill=(255, 218, 69))
    draw.rectangle((631, 823, 646, 852), fill=ORANGE)

    # Canopy fractures in bone white over the cyan glass.
    draw.line([(509, 212), (492, 247), (505, 282), (486, 322), (500, 364)], fill=CREAM, width=6)
    draw.line([(493, 247), (552, 267)], fill=CREAM, width=5)
    draw.line([(505, 282), (566, 315)], fill=CREAM, width=4)
    draw.line([(531, 307), (549, 353)], fill=CREAM, width=4)

    # Heavy charcoal over broad armour planes, with retained blue facets.
    draw.polygon([(263, 493), (330, 456), (393, 492), (378, 571), (305, 594),
                  (267, 551)], fill=SOOT_MID)
    draw.polygon([(284, 499), (337, 477), (367, 505), (346, 549), (302, 565)], fill=DEEP)
    draw.polygon([(431, 650), (509, 666), (501, 746), (437, 760), (403, 707)], fill=SOOT)
    draw.polygon([(544, 645), (626, 666), (647, 735), (585, 764), (538, 728)], fill=SOOT_MID)
    draw.polygon([(459, 744), (511, 753), (533, 820), (491, 840), (448, 806)], fill=DEEP)
    draw.line([(285, 518), (346, 558)], fill=BURNT, width=6)
    draw.line([(421, 676), (477, 735)], fill=BURNT, width=5)
    draw.line([(573, 678), (618, 730)], fill=BURNT, width=5)

    # A few orange embers, clipped to the original silhouette after drawing.
    for box in [(228, 610, 239, 624), (749, 622, 763, 639), (406, 703, 419, 719),
                (690, 576, 700, 588), (319, 666, 329, 680)]:
        draw.rectangle(box, fill=ORANGE)
        x0, y0, x1, y1 = box
        draw.rectangle((x0 + 3, y0 + 3, x1 - 4, y1 - 4), fill=AMBER)


def build(source: Path, reference: Path, out_dir: Path) -> None:
    intact, mask = normalise(source, reference)
    damaged = clipped_damage(intact, mask, state1_ops)
    wreck = clipped_damage(intact, mask, state2_ops)
    out_dir.mkdir(parents=True, exist_ok=True)
    intact.save(out_dir / "key_state0.png")
    damaged.save(out_dir / "key_state1.png")
    wreck.save(out_dir / "key_state2.png")

    preview = Image.new("RGB", (3 * 1024 + 48, 1024), (11, 11, 26))
    for i, image in enumerate((intact, damaged, wreck)):
        preview.paste(image, (24 + i * 1040, 0))
    preview.save(out_dir / "preview_j1.png")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("--reference", type=Path, default=Path(__file__).resolve().parents[1] / "ref_old_state0.png")
    parser.add_argument("--out-dir", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    build(args.source, args.reference, args.out_dir)


if __name__ == "__main__":
    main()
