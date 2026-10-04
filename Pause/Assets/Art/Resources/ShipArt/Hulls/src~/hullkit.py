"""Drawing kit for the player hulls (Akira flat-cel style, docs/art-style.md).

A ship is a list of Forms (solid pieces: fuselage, wings, pods) plus canopy,
lights and nozzles, authored in a 128 u canvas, nose up, light from the upper
left. Every frame of every ship is the same drawing pushed through a point
transform (bob, bank warp) and a recolour (hit flash, damage), so the whole
animation is the frame table in build.py.

Per form the cel rules are applied mechanically:
  base fill, ONE hard shadow tone (lower-right rim sliver + any `shade`
  polygons), ONE highlight tone (upper-left rim kick), then ink.
Each form's shading is clipped to its own outline, so shade polygons can be
loose half-planes.

Layer stack: the guide's groups (base / shadow / highlight / ink / glow) are
kept per form, in back-to-front form order, because overlapping forms (a pod
over a wing) need their own complete stack. One extra group, `ink-under`, is
the whole silhouette stroked first, which is what makes the outer contour a
single 4 u line around the union of all forms.
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "../../../../../../.."))
sys.path.append(os.path.join(REPO, "docs/art-samples/src"))
from akira import *  # noqa: E402,F401  palette + svg helpers (the sample pipeline)

OUTER = 4.0      # visible outer contour, u
FORM_INK = 2.5   # each form's own (centred) outline
PANEL = 2.0
DETAIL = 1.5
LIGHT_SCALE = 1.45  # running lights must read at ~110 px on a phone
LIVERY_INK = 1.4    # seam between a skin's livery pattern and the hull colour


def P(points):
    return [(float(x), float(y)) for x, y in points]


def sym(half, cx=64):
    """Half outline from the top centre down the LEFT side -> closed outline."""
    return mirror(P(half), cx)


def mxs(points, cx=64):
    return [(2 * cx - x, y) for x, y in points]


def box(x0, y0, x1, y1):
    return [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]


def right_of(x, y0=-20, y1=150):
    return box(x, y0, 150, y1)


def below(y, x0=-20, x1=150):
    return box(x0, y, x1, 150)


class Form:
    def __init__(self, outline, tone="hull", shade=(), kick=2.4, rim=3.2, ink=FORM_INK,
                 details=(), lines=(), name=""):
        self.outline = P(outline)
        self.tone = tone            # "hull" (identity hue), "gun", "red", "bone", "teal"
        self.shade = [P(s) for s in shade]
        self.kick = kick            # upper-left highlight sliver width
        self.rim = rim              # lower-right shadow sliver width
        self.ink = ink
        self.details = list(details)  # (points, colour-role, ink width or 0)
        self.lines = list(lines)      # (points, width) open ink polylines
        self.name = name


class Ship:
    def __init__(self, key, forms, canopy=None, lights=(), nozzles=(), stripes=(), glints=None,
                 bank=True):
        self.key = key
        self.forms = forms
        self.canopy = P(canopy) if canopy else None
        self.lights = list(lights)    # (x, y, r, role) role: "red" blinks, "amber" steady
        self.nozzles = list(nozzles)  # (x, y, half-width, plume scale)
        self.stripes = list(stripes)  # (points, colour-role) drawn on top of the forms
        self.bank = bank


# ------------------------------------------------------------- transforms --
class Pose:
    def __init__(self, dy=0.0, bank=0.0, lights=True, glint=None, flash=False, damage=0):
        self.dy = dy          # vertical bob, u (negative = up)
        self.bank = bank      # -1 leaning into a left move, +1 right
        self.lights = lights
        self.glint = glint    # 0..1 position of the canopy glint, or None
        self.flash = flash
        self.damage = damage  # 0 intact, 1 damaged, 2 critical


def warp(points, pose, cx=64):
    out = []
    b = pose.bank
    for x, y in points:
        if b:
            d = x - cx
            # the wing on the side we lean into dips (foreshortened), the
            # other rises; the whole hull slides a touch into the turn.
            near = (d < 0) == (b < 0)
            k = 0.80 if near else 0.95
            x = cx + d * k + 1.5 * b
            y = y + (1.2 if near else -0.6) * abs(d) / 60.0
        out.append((x, y + pose.dy))
    return out


# ----------------------------------------------------------------- colour --
def tones(ship_hue, role, flash):
    """(base, shadow, highlight) for a form role."""
    if flash:
        return BONE, BONE, BONE
    if role == "hull":
        return ship_hue
    if role == "gun":
        return GUN, GUN_SH, GUN_HI
    if role == "red":
        return RED, RED_SH, RED_HI
    if role == "bone":
        return BONE, "#B9AE98", BONE
    if role == "teal":
        return TEAL, TEAL_SH, CYAN
    if role == "amber":
        return AMBER, SODIUM_SH, BONE
    raise ValueError(role)


def colour(ship_hue, role, flash):
    if flash:
        return BONE
    table = {"bone": BONE, "red": RED, "red_sh": RED_SH, "amber": AMBER, "sodium": SODIUM,
             "teal": TEAL, "teal_sh": TEAL_SH, "cyan": CYAN, "gun": GUN, "gun_sh": GUN_SH,
             "gun_hi": GUN_HI, "ink": INK, "hull": ship_hue[0], "hull_sh": ship_hue[1],
             "hull_hi": ship_hue[2]}
    return table.get(role, role)


# ------------------------------------------------------------------ render --
def _path(points):
    return "M" + " L".join(f"{f(x)},{f(y)}" for x, y in points) + " Z"


def _shift(points, d):
    return [(x + d, y + d) for x, y in points]


def damage_draw(ft, hue, W, seed):
    import damage
    return damage.draw(ft, hue, W, seed)


def damage_holes(ship, state):
    if not getattr(ship, "damage", None):
        return []
    import damage
    return damage.holes(ship, state)


def canopy_crack(canopy, W):
    import damage
    return damage.canopy_crack(canopy, W)


def render(ship, hue, pose, uid="s", livery=None, trim=None):
    """SVG body for one frame. hue = (base, shadow, highlight) of the identity colour.

    Skins (skins.py) pass a different hue, plus optionally
      livery = (polygons, (base, shadow, highlight)): a flat pattern painted on
               every hull-tone form, cel-shaded like the form under it (its own
               base / shadow / kick tones, the form's own shadow shapes) with a
               thin ink seam where it meets the hull colour;
      trim   = {role: role} recolouring the stripes (e.g. bone -> red on a
               bone hull, so the trim still reads).
    With neither, the output is exactly the default hull's."""
    W = lambda pts: warp(pts, pose)
    trim = trim or {}
    fl = pose.flash
    ink_col = RED if fl else INK
    defs = []
    out = []

    # silhouette clip (lights' halos never leave the hull: clean alpha)
    sil = "".join(f'<path d="{_path(W(fm.outline))}"/>' for fm in ship.forms)
    defs.append(f'<clipPath id="{uid}sil">{sil}</clipPath>')

    # ink-under: the whole silhouette stroked once, so the outer contour is
    # one even OUTER-wide line around the union of every form.
    under_w = 2 * OUTER - FORM_INK
    out.append('<g id="ink-under">' + "".join(
        f'<path d="{_path(W(fm.outline))}" fill="{ink_col}" stroke="{ink_col}" '
        f'stroke-width="{f(under_w)}" stroke-linejoin="round"/>' for fm in ship.forms) + "</g>")

    for i, fm in enumerate(ship.forms):
        base, sh, hi = tones(hue, fm.tone, fl)
        o = W(fm.outline)
        cid = f"{uid}f{i}"
        defs.append(f'<clipPath id="{cid}"><path d="{_path(o)}"/></clipPath>')
        g = [f'<g class="form" id="{fm.name or "form" + str(i)}">']
        # highlight tone first, base shifted down-right leaves the kick sliver
        pat = [W(p) for p in livery[0]] if livery and fm.tone == "hull" and not fl else None
        g.append(f'<g id="highlight" clip-path="url(#{cid})"><path d="{_path(o)}" fill="{hi}"/></g>')
        if pat:
            g.append(f'<g id="livery-highlight" clip-path="url(#{cid})">'
                     + "".join(poly(p, livery[1][2]) for p in pat) + "</g>")
        g.append(f'<g id="base" clip-path="url(#{cid})"><path d="{_path(_shift(o, fm.kick))}" fill="{base}"/></g>')
        if pat:
            defs.append(f'<clipPath id="{cid}b"><path d="{_path(_shift(o, fm.kick))}"/></clipPath>')
            g.append(f'<g id="livery-base" clip-path="url(#{cid})"><g clip-path="url(#{cid}b)">'
                     + "".join(poly(p, livery[1][0]) for p in pat) + "</g></g>")
        shp = f'<path d="{_path(o)} {_path(_shift(o, -fm.rim))}" fill="{sh}" fill-rule="evenodd"/>'
        for s in fm.shade:
            shp += f'<path d="{_path(W(s))}" fill="{sh}"/>'
        g.append(f'<g id="shadow" clip-path="url(#{cid})">{shp}</g>')
        if pat:
            clip = f'<path d="{_path(o)} {_path(_shift(o, -fm.rim))}" clip-rule="evenodd"/>'
            clip += "".join(f'<path d="{_path(W(s))}"/>' for s in fm.shade)
            defs.append(f'<clipPath id="{cid}s">{clip}</clipPath>')
            g.append(f'<g id="livery-shadow" clip-path="url(#{cid})"><g clip-path="url(#{cid}s)">'
                     + "".join(poly(p, livery[1][1]) for p in pat) + "</g></g>")
            g.append(f'<g id="livery-ink" clip-path="url(#{cid})">'
                     + "".join(inkpoly(p, LIVERY_INK, ink_col) for p in pat) + "</g>")
        det = ""
        for pts, role, w in fm.details:
            p = W(pts)
            det += f'<path d="{_path(p)}" fill="{colour(hue, role, fl)}"/>'
            if w:
                det += f'<path d="{_path(p)}" fill="none" stroke="{ink_col}" stroke-width="{f(w)}" stroke-linejoin="round"/>'
        g.append(f'<g id="detail" clip-path="url(#{cid})">{det}</g>')
        ink = f'<path d="{_path(o)}" fill="none" stroke="{ink_col}" stroke-width="{f(fm.ink)}" stroke-linejoin="round"/>'
        for pts, w in fm.lines:
            ink += line(W(pts), w, ink_col)
        g.append(f'<g id="ink">{ink}</g>')
        # this form's damage (damage.py), clipped to the form and re-inked:
        # drawn inside the outline, so the alpha never changes
        feats = [ft for ft in getattr(ship, "damage", {}).get(pose.damage, []) if ft[1] == i] if not fl else []
        if feats:
            dmg = "".join(damage_draw(ft, hue, W, k + 7 * i) for k, ft in enumerate(feats))
            g.append(f'<g id="damage" clip-path="url(#{cid})">{dmg}</g>')
            g.append(f'<g id="damage-ink"><path d="{_path(o)}" fill="none" stroke="{ink_col}" '
                     f'stroke-width="{f(fm.ink)}" stroke-linejoin="round"/></g>')
        g.append("</g>")
        out.append("".join(g))

    # stripes / trim on top of everything (identity livery + red friendly trim)
    st = ""
    for pts, role in ship.stripes:
        p = W(pts)
        st += f'<path d="{_path(p)}" fill="{colour(hue, trim.get(role, role), fl)}" stroke="{ink_col}" stroke-width="1.6" stroke-linejoin="round"/>'
    holes = damage_holes(ship, pose.damage) if pose.damage and not fl else []
    if holes:
        # stripes never paint over a blown panel or a tear
        m = '<rect x="-64" y="-64" width="256" height="256" fill="white"/>'
        m += "".join(poly(W(h), "black") for h in holes)
        defs.append(f'<mask id="{uid}holes" maskUnits="userSpaceOnUse" x="-64" y="-64" width="256" height="256">{m}</mask>')
        st = f'<g mask="url(#{uid}holes)">{st}</g>'
    out.append(f'<g id="highlight-trim" clip-path="url(#{uid}sil)">{st}</g>')

    # nozzles: gunmetal mouth, ink lip, hot amber slot (the plume starts here)
    nz = ""
    for x, y, w, _ in ship.nozzles:
        if w <= 0:
            continue  # spinning craft: no nozzle, the wind wake is its engine
        mouth = W([(x - w, y - 4), (x + w, y - 4), (x + w * 0.8, y + 1), (x - w * 0.8, y + 1)])
        nz += poly(mouth, colour(hue, "gun", fl)) + inkpoly(mouth, 2, ink_col)
        slot = W([(x - w * 0.6, y - 1.6), (x + w * 0.6, y - 1.6), (x + w * 0.5, y + 0.4), (x - w * 0.5, y + 0.4)])
        nz += poly(slot, BONE if fl else AMBER)
    out.append(f'<g id="nozzles">{nz}</g>')

    # canopy: teal glass, one shadow, glint sweep
    if ship.canopy:
        c = W(ship.canopy)
        cx = sum(p[0] for p in ship.canopy) / len(ship.canopy)
        cid = f"{uid}can"
        defs.append(f'<clipPath id="{cid}"><path d="{_path(c)}"/></clipPath>')
        cb, cs, chi = tones(hue, "teal", fl)
        can = poly(c, cb)
        can += f'<g clip-path="url(#{cid})">' + poly(W(right_of(cx)), cs)
        ys = [p[1] for p in ship.canopy]
        y0, y1 = min(ys), max(ys)
        # resting kick on the upper-left of the glass
        can += poly(W([(cx - 3.5, y0 + 4), (cx - 1.5, y0 + 2), (cx - 1.5, y0 + (y1 - y0) * 0.55), (cx - 3.5, y0 + (y1 - y0) * 0.65)]), BONE)
        if pose.glint is not None and not fl:
            gy = y0 + (y1 - y0) * pose.glint
            can += poly(W([(cx - 12, gy + 2), (cx + 12, gy - 6), (cx + 12, gy - 2), (cx - 12, gy + 6)]), BONE)
            can += poly(W([(cx - 12, gy + 8), (cx + 12, gy), (cx + 12, gy + 1.5), (cx - 12, gy + 9.5)]), CYAN)
        if pose.damage >= 2 and not fl and getattr(ship, "damage", None):
            can += canopy_crack(ship.canopy, W)   # the last life cracks the glass
        can += "</g>" + inkpoly(c, 2.5, ink_col)
        out.append(f'<g id="canopy">{can}</g>')

    # lights (glow layer): flat diamond + bone core; a hard 4-point cel flare
    # (no blur: a blurred halo over a coloured hull reads as mud), clipped to
    # the hull so nothing leaks past the ink
    gl, halo = "", ""
    for x, y, r, role in ship.lights:
        r *= LIGHT_SCALE
        (lx, ly), = W([(x, y)])
        if fl:
            col, on = BONE, True
        elif role == "red":
            on = pose.lights
            col = RED if on else RED_SH
        else:
            on = True
            col = AMBER
        if on and not fl:
            halo += poly(star(lx, ly, r * 2.5, r * 0.55, 4, rot=45 if role == 'red' else 0), col)
        d = [(lx, ly - r * 1.25), (lx + r, ly), (lx, ly + r * 1.25), (lx - r, ly)]
        gl += poly(d, col) + inkpoly(d, 1.5, ink_col)
        if on:
            gl += poly([(lx, ly - r * 0.55), (lx + r * 0.42, ly), (lx, ly + r * 0.55), (lx - r * 0.42, ly)], BONE)
    out.append(f'<g id="glow"><g clip-path="url(#{uid}sil)">{halo}</g>{gl}</g>')

    return "".join(out), "".join(defs)
