"""Purchasable colourways ("skins") for every roster hull.

Every ship has five skins, by index (the number the game saves):

    0  Stock      its own identity hue (ships.HUES), free
    1  palette    a cool swap: Night (indigo / cyan kick), or Neon (teal) when
                  Night would look like the stock hue
    2  palette    a warm swap: Sodium (sodium orange / amber kick), else Amber,
                  else Neon, whichever is furthest from the stock hue
    3  Bone       off-white hull; its bone trim turns friendly red so it reads
    4  special    the ship's own livery: the stock hue plus a themed flat
                  pattern (racing stripes, chevrons, diagonal bands or a
                  two-tone split) in a second palette colour

All colours come from the Akira palette (docs/art-style.md, akira.py), and the
friendly red stays the accent on every skin: running lights, red trim and the
hit flash never change. Only colours change, never shapes, so every skin has
the stock sheet's exact alpha (same sprite rect, collider, shield, nozzles).

build_skins.py renders them; prices are game data (ShipSkins.cs), not art.
"""
from hullkit import *  # noqa: F401,F403
from ships import HUES, ORDER

BONE_SH = "#B9AE98"

PALETTES = {
    "Night":  (INDIGO_1, INDIGO_0, CYAN),
    "Neon":   (TEAL, TEAL_SH, CYAN),
    "Sodium": (SODIUM, SODIUM_SH, AMBER),
    "Amber":  (AMBER, SODIUM, BONE),
    "Bone":   (BONE, BONE_SH, BONE),
}

# stripe recolours per palette swap (role -> role)
TRIMS = {"Bone": {"bone": "red"}}

COOL = ["Night", "Neon"]
WARM = ["Sodium", "Amber", "Neon"]
# a swap closer than this (RGB distance) to the stock hue would look like it
TOO_CLOSE = 70

# pattern colours, (base, shadow, highlight)
L_BONE = (BONE, BONE_SH, BONE)
L_AMBER = (AMBER, SODIUM, BONE)
L_GUN = (GUN, GUN_SH, GUN_HI)
L_TEAL = (TEAL, TEAL_SH, CYAN)
L_INDIGO = (INDIGO_1, INDIGO_0, CYAN)
L_RED = (RED, RED_SH, RED_HI)

# key: (name, pattern, pattern colours)
SPECIALS = {
    "NeonComet":   ("Kaneda", "racing", L_BONE),
    "VoltViper":   ("Storm", "chevrons", L_AMBER),
    "SolarFang":   ("Eclipse", "split", L_GUN),
    "CrimsonHalo": ("Seraph", "chevrons", L_BONE),
    "IonLancer":   ("Lance", "racing", L_TEAL),
    "JadePhantom": ("Wraith", "chevrons", L_GUN),
    "GoldWarden":  ("Regent", "split", L_INDIGO),
    "Lightning":   ("Hazard", "diagonal", L_GUN),
    "Ligher":      ("Flint", "chevrons", L_BONE),
    "Paranoid":    ("Signal", "diagonal", L_AMBER),
    "Ninja":       ("Ronin", "split", L_RED),
    "Saboteur":    ("Jester", "chevrons", L_TEAL),
    "UFO":         ("Abductor", "split", L_TEAL),
    "Dove":        ("Courier", "split", L_BONE),
    "Turtle":      ("Shellback", "chevrons", L_GUN),
}

KIND_STOCK, KIND_PALETTE, KIND_SPECIAL = "Stock", "Palette", "Special"


class Skin:
    def __init__(self, name, kind, hue, pattern=None, pattern_hue=None, trim=None):
        self.name = name
        self.kind = kind
        self.hue = hue                  # (base, shadow, highlight) for "hull" forms
        self.pattern = pattern          # None | racing | chevrons | diagonal | split
        self.pattern_hue = pattern_hue  # (base, shadow, highlight) of the pattern
        self.trim = trim or {}


def _rgb(h):
    return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))


def _dist(a, b):
    return sum((x - y) ** 2 for x, y in zip(_rgb(a), _rgb(b))) ** .5


def _pick(key, options, taken):
    stock = HUES[key][0]
    ok = [o for o in options if o not in taken and _dist(PALETTES[o][0], stock) >= TOO_CLOSE]
    if not ok:
        raise ValueError(f"no distinct swap for {key} among {options}")
    return ok[0]


def skins_for(key):
    out = [Skin("Stock", KIND_STOCK, HUES[key])]
    taken = []
    for options in (COOL, WARM, ["Bone"]):
        name = _pick(key, options, taken)
        taken.append(name)
        out.append(Skin(name, KIND_PALETTE, PALETTES[name], trim=TRIMS.get(name)))
    name, pattern, colours = SPECIALS[key]
    out.append(Skin(name, KIND_SPECIAL, HUES[key], pattern, colours))
    return out


SKINS = {key: skins_for(key) for key in ORDER}


# ------------------------------------------------------------- patterns --
def hull_bbox(ship):
    xs, ys = [], []
    for fm in ship.forms:
        if fm.tone == "hull":
            xs += [p[0] for p in fm.outline]
            ys += [p[1] for p in fm.outline]
    return min(xs), min(ys), max(xs), max(ys)


def livery(ship, pattern):
    """Pattern polygons in the fitted 128 u canvas (nose up). They may run
    past the hull: the renderer clips them to each hull-tone form."""
    x0, y0, x1, y1 = hull_bbox(ship)
    cx = 64.0
    w, h = x1 - x0, y1 - y0
    if pattern == "racing":
        # twin bands either side of the spine, nose to tail (the Akira bike)
        t = max(3.6, w * .06)
        gap = max(2.6, w * .04)
        return [box(cx - gap - t, y0 - 6, cx - gap, y1 + 6), box(cx + gap, y0 - 6, cx + gap + t, y1 + 6)]
    if pattern == "chevrons":
        # three nose-up chevrons stacked down the hull
        t = max(4.0, h * .065)
        half = w * .5 + 6
        rise = min(half * .5, h * .16)
        out = []
        for k in range(3):
            y = y0 + h * (.36 + k * .17)
            out.append([(cx, y), (cx + half, y + rise), (cx + half, y + rise + t), (cx, y + t),
                        (cx - half, y + rise + t), (cx - half, y + rise)])
        return out
    if pattern == "diagonal":
        # three parallel bands sweeping back from the upper left
        t = max(4.0, h * .075)
        slope = .75   # x shift per unit of y
        out = []
        for k in range(3):
            c = x0 + w * (.18 + k * .3)
            ya, yb = y0 - 8, y1 + 8
            out.append([(c - slope * (ya - y0) - t / 2, ya), (c - slope * (ya - y0) + t / 2, ya),
                        (c - slope * (yb - y0) + t / 2, yb), (c - slope * (yb - y0) - t / 2, yb)])
        return out
    if pattern == "split":
        # two-tone: the aft section in the second colour, cut with a nose-up V
        ym = y0 + h * .56
        dip = min(h * .12, 10)
        return [[(x0 - 10, ym + dip), (cx, ym - dip), (x1 + 10, ym + dip), (x1 + 10, y1 + 10), (x0 - 10, y1 + 10)]]
    raise ValueError(pattern)


def guide_colours():
    """Every colour a skin may use: the guide palette plus BONE's shadow."""
    import akira
    pal = {v.upper() for k, v in vars(akira).items() if k.isupper() and isinstance(v, str) and v.startswith("#")}
    pal.add(BONE_SH)
    return pal
