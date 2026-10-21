from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
TOOLS = Path(__file__).resolve().parent
GREEN = (0, 255, 0)
INK = (5, 6, 12)


def is_chroma(pixel):
    r, g, b = pixel
    # The generator leaves a few dark-green antialias pixels around the hull.
    # Key all green-dominant pixels, while preserving cyan (blue-dominant),
    # white/grey (balanced), and the warm SolarFang ramps.
    return g >= 32 and g > r * 1.05 and g > b * 1.05


def keyed(path):
    image = Image.open(path).convert("RGB")
    if image.size != (1024, 1024):
        image = image.resize((1024, 1024), Image.Resampling.NEAREST)
    pixels = image.load()
    mask = Image.new("1", image.size, 0)
    mask_pixels = mask.load()
    for y in range(1024):
        for x in range(1024):
            if is_chroma(pixels[x, y]):
                pixels[x, y] = GREEN
            else:
                mask_pixels[x, y] = 1
    return image, mask


def clip_to_mask(image, mask):
    pixels = image.load()
    mask_pixels = mask.load()
    for y in range(1024):
        for x in range(1024):
            if not mask_pixels[x, y]:
                pixels[x, y] = GREEN


def source_region(target, source, box, mask):
    left, top, right, bottom = box
    target_pixels = target.load()
    source_pixels = source.load()
    mask_pixels = mask.load()
    for y in range(max(0, top), min(1024, bottom)):
        for x in range(max(0, left), min(1024, right)):
            if mask_pixels[x, y] and not is_chroma(source_pixels[x, y]):
                target_pixels[x, y] = source_pixels[x, y]


def paint(image, mask, callback):
    callback(ImageDraw.Draw(image))
    clip_to_mask(image, mask)


def line(draw, points, fill, width=3):
    draw.line(points, fill=fill, width=width, joint="curve")


def build_state1(state0, mask, damage_ref):
    image = state0.copy()
    # Generated damage pass supplies the painted texture; these bounds limit it to the
    # requested blown-panel, reactor, and nozzle areas before the exact state-0 mask clips it.
    source_region(image, damage_ref, (285, 500, 535, 925), mask)
    source_region(image, damage_ref, (425, 615, 625, 885), mask)
    source_region(image, damage_ref, (530, 820, 690, 950), mask)

    def details(draw):
        # Left flank: a dark cavity, broken armour lip, ribs, and exposed frame.
        draw.polygon([(345, 600), (452, 575), (493, 673), (475, 853),
                      (427, 914), (338, 854), (326, 714)], fill=(8, 10, 18))
        draw.polygon([(365, 635), (430, 617), (459, 687), (448, 826),
                      (413, 876), (365, 830)], fill=(19, 28, 38))
        line(draw, [(362, 656), (405, 690), (398, 830)], (91, 61, 38), 6)
        line(draw, [(399, 625), (435, 700), (425, 864)], (126, 84, 40), 5)
        line(draw, [(441, 650), (455, 735), (432, 884)], (47, 80, 89), 4)
        line(draw, [(350, 722), (397, 748), (455, 727)], (23, 29, 43), 8)
        # Amber and cyan arcing wires stay inside the cavity.
        line(draw, [(361, 700), (391, 718), (405, 770), (385, 825), (424, 871)], (252, 122, 8), 6)
        line(draw, [(378, 680), (414, 710), (421, 773), (410, 838)], (255, 190, 53), 3)
        line(draw, [(352, 770), (382, 786), (395, 841)], (11, 208, 246), 5)
        line(draw, [(445, 704), (420, 756), (437, 805)], (21, 216, 249), 3)
        for x, y in [(370, 716), (401, 771), (388, 824), (425, 868)]:
            draw.rectangle((x - 4, y - 4, x + 4, y + 4), fill=(255, 247, 157))

        # Cracked reactor ring and heat damage around it.
        line(draw, [(493, 644), (478, 685), (501, 718), (485, 756),
                    (510, 790), (496, 842)], (255, 111, 11), 8)
        line(draw, [(493, 644), (478, 685), (501, 718), (485, 756),
                    (510, 790), (496, 842)], (255, 239, 107), 3)
        line(draw, [(530, 662), (546, 699), (532, 733)], (80, 25, 13), 7)
        for box in [(455, 632, 481, 655), (474, 837, 500, 862),
                    (543, 650, 565, 674), (548, 817, 578, 844)]:
            draw.ellipse(box, fill=(48, 20, 15))
        # Scorch streaks and three hairline hull cracks.
        line(draw, [(440, 590), (430, 624), (442, 651)], (48, 23, 19), 8)
        line(draw, [(579, 625), (595, 662), (583, 694)], (54, 24, 18), 7)
        line(draw, [(682, 580), (704, 602), (696, 633), (718, 651)], (54, 24, 18), 4)
        line(draw, [(735, 715), (716, 738), (726, 765), (707, 788)], (62, 25, 18), 4)
        line(draw, [(294, 771), (319, 789), (309, 815), (332, 833)], (58, 25, 18), 4)

        # Scorched center nozzle and a compact smoke wisp contained by the hull.
        draw.rectangle((570, 868, 660, 930), fill=(24, 17, 20))
        draw.rectangle((584, 874, 648, 898), fill=(75, 45, 30))
        draw.rectangle((592, 882, 640, 894), fill=(219, 77, 10))
        line(draw, [(604, 866), (596, 845), (611, 827), (603, 811)], (65, 67, 71), 9)
        line(draw, [(625, 866), (634, 846), (621, 829), (629, 811)], (111, 112, 111), 7)

    paint(image, mask, details)
    return image


def build_state2(state1, mask, damage_ref):
    image = state1.copy()
    source_region(image, damage_ref, (85, 500, 280, 900), mask)
    source_region(image, damage_ref, (410, 260, 625, 900), mask)
    source_region(image, damage_ref, (825, 790, 990, 945), mask)

    def details(draw):
        # Bent/cracked left fang fin, painted without touching its outer mask.
        draw.polygon([(128, 570), (193, 553), (231, 626), (215, 775),
                      (178, 835), (145, 780)], fill=(12, 15, 27))
        line(draw, [(149, 584), (178, 658), (163, 730), (195, 790)], (66, 75, 97), 8)
        line(draw, [(139, 650), (177, 681), (209, 662)], (255, 91, 8), 7)
        line(draw, [(144, 651), (177, 681), (209, 662)], (255, 196, 56), 2)
        line(draw, [(165, 560), (177, 612), (162, 669)], (6, 7, 14), 8)

        # Split reactor housing with an orange core, opened ribs, and electrical arcs.
        draw.polygon([(458, 655), (497, 625), (570, 638), (625, 695),
                      (619, 822), (573, 876), (478, 850), (443, 783)], fill=(8, 9, 15))
        draw.polygon([(493, 695), (532, 677), (584, 708), (600, 764),
                      (573, 813), (523, 823), (486, 782)], fill=(105, 34, 8))
        draw.ellipse((490, 699, 597, 816), fill=(194, 58, 5))
        draw.ellipse((501, 710, 587, 804), fill=(252, 122, 8))
        draw.ellipse((522, 730, 570, 784), fill=(255, 247, 181))
        line(draw, [(468, 665), (498, 704), (480, 749), (506, 791), (482, 844)], (103, 120, 131), 7)
        line(draw, [(595, 652), (574, 694), (602, 735), (583, 781), (612, 829)], (84, 93, 102), 7)
        line(draw, [(456, 716), (492, 735), (470, 778), (506, 804)], (255, 91, 8), 7)
        line(draw, [(570, 650), (603, 681), (584, 708), (615, 738), (591, 765)], (255, 165, 21), 5)
        line(draw, [(487, 842), (522, 813), (568, 840), (603, 806)], (255, 78, 8), 6)

        # Bone-white cracks across the canopy glass.
        line(draw, [(492, 334), (520, 380), (505, 421), (537, 457), (515, 516)], (12, 47, 68), 9)
        line(draw, [(492, 334), (520, 380), (505, 421), (537, 457), (515, 516)], (245, 253, 243), 4)
        line(draw, [(571, 390), (542, 421), (557, 451), (530, 480)], (245, 253, 243), 4)
        line(draw, [(503, 422), (474, 446)], (245, 253, 243), 3)

        # Heavy directional soot over the plates; retain orange lit edges and identity lights.
        soot = (38, 25, 24)
        for poly in [
            [(289, 690), (342, 665), (358, 722), (326, 760), (285, 744)],
            [(364, 505), (422, 485), (447, 536), (414, 579), (372, 557)],
            [(650, 528), (711, 501), (739, 549), (705, 591), (663, 577)],
            [(690, 744), (748, 720), (771, 774), (738, 813), (696, 799)],
            [(430, 850), (483, 827), (502, 879), (467, 913), (427, 889)],
            [(627, 850), (688, 827), (718, 870), (684, 911), (640, 893)],
        ]:
            draw.polygon(poly, fill=soot)
        for x, y in [(314, 706), (330, 726), (393, 530), (408, 550),
                     (688, 535), (715, 559), (718, 769), (742, 786),
                     (452, 866), (665, 872), (374, 801), (758, 676)]:
            draw.rectangle((x, y, x + 7, y + 7), fill=(15, 15, 21))

        # Torn right rear tip: dark bites and an orange-hot inner rim, all inside the edge.
        draw.polygon([(884, 838), (926, 821), (967, 849), (950, 884),
                      (969, 906), (925, 914), (895, 889)], fill=(9, 9, 16))
        line(draw, [(887, 840), (920, 850), (944, 839), (958, 866),
                    (935, 881), (957, 903)], (255, 92, 7), 7)
        line(draw, [(890, 841), (921, 851), (944, 840), (958, 866),
                    (935, 881), (957, 903)], (255, 191, 41), 2)

        # Cracked central spine and a few small orange embers.
        line(draw, [(579, 814), (605, 842), (585, 866), (612, 894),
                    (592, 920)], (12, 10, 15), 9)
        line(draw, [(579, 814), (605, 842), (585, 866), (612, 894),
                    (592, 920)], (255, 132, 10), 3)
        for box, color in [
            ((349, 620, 360, 631), (255, 119, 8)),
            ((733, 650, 744, 661), (255, 190, 37)),
            ((413, 888, 424, 899), (255, 107, 5)),
            ((671, 791, 682, 802), (255, 168, 21)),
        ]:
            draw.ellipse(box, fill=color)

    paint(image, mask, details)
    return image


def preview(images):
    gap = 24
    canvas = Image.new("RGB", (1024 * 3 + gap * 2, 1024), "#0b0b1a")
    for index, image in enumerate(images):
        shown = image.copy()
        pixels = shown.load()
        for y in range(1024):
            for x in range(1024):
                if pixels[x, y] == GREEN:
                    pixels[x, y] = (11, 11, 26)
        canvas.paste(shown, (index * (1024 + gap), 0))
    canvas.save(ROOT / "preview_j1.png")


def main():
    state0, mask = keyed(TOOLS / "_solar_candidate3.png")
    damage1, _ = keyed(TOOLS / "_damage_candidate1.png")
    damage2, _ = keyed(TOOLS / "_damage_candidate2.png")
    state1 = build_state1(state0, mask, damage1)
    state2 = build_state2(state1, mask, damage2)
    for name, image in [("key_state0.png", state0), ("key_state1.png", state1),
                        ("key_state2.png", state2)]:
        clip_to_mask(image, mask)
        image.save(ROOT / name)
    preview([state0, state1, state2])
    print("built key_state0.png, key_state1.png, key_state2.png, preview_j1.png")


if __name__ == "__main__":
    main()
