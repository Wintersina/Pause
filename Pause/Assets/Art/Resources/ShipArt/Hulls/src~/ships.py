"""The fifteen roster hulls, redrawn in the Akira flat-cel style.

Ids and keys follow Scripts/Ship/ShipId.cs (roster index 1..15). Each ship
keeps its name, its silhouette family and its own identity hue; red stays the
starter's colour and the shared friendly accent (lights, trim) on every hull.

Coordinates are a rough 128 u design space, nose up. build.py fits each ship
to the canvas and to its old sprite's aspect ratio (so the in-game collider
and size stay put), then renders every frame.
"""
from hullkit import *  # noqa: F401,F403

# identity hue per ship: (base, one shadow, one highlight)
HUES = {
    "NeonComet":   (RED, RED_SH, RED_HI),
    "VoltViper":   ("#2E9BE6", "#174C8C", "#8FD3FF"),
    "SolarFang":   ("#F07A1E", "#9A3E12", "#FFC27A"),
    "CrimsonHalo": ("#C8285E", "#6E1136", "#FF7AA2"),
    "IonLancer":   ("#3A5BE0", "#1C2A80", "#9AB0FF"),
    "JadePhantom": ("#22B07A", "#0E5A44", "#8EF0C4"),
    "GoldWarden":  ("#D99A1A", "#7E4E0C", "#FFD36A"),
    "Lightning":   ("#EEDC32", "#8E8414", "#FFF6A8"),
    "Ligher":      ("#C8662E", "#6E3014", "#F2A472"),
    "Paranoid":    ("#5DBB3A", "#2A5E1E", "#B6EE8A"),
    "Ninja":       ("#4A58C8", "#232A6E", "#9FA8F0"),
    "Saboteur":    ("#B83CC0", "#5E1A66", "#E89AF0"),
    "UFO":         ("#7A52DC", "#3A2478", "#BBA4FF"),
    "Dove":        ("#36C2C2", "#146A70", "#A6F2EE"),
    "Turtle":      ("#A87A3E", "#5A3A18", "#E2BE84"),
}

HUE_NAMES = {
    "NeonComet": "Kaneda red", "VoltViper": "volt azure", "SolarFang": "solar orange",
    "CrimsonHalo": "crimson rose", "IonLancer": "ion cobalt", "JadePhantom": "jade",
    "GoldWarden": "warden gold", "Lightning": "lightning lemon", "Ligher": "copper",
    "Paranoid": "leaf green", "Ninja": "night indigo", "Saboteur": "orchid", "UFO": "violet",
    "Dove": "aqua", "Turtle": "shell tan",
}

# the old sprite rects (px at PPU 100) every new hull must keep the aspect and
# world size of: shopingShips.LoadRuntimeSprite / OriginalShipArt, before.
OLD_RECTS = {
    "NeonComet": (32, 29), "VoltViper": (28, 23), "SolarFang": (29, 28), "CrimsonHalo": (46, 57),
    "IonLancer": (47, 55), "JadePhantom": (56, 55), "GoldWarden": (51, 55), "Lightning": (28, 27),
    "Ligher": (20, 29), "Paranoid": (30, 22), "Ninja": (30, 30), "Saboteur": (24, 30),
    "UFO": (26, 26), "Dove": (20, 24), "Turtle": (22, 29),
}

ORDER = ["NeonComet", "VoltViper", "SolarFang", "CrimsonHalo", "IonLancer", "JadePhantom",
         "GoldWarden", "Lightning", "Ligher", "Paranoid", "Ninja", "Saboteur", "UFO", "Dove",
         "Turtle"]

TWIN = 0.72
SPINNERS = {"Ninja", "UFO"}   # ShipExhaust.UsesWind: they spin, no bank pose


def both(form_fn):
    """A left-side form and its mirror."""
    return [form_fn(False), form_fn(True)]


def side(points, flip):
    return mxs(points) if flip else P(points)


def neon_comet():
    # the approved sample dart (docs/art-samples player_ship), split into forms
    hull = sym([(64, 4), (59, 18), (56, 36), (53, 50), (46, 57), (12, 86), (8, 98),
                (18, 101), (42, 95), (44, 104), (47, 116), (58, 116), (60, 108), (64, 110)])
    pod = lambda fl: Form(side([(44, 94), (46, 104), (47, 116), (58, 116), (60, 108), (58, 92), (50, 88)], fl),
                          "gun", shade=[right_of(54)], kick=1.6, rim=2.2, name="pod")
    body = Form(hull, "hull", shade=[right_of(65, 30), [(12, 86), (8, 98), (18, 101), (42, 95), (40, 88)],
                                     [(116, 86), (120, 98), (110, 101), (86, 95), (82, 82), (100, 75)]],
                lines=[([(64, 54), (64, 104)], PANEL), ([(53, 50), (52, 88)], PANEL), ([(75, 50), (76, 88)], PANEL),
                       ([(30, 68), (38, 80)], DETAIL), ([(98, 68), (90, 80)], DETAIL)], name="hull")
    stripes = [([(24, 81), (30, 77), (38, 93), (31, 95)], "bone"), (mxs([(24, 81), (30, 77), (38, 93), (31, 95)]), "bone"),
               ([(61, 8), (67, 8), (65.5, 14), (62.5, 14)], "bone")]
    return Ship("NeonComet", [body, pod(False), pod(True)],
                canopy=[(64, 22), (59, 34), (59, 48), (64, 54), (69, 48), (69, 34)],
                lights=[(11, 95, 3.0, "red"), (117, 95, 3.0, "red"), (64, 62, 2.2, "amber")],
                nozzles=[(52.5, 116, 5, TWIN), (75.5, 116, 5, TWIN)], stripes=stripes)


def volt_viper():
    wing = lambda fl: Form(side([(42, 36), (12, 50), (4, 64), (8, 72), (24, 70), (42, 66)], fl), "hull",
                           shade=[below(62)], name="wing", lines=[(side([(14, 60), (34, 54)], fl), DETAIL)])
    leg = lambda fl: Form(side([(45, 78), (59, 78), (59, 100), (56, 104), (48, 104), (45, 100)], fl), "gun",
                          shade=[right_of(56) if not fl else right_of(80)], kick=1.6, rim=2.2, name="leg")
    body = Form(sym([(64, 10), (52, 10), (44, 18), (40, 30), (40, 70), (44, 82), (52, 88), (64, 90)]), "hull",
                shade=[right_of(66), below(80)],
                lines=[([(44, 46), (84, 46)], PANEL), ([(64, 46), (64, 86)], PANEL),
                       ([(46, 60), (56, 60)], DETAIL), ([(72, 60), (82, 60)], DETAIL)], name="hull")
    bolt = [(18, 56), (28, 53), (24, 60), (34, 58), (20, 67), (24, 61), (14, 63)]
    stripes = [(bolt, "bone"), (mxs(bolt), "bone"),
               ([(50, 16), (78, 16), (75, 20), (53, 20)], "red")]
    return Ship("VoltViper", [wing(False), wing(True), leg(False), leg(True), body],
                canopy=[(64, 18), (55, 24), (54, 38), (60, 42), (68, 42), (74, 38), (73, 24)],
                lights=[(8, 66, 3.0, "red"), (120, 66, 3.0, "red"), (64, 76, 2.2, "amber")],
                nozzles=[(52, 103, 5, TWIN), (76, 103, 5, TWIN)], stripes=stripes)


def solar_fang():
    fang = lambda fl: Form(side([(22, 96), (14, 56), (20, 54), (34, 88)], fl), "gun",
                           shade=[right_of(18) if not fl else right_of(110)], kick=1.4, rim=2.0, name="fang")
    body = Form(sym([(64, 6), (58, 22), (46, 46), (28, 74), (12, 100), (8, 110), (24, 110), (36, 102),
                     (46, 106), (56, 100), (64, 108)]), "hull",
                shade=[right_of(65), [(8, 110), (24, 110), (36, 102), (30, 96)]],
                details=[(ngon(64, 72, 15, 8, 22.5), "amber", 2.5), (ngon(64, 72, 8, 8, 22.5), "sodium", 1.5)],
                lines=[([(46, 50), (40, 92)], PANEL), ([(82, 50), (88, 92)], PANEL),
                       ([(52, 92), (76, 92)], DETAIL)], name="hull")
    stripes = [([(20, 98), (34, 84), (38, 88), (26, 102)], "red"), (mxs([(20, 98), (34, 84), (38, 88), (26, 102)]), "red")]
    return Ship("SolarFang", [fang(False), fang(True), body],
                canopy=[(64, 24), (59, 34), (59, 46), (64, 52), (69, 46), (69, 34)],
                lights=[(14, 104, 3.0, "red"), (114, 104, 3.0, "red")],
                nozzles=[(64, 106, 6, 1.0)], stripes=stripes)


def _arc(cx, cy, r0, r1, a0, a1, n=4):
    outer = [(cx + r1 * math.cos(math.radians(a)), cy + r1 * math.sin(math.radians(a)))
             for a in [a0 + (a1 - a0) * i / n for i in range(n + 1)]]
    inner = [(cx + r0 * math.cos(math.radians(a)), cy + r0 * math.sin(math.radians(a)))
             for a in [a1 - (a1 - a0) * i / n for i in range(n + 1)]]
    return outer + inner


def crimson_halo():
    wing = lambda fl: Form(side([(57, 44), (30, 70), (12, 88), (12, 96), (36, 90), (56, 84)], fl), "hull",
                           shade=[below(84)] + ([right_of(0)] if fl else []), name="wing",
                           lines=[(side([(24, 84), (44, 70)], fl), DETAIL)])
    tail = lambda fl: Form(side([(56, 94), (40, 106), (38, 114), (56, 108)], fl), "hull",
                           shade=[right_of(0)] if fl else [], name="tail")
    halo = lambda fl: Form(side(_arc(64, 40, 17, 23, 110, 250, 4), fl), "bone", kick=1.2, rim=1.8, ink=2.0,
                           name="halo")
    body = Form(sym([(64, 2), (61, 14), (58, 40), (56, 70), (54, 98), (56, 112), (64, 116)]), "hull",
                shade=[right_of(65)], lines=[([(64, 56), (64, 108)], PANEL)], name="hull")
    stripes = [([(58, 74), (70, 74), (70, 78), (58, 78)], "red"), ([(58, 82), (70, 82), (70, 85), (58, 85)], "red")]
    return Ship("CrimsonHalo", [wing(False), wing(True), tail(False), tail(True), body, halo(False), halo(True)],
                canopy=[(64, 24), (60, 32), (60, 46), (64, 52), (68, 46), (68, 32)],
                lights=[(13, 92, 2.8, "red"), (115, 92, 2.8, "red"), (64, 64, 2.0, "amber")],
                nozzles=[(59, 114, 3.6, TWIN), (69, 114, 3.6, TWIN)], stripes=stripes)


def ion_lancer():
    wing = lambda fl: Form(side([(57, 50), (22, 84), (10, 106), (20, 108), (36, 96), (56, 90)], fl), "hull",
                           shade=[below(92)] + ([right_of(0)] if fl else []), name="wing",
                           lines=[(side([(22, 96), (48, 72)], fl), DETAIL)])
    canard = lambda fl: Form(side([(60, 28), (46, 40), (46, 45), (59, 40)], fl), "hull",
                             shade=[right_of(0)] if fl else [], kick=1.2, rim=1.6, ink=2.0, name="canard")
    pod = lambda fl: Form(side([(50, 82), (58, 82), (59, 106), (57, 110), (51, 110), (49, 106)], fl), "gun",
                          shade=[right_of(55) if not fl else right_of(79)], kick=1.4, rim=2.0, name="pod")
    body = Form(sym([(64, 0), (62.5, 18), (60, 34), (57, 52), (56, 86), (58, 102), (64, 104)]), "hull",
                shade=[right_of(65)], lines=[([(64, 58), (64, 100)], PANEL)], name="hull")
    stripes = [([(62.5, 6), (65.5, 6), (65, 18), (63, 18)], "bone"),
               ([(24, 98), (30, 92), (34, 95), (28, 101)], "red"), (mxs([(24, 98), (30, 92), (34, 95), (28, 101)]), "red")]
    return Ship("IonLancer", [wing(False), wing(True), canard(False), canard(True), pod(False), pod(True), body],
                canopy=[(64, 30), (60.5, 38), (60.5, 50), (64, 56), (67.5, 50), (67.5, 38)],
                lights=[(12, 104, 2.8, "red"), (116, 104, 2.8, "red")],
                nozzles=[(54, 109, 4, TWIN), (74, 109, 4, TWIN)], stripes=stripes)


def jade_phantom():
    wing = lambda fl: Form(side([(58, 46), (34, 48), (16, 42), (6, 26), (4, 44), (8, 62), (18, 78), (24, 74),
                                 (32, 90), (40, 84), (57, 92)], fl), "hull",
                           shade=[below(76)] + ([right_of(0)] if fl else []), name="wing",
                           lines=[(side([(14, 50), (30, 62), (52, 66)], fl), DETAIL),
                                  (side([(22, 58), (26, 74)], fl), DETAIL)])
    body = Form(sym([(64, 8), (60, 20), (58, 48), (57, 92), (60, 104), (64, 106)]), "hull",
                shade=[right_of(65)], lines=[([(64, 58), (64, 100)], PANEL)], name="hull")
    stripes = [(side([(8, 34), (12, 46), (9, 46)], False), "red"), (mxs([(8, 34), (12, 46), (9, 46)]), "red"),
               ([(60, 70), (68, 70), (68, 74), (60, 74)], "bone")]
    return Ship("JadePhantom", [wing(False), wing(True), body],
                canopy=[(64, 22), (60.5, 30), (60.5, 46), (64, 52), (67.5, 46), (67.5, 30)],
                lights=[(7, 30, 2.6, "red"), (121, 30, 2.6, "red"), (64, 84, 2.0, "amber")],
                nozzles=[(59.5, 104, 3.6, TWIN), (68.5, 104, 3.6, TWIN)], stripes=stripes)


def gold_warden():
    wing = lambda fl: Form(side([(54, 38), (34, 44), (16, 60), (12, 82), (24, 86), (40, 78), (54, 76)], fl), "hull",
                           shade=[below(76)] + ([right_of(0)] if fl else []), name="wing",
                           lines=[(side([(20, 70), (44, 56)], fl), DETAIL)])
    gun = lambda fl: Form(side([(25, 30), (29, 38), (29, 74), (21, 74), (21, 38)], fl), "gun",
                          shade=[right_of(26) if not fl else right_of(103)], kick=1.4, rim=2.0, name="gun")
    eng = lambda fl: Form(side([(40, 80), (50, 80), (50, 98), (47, 102), (43, 102), (40, 98)], fl), "gun",
                          shade=[right_of(46) if not fl else right_of(83)], kick=1.4, rim=2.0, name="engine")
    body = Form(sym([(64, 4), (58, 14), (55, 30), (53, 72), (55, 98), (59, 110), (64, 110)]), "hull",
                shade=[right_of(65)],
                lines=[([(64, 56), (64, 104)], PANEL), ([(54, 40), (74, 40)], DETAIL), ([(54, 70), (74, 70)], DETAIL)],
                name="hull")
    stripes = [([(58, 76), (70, 76), (64, 84)], "red"), ([(58, 86), (70, 86), (64, 94)], "red")]
    return Ship("GoldWarden", [wing(False), wing(True), eng(False), eng(True), gun(False), gun(True), body],
                canopy=[(64, 18), (59, 28), (59, 46), (64, 52), (69, 46), (69, 28)],
                lights=[(14, 80, 2.8, "red"), (114, 80, 2.8, "red")],
                nozzles=[(64, 109, 5, 1.0), (45, 101, 3.6, .5), (83, 101, 3.6, .5)], stripes=stripes)


def lightning():
    wing = lambda fl: Form(side([(56, 30), (36, 34), (14, 62), (6, 86), (20, 92), (36, 86), (56, 80)], fl), "hull",
                           shade=[below(82)] + ([right_of(0)] if fl else []), name="wing")
    pod = lambda fl: Form(side([(42, 6), (48, 20), (48, 98), (45, 104), (39, 104), (36, 98), (36, 20)], fl), "hull",
                          shade=[right_of(43) if not fl else right_of(86)], kick=1.8, rim=2.4, name="pod",
                          lines=[(side([(36, 40), (48, 40)], fl), DETAIL), (side([(36, 84), (48, 84)], fl), DETAIL)])
    body = Form(sym([(64, 2), (58, 16), (56, 60), (58, 88), (64, 92)]), "hull", shade=[right_of(65)],
                lines=[([(64, 54), (64, 88)], PANEL)], name="hull")
    bolt = [(22, 58), (32, 54), (26, 66), (34, 64), (16, 82), (22, 70), (14, 72)]
    stripes = [(bolt, "red"), (mxs(bolt), "red")]
    return Ship("Lightning", [wing(False), wing(True), pod(False), pod(True), body],
                canopy=[(64, 20), (60, 28), (60, 42), (64, 48), (68, 42), (68, 28)],
                lights=[(8, 84, 2.8, "red"), (120, 84, 2.8, "red"), (64, 74, 2.0, "amber")],
                nozzles=[(42, 103, 4.5, TWIN), (86, 103, 4.5, TWIN)], stripes=stripes)


def ligher():
    fin = lambda fl: Form(side([(44, 70), (30, 92), (32, 100), (46, 92)], fl), "hull",
                          shade=[right_of(0)] if fl else [], kick=1.4, rim=2.0, name="fin")
    cap = Form([(64, 0), (57, 12), (64, 20), (71, 12)], "teal", kick=1.4, rim=2.0, ink=2.0, name="cap")
    body = Form(sym([(64, 8), (56, 18), (46, 42), (40, 64), (44, 86), (54, 102), (60, 112), (64, 112)]), "hull",
                shade=[right_of(65), below(98)],
                lines=[([(46, 64), (82, 64)], PANEL), ([(64, 64), (64, 106)], PANEL),
                       ([(52, 30), (60, 26)], DETAIL), ([(76, 30), (68, 26)], DETAIL)], name="hull")
    stripes = [([(48, 72), (58, 72), (58, 76), (47, 76)], "red"), (mxs([(48, 72), (58, 72), (58, 76), (47, 76)]), "red")]
    return Ship("Ligher", [fin(False), fin(True), body, cap],
                canopy=[(64, 30), (57, 42), (64, 56), (71, 42)],
                lights=[(33, 95, 2.4, "red"), (95, 95, 2.4, "red"), (64, 88, 2.0, "amber")],
                nozzles=[(64, 111, 4.5, 1.0)], stripes=stripes)


def paranoid():
    pod = lambda fl: Form(side([(10, 24), (22, 24), (25, 32), (25, 82), (21, 90), (11, 90), (7, 82), (7, 32)], fl),
                          "hull", shade=[right_of(16) if not fl else right_of(112), below(80)], name="pod",
                          lines=[(side([(7, 44), (25, 44)], fl), DETAIL), (side([(7, 70), (25, 70)], fl), DETAIL)])
    strut = lambda fl: Form(side([(24, 46), (40, 44), (40, 66), (24, 64)], fl), "gun",
                            shade=[below(58)], kick=1.2, rim=1.8, name="strut")
    disc = ngon(64, 57, 36, 12, 15, sx=1.0, sy=0.98)
    body = Form(disc, "hull", shade=[right_of(68), below(80)], name="hull",
                details=[(ngon(64, 57, 24, 12, 15), "hull_sh", 2.0)],
                lines=[([(36, 34), (46, 42)], DETAIL), ([(92, 34), (82, 42)], DETAIL),
                       ([(36, 80), (46, 72)], DETAIL), ([(92, 80), (82, 72)], DETAIL)])
    stripes = [([(10, 30), (22, 30), (24, 36), (8, 36)], "red"), (mxs([(10, 30), (22, 30), (24, 36), (8, 36)]), "red")]
    return Ship("Paranoid", [strut(False), strut(True), pod(False), pod(True), body],
                canopy=[(64, 42), (54, 50), (54, 62), (64, 70), (74, 62), (74, 50)],
                lights=[(64, 26, 2.4, "red"), (64, 88, 2.2, "amber")],
                nozzles=[(16, 89, 5, TWIN), (112, 89, 5, TWIN)], stripes=stripes)


def ninja():
    blade = star(64, 64, 60, 27, 4, rot=0)
    body = Form(blade, "hull", shade=[[(64, 64), (124, 64), (64, 124)], [(64, 64), (64, 4), (77, 51)]],
                name="shuriken",
                lines=[([(64, 64), (64, 14)], DETAIL), ([(64, 64), (114, 64)], DETAIL),
                       ([(64, 64), (64, 114)], DETAIL), ([(64, 64), (14, 64)], DETAIL)])
    ring = Form(ngon(64, 64, 20, 8, 22.5), "gun", shade=[right_of(64)], kick=1.6, rim=2.0, name="hub")
    stripes = [([(64, 10), (67, 22), (61, 22)], "red"), ([(64, 118), (61, 106), (67, 106)], "red")]
    return Ship("Ninja", [body, ring],
                canopy=[(64, 54), (54, 64), (64, 74), (74, 64)],
                lights=[(14, 64, 2.4, "red"), (114, 64, 2.4, "red")],
                nozzles=[(64, 104, 0, 1.0)], stripes=stripes, bank=False)


def saboteur():
    horn = lambda fl: Form(side([(30, 4), (38, 6), (46, 40), (50, 58), (40, 64), (30, 46), (26, 22)], fl), "hull",
                           shade=[right_of(38) if not fl else right_of(98)], name="horn",
                           lines=[(side([(32, 24), (40, 22)], fl), DETAIL)])
    cradle = Form([(30, 46), (40, 64), (48, 80), (64, 88), (80, 80), (88, 64), (98, 46), (102, 62), (94, 88),
                   (74, 100), (54, 100), (34, 88), (26, 62)], "hull", shade=[right_of(66), below(90)], name="cradle")
    tail = Form([(58, 96), (70, 96), (68, 110), (64, 116), (60, 110)], "gun", shade=[right_of(64)],
                kick=1.4, rim=1.8, name="tail")
    core = Form(ngon(64, 64, 21, 8, 22.5), "hull", shade=[right_of(66)], name="core",
                details=[(ngon(64, 64, 13, 8, 22.5), "hull_sh", 2.0)])
    stripes = [([(31, 8), (37, 9), (39, 16), (30, 15)], "red"), (mxs([(31, 8), (37, 9), (39, 16), (30, 15)]), "red")]
    return Ship("Saboteur", [tail, horn(False), horn(True), cradle, core],
                canopy=[(64, 55), (57, 62), (57, 68), (64, 74), (71, 68), (71, 62)],
                lights=[(34, 80, 2.4, "red"), (94, 80, 2.4, "red")],
                nozzles=[(64, 113, 3.4, 1.0)], stripes=stripes)


def ufo():
    disc = ngon(64, 64, 60, 12, 15)
    inner = ngon(64, 64, 40, 12, 15)
    body = Form(disc, "hull", shade=[right_of(70), below(90)], name="disc",
                details=[(inner, "hull_sh", 2.0)],
                lines=[([disc[k], inner[k]], DETAIL) for k in range(0, 12, 2)])
    hub = Form(ngon(64, 64, 22, 6, 0), "gun", shade=[right_of(64)], kick=1.6, rim=2.0, name="hub")
    lights = []
    for k in range(6):
        a = math.radians(k * 60 - 60)
        lights.append((64 + 50 * math.cos(a), 64 + 50 * math.sin(a), 2.6, "red" if k % 2 == 0 else "amber"))
    return Ship("UFO", [body, hub], canopy=[(64, 52), (54, 58), (54, 70), (64, 76), (74, 70), (74, 58)],
                lights=lights, nozzles=[(64, 104, 0, 1.0)], stripes=[], bank=False)


def dove():
    wing = lambda fl: Form(side([(38, 38), (20, 50), (16, 64), (24, 66), (40, 58)], fl), "hull",
                           shade=[below(58)] + ([right_of(0)] if fl else []), kick=1.6, rim=2.2, name="wing")
    tail = Form([(56, 94), (64, 90), (72, 94), (74, 110), (64, 116), (54, 110)], "gun", shade=[right_of(64)],
                kick=1.4, rim=2.0, name="tail")
    body = Form(sym([(64, 4), (48, 8), (38, 20), (34, 42), (40, 70), (52, 90), (58, 98), (64, 100)]), "hull",
                shade=[right_of(65), below(86)],
                lines=[([(64, 60), (64, 96)], PANEL), ([(40, 40), (52, 48)], DETAIL), ([(88, 40), (76, 48)], DETAIL)],
                name="hull")
    stripes = [([(48, 70), (58, 76), (58, 80), (50, 76)], "red"), (mxs([(48, 70), (58, 76), (58, 80), (50, 76)]), "red")]
    return Ship("Dove", [wing(False), wing(True), tail, body],
                canopy=[(64, 18), (56, 26), (56, 44), (64, 52), (72, 44), (72, 26)],
                lights=[(19, 61, 2.4, "red"), (109, 61, 2.4, "red")],
                nozzles=[(64, 114, 4, 1.0)], stripes=stripes)


def turtle():
    flip = lambda pts, fl: Form(side(pts, fl), "gun", shade=[below(0)] if fl else [], kick=1.2, rim=1.8, ink=2.0,
                                name="flipper")
    shell = Form(sym([(64, 6), (50, 9), (40, 20), (35, 46), (37, 76), (45, 96), (56, 106), (64, 108)]), "hull",
                 shade=[right_of(66), below(94)], name="shell",
                 details=[(ngon(64, 46, 11, 6, 30), "hull_hi", 1.8), (ngon(64, 80, 10, 6, 30), "hull_hi", 1.8)],
                 lines=[([(53, 46), (40, 36)], PANEL), ([(75, 46), (88, 36)], PANEL),
                        ([(53, 46), (38, 60)], PANEL), ([(75, 46), (90, 60)], PANEL),
                        ([(55, 80), (40, 74)], PANEL), ([(73, 80), (88, 74)], PANEL),
                        ([(64, 57), (64, 70)], PANEL), ([(58, 88), (50, 98)], DETAIL), ([(70, 88), (78, 98)], DETAIL)])
    fr = [(38, 26), (26, 18), (24, 28), (36, 36)]
    bk = [(38, 80), (26, 92), (32, 96), (42, 88)]
    stripes = [([(58, 9), (70, 9), (68, 14), (60, 14)], "red")]
    return Ship("Turtle", [flip(fr, False), flip(fr, True), flip(bk, False), flip(bk, True), shell],
                canopy=[(64, 20), (57, 26), (57, 32), (64, 36), (71, 32), (71, 26)],
                lights=[(26, 23, 2.2, "red"), (102, 23, 2.2, "red"), (64, 64, 2.0, "amber")],
                nozzles=[(64, 107, 4, 1.0)], stripes=stripes)


BUILDERS = {
    "NeonComet": neon_comet, "VoltViper": volt_viper, "SolarFang": solar_fang, "CrimsonHalo": crimson_halo,
    "IonLancer": ion_lancer, "JadePhantom": jade_phantom, "GoldWarden": gold_warden, "Lightning": lightning,
    "Ligher": ligher, "Paranoid": paranoid, "Ninja": ninja, "Saboteur": saboteur, "UFO": ufo, "Dove": dove,
    "Turtle": turtle,
}
