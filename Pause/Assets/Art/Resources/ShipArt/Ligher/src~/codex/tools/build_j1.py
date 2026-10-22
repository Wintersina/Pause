#!/usr/bin/env python3
"""Build the three Ligher J1 poses from a generated intact pixel-art source."""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


GREEN = np.array([0, 255, 0], dtype=np.uint8)
INK = (5, 6, 12)
SOOT = (24, 15, 18)
SOOT2 = (53, 25, 17)
COPPER_DARK = (58, 24, 8)
COPPER = (110, 48, 20)
COPPER_MID = (200, 102, 46)
COPPER_LIGHT = (242, 164, 114)
COPPER_HOT = (255, 200, 144)
CYAN_DARK = (8, 55, 80)
CYAN = (11, 145, 204)
CYAN_HOT = (11, 208, 246)
WHITE = (245, 253, 253)
BONE = (255, 251, 224)
AMBER = (252, 184, 9)
AMBER_HOT = (255, 251, 224)
RED = (216, 35, 44)


def is_green(rgb: np.ndarray) -> np.ndarray:
    """Return a mask for generated/reference chroma-green pixels."""
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    return (g > 70) & (g > r * 1.45) & (g > b * 1.45)


def crop_subject(im: Image.Image) -> tuple[Image.Image, np.ndarray, tuple[int, int, int, int]]:
    rgb = np.asarray(im.convert("RGB"))
    mask = ~is_green(rgb)
    ys, xs = np.where(mask)
    bbox = (int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1))
    return im.convert("RGB").crop(bbox), mask[bbox[1] : bbox[3], bbox[0] : bbox[2]], bbox


def old_silhouette(old_path: Path) -> np.ndarray:
    rgb = np.asarray(Image.open(old_path).convert("RGB"))
    # The supplied old reference is already flat keyed green; preserve its
    # exact non-green pixel contract rather than applying the generated-image
    # chroma heuristic to it.
    return np.any(rgb != GREEN, axis=2)


def align_generated(source_path: Path, old_path: Path) -> tuple[Image.Image, np.ndarray]:
    source = Image.open(source_path).convert("RGB")
    subject, subject_mask, _ = crop_subject(source)
    mask0 = old_silhouette(old_path)
    ys, xs = np.where(mask0)
    box = (int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1))
    target_size = (box[2] - box[0], box[3] - box[1])
    subject = subject.resize(target_size, Image.Resampling.NEAREST)
    subject_mask_im = Image.fromarray((subject_mask * 255).astype(np.uint8), "L").resize(
        target_size, Image.Resampling.NEAREST
    )
    canvas = Image.new("RGB", (1024, 1024), tuple(GREEN.tolist()))
    canvas.paste(subject, (box[0], box[1]), subject_mask_im)
    arr = np.asarray(canvas).copy()
    # The old Ligher silhouette is the gameplay contract. Fill any tiny generated
    # edge gaps by copying the nearest generated subject pixel, never by adding a
    # new outline or a transparent/green edge.
    generated_mask = np.asarray(subject_mask_im) > 0
    region = arr[box[1] : box[3], box[0] : box[2]]
    missing = (~generated_mask) & mask0[box[1] : box[3], box[0] : box[2]]
    if missing.any():
        coords = np.argwhere(generated_mask)
        for y, x in np.argwhere(missing):
            d = coords - np.array([y, x])
            idx = int(np.argmin((d * d).sum(axis=1)))
            region[y, x] = region[coords[idx][0], coords[idx][1]]
    arr[~mask0] = GREEN
    # Remove any accidentally sampled green from the ship interior.
    bad = mask0 & is_green(arr)
    if bad.any():
        valid = mask0 & ~is_green(arr)
        coords = np.argwhere(valid)
        for y, x in np.argwhere(bad):
            d = coords - np.array([y, x])
            idx = int(np.argmin((d * d).sum(axis=1)))
            arr[y, x] = arr[coords[idx][0], coords[idx][1]]
    return Image.fromarray(arr, "RGB"), mask0


def clipped_draw(base: Image.Image, silhouette: np.ndarray, fn) -> Image.Image:
    layer = base.copy()
    draw = ImageDraw.Draw(layer)
    fn(draw)
    arr = np.asarray(layer)
    base_arr = np.asarray(base).copy()
    base_arr[silhouette] = arr[silhouette]
    base_arr[~silhouette] = GREEN
    return Image.fromarray(base_arr, "RGB")


def line(draw: ImageDraw.ImageDraw, points, fill, width=3):
    draw.line(points, fill=fill, width=width, joint="curve")


def cavity(draw: ImageDraw.ImageDraw, points, rim=True, ribs=True):
    if rim:
        draw.polygon(points, fill=COPPER_DARK)
        # A hot broken rim makes the exposed machinery read as painted plating.
        rim_points = list(points) + [points[0]]
        line(draw, rim_points, COPPER_HOT, 4)
        line(draw, [points[i] for i in (0, 1, 2)], COPPER_MID, 7)
    draw.polygon(points, fill=SOOT)
    if ribs:
        p = np.asarray(points)
        lo = p.min(axis=0)
        hi = p.max(axis=0)
        for t in (0.25, 0.5, 0.75):
            x = int(lo[0] * (1 - t) + hi[0] * t)
            line(draw, [(x - 18, int(lo[1] + 8)), (x + 10, int(hi[1] - 8))], (81, 61, 49), 5)
        line(draw, [(int(lo[0] + 8), int(hi[1] * 0.65)), (int(hi[0] - 8), int(lo[1] + 0.35 * (hi[1] - lo[1])))], AMBER, 4)


def wreck_cavity(draw: ImageDraw.ImageDraw, points):
    """A broken, textured cavity; avoid a single flat sticker-like fill."""
    p = np.asarray(points)
    lo = p.min(axis=0)
    hi = p.max(axis=0)
    draw.polygon(points, fill=(35, 20, 18))
    # Uneven interior machinery planes.
    draw.polygon(
        [
            (int(lo[0] + 0.15 * (hi[0] - lo[0])), int(lo[1] + 0.24 * (hi[1] - lo[1]))),
            (int(lo[0] + 0.55 * (hi[0] - lo[0])), int(lo[1] + 0.12 * (hi[1] - lo[1]))),
            (int(lo[0] + 0.76 * (hi[0] - lo[0])), int(lo[1] + 0.55 * (hi[1] - lo[1]))),
            (int(lo[0] + 0.30 * (hi[0] - lo[0])), int(lo[1] + 0.70 * (hi[1] - lo[1]))),
        ],
        fill=(61, 34, 25),
    )
    draw.polygon(
        [
            (int(lo[0] + 0.30 * (hi[0] - lo[0])), int(lo[1] + 0.06 * (hi[1] - lo[1]))),
            (int(lo[0] + 0.50 * (hi[0] - lo[0])), int(lo[1] + 0.25 * (hi[1] - lo[1]))),
            (int(lo[0] + 0.39 * (hi[0] - lo[0])), int(lo[1] + 0.86 * (hi[1] - lo[1]))),
        ],
        fill=(92, 57, 38),
    )
    # Broken rim segments, not a continuous outlined polygon.
    for a, b in ((0, 1), (1, 2), (3, 4 if len(points) > 4 else 0)):
        line(draw, [points[a], points[b]], COPPER_HOT, 3)
    for frac, color in ((0.26, (93, 87, 75)), (0.51, (118, 82, 49)), (0.76, AMBER)):
        x = int(lo[0] * (1 - frac) + hi[0] * frac)
        line(draw, [(x - 11, int(lo[1] + 8)), (x + 8, int(hi[1] - 8))], color, 4)
    line(draw, [(int(lo[0] + 8), int(lo[1] + 0.64 * (hi[1] - lo[1]))), (int(hi[0] - 8), int(lo[1] + 0.40 * (hi[1] - lo[1])))], CYAN, 3)
    draw.rectangle((int(lo[0] + 0.48 * (hi[0] - lo[0])), int(lo[1] + 0.44 * (hi[1] - lo[1])), int(lo[0] + 0.56 * (hi[0] - lo[0])), int(lo[1] + 0.52 * (hi[1] - lo[1]))), fill=AMBER_HOT)


def wires(draw: ImageDraw.ImageDraw, points):
    line(draw, points, AMBER, 4)
    line(draw, [(x + 7, y + 3) for x, y in points], CYAN_HOT, 3)
    for x, y in points[1:-1]:
        draw.rectangle((x - 3, y - 3, x + 3, y + 3), fill=AMBER_HOT)


def sparks(draw: ImageDraw.ImageDraw, points):
    for x, y in points:
        line(draw, [(x - 12, y), (x + 12, y)], AMBER, 3)
        line(draw, [(x, y - 12), (x, y + 12)], CYAN_HOT, 3)
        draw.point((x, y), fill=WHITE)


def state1(base: Image.Image, silhouette: np.ndarray) -> Image.Image:
    def paint(draw):
        # Upper-left panel blown inward; ribs, sparking wires and a hot metal lip.
        cavity(draw, [(329, 331), (445, 299), (479, 414), (429, 525), (335, 494)])
        wires(draw, [(348, 470), (373, 431), (390, 385), (425, 345)])
        sparks(draw, [(365, 362), (415, 451)])
        # Smaller right-side impact and its soot fan.
        cavity(draw, [(587, 480), (683, 514), (677, 607), (616, 642), (574, 572)], ribs=False)
        wires(draw, [(597, 548), (625, 565), (663, 594)])
        # Soot streaks run backward over both halves and fins.
        for pts in [
            [(304, 500), (291, 585), (300, 659), (286, 726)],
            [(345, 575), (330, 663), (347, 739)],
            [(721, 515), (740, 604), (722, 687), (744, 755)],
            [(676, 580), (696, 667), (682, 740)],
            [(273, 753), (249, 815), (268, 872)],
            [(752, 754), (775, 820), (749, 880)],
        ]:
            line(draw, pts, SOOT, 12)
            line(draw, [(x + 7, y) for x, y in pts], SOOT2, 5)
        # Fine cracks across nose cap and fuselage.
        line(draw, [(443, 112), (462, 139), (450, 174), (472, 205)], INK, 5)
        line(draw, [(570, 136), (548, 163), (562, 198), (540, 229)], INK, 5)
        line(draw, [(398, 264), (425, 284), (414, 316)], SOOT, 5)
        line(draw, [(615, 268), (591, 296), (602, 331)], SOOT, 5)
        line(draw, [(457, 665), (438, 698), (454, 733), (437, 774)], SOOT, 5)
        # Dented left fin and a scorched nozzle lip with smoke kept inside the hull.
        draw.polygon([(245, 782), (287, 758), (336, 798), (304, 847), (257, 841)], fill=SOOT)
        line(draw, [(253, 802), (288, 809), (316, 828)], COPPER_HOT, 5)
        draw.ellipse((459, 883, 565, 950), fill=SOOT)
        draw.rectangle((478, 914, 548, 944), fill=(84, 70, 65))
        line(draw, [(487, 908), (480, 888), (493, 870)], (116, 110, 104), 5)
        line(draw, [(526, 910), (538, 889), (531, 872)], (80, 79, 83), 5)
        # Repaint a few damage-edge highlights so the change is not a flat sticker.
        line(draw, [(329, 331), (356, 322), (381, 316)], COPPER_LIGHT, 5)
        line(draw, [(587, 480), (619, 491), (649, 505)], COPPER_LIGHT, 5)

    return clipped_draw(base, silhouette, paint)


def state2(base: Image.Image, silhouette: np.ndarray) -> Image.Image:
    def paint(draw):
        # Nose and upper shoulders: blown panels on both sides, chipped gem.
        wreck_cavity(draw, [(407, 112), (451, 88), (477, 137), (460, 194), (414, 184)])
        wreck_cavity(draw, [(547, 138), (586, 126), (608, 206), (577, 235), (549, 205)])
        draw.polygon([(485, 96), (511, 82), (532, 126), (509, 151)], fill=SOOT)
        line(draw, [(490, 100), (509, 126)], BONE, 5)
        # Mid-hull cavities on both halves with orange-edged machinery.
        wreck_cavity(draw, [(303, 484), (372, 460), (408, 540), (370, 604), (306, 582)])
        wreck_cavity(draw, [(622, 466), (692, 486), (711, 561), (667, 600), (621, 545)])
        wreck_cavity(draw, [(360, 661), (414, 648), (445, 721), (401, 759), (354, 729)])
        wreck_cavity(draw, [(566, 658), (628, 676), (667, 728), (616, 766), (572, 730)])
        wires(draw, [(315, 526), (350, 545), (385, 582), (413, 617)])
        wires(draw, [(626, 520), (657, 550), (692, 575), (711, 612)])
        wires(draw, [(383, 691), (414, 712), (438, 751)])
        wires(draw, [(581, 684), (610, 717), (651, 750)])
        sparks(draw, [(335, 487), (686, 511), (399, 718), (626, 713)])
        # Char bands spread from nose to tail across most of the plating.
        for pts in [
            [(326, 244), (295, 339), (315, 411)],
            [(691, 243), (721, 335), (703, 415)],
            [(268, 620), (240, 704), (258, 777)],
            [(748, 624), (780, 706), (758, 783)],
            [(408, 796), (391, 872), (423, 925)],
            [(625, 792), (651, 872), (612, 925)],
        ]:
            line(draw, pts, SOOT, 16)
            line(draw, [(x + 8, y - 2) for x, y in pts], SOOT2, 6)
        # Canopy: one broken facet and bone-white cracks, kept teal-blue.
        draw.polygon([(456, 277), (492, 258), (508, 319), (482, 359), (447, 330)], fill=CYAN_DARK)
        line(draw, [(470, 271), (493, 326), (481, 392), (500, 448)], BONE, 6)
        line(draw, [(500, 326), (530, 354), (548, 409)], BONE, 5)
        line(draw, [(481, 392), (452, 424), (444, 472)], BONE, 5)
        draw.rectangle((480, 309, 493, 332), fill=WHITE)
        # Torn fins, jagged bites, burn marks, and internal sparks.
        wreck_cavity(draw, [(240, 773), (267, 746), (305, 761), (294, 797), (310, 820), (278, 839), (247, 827)])
        wreck_cavity(draw, [(719, 770), (749, 744), (788, 769), (776, 802), (791, 824), (758, 841), (729, 823)])
        line(draw, [(251, 775), (276, 792), (302, 783)], COPPER_HOT, 5)
        line(draw, [(735, 781), (761, 797), (784, 783)], COPPER_HOT, 5)
        sparks(draw, [(278, 746), (765, 743), (310, 839), (744, 842)])
        # Tail machinery and a badly scorched, still-clean nozzle.
        wreck_cavity(draw, [(414, 835), (468, 817), (488, 903), (460, 933), (420, 913)])
        wreck_cavity(draw, [(538, 819), (594, 837), (610, 910), (559, 932), (532, 900)])
        draw.ellipse((458, 895, 570, 963), fill=SOOT)
        draw.rectangle((478, 923, 548, 950), fill=(90, 72, 58))
        line(draw, [(483, 921), (477, 900), (492, 879)], (124, 110, 91), 5)
        line(draw, [(531, 921), (542, 899), (535, 878)], (77, 77, 77), 5)
        # A few hot embers remain inside the damaged silhouette.
        for x, y in [(373, 389), (649, 390), (301, 679), (716, 680), (445, 855), (589, 852)]:
            draw.rectangle((x - 4, y - 4, x + 4, y + 4), fill=AMBER_HOT)
            draw.point((x, y), fill=WHITE)

    return clipped_draw(base, silhouette, paint)


def make_preview(images: list[Image.Image], out_path: Path):
    canvas = Image.new("RGB", (1000, 372), (11, 11, 26))
    for i, im in enumerate(images):
        keyed = np.asarray(im).copy()
        keyed[is_green(keyed)] = np.array((11, 11, 26), dtype=np.uint8)
        thumb = Image.fromarray(keyed).resize((320, 320), Image.Resampling.NEAREST)
        canvas.paste(thumb, (10 + i * 330, 26))
    canvas.save(out_path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", type=Path, required=True)
    root = Path(__file__).resolve().parents[1]
    ap.add_argument("--old", type=Path, default=root / "ref_old_state0.png")
    ap.add_argument("--out", type=Path, default=root)
    args = ap.parse_args()
    base, silhouette = align_generated(args.source, args.old)
    damaged = state1(base, silhouette)
    wreck = state2(damaged, silhouette)
    base.save(args.out / "key_state0.png")
    damaged.save(args.out / "key_state1.png")
    wreck.save(args.out / "key_state2.png")
    make_preview([base, damaged, wreck], args.out / "preview_j1.png")


if __name__ == "__main__":
    main()
