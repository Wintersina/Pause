"""Per-skin damage designs, batch C: Ninja, Saboteur, UFO, Dove, Turtle.

Loaded by damage.py into damage.SKIN_D (see the per-skin block there). Plain
data only (no import of damage: this module runs while damage.py is still
loading). Same feature kinds, form refs and design-space coordinates as
damage.D; row 2 adds to row 1; the emitters slot stays None (the game's
emitter table is per ship), so every skin keeps some damage on its ship's
emitter spots (see SKIN_CONCEPTS for where the main damage sits).

Readability per palette, with the shared feature kinds:
  Night  (indigo, cyan kick)   soot would be dark on navy, so no scorch at all:
                               big tears (hot sodium rim, cyan curled lip),
                               amber-glow panels and cracks (cyan kick).
  Sodium (orange, amber kick)  soot and bends read as dark on orange; panels
                               glow cyan against it.
  Bone   (off-white)           ink cracks and soot read hardest; panels are a
                               dark hole with an amber glow.
  Special (stock hue + livery) a damage story tied to the skin's name.
"""


def _slash(a, b, w):
    """A thin straight cut from a to b, w wide (a parallelogram)."""
    dx, dy = b[0] - a[0], b[1] - a[1]
    ln = (dx * dx + dy * dy) ** .5
    nx, ny = -dy / ln * w / 2, dx / ln * w / 2
    return [(a[0] - nx, a[1] - ny), (b[0] - nx, b[1] - ny), (b[0] + nx, b[1] + ny), (a[0] + nx, a[1] + ny)]


SKIN_D = {}

# --------------------------------------------------------------- Ninja --
# emitters: row 1 sparks top blade (64,29), smoke hub (77,77);
#           row 2 arc left blade (36,64), flame bottom blade (64,98), leak right blade (106,64)
SKIN_D[("Ninja", "Night")] = {
    1: ([("panel", "shuriken", [(88, 57), (102, 57), (108, 63), (102, 70), (89, 70)], "amber"),
         ("crack", "shuriken", [(62, 14), (66, 22), (62, 30), (66, 40)]),
         ("crack", "shuriken", [(96, 57), (92, 52), (86, 49)]),
         ("crack", "shuriken", [(91, 70), (87, 76), (84, 80)]),
         ("crack", "hub", [(67, 69), (72, 73), (76, 78)])], None),
    2: ([("tear", "shuriken", [(24, 58), (38, 57), (41, 69), (25, 70)]),
         ("tear", "shuriken", [(59, 100), (69, 100), (70, 110), (64, 116), (58, 110)]),
         ("crack", "shuriken", [(70, 28), (67, 38), (71, 46)])], None),
    "seed": "Ninja/Night",
}
SKIN_D[("Ninja", "Sodium")] = {
    1: ([("panel", "shuriken", [(28, 59), (39, 58), (41, 67), (29, 69)], "cyan"),
         ("scorch", "shuriken", 64, 30, 4.5),
         ("crack", "hub", [(68, 69), (73, 73), (72, 79)])], None),
    2: ([("bend", "shuriken", [(108, 57), (124, 64), (108, 71)], [(109, 59), (112, 69)]),
         ("panel", "shuriken", [(57, 87), (71, 87), (71, 100), (57, 100)], "cyan"),
         ("scorch", "shuriken", 60, 16, 4),
         ("scorch", "shuriken", 34, 75, 3.5)], None),
    "seed": "Ninja/Sodium",
}
SKIN_D[("Ninja", "Bone")] = {
    1: ([("panel", "shuriken", [(59, 88), (69, 87), (70, 98), (60, 99)], "amber"),
         ("crack", "shuriken", [(58, 40), (61, 32), (64, 27), (66, 19)]),
         ("crack", "hub", [(68, 70), (74, 75), (72, 80)]),
         ("scorch", "shuriken", 98, 63, 3.5)], None),
    2: ([("tear", "shuriken", [(20, 57), (33, 56), (37, 64), (31, 71), (20, 70)]),
         ("bend", "shuriken", [(59, 6), (69, 6), (71, 20), (57, 20)], [(58, 16), (70, 11)]),
         ("scorch", "shuriken", 104, 59, 4),
         ("crack", "shuriken", [(60, 104), (65, 110), (62, 116)])], None),
    "seed": "Ninja/Bone/b",
}
SKIN_D[("Ninja", "Ronin")] = {
    1: ([("tear", "shuriken", _slash((67, 95), (95, 67), 4.5)),
         ("crack", "shuriken", [(60, 22), (65, 30), (61, 39)])], None),
    2: ([("tear", "shuriken", _slash((34, 62), (62, 34), 4.5)),
         ("crack", "shuriken", [(98, 59), (106, 64), (101, 69), (109, 71)]),
         ("scorch", "shuriken", 64, 109, 3.5)], None),
    "seed": "Ninja/Ronin",
}

# ------------------------------------------------------------ Saboteur --
# emitters: row 1 sparks left horn (37,32), smoke tail (64,104);
#           row 2 arc cradle left (43,81), flame cradle right (86,84), leak right horn tip (94,12)
SKIN_D[("Saboteur", "Night")] = {
    1: ([("panel", "cradle", [(73, 87), (86, 82), (91, 90), (81, 97), (73, 95)], "amber"),
         ("crack", "horn.L", [(34, 20), (38, 28), (35, 36), (39, 44)]),
         ("crack", "cradle", [(74, 92), (66, 95), (58, 93), (50, 90)]),
         ("crack", "cradle", [(88, 84), (94, 74), (97, 64)]),
         ("crack", "tail", [(62, 97), (66, 101), (63, 105)])], None),
    2: ([("tear", "horn.R", [(87, 18), (96, 17), (98, 29), (89, 31)]),
         ("panel", "cradle", [(40, 80), (48, 84), (48, 94), (40, 90)], "cyan"),
         ("crack", "core", [(76, 52), (80, 60), (78, 67)])], None),
    "seed": "Saboteur/Night",
}
SKIN_D[("Saboteur", "Sodium")] = {
    1: ([("panel", "core", [(73, 54), (81, 57), (83, 70), (75, 73)], "cyan"),
         ("scorch", "horn.L", 36, 31, 5),
         ("scorch", "cradle", 32, 60, 4),
         ("crack", "tail", [(62, 97), (66, 101), (63, 105)])], None),
    2: ([("bend", "horn.L", [(28, 18), (40, 16), (43, 28), (30, 30)], [(29, 26), (42, 22)]),
         ("tear", "cradle", [(36, 80), (46, 84), (48, 94), (38, 92)]),
         ("scorch", "cradle", 84, 86, 4.5),
         ("scorch", "horn.R", 93, 23, 3.5)], None),
    "seed": "Saboteur/Sodium",
}
SKIN_D[("Saboteur", "Bone")] = {
    1: ([("panel", "horn.R", [(86, 24), (96, 22), (99, 35), (89, 37)], "amber"),
         ("crack", "horn.L", [(32, 12), (35, 22), (33, 30), (38, 40)]),
         ("crack", "horn.L", [(35, 24), (40, 30), (42, 38)]),
         ("scorch", "cradle", 64, 94, 4.5)], None),
    2: ([("tear", "core", [(45, 58), (53, 52), (56, 64), (50, 72), (45, 68)]),
         ("scorch", "cradle", 86, 86, 4.5),
         ("scorch", "cradle", 41, 84, 4),
         ("crack", "cradle", [(30, 52), (34, 60), (31, 68)])], None),
    "seed": "Saboteur/Bone",
}
SKIN_D[("Saboteur", "Jester")] = {
    1: ([("tear", "cradle", [(50, 89), (64, 93), (78, 89), (76, 97), (64, 99), (52, 97)]),
         ("crack", "horn.L", [(33, 22), (37, 30), (34, 38)]),
         ("crack", "core", [(50, 52), (54, 58), (52, 66)])], None),
    2: ([("panel", "horn.R", [(87, 16), (96, 15), (98, 27), (89, 29)], "cyan"),
         ("panel", "cradle", [(36, 70), (44, 74), (46, 84), (37, 82)], "amber"),
         ("scorch", "cradle", 86, 85, 4.5)], None),
    "seed": "Saboteur/Jester",
}

# ----------------------------------------------------------------- UFO --
# emitters: row 1 sparks left rim (20,74), smoke lower right (90,96);
#           row 2 arc lower left (39,103), flame upper right rim (106,40), leak bottom (70,108)
SKIN_D[("UFO", "Night")] = {
    1: ([("panel", "disc", [(50, 10), (66, 7), (78, 13), (74, 23), (54, 23)], "amber"),
         ("crack", "disc", [(14, 68), (20, 74), (18, 82), (24, 88)]),
         ("crack", "disc", [(84, 89), (90, 96), (87, 104)]),
         ("crack", "disc", [(50, 20), (44, 28), (38, 32)]),
         ("crack", "disc", [(76, 22), (82, 30), (90, 34)])], None),
    2: ([("tear", "disc", [(27, 89), (38, 87), (42, 98), (31, 100)]),
         ("panel", "disc", [(101, 35), (110, 36), (112, 45), (103, 47)], "cyan"),
         ("crack", "disc", [(66, 103), (71, 109), (68, 116)])], None),
    "seed": "UFO/Night",
}
SKIN_D[("UFO", "Sodium")] = {
    1: ([("panel", "disc", [(99, 72), (110, 70), (114, 82), (103, 86)], "cyan"),
         ("scorch", "disc", 21, 75, 5),
         ("scorch", "disc", 91, 96, 5),
         ("scorch", "disc", 32, 44, 4)], None),
    2: ([("tear", "disc", [(22, 30), (35, 26), (42, 36), (32, 43), (22, 40)]),
         ("bend", "disc", [(52, 109), (76, 109), (72, 120), (56, 120)], [(54, 113), (74, 114)]),
         ("scorch", "disc", 106, 40, 4.5),
         ("scorch", "disc", 41, 97, 4)], None),
    "seed": "UFO/Sodium",
}
SKIN_D[("UFO", "Bone")] = {
    1: ([("panel", "disc", [(35, 36), (48, 31), (52, 44), (40, 49)], "amber"),
         ("crack", "disc", [(13, 70), (21, 74), (28, 72), (35, 78)]),
         ("crack", "disc", [(78, 86), (88, 93), (94, 100)]),
         ("scorch", "disc", 98, 52, 4)], None),
    2: ([("tear", "disc", [(21, 86), (33, 84), (37, 94), (27, 98)]),
         ("panel", "disc", [(100, 36), (110, 38), (112, 48), (102, 49)], "cyan"),
         ("scorch", "disc", 70, 107, 4)], None),
    "seed": "UFO/Bone",
}
SKIN_D[("UFO", "Abductor")] = {
    1: ([("panel", "disc", [(55, 100), (72, 98), (77, 108), (66, 116), (53, 110)], "amber"),
         ("crack", "disc", [(16, 70), (22, 76), (18, 84)]),
         ("scorch", "disc", 92, 94, 4.5),
         ("crack", "disc", [(52, 102), (44, 98), (36, 92)]),
         ("crack", "disc", [(76, 102), (84, 106), (90, 104)])], None),
    2: ([("tear", "disc", [(100, 34), (110, 36), (112, 46), (102, 48)]),
         ("scorch", "disc", 40, 96, 4.5),
         ("crack", "disc", [(30, 30), (38, 36), (36, 44)])], None),
    "seed": "UFO/Abductor",
}

# ---------------------------------------------------------------- Dove --
# emitters: row 1 sparks left flank (46,55), smoke tail (64,104);
#           row 2 arc right wing (100,56), flame belly right (73,86), leak belly left (54,84)
SKIN_D[("Dove", "Night")] = {
    1: ([("panel", "wing.L", [(20, 52), (30, 46), (36, 52), (34, 60), (24, 62)], "amber"),
         ("crack", "hull", [(42, 46), (46, 54), (44, 62), (48, 69)]),
         ("crack", "hull", [(38, 26), (41, 36), (39, 44)]),
         ("crack", "hull", [(46, 70), (50, 78), (48, 86)]),
         ("crack", "tail", [(60, 100), (64, 104), (62, 108)])], None),
    2: ([("tear", "hull", [(76, 50), (86, 48), (88, 60), (78, 62)]),
         ("crack", "wing.R", [(96, 50), (100, 56), (98, 62)]),
         ("crack", "hull", [(52, 78), (56, 84), (54, 90)]),
         ("crack", "hull", [(70, 80), (74, 86), (72, 92)])], None),
    "seed": "Dove/Night",
}
SKIN_D[("Dove", "Sodium")] = {
    1: ([("panel", "hull", [(78, 40), (88, 42), (90, 54), (80, 56)], "cyan"),
         ("scorch", "hull", 46, 55, 5),
         ("scorch", "hull", 78, 70, 4),
         ("crack", "tail", [(60, 100), (64, 104), (62, 108)])], None),
    2: ([("tear", "hull", [(44, 72), (52, 74), (56, 84), (48, 85)]),
         ("scorch", "hull", 73, 86, 4.5),
         ("scorch", "wing.R", 100, 56, 4)], None),
    "seed": "Dove/Sodium",
}
SKIN_D[("Dove", "Bone")] = {
    1: ([("tear", "hull", [(56, 6), (66, 4), (72, 8), (70, 14), (58, 14)]),
         ("crack", "hull", [(44, 28), (42, 38), (46, 48), (44, 58)]),
         ("crack", "hull", [(84, 24), (86, 34), (82, 42)]),
         ("scorch", "hull", 62, 92, 4)], None),
    2: ([("panel", "wing.R", [(95, 50), (104, 50), (106, 58), (98, 61)], "amber"),
         ("tear", "hull", [(68, 78), (78, 74), (80, 84), (72, 88)]),
         ("scorch", "hull", 54, 84, 4)], None),
    "seed": "Dove/Bone",
}
SKIN_D[("Dove", "Courier")] = {
    1: ([("tear", "hull", [(56, 80), (70, 80), (72, 92), (64, 96), (56, 92)]),
         ("crack", "hull", [(42, 49), (46, 56), (44, 64)]),
         ("crack", "tail", [(60, 101), (64, 105), (62, 108)])], None),
    2: ([("panel", "wing.R", [(94, 52), (102, 50), (106, 58), (98, 62)], "cyan"),
         ("panel", "hull", [(40, 28), (50, 28), (50, 40), (42, 40)], "amber"),
         ("scorch", "hull", 76, 78, 4)], None),
    "seed": "Dove/Courier",
}

# -------------------------------------------------------------- Turtle --
# emitters: row 1 sparks shell left (46,65), smoke shell aft (64,100);
#           row 2 arc rear scute (64,80), flame shell right (87,70), leak shell aft left (50,94)
SKIN_D[("Turtle", "Night")] = {
    1: ([("panel", "shell", [(77, 26), (88, 30), (92, 42), (84, 49), (76, 41)], "amber"),
         ("crack", "shell", [(40, 58), (46, 65), (43, 72), (48, 78)]),
         ("crack", "shell", [(58, 88), (62, 94), (60, 99)]),
         ("crack", "shell", [(86, 48), (90, 56), (88, 64)]),
         ("crack", "shell", [(82, 24), (78, 16), (72, 11)])], None),
    2: ([("tear", "shell", [(72, 76), (82, 74), (84, 86), (74, 88)]),
         ("tear", "shell", [(42, 84), (50, 82), (54, 92), (46, 96)]),
         ("crack", "shell", [(76, 52), (82, 58), (80, 65)])], None),
    "seed": "Turtle/Night",
}
SKIN_D[("Turtle", "Sodium")] = {
    1: ([("panel", "shell", [(57, 40), (71, 40), (72, 52), (56, 52)], "cyan"),
         ("scorch", "shell", 46, 65, 5),
         ("scorch", "shell", 62, 93, 4),
         ("scorch", "shell", 82, 58, 3.5)], None),
    2: ([("tear", "shell", [(80, 84), (88, 80), (90, 90), (82, 94)]),
         ("scorch", "shell", 64, 80, 4),
         ("scorch", "shell", 87, 70, 3.5),
         ("scorch", "shell", 50, 92, 3.5)], None),
    "seed": "Turtle/Sodium",
}
SKIN_D[("Turtle", "Bone")] = {
    1: ([("tear", "shell", [(36, 74), (46, 72), (50, 84), (42, 90), (38, 86)]),
         ("crack", "shell", [(46, 54), (46, 64), (52, 70), (50, 77)]),
         ("crack", "shell", [(46, 64), (40, 60), (39, 52)]),
         ("scorch", "shell", 62, 92, 4)], None),
    2: ([("panel", "shell", [(80, 62), (90, 60), (92, 72), (82, 74)], "cyan"),
         ("crack", "shell", [(58, 74), (64, 80), (70, 76)]),
         ("scorch", "shell", 52, 96, 3.5)], None),
    "seed": "Turtle/Bone",
}
SKIN_D[("Turtle", "Shellback")] = {
    1: ([("panel", "shell", [(74, 84), (84, 80), (88, 90), (80, 98), (74, 94)], "amber"),
         ("crack", "shell", [(42, 60), (46, 65), (44, 72)]),
         ("scorch", "shell", 58, 92, 3.5),
         ("scorch", "shell", 84, 72, 4),
         ("crack", "shell", [(72, 84), (66, 78), (60, 72), (54, 70)])], None),
    2: ([("tear", "shell", [(40, 26), (50, 26), (52, 38), (42, 40)]),
         ("scorch", "shell", 87, 70, 4.5),
         ("crack", "shell", [(60, 76), (64, 82), (68, 78)]),
         ("tear", "shell", [(44, 86), (52, 86), (54, 96), (46, 96)])], None),
    "seed": "Turtle/Shellback",
}

# one line per skin: (damaged, critical); critical keeps the damaged state
SKIN_CONCEPTS = {
    ("Ninja", "Night"):      ("right blade torn open (hot rim), cracks up the top blade and hub",
                              "left blade panel blown, bottom tip torn away, canopy cracked"),
    ("Ninja", "Sodium"):     ("left blade panel blown (cyan), soot on the top and right blades",
                              "right blade tip bent, bottom blade panel open, more soot, canopy cracked"),
    ("Ninja", "Bone"):       ("bottom blade panel blown, ink crack web up the top blade, hub cracked",
                              "left blade torn, top tip bent, soot on the right blade, canopy cracked"),
    ("Ninja", "Ronin"):      ("one sword slash through the lower-right blade roots",
                              "a second slash crossing it (an X through the hub), canopy cracked"),
    ("Saboteur", "Night"):   ("lower-right cradle torn open, cracks down the left horn and tail",
                              "right horn panel and left cradle panel blown, core cracked, canopy cracked"),
    ("Saboteur", "Sodium"):  ("core housing blown open (cyan), soot on the left horn and cradle",
                              "left horn bent, left cradle torn, soot right, canopy cracked"),
    ("Saboteur", "Bone"):    ("right horn panel blown, left horn shattered with ink cracks",
                              "core housing ripped, soot both sides of the cradle, canopy cracked"),
    ("Saboteur", "Jester"):  ("a torn grin across the cradle's chin, cracked horn and core",
                              "right horn and left cradle panels blown, soot right, canopy cracked"),
    ("UFO", "Night"):        ("top rim punched through (hot rim), cracks at the left and lower rims",
                              "lower-left and upper-right panels blown, bottom cracked, canopy cracked"),
    ("UFO", "Sodium"):       ("right rim panel blown (cyan), soot round the left, lower and upper rim",
                              "upper-left rim torn, bottom rim bent, more soot, canopy cracked"),
    ("UFO", "Bone"):         ("inner deck panel blown upper left, ink cracks across the rims",
                              "lower-left rim torn, upper-right panel blown, canopy cracked"),
    ("UFO", "Abductor"):     ("beam bay torn open on the underside, rim cracked",
                              "upper-right panel blown, soot lower left, canopy cracked"),
    ("Dove", "Night"):       ("left wing torn open (hot rim), flank and tail cracked",
                              "right flank panel blown, right wing and belly cracked, canopy cracked"),
    ("Dove", "Sodium"):      ("right shoulder panel blown (cyan), soot on both flanks",
                              "lower-left hull torn, soot on the belly and right wing, canopy cracked"),
    ("Dove", "Bone"):        ("beak torn off the nose, ink cracks down both cheeks",
                              "right wing panel blown, right belly torn, canopy cracked"),
    ("Dove", "Courier"):     ("cargo bay ripped open in the belly, flank and tail cracked",
                              "right wing and left shoulder panels blown, canopy cracked"),
    ("Turtle", "Night"):     ("upper-right shell split open (hot rim), cracks on the left and aft",
                              "rear-right panel blown, aft-left shell torn, canopy cracked"),
    ("Turtle", "Sodium"):    ("front scute blown (cyan glow), soot across the shell",
                              "aft-right shell torn, rear scute and flanks scorched, canopy cracked"),
    ("Turtle", "Bone"):      ("left shell edge chipped out, ink crack web over the left plates",
                              "right panel blown, rear scute cracked, canopy cracked"),
    ("Turtle", "Shellback"): ("rear-right shell breached",
                              "front-left panel blown, aft-left torn, right scorched, canopy cracked"),
}
