"""UI samples: HUD block, quick-action icons (Replay / Home), death panel.

Shapes are chamfered (cut corners), never rounded. Type is Orbitron Bold
(already in the project at Art/Fonts/Orbitron) with an ink stroke under the fill.
"""
import math
from akira import *
import sprites as S

MUTED = "#8C93B8"
CARD = "#151B30"


def chamfer(x, y, w, h, c, cuts=(1, 1, 1, 1)):
    """Rectangle with 45-degree cut corners (tl, tr, br, bl flags)."""
    tl, tr, br, bl = [c * k for k in cuts]
    return [(x + tl, y), (x + w - tr, y), (x + w, y + tr), (x + w, y + h - br),
            (x + w - br, y + h), (x + bl, y + h), (x, y + h - bl), (x, y + tl)]


def text(x, y, s, size, fill, anchor="start", ink=3.5, italic=True, spacing=1):
    sk = ' transform="skewX(-8)"' if italic else ""
    xx = x + (y * math.tan(math.radians(8)) if italic else 0)
    return (f'<text x="{f(xx)}" y="{f(y)}"{sk} font-family="Orbitron" font-weight="bold" font-size="{size}" '
            f'letter-spacing="{spacing}" text-anchor="{anchor}" fill="{fill}" stroke="{INK}" stroke-width="{ink}" '
            f'stroke-linejoin="round" paint-order="stroke">{s}</text>')


# ------------------------------------------------------------------- HUD ---
def hud():
    out = ""
    panel = chamfer(4, 4, 372, 164, 18, (0, 1, 0, 1))
    out += poly(panel, NIGHT_1, 'opacity="0.9"') + inkpoly(panel, 4)
    # red slab tab, top-left (the Akira title-card stripe)
    out += poly([(4, 4), (150, 4), (136, 24), (4, 24)], RED) + line([(150, 4), (136, 24), (4, 24)], 3)
    out += text(14, 20, "FLIGHT", 13, BONE, ink=2.5)
    out += line([(160, 14), (350, 14)], 2, RED) + line([(160, 14), (300, 14)], 2, BONE)  # rule
    # rows
    out += text(20, 56, "SPEED", 14, CYAN)
    out += text(360, 60, "0.42", 30, BONE, "end", 4)
    out += text(20, 96, "STAR DUST", 14, AMBER)
    out += embed(S.stardust(0), 64, 64, 168, 74, 26, 26)
    out += text(360, 100, "1,240", 30, BONE, "end", 4)
    out += text(20, 140, "PAUSES", 14, RED, ink=3.5)
    # segmented pause meter: 10 hard cells, 7 lit
    for k in range(10):
        x = 128 + k * 23
        cell = [(x + 4, 124), (x + 20, 124), (x + 16, 146), (x, 146)]
        out += poly(cell, RED if k < 7 else "#25223A") + inkpoly(cell, 2.5)
        if k < 7:
            out += poly([(x + 5, 126), (x + 18, 126), (x + 17.4, 129), (x + 4.4, 129)], RED_HI)
    out += text(366, 162, "7", 14, BONE, "end", 2.5)
    return out


# ----------------------------------------------------------------- ICONS ---
def plate():
    p = chamfer(8, 8, 112, 112, 22)
    o = poly(p, NIGHT_1) + poly(chamfer(14, 14, 100, 100, 18), INDIGO_0)
    o += poly([(14, 32), (32, 14), (60, 14), (14, 60)], "#22285A")  # one hard sheen facet
    o += inkpoly(p, 5) + inkpoly(chamfer(14, 14, 100, 100, 18), 2, RED)
    return o


def glyph_stack(draw):
    """Ink-outlined off-white glyph with a hard red drop shadow (cel offset)."""
    return draw(5, 5, RED, 22) + draw(0, 0, INK, 22) + draw(0, 0, BONE, 12)


def replay_glyph(dx, dy, col, w):
    """Angular (octagonal) loop arrow: the arc is a polyline, the head a hard triangle."""
    cx, cy, r = 64 + dx, 66 + dy, 27
    angs = [-55 + k * 40 for k in range(8)]          # -55 .. 225 deg, gap at the top
    p = [(cx + r * math.cos(math.radians(a)), cy + r * math.sin(math.radians(a))) for a in angs]
    o = (f'<polyline points="{pts(p)}" fill="none" stroke="{col}" stroke-width="{w}" '
         f'stroke-linejoin="miter" stroke-linecap="butt"/>')
    a = math.radians(-55)
    ex, ey = p[0]
    tx, ty = math.sin(a), -math.cos(a)                # tangent, pointing back along the loop
    nx, ny = math.cos(a), math.sin(a)
    g = (w - 12) / 2
    hw, hl = 17 + g, 18 + g * 1.6
    head = [(ex + nx * hw, ey + ny * hw), (ex - nx * hw, ey - ny * hw), (ex + tx * hl, ey + ty * hl)]
    head = [(x - tx * g * 0.6, y - ty * g * 0.6) for x, y in head]
    o += poly(head, col)
    return o


HOUSE = [(64, 26), (100, 58), (90, 58), (90, 98), (72, 98), (72, 76), (56, 76), (56, 98), (38, 98), (38, 58), (28, 58)]


def home_glyph(dx, dy, col, w):
    p = xf(HOUSE, dx=dx, dy=dy)
    if col == BONE:
        return poly(p, BONE) + poly(xf([(64, 31), (92, 56), (86, 56), (64, 37)], dx=dx, dy=dy), "#C9BFA8")
    return poly(p, col) + inkpoly(p, (w - 12) if col == INK else 10, col)


def icon(kind):
    g = replay_glyph if kind == "replay" else home_glyph
    return plate() + glyph_stack(g)


# ----------------------------------------------------------- DEATH PANEL ---
def death_panel():
    o = ""
    W_, H_ = 420, 600
    panel = chamfer(6, 6, W_ - 12, H_ - 12, 26)
    o += poly(panel, NIGHT_1, 'opacity="0.95"') + inkpoly(panel, 5)
    o += inkpoly(chamfer(14, 14, W_ - 28, H_ - 28, 20), 1.5, "#2E3560")
    # title slab: red parallelogram, heavy italic type
    slab = [(26, 34), (W_ - 18, 34), (W_ - 34, 104), (10, 104)]
    o += poly([(p[0] + 6, p[1] + 6) for p in slab], INK) + poly(slab, RED)
    o += poly([(26, 34), (W_ - 18, 34), (W_ - 21, 46), (23, 46)], RED_HI)
    o += inkpoly(slab, 4)
    o += text(W_ / 2 - 6, 88, "FLIGHT COMPLETE", 30, BONE, "middle", 5, spacing=1)
    # stat cards
    rows = [("BEST SPEED", "ALL-TIME", "0.61", CYAN, True), ("THIS RUN", "SPEED", "0.48", RED, False),
            ("STAR DUST", "EARNED THIS RUN", "+312", AMBER, False)]
    for k, (lab, sub, val, acc, best) in enumerate(rows):
        y = 132 + k * 104
        card = chamfer(30, y, W_ - 60, 88, 14, (0, 1, 0, 1))
        o += poly(card, CARD) + inkpoly(card, 3.5)
        o += poly([(38, y + 12), (50, y + 12), (46, y + 76), (34, y + 76)], acc) + inkpoly([(38, y + 12), (50, y + 12), (46, y + 76), (34, y + 76)], 2)
        o += text(62, y + 36, lab, 17, acc)
        o += text(62, y + 62, sub, 11, MUTED, ink=2.5)
        o += text(W_ - 46, y + 60, val, 34, BONE, "end", 5)
        if best:
            tag = chamfer(W_ - 168, y - 12, 104, 26, 8)
            o += poly(tag, AMBER) + inkpoly(tag, 3)
            o += text(W_ - 116, y + 6, "NEW BEST", 12, INK, "middle", 0)
    o += text(W_ - 46, 470, "TOTAL  9,860", 12, MUTED, "end", 2.5)
    # buttons
    for k, (lab, acc, kind) in enumerate([("REPLAY", CYAN, "replay"), ("MENU", RED, "home")]):
        x = 30 + k * 186
        b = chamfer(x, 492, 174, 74, 14)
        o += poly(xf(b, dx=5, dy=5), acc) + poly(b, CARD) + inkpoly(b, 4)
        g = replay_glyph if kind == "replay" else home_glyph
        o += embed(glyph_stack(g), 128, 128, x + 6, 500, 58, 58)
        o += text(x + 66, 538, lab, 19, BONE, ink=4)
    return o


def all_ui():
    return [
        ("ui_hud", 380, 172, hud(), "HUD block: speed / star dust / pause meter. Chamfered panel, Orbitron Bold with ink."),
        ("ui_icon_replay", 128, 128, icon("replay"), "Quick action: Replay. Chamfered plate, off-white glyph, ink, hard red cel shadow."),
        ("ui_icon_home", 128, 128, icon("home"), "Quick action: Home. Chamfered plate, off-white glyph, ink, hard red cel shadow."),
        ("ui_death_panel", 420, 600, death_panel(), "Death panel (FLIGHT COMPLETE): red title slab, stat cards, Replay/Menu."),
    ]
