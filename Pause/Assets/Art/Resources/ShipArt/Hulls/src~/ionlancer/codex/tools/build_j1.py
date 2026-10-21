from __future__ import annotations

from pathlib import Path
from colorsys import rgb_to_hsv

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
OLD = ROOT / "ref_old_state0.png"
PAINT_SOURCE = Path("/Users/sina/.codex/generated_images/01a15ffd-44b8-70c2-9dc9-b7f47884353b/exec-6e036dc7-1ff8-41e1-8faa-18f8259ec551.png")
GREEN = (0, 255, 0)


def non_green_mask(im: Image.Image) -> Image.Image:
    pix = im.convert("RGB")
    mask = Image.new("1", pix.size, 0)
    out = mask.load()
    src = pix.load()
    for y in range(pix.height):
        for x in range(pix.width):
            out[x, y] = src[x, y] != GREEN
    return mask


def generated_mask(im: Image.Image) -> Image.Image:
    """Reject the generator's green-screen gradient while keeping cyan highlights."""
    rgb = im.convert("RGB")
    out = Image.new("1", rgb.size, 0)
    dst = out.load()
    src = rgb.load()
    for y in range(rgb.height):
        for x in range(rgb.width):
            r, g, b = src[x, y]
            # The generated background is green-dominant; cyan glass has blue >= green.
            dst[x, y] = not (g > 120 and g > r * 1.20 and g > b * 1.12)
    return out


def bbox(mask: Image.Image) -> tuple[int, int, int, int]:
    return mask.getbbox() or (0, 0, 1, 1)


def crop_to_bbox(im: Image.Image, box: tuple[int, int, int, int]) -> Image.Image:
    return im.crop(box)


def tint_for_green_free(rgb: tuple[int, int, int]) -> tuple[int, int, int]:
    r, g, b = rgb
    h, s, v = rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
    # Remove accidental green diffuse pixels while preserving cyan emissive pixels.
    if s > 0.45 and 0.20 < h < 0.46 and b < g * 0.92:
        return (18, 38, 110)
    # Keep the palette away from the forbidden pink/magenta band.
    if s > 0.50 and (h > 0.88 or h < 0.04) and r > b * 1.10:
        return (210, 35, 42)
    return rgb


def make_base() -> tuple[Image.Image, Image.Image]:
    old = Image.open(OLD).convert("RGB")
    old_silhouette = non_green_mask(old)
    target_box = bbox(old_silhouette)
    target_w = target_box[2] - target_box[0]
    target_h = target_box[3] - target_box[1]

    src = Image.open(PAINT_SOURCE).convert("RGB")
    src_mask = generated_mask(src)
    src_box = bbox(src_mask)
    src = crop_to_bbox(src, src_box)
    src_mask = crop_to_bbox(src_mask, src_box)
    # Nearest-neighbour only: preserve the generator's stepped pixel clusters.
    src = src.resize((target_w, target_h), Image.Resampling.NEAREST)
    src_mask = src_mask.resize((target_w, target_h), Image.Resampling.NEAREST)
    # Keep the generated painted edge as the actual silhouette, but fit its extents
    # exactly to the old gameplay framing contract.
    silhouette = Image.new("1", old.size, 0)
    silhouette.paste(src_mask, (target_box[0], target_box[1]))

    canvas = Image.new("RGB", old.size, GREEN)
    cpix = canvas.load()
    srcpix = src.load()
    # Generated paint covers the gameplay contract silhouette wherever possible. The
    # remaining pixels are a restrained deep-navy hull underpaint, not a copied outline.
    for y in range(target_h):
        for x in range(target_w):
            dx, dy = target_box[0] + x, target_box[1] + y
            if silhouette.getpixel((dx, dy)):
                cpix[dx, dy] = (9, 16, 54)
                if src_mask.getpixel((x, y)):
                    cpix[dx, dy] = tint_for_green_free(srcpix[x, y])

    # Re-key the exact outside of the old gameplay silhouette.
    for y in range(canvas.height):
        for x in range(canvas.width):
            if not silhouette.getpixel((x, y)):
                cpix[x, y] = GREEN
    return canvas, silhouette


def clipped_layer(base: Image.Image, silhouette: Image.Image) -> Image.Image:
    out = base.copy()
    pix = out.load()
    for y in range(out.height):
        for x in range(out.width):
            if not silhouette.getpixel((x, y)):
                pix[x, y] = GREEN
    return out


def draw_state1(base: Image.Image, silhouette: Image.Image) -> Image.Image:
    im = base.copy()
    d = ImageDraw.Draw(im)
    ink = (5, 6, 12)
    cavity = (7, 10, 25)
    cavity2 = (16, 19, 38)
    rib = (55, 67, 104)
    cyan = (11, 208, 246)
    cyan_hi = (122, 246, 252)
    amber = (252, 122, 8)
    soot = (25, 22, 28)
    ash = (74, 60, 55)

    # Left wing panel blown away, but all marks remain inside the inherited silhouette.
    d.polygon([(132, 744), (179, 696), (263, 671), (328, 700), (355, 761),
               (326, 824), (276, 866), (207, 895), (145, 879)], fill=cavity)
    d.polygon([(151, 757), (194, 718), (256, 700), (310, 720), (329, 764),
               (299, 814), (254, 845), (201, 867), (160, 854)], fill=cavity2)
    d.polygon([(169, 825), (215, 791), (278, 778), (315, 807), (282, 854),
               (225, 878), (177, 862)], fill=(10, 13, 29))
    for off in (0, 13, 27):
        d.line([(173 + off, 750 - off // 2), (225 + off, 661), (290 + off // 2, 620)], fill=rib, width=5)
    d.line([(150, 849), (216, 788), (301, 730)], fill=(111, 127, 163), width=3)
    # Sparking amber/cyan wire runs in the opened bay.
    d.line([(157, 851), (204, 815), (224, 774), (267, 751), (315, 715)], fill=amber, width=4)
    d.line([(181, 869), (211, 830), (247, 806), (272, 765)], fill=cyan, width=4)
    d.rectangle((205, 807, 211, 813), fill=(255, 251, 224))
    d.rectangle((285, 735, 291, 741), fill=cyan_hi)
    d.rectangle((164, 846, 171, 853), fill=amber)
    # Soot and directional burn scars around the cavity.
    for poly in [
        [(126, 724), (166, 707), (188, 733), (159, 768), (127, 757)],
        [(247, 665), (291, 654), (310, 680), (272, 704)],
        [(306, 817), (344, 790), (360, 817), (326, 850)],
    ]:
        d.polygon(poly, fill=soot)
    d.line([(135, 699), (180, 720), (213, 710)], fill=ash, width=5)
    d.line([(286, 651), (305, 677), (335, 690)], fill=ash, width=4)
    d.line([(212, 884), (249, 861), (285, 863)], fill=ash, width=4)

    # Hairline cracks across the central plating.
    crack = (38, 44, 68)
    d.line([(355, 505), (371, 520), (365, 541), (381, 554)], fill=crack, width=3)
    d.line([(669, 554), (650, 567), (658, 589), (642, 604)], fill=crack, width=3)
    d.line([(438, 789), (424, 804), (432, 824), (417, 837)], fill=crack, width=3)

    # Scorched left engine pod and contained smoke.
    d.polygon([(355, 824), (393, 810), (418, 834), (414, 921), (375, 942), (351, 916)], fill=(28, 24, 30))
    d.rectangle((365, 852, 401, 879), fill=(55, 39, 41))
    d.line([(361, 886), (394, 898), (412, 918)], fill=(122, 55, 32), width=5)
    d.rectangle((380, 930, 407, 945), fill=(44, 35, 38))
    d.rectangle((378, 939, 409, 949), fill=amber)
    d.rectangle((385, 941, 402, 949), fill=(255, 251, 224))
    d.polygon([(365, 812), (344, 800), (355, 784), (375, 792), (368, 805)], fill=(53, 54, 66))
    d.polygon([(347, 786), (330, 774), (343, 761), (359, 773)], fill=(78, 77, 83))

    return clipped_layer(im, silhouette)


def draw_state2(state1: Image.Image, silhouette: Image.Image) -> Image.Image:
    im = state1.copy()
    d = ImageDraw.Draw(im)
    ink = (5, 6, 12)
    deep = (10, 12, 25)
    char = (34, 23, 28)
    char2 = (62, 38, 35)
    orange = (252, 122, 8)
    orange_hi = (253, 240, 106)
    cyan = (11, 208, 246)
    cyan_hi = (122, 246, 252)
    bone = (245, 253, 253)

    # Right canard bent and cracked, rendered as an inset broken panel.
    d.polygon([(655, 350), (718, 369), (752, 414), (729, 439), (690, 418), (664, 390)], fill=deep)
    d.line([(664, 366), (698, 391), (735, 427)], fill=(63, 77, 132), width=6)
    d.line([(696, 383), (686, 404), (703, 422)], fill=bone, width=3)
    d.line([(721, 411), (735, 420), (748, 412)], fill=char2, width=4)

    # Right wingtip torn open: dark bites stay inward from the old silhouette edge.
    d.polygon([(837, 844), (877, 831), (898, 853), (880, 883), (901, 899),
               (875, 925), (844, 905), (856, 884), (829, 866)], fill=deep)
    d.polygon([(857, 848), (884, 855), (871, 872), (889, 889), (865, 901), (846, 882)], fill=orange)
    d.polygon([(861, 855), (878, 859), (868, 870), (878, 889), (860, 896), (852, 880)], fill=(111, 38, 21))
    d.line([(829, 858), (854, 868), (873, 862)], fill=char2, width=5)
    d.line([(843, 899), (866, 882), (888, 899)], fill=ink, width=5)

    # Right-wing exposed wiring with cyan arcs.
    d.line([(759, 748), (788, 770), (802, 802), (828, 815), (849, 842)], fill=cyan, width=4)
    d.line([(771, 753), (803, 781), (811, 813), (841, 829)], fill=cyan_hi, width=3)
    d.rectangle((796, 777, 804, 785), fill=bone)
    d.rectangle((839, 825, 848, 833), fill=cyan_hi)

    # Canopy cracks, deliberately bone-white and confined to the glass.
    d.line([(496, 340), (507, 365), (497, 401), (513, 434), (504, 467), (518, 503)], fill=bone, width=3)
    d.line([(506, 401), (533, 414), (548, 439)], fill=bone, width=3)
    d.line([(498, 468), (478, 490), (482, 523)], fill=(191, 226, 226), width=3)

    # Heavy char and soot over plating, kept sparse enough for the panel language to read.
    char_layer = Image.new("RGBA", im.size, (0, 0, 0, 0))
    char_draw = ImageDraw.Draw(char_layer)
    for poly in [
        [(282, 606), (350, 580), (382, 614), (360, 669), (307, 682), (273, 646)],
        [(642, 594), (715, 577), (750, 618), (721, 674), (663, 666), (634, 630)],
        [(345, 700), (420, 680), (450, 725), (425, 774), (360, 770), (332, 734)],
        [(574, 700), (650, 681), (685, 723), (660, 778), (598, 774), (566, 735)],
        [(210, 846), (298, 808), (345, 845), (319, 895), (245, 914), (198, 882)],
        [(681, 828), (772, 804), (823, 842), (795, 897), (720, 914), (671, 876)],
        [(410, 835), (470, 814), (491, 854), (473, 911), (421, 925), (399, 883)],
        [(533, 825), (593, 813), (616, 858), (598, 918), (546, 924), (522, 879)],
    ]:
        char_draw.polygon(poly, fill=(*char, 172))
    im = Image.alpha_composite(im.convert("RGBA"), char_layer).convert("RGB")
    d = ImageDraw.Draw(im)
    d.line([(300, 608), (356, 636)], fill=char2, width=5)
    d.line([(666, 613), (731, 641)], fill=char2, width=5)
    d.line([(366, 718), (425, 742)], fill=(103, 55, 43), width=4)
    d.line([(598, 744), (659, 720)], fill=(103, 55, 43), width=4)
    d.line([(230, 868), (306, 844)], fill=(103, 55, 43), width=5)
    d.line([(705, 867), (786, 847)], fill=(103, 55, 43), width=5)

    # A few contained embers in the damaged bays.
    for x, y in ((324, 618), (345, 642), (733, 683), (768, 705), (412, 775), (622, 806)):
        d.rectangle((x, y, x + 6, y + 8), fill=orange)
        d.rectangle((x + 2, y + 1, x + 4, y + 4), fill=orange_hi)
    return clipped_layer(im, silhouette)


def make_preview(images: list[Image.Image]) -> Image.Image:
    out = Image.new("RGB", (1024 * len(images), 1024), (11, 11, 26))
    for i, image in enumerate(images):
        keyed = Image.new("RGB", image.size, (11, 11, 26))
        src = image.load()
        dst = keyed.load()
        for y in range(image.height):
            for x in range(image.width):
                if src[x, y] != GREEN:
                    dst[x, y] = src[x, y]
        out.paste(keyed, (i * 1024, 0))
    return out


def main() -> None:
    base, silhouette = make_base()
    state1 = draw_state1(base, silhouette)
    state2 = draw_state2(state1, silhouette)
    base.save(ROOT / "key_state0.png")
    state1.save(ROOT / "key_state1.png")
    state2.save(ROOT / "key_state2.png")
    make_preview([base, state1, state2]).save(ROOT / "preview_j1.png")


if __name__ == "__main__":
    main()
