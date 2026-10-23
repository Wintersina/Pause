#!/usr/bin/env python3
"""Build the three Lightning key poses from the selected generated redraw.

The generated candidate supplies the painted hull. Damage is deliberately drawn as
small, hard-stepped pixel-art plates, cavities, ribs, wires, cracks and scorch marks;
all changes are clipped to the state-0 foreground mask.
"""

from pathlib import Path
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "candidate_intact.png"
OUT = ROOT
GREEN = (0, 255, 0)
BG = (11, 11, 26)

# Old Lightning framing contract: exact non-green bounds of ref_old_state0.png.
BOX = (48, 64, 976, 960)
W = H = 1024

INK = (5, 6, 12)
NAVY = (11, 15, 21)
STEEL = (47, 52, 59)
STEEL_HI = (176, 180, 182)
OLIVE = (90, 84, 16)
GOLD = (142, 132, 20)
LEMON = (238, 220, 50)
CREAM = (255, 246, 168)
RED = (216, 35, 44)
RED_HI = (255, 91, 69)
CYAN = (11, 208, 246)
CYAN_DARK = (11, 79, 122)
AMBER = (252, 122, 8)
AMBER_HI = (255, 251, 224)
CHAR = (42, 23, 15)
CHAR_HI = (106, 36, 4)
WIRE_CYAN = (122, 246, 252)


def harden_candidate() -> tuple[Image.Image, Image.Image]:
    src = Image.open(SOURCE).convert("RGBA")
    alpha = src.getchannel("A")
    bbox = alpha.point(lambda p: 255 if p >= 128 else 0).getbbox()
    if bbox is None:
        raise RuntimeError("candidate_intact.png has no opaque foreground")
    crop = src.crop(bbox)
    crop = crop.resize((BOX[2] - BOX[0], BOX[3] - BOX[1]), Image.Resampling.NEAREST)
    # The game art is hard-edged: no semi-transparent solid pixels.
    a = crop.getchannel("A").point(lambda p: 255 if p >= 128 else 0)
    rgb = crop.convert("RGB")
    base = Image.new("RGB", (W, H), GREEN)
    base.paste(rgb, (BOX[0], BOX[1]), a)
    mask = Image.new("L", (W, H), 0)
    mask.paste(a, (BOX[0], BOX[1]))
    return base, mask


def poly(draw: ImageDraw.ImageDraw, pts, fill):
    draw.polygon(pts, fill=fill)


def line(draw: ImageDraw.ImageDraw, pts, fill, width=2):
    draw.line(pts, fill=fill, width=width, joint="curve")


def specks(draw, points, fill, size=2):
    for x, y in points:
        draw.rectangle((x, y, x + size, y + size), fill=fill)


def left_pod_cavity(draw):
    # Blown upper panel on the left outer pod: stepped cavity, ribs, and live cables.
    poly(draw, [(270, 388), (307, 372), (338, 392), (350, 452), (337, 507),
                (302, 526), (272, 500), (258, 449)], INK)
    poly(draw, [(278, 397), (306, 384), (327, 401), (333, 449), (322, 495),
                (294, 507), (277, 483)], NAVY)
    line(draw, [(279, 411), (317, 435), (293, 486)], STEEL_HI, 4)
    line(draw, [(306, 390), (303, 440), (325, 478)], STEEL, 5)
    line(draw, [(274, 462), (305, 449), (331, 459)], CHAR_HI, 3)
    line(draw, [(276, 421), (294, 439), (281, 478)], AMBER, 3)
    line(draw, [(296, 390), (316, 415), (310, 452), (332, 484)], WIRE_CYAN, 3)
    line(draw, [(314, 397), (321, 423), (311, 455), (327, 476)], AMBER_HI, 2)
    specks(draw, [(268, 410), (335, 427), (286, 503), (340, 474)], AMBER_HI, 2)


def scorch_wings_and_spine(draw):
    # Soot streaks are stepped rather than translucent overlays.
    for pts in [
        [(112, 680), (152, 661), (193, 642), (218, 648), (181, 665), (145, 694)],
        [(899, 680), (864, 664), (824, 645), (799, 650), (833, 671), (870, 696)],
        [(413, 560), (445, 552), (476, 568), (463, 582), (430, 575)],
        [(579, 566), (612, 552), (645, 562), (627, 578), (594, 584)],
    ]:
        poly(draw, pts, CHAR)
    line(draw, [(121, 677), (151, 670), (181, 653)], CHAR_HI, 4)
    line(draw, [(903, 681), (870, 670), (841, 654)], CHAR_HI, 4)
    line(draw, [(430, 564), (449, 570), (462, 566)], AMBER, 3)
    line(draw, [(596, 574), (614, 563), (636, 568)], AMBER, 3)
    specks(draw, [(149, 657), (190, 644), (842, 649), (878, 662), (450, 549), (624, 550)], AMBER_HI, 2)

    # Hairline cracks across lemon plates.
    for pts in [
        [(205, 556), (221, 569), (216, 588), (232, 602)],
        [(820, 552), (801, 568), (808, 586), (790, 602)],
        [(374, 704), (391, 716), (385, 738), (401, 749)],
        [(650, 703), (634, 716), (640, 738), (624, 750)],
        [(479, 618), (494, 629), (488, 648), (501, 659)],
    ]:
        line(draw, pts, INK, 2)
        line(draw, [(x, y - 2) for x, y in pts[:-1]], GOLD, 1)


def scorched_nozzles(draw):
    for cx in (343, 681):
        poly(draw, [(cx - 30, 854), (cx - 20, 842), (cx + 19, 842),
                    (cx + 31, 860), (cx + 21, 893), (cx - 22, 894)], CHAR)
        poly(draw, [(cx - 20, 856), (cx + 18, 854), (cx + 24, 868),
                    (cx + 15, 882), (cx - 17, 880)], NAVY)
        line(draw, [(cx - 17, 862), (cx + 17, 865)], AMBER, 4)
        specks(draw, [(cx - 22, 846), (cx + 18, 850), (cx - 10, 887)], CHAR_HI, 3)


def state_one_extra_scars(draw):
    # Broaden the painted soot and broken plating so the intermediate pose reads
    # clearly at phone scale, while leaving the bolt and wing structure legible.
    for pts in [
        [(238, 519), (271, 504), (309, 516), (326, 548), (306, 570),
         (274, 559), (252, 580), (230, 557)],
        [(786, 519), (753, 504), (715, 516), (698, 548), (718, 570),
         (750, 559), (772, 580), (794, 557)],
    ]:
        poly(draw, pts, CHAR)
    line(draw, [(246, 526), (274, 520), (304, 536)], CHAR_HI, 4)
    line(draw, [(778, 526), (750, 520), (720, 536)], CHAR_HI, 4)
    line(draw, [(257, 548), (282, 535), (306, 553)], AMBER, 3)
    line(draw, [(767, 548), (742, 535), (718, 553)], AMBER, 3)
    for pts in [[(404, 506), (424, 494), (445, 505), (434, 526), (410, 532)],
                [(620, 506), (600, 494), (579, 505), (590, 526), (614, 532)]]:
        poly(draw, pts, NAVY)
        line(draw, [pts[0], pts[2], pts[4]], STEEL_HI, 2)


def state_one(base: Image.Image) -> Image.Image:
    im = base.copy()
    d = ImageDraw.Draw(im)
    left_pod_cavity(d)
    scorch_wings_and_spine(d)
    scorched_nozzles(d)
    state_one_extra_scars(d)
    # Partially scorched left bolt, preserving readable red hot segments.
    poly(d, [(126, 645), (163, 619), (194, 584), (214, 595), (185, 625),
             (207, 624), (177, 656), (147, 671)], CHAR)
    line(d, [(137, 648), (164, 633), (190, 603)], RED_HI, 4)
    line(d, [(161, 653), (180, 634)], RED, 4)
    return im


def canopy_cracks(draw):
    # Bone-white fracture network over the existing cyan canopy.
    line(draw, [(505, 215), (500, 258), (511, 286), (501, 326), (514, 357)], AMBER_HI, 3)
    line(draw, [(501, 270), (475, 293), (481, 324)], AMBER_HI, 3)
    line(draw, [(508, 287), (536, 309), (531, 343)], AMBER_HI, 3)
    line(draw, [(503, 337), (486, 368), (500, 397)], AMBER_HI, 3)
    specks(draw, [(470, 290), (540, 307), (496, 372), (516, 257)], AMBER_HI, 2)


def right_tip_breach(draw):
    # Jagged bite inside the right wingtip, with a lit orange inner rim.
    poly(draw, [(856, 741), (886, 718), (921, 733), (949, 770), (930, 804),
                (900, 792), (879, 809), (856, 788)], INK)
    poly(draw, [(866, 748), (891, 732), (917, 744), (937, 770), (922, 787),
                (902, 778), (882, 798), (867, 783)], CHAR)
    line(draw, [(862, 744), (889, 728), (920, 740), (942, 769)], AMBER, 5)
    line(draw, [(876, 790), (900, 774), (923, 786)], AMBER_HI, 2)
    specks(draw, [(911, 752), (928, 774), (887, 756), (907, 796)], AMBER_HI, 2)


def right_nose_breach(draw):
    # Bent, burning right pod nose; stays inside the unchanged external outline.
    poly(draw, [(715, 308), (747, 288), (779, 309), (791, 352), (774, 386),
                (744, 378), (727, 398), (706, 374)], INK)
    poly(draw, [(728, 315), (750, 303), (771, 316), (779, 349), (761, 367),
                (744, 358), (730, 377), (718, 360)], CHAR)
    line(draw, [(718, 350), (742, 332), (763, 348), (783, 335)], AMBER, 5)
    line(draw, [(736, 316), (752, 341), (739, 364)], AMBER_HI, 2)
    line(draw, [(754, 306), (762, 328), (780, 348)], RED_HI, 3)
    specks(draw, [(718, 321), (784, 360), (746, 383), (770, 316)], AMBER_HI, 2)


def left_wing_breach(draw):
    poly(draw, [(122, 738), (165, 715), (213, 728), (241, 766), (219, 807),
                (185, 798), (159, 824), (126, 805)], INK)
    poly(draw, [(138, 744), (166, 729), (202, 738), (226, 765), (210, 790),
                (185, 781), (161, 807), (141, 794)], NAVY)
    # Exposed machinery plates and ribs.
    poly(draw, [(145, 754), (164, 742), (177, 756), (169, 782), (150, 790)], STEEL)
    poly(draw, [(187, 742), (208, 754), (217, 774), (202, 787), (189, 771)], GOLD)
    line(draw, [(145, 748), (170, 772), (155, 799)], STEEL_HI, 3)
    line(draw, [(178, 739), (187, 764), (210, 785)], AMBER, 3)
    line(draw, [(151, 766), (185, 753), (207, 770)], WIRE_CYAN, 2)
    specks(draw, [(132, 764), (218, 757), (170, 806), (202, 796)], AMBER_HI, 2)


def center_spine(draw):
    poly(draw, [(480, 470), (505, 456), (532, 466), (546, 526), (532, 610),
                (520, 704), (502, 758), (484, 702), (472, 610), (462, 526)], INK)
    poly(draw, [(485, 482), (504, 474), (525, 480), (535, 528), (521, 602),
                (511, 688), (501, 719), (491, 683), (480, 604), (471, 528)], NAVY)
    for x in (480, 493, 516, 529):
        line(draw, [(x, 492), (x + 4, 548), (x - 3, 607), (x + 4, 675)], STEEL, 4)
    line(draw, [(491, 484), (508, 532), (494, 593), (514, 650), (503, 706)], AMBER, 3)
    line(draw, [(520, 490), (503, 552), (519, 612), (499, 677)], WIRE_CYAN, 3)
    line(draw, [(470, 522), (486, 546), (478, 590)], AMBER_HI, 2)
    specks(draw, [(467, 534), (538, 521), (492, 617), (521, 642), (500, 717)], AMBER_HI, 2)


def heavy_burns(draw):
    for pts in [
        [(248, 600), (287, 581), (326, 594), (306, 626), (274, 642)],
        [(776, 592), (739, 580), (704, 598), (722, 628), (755, 638)],
        [(328, 758), (360, 743), (389, 761), (373, 788), (340, 797)],
        [(692, 758), (662, 743), (634, 763), (649, 789), (680, 798)],
    ]:
        poly(draw, pts, CHAR)
    for pts in [
        [(255, 610), (278, 598), (302, 606)], [(769, 608), (746, 598), (722, 610)],
        [(338, 771), (360, 758), (379, 769)], [(684, 770), (663, 757), (644, 771)],
    ]:
        line(draw, pts, CHAR_HI, 4)
        specks(draw, [pts[0], pts[-1]], AMBER, 2)


def wreck_field_scars(draw):
    # Large, irregular fields of missing plating. Each field contains a few
    # surviving ribs/highlights so it reads as a cavity rather than a sticker.
    fields = [
        ([(185, 566), (221, 545), (267, 552), (302, 588), (286, 631),
          (245, 646), (210, 622)], [(200, 580), (235, 590), (270, 574), (289, 603)]),
        ([(839, 566), (803, 545), (757, 552), (722, 588), (738, 631),
          (779, 646), (814, 622)], [(824, 580), (789, 590), (754, 574), (735, 603)]),
        ([(190, 792), (226, 758), (273, 762), (312, 800), (290, 842),
          (248, 852), (210, 830)], [(208, 798), (245, 785), (278, 811), (290, 833)]),
        ([(834, 792), (798, 758), (751, 762), (712, 800), (734, 842),
          (776, 852), (814, 830)], [(816, 798), (779, 785), (746, 811), (734, 833)]),
        ([(452, 744), (480, 724), (509, 739), (532, 779), (522, 836),
          (501, 866), (474, 835), (462, 789)], [(470, 754), (490, 777), (514, 760), (520, 816)]),
    ]
    for pts, ribs in fields:
        poly(draw, pts, CHAR)
        line(draw, ribs, NAVY, 9)
        line(draw, [(x + 4, y - 4) for x, y in ribs], STEEL, 3)
        line(draw, [(x - 5, y + 4) for x, y in ribs[::2]], AMBER, 3)
        specks(draw, [pts[1], pts[3], pts[-1]], AMBER_HI, 2)
    # Additional darkened plate facets across both outer pods.
    for pts in [
        [(300, 590), (339, 570), (372, 594), (363, 661), (330, 681), (305, 652)],
        [(724, 590), (685, 570), (652, 594), (661, 661), (694, 681), (719, 652)],
        [(303, 690), (337, 674), (368, 697), (358, 752), (327, 767), (306, 740)],
        [(721, 690), (687, 674), (656, 697), (666, 752), (697, 767), (718, 740)],
    ]:
        poly(draw, pts, NAVY)
        line(draw, [pts[0], pts[2], pts[4]], CHAR_HI, 5)
        line(draw, [pts[1], pts[3], pts[5]], STEEL_HI, 2)
    # Split the centre lower plate into exposed cross-ribs.
    for y in (772, 798, 824, 850):
        line(draw, [(472, y), (533, y + 6)], STEEL_HI, 3)
        line(draw, [(477, y + 6), (528, y)], AMBER, 2)


def sparks(draw):
    for x, y, col in [(249, 440, AMBER_HI), (260, 430, CYAN), (293, 536, AMBER),
                      (760, 406, AMBER_HI), (791, 416, CYAN), (831, 510, AMBER),
                      (225, 817, AMBER_HI), (814, 823, AMBER_HI), (544, 730, AMBER)]:
        line(draw, [(x - 5, y), (x + 5, y)], col, 2)
        line(draw, [(x, y - 5), (x, y + 5)], col, 2)


def state_two(base: Image.Image) -> Image.Image:
    im = state_one(base)
    d = ImageDraw.Draw(im)
    canopy_cracks(d)
    right_tip_breach(d)
    right_nose_breach(d)
    left_wing_breach(d)
    center_spine(d)
    heavy_burns(d)
    wreck_field_scars(d)
    sparks(d)
    # A few exposed gold/steel fasteners around the new cavities.
    specks(d, [(267, 545), (340, 534), (696, 529), (775, 542), (119, 720), (908, 710)], STEEL_HI, 3)
    return im


def clip_to_mask(im: Image.Image, base: Image.Image, mask: Image.Image) -> Image.Image:
    # Damage art never changes background pixels, even where a jagged mark reaches an edge.
    return Image.composite(im, base, mask)


def main():
    base, mask = harden_candidate()
    s1 = clip_to_mask(state_one(base), base, mask)
    s2 = clip_to_mask(state_two(base), base, mask)
    base.save(OUT / "key_state0.png")
    s1.save(OUT / "key_state1.png")
    s2.save(OUT / "key_state2.png")

    preview = Image.new("RGB", (W * 3, H), BG)
    preview_mask = Image.eval(mask, lambda p: 255 if p else 0)
    for i, im in enumerate((base, s1, s2)):
        # Display green-keyed art on a dark review background.
        keyed = Image.new("RGB", im.size, BG)
        keyed.paste(im, mask=preview_mask)
        preview.paste(keyed, (i * W, 0))
    preview.save(OUT / "preview_j1.png")


if __name__ == "__main__":
    main()
