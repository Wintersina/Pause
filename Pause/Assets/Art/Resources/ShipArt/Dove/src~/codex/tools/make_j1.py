from pathlib import Path
import colorsys

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
GREEN = (0, 255, 0)


def is_green(pixel):
    r, g, b = pixel[:3]
    return g > 60 and g > r * 1.25 and g > b * 1.25


def prepare_base():
    path = ROOT / "key_state0.png"
    image = Image.open(path).convert("RGB")
    if image.size != (1024, 1024):
        image = image.resize((1024, 1024), Image.Resampling.NEAREST)
    pixels = image.load()
    for y in range(image.height):
        for x in range(image.width):
            if is_green(pixels[x, y]):
                pixels[x, y] = GREEN
                continue
            r, g, b = pixels[x, y]
            hue, saturation, value = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            if saturation > 0.5 and value > 0.18 and 300 <= hue * 360 < 345:
                pixels[x, y] = (5, 6, 12)
    image.save(path, format="PNG", optimize=True)
    return image


def line(draw, points, fill, width=4):
    draw.line(points, fill=fill, width=width, joint="curve")


def state1(base):
    image = base.copy()
    draw = ImageDraw.Draw(image)

    ink = (5, 6, 12)
    socket = (8, 14, 26)
    deep = (10, 20, 34)
    soot = (18, 20, 28)
    soot_hot = (67, 43, 29)
    teal_dark = (14, 74, 80)
    teal = (20, 106, 112)
    cyan = (11, 208, 246)
    cyan_dark = (11, 145, 204)
    amber = (252, 122, 8)
    amber_hot = (252, 184, 9)
    bone = (255, 251, 224)

    # One large, readable blown panel beside and just below the canopy.
    draw.polygon(
        [(298, 377), (330, 350), (382, 349), (425, 377), (452, 425),
         (445, 480), (414, 523), (365, 540), (319, 516), (292, 468)],
        fill=soot,
    )
    draw.polygon(
        [(278, 389), (294, 362), (326, 340), (350, 348), (326, 373),
         (303, 410), (300, 462), (316, 505), (342, 535), (326, 551),
         (291, 526), (270, 477)],
        fill=soot,
    )
    draw.polygon(
        [(315, 391), (348, 367), (393, 374), (424, 407), (428, 459),
         (399, 500), (362, 520), (327, 494), (307, 454)],
        fill=socket,
    )
    # Scorched broken plating around the opening.
    draw.polygon([(294, 390), (311, 377), (325, 389), (314, 419), (296, 430)], fill=soot_hot)
    draw.polygon([(402, 365), (431, 378), (444, 398), (430, 411), (410, 396)], fill=soot_hot)
    draw.polygon([(403, 492), (430, 474), (443, 488), (422, 517), (399, 525)], fill=soot_hot)
    draw.rectangle((333, 368, 347, 381), fill=teal_dark)
    draw.rectangle((402, 381, 417, 395), fill=teal)

    # Exposed ribs and frame.
    line(draw, [(320, 398), (352, 427), (345, 491)], teal, 9)
    line(draw, [(385, 377), (371, 424), (395, 488)], teal_dark, 10)
    line(draw, [(314, 454), (360, 448), (420, 467)], (54, 194, 194), 6)
    line(draw, [(330, 493), (362, 466), (408, 424)], (74, 109, 123), 5)
    line(draw, [(351, 386), (364, 418), (405, 402)], bone, 4)

    # Sparking cyan and amber cable runs.
    line(draw, [(324, 420), (343, 433), (355, 464), (382, 482)], cyan, 5)
    line(draw, [(407, 398), (391, 421), (395, 451), (419, 475)], amber, 5)
    line(draw, [(336, 469), (352, 456), (382, 459)], amber_hot, 4)
    draw.rectangle((317, 415, 326, 424), fill=cyan)
    draw.rectangle((407, 395, 416, 404), fill=amber_hot)
    draw.rectangle((381, 480, 391, 489), fill=bone)
    draw.rectangle((423, 457, 432, 466), fill=amber)

    # One small right-shoulder scorch and one short hairline crack.
    draw.polygon([(720, 344), (748, 340), (773, 356), (777, 382),
                  (757, 402), (729, 394), (714, 373)], fill=soot)
    draw.polygon([(736, 349), (755, 351), (764, 366), (750, 381),
                  (730, 374)], fill=soot_hot)
    draw.rectangle((747, 350, 754, 358), fill=amber)
    line(draw, [(785, 350), (773, 366), (777, 384), (765, 396)], ink, 4)
    line(draw, [(784, 350), (775, 365)], (54, 194, 194), 2)

    return mask_to_base(image, base)


def state2(base, damaged):
    image = damaged.copy()
    draw = ImageDraw.Draw(image)

    ink = (5, 6, 12)
    socket = (8, 14, 26)
    deep = (10, 20, 34)
    soot = (18, 20, 28)
    soot_hot = (67, 43, 29)
    teal_dark = (14, 74, 80)
    teal = (20, 106, 112)
    cyan = (11, 208, 246)
    cyan_dark = (11, 145, 204)
    amber = (252, 122, 8)
    amber_hot = (252, 184, 9)
    bone = (255, 251, 224)
    white = (245, 253, 253)

    # Cracked canopy: dark fracture under bone-white breaks, all inside the glass.
    line(draw, [(478, 226), (506, 274), (540, 292), (576, 330), (610, 353)], ink, 12)
    line(draw, [(478, 226), (506, 274), (540, 292), (576, 330), (610, 353)], bone, 6)
    line(draw, [(612, 211), (588, 251), (567, 292), (540, 340), (519, 399)], ink, 10)
    line(draw, [(612, 211), (588, 251), (567, 292), (540, 340), (519, 399)], white, 5)
    line(draw, [(447, 372), (480, 389), (508, 423)], bone, 5)
    # One shattered upper-left corner, still clipped to the canopy silhouette.
    draw.polygon([(468, 202), (501, 190), (515, 218), (495, 242), (468, 231)], fill=socket)
    draw.polygon([(468, 202), (489, 196), (497, 216), (481, 222)], fill=cyan_dark)
    draw.polygon([(499, 194), (517, 207), (507, 226), (496, 216)], fill=bone)

    # Heavier soot radiating from the existing right-shoulder hit.
    draw.polygon([(697, 326), (728, 312), (773, 322), (798, 351),
                  (792, 393), (771, 424), (731, 417), (703, 392),
                  (690, 355)], fill=soot)
    draw.polygon([(714, 331), (741, 323), (769, 334), (785, 356),
                  (772, 383), (748, 396), (721, 382), (706, 357)], fill=soot_hot)

    # Right wing tip: bites and a hot orange inner rim, painted inside the old outline.
    draw.polygon([(862, 526), (894, 501), (930, 510), (954, 542),
                  (936, 581), (900, 592), (872, 571)], fill=deep)
    draw.polygon([(844, 505), (870, 485), (918, 491), (950, 516),
                  (964, 551), (947, 589), (920, 611), (887, 600),
                  (861, 580), (845, 548)], fill=soot)
    draw.polygon([(877, 527), (900, 516), (924, 526), (934, 544),
                  (916, 550), (905, 538), (891, 554), (876, 548)], fill=soot)
    line(draw, [(864, 521), (894, 511), (923, 523), (941, 546)], amber, 8)
    line(draw, [(873, 557), (899, 568), (927, 557)], amber_hot, 5)
    draw.polygon([(891, 521), (902, 518), (910, 529), (900, 535)], fill=bone)

    # Left wing tip: smaller jagged bite with a dark cavity.
    draw.polygon([(152, 541), (175, 516), (207, 510), (232, 528),
                  (218, 561), (190, 572), (163, 563)], fill=deep)
    draw.polygon([(139, 526), (158, 500), (192, 493), (226, 506),
                  (244, 535), (229, 573), (205, 591), (171, 581),
                  (147, 560)], fill=soot)
    draw.polygon([(172, 528), (194, 519), (212, 533), (199, 544),
                  (187, 537), (176, 550)], fill=socket)
    line(draw, [(163, 523), (185, 515), (211, 523)], amber, 6)
    draw.rectangle((184, 521, 194, 530), fill=amber_hot)

    # Open lower belly panel with machinery and a small fire glow.
    draw.polygon([(466, 730), (552, 730), (579, 760), (573, 838),
                  (548, 875), (476, 875), (449, 838), (446, 766)], fill=soot)
    draw.polygon([(437, 716), (466, 696), (552, 697), (589, 728),
                  (603, 788), (590, 851), (558, 895), (468, 897),
                  (429, 856), (421, 772)], fill=soot)
    draw.polygon([(417, 744), (432, 711), (458, 697), (468, 713),
                  (445, 750), (440, 810), (456, 857), (443, 881),
                  (420, 851), (408, 797)], fill=soot)
    draw.polygon([(478, 750), (541, 748), (558, 770), (552, 829),
                  (531, 852), (488, 850), (469, 827), (466, 772)], fill=socket)
    # Exposed machinery.
    draw.rectangle((482, 768, 497, 832), fill=teal_dark)
    draw.rectangle((522, 759, 537, 825), fill=(47, 77, 91))
    line(draw, [(492, 778), (515, 791), (540, 780)], cyan, 5)
    line(draw, [(482, 827), (514, 812), (548, 830)], amber, 5)
    draw.rectangle((507, 784, 521, 805), fill=teal)
    draw.rectangle((507, 807, 522, 821), fill=cyan_dark)
    # Small fire core and soot ring.
    draw.polygon([(503, 845), (510, 826), (519, 843), (527, 830),
                  (530, 852), (515, 863)], fill=amber)
    draw.rectangle((510, 840, 521, 851), fill=amber_hot)
    draw.rectangle((514, 840, 519, 847), fill=bone)
    draw.polygon([(452, 751), (470, 738), (482, 746), (469, 760)], fill=soot_hot)
    draw.polygon([(548, 739), (570, 750), (580, 772), (561, 778)], fill=soot_hot)

    # A few deliberate embers, not a particle field.
    draw.rectangle((433, 699, 441, 708), fill=amber)
    draw.rectangle((585, 702, 594, 713), fill=amber_hot)
    draw.rectangle((642, 523, 649, 531), fill=amber)
    draw.rectangle((276, 594, 283, 602), fill=amber_hot)

    return mask_to_base(image, base)


def mask_to_base(image, base):
    out = image.copy()
    source = base.load()
    pixels = out.load()
    for y in range(out.height):
        for x in range(out.width):
            if is_green(source[x, y]):
                pixels[x, y] = GREEN
    return out


def make_preview(images):
    preview = Image.new("RGB", (3072, 1024), (11, 11, 26))
    target = preview.load()
    for index, image in enumerate(images):
        source = image.load()
        x_offset = index * 1024
        for y in range(1024):
            for x in range(1024):
                pixel = source[x, y]
                if not is_green(pixel):
                    target[x_offset + x, y] = pixel
    preview.save(ROOT / "preview_j1.png", format="PNG", optimize=True)


def main():
    base = prepare_base()
    damaged = state1(base)
    wreck = state2(base, damaged)
    damaged.save(ROOT / "key_state1.png", format="PNG", optimize=True)
    wreck.save(ROOT / "key_state2.png", format="PNG", optimize=True)
    make_preview([base, damaged, wreck])


if __name__ == "__main__":
    main()
