#!/usr/bin/env python3
"""Builds the deliverables in docs/art-samples/ from ../frames/*.png:

  <anim>_strip.png   horizontal frame strip (frames butted, transparent)
  <anim>.gif         preview loop over the night backdrop, real hold timings
  <asset>.png        single images (first/key frame for animated assets)
  palette.png        the palette, grouped by role
  before_after.png   current game assets beside the redraws
  sample-sheet.png   everything on one labelled sheet

Needs Pillow only (GIFs are written by Pillow; no ImageMagick/ffmpeg).
"""
import os, re, shutil
from PIL import Image, ImageDraw, ImageFont
import sprites as S
import akira as A

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.dirname(HERE)
FR = os.path.join(OUT, "frames")
REPO = os.path.dirname(os.path.dirname(OUT))
ASSETS = os.path.join(REPO, "Pause", "Assets")
FONT = os.path.join(ASSETS, "Art", "Orbitron", "Orbitron-Bold.ttf")
TICK_MS = 1000 / 24

ANIMS = {  # name: (frame prefix, n frames, timing in 24 fps ticks)
    "player_exhaust": ("player_exhaust", 6, S.EXHAUST_TIMING),
    "enemy_fighter_idle": ("enemy_fighter", 4, S.FIGHTER_TIMING),
    "enemy_alien_idle": ("enemy_alien", 4, S.ALIEN_TIMING),
    "enemy_mine_pulse": ("enemy_mine", 4, S.MINE_TIMING),
    "explosion": ("explosion", 6, S.EXPLOSION_TIMING),
    "pickup_stardust_spin": ("pickup_stardust", 4, S.STARDUST_TIMING),
    "pickup_heal_pulse": ("pickup_heal", 4, S.HEAL_TIMING),
    "robot_talk": ("robot", 3, S.ROBOT_TIMING),
}
SINGLES = {  # output name: frame file
    "player_ship": "player_ship", "enemy_fighter": "enemy_fighter_0", "enemy_alien": "enemy_alien_0",
    "enemy_mine": "enemy_mine_0", "asteroid": "asteroid", "pickup_stardust": "pickup_stardust_0",
    "pickup_heal": "pickup_heal_0", "robot": "robot_0", "world_space": "world_space",
    "world_frost": "world_frost", "ui_hud": "ui_hud", "ui_icon_replay": "ui_icon_replay",
    "ui_icon_home": "ui_icon_home", "ui_death_panel": "ui_death_panel",
}
BG = (14, 20, 36, 255)        # NIGHT_1
SHEET_BG = (9, 12, 24, 255)
BONE = (244, 234, 212, 255)
RED = (216, 35, 44, 255)
MUTED = (140, 147, 184, 255)


def font(n):
    return ImageFont.truetype(FONT, n)


def load(name):
    return Image.open(os.path.join(FR, name + ".png")).convert("RGBA")


def frames(prefix, n):
    return [load(f"{prefix}_{i}") for i in range(n)]


def strip(ims):
    s = Image.new("RGBA", (sum(i.width for i in ims), max(i.height for i in ims)), (0, 0, 0, 0))
    x = 0
    for i in ims:
        s.alpha_composite(i, (x, 0)); x += i.width
    return s


def on_bg(im, bg=BG, pad=0):
    b = Image.new("RGBA", (im.width + pad * 2, im.height + pad * 2), bg)
    b.alpha_composite(im, (pad, pad))
    return b


def fit(im, w=None, h=None, nearest=False):
    s = min(w / im.width if w else 1e9, h / im.height if h else 1e9)
    return im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))),
                     Image.NEAREST if nearest else Image.LANCZOS)


def build_anims():
    out = {}
    for name, (prefix, n, timing) in ANIMS.items():
        ims = frames(prefix, n)
        strip(ims).save(os.path.join(OUT, f"{name}_strip.png"))
        g = [on_bg(i, pad=8).convert("RGB").quantize(colors=255, method=Image.MEDIANCUT, dither=Image.NONE) for i in ims]
        g[0].save(os.path.join(OUT, f"{name}.gif"), save_all=True, append_images=g[1:],
                  duration=[round(t * TICK_MS) for t in timing], loop=0, disposal=2)
        out[name] = ims
    return out


def build_singles():
    for out, src in SINGLES.items():
        shutil.copyfile(os.path.join(FR, src + ".png"), os.path.join(OUT, out + ".png"))


PALETTE = [
    ("NIGHT / SKY", [("NIGHT_0", A.NIGHT_0), ("NIGHT_1", A.NIGHT_1), ("INDIGO_0", A.INDIGO_0), ("INDIGO_1", A.INDIGO_1), ("DUSK", A.DUSK)]),
    ("HERO - KANEDA RED", [("RED", A.RED), ("RED_SH", A.RED_SH), ("RED_HI", A.RED_HI)]),
    ("CITY GLOW - SODIUM", [("SODIUM", A.SODIUM), ("AMBER", A.AMBER), ("SODIUM_SH", A.SODIUM_SH)]),
    ("NEON", [("TEAL", A.TEAL), ("CYAN", A.CYAN), ("TEAL_SH", A.TEAL_SH)]),
    ("ACCENT (SPARING)", [("MAGENTA", A.MAGENTA), ("MAGENTA_SH", A.MAGENTA_SH)]),
    ("INK + HIGHLIGHT", [("INK", A.INK), ("BONE", A.BONE)]),
    ("PLAYER METAL", [("GUN", A.GUN), ("GUN_SH", A.GUN_SH), ("GUN_HI", A.GUN_HI)]),
    ("ENEMY - STEEL", [("STEEL", A.STEEL), ("STEEL_SH", A.STEEL_SH), ("STEEL_HI", A.STEEL_HI)]),
    ("ENEMY - BRUISE", [("BRUISE", A.BRUISE), ("BRUISE_SH", A.BRUISE_SH), ("BRUISE_HI", A.BRUISE_HI)]),
    ("ENEMY - BILE", [("BILE", A.BILE), ("BILE_SH", A.BILE_SH), ("BILE_HI", A.BILE_HI), ("BILE_LIGHT", A.BILE_LIGHT)]),
    ("HAZARD ROCK", [("ROCK", A.ROCK), ("ROCK_SH", A.ROCK_SH), ("ROCK_HI", A.ROCK_HI)]),
    ("HEAL", [("HEAL", A.HEAL), ("HEAL_SH", A.HEAL_SH)]),
]
WORLD_PALETTES = [
    ("SPACE", ["#070A16", "#0E1424", "#1A1F45", "#2A2E6B", "#3A2A5C", "#F2862B"]),
    ("FROST", ["#04080F", "#0A1A2A", "#123248", "#16324A", "#9FE8F0", "#FFB43C"]),
    ("VERDANT", ["#05100E", "#0B1F1C", "#143430", "#1E4A3C", "#7FAF6A", "#F2862B"]),
    ("EMBER", ["#120608", "#24090E", "#3E1016", "#5A1A1A", "#F2862B", "#FFB43C"]),
]


def hexrgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (255,)


def build_palette():
    sw, gap = 150, 18
    rows = PALETTE + [("WORLD: " + n, [(c, c) for c in cs]) for n, cs in WORLD_PALETTES]
    W = 340 + 6 * (sw + gap) + 40
    H = 90 + len(rows) * (sw * 0.55 + 70)
    im = Image.new("RGBA", (int(W), int(H)), SHEET_BG)
    d = ImageDraw.Draw(im)
    d.text((30, 26), "AKIRA PALETTE", font=font(40), fill=BONE)
    y = 90
    for label, cols in rows:
        d.text((30, y + 24), label, font=font(20), fill=MUTED)
        for k, (nm, hx) in enumerate(cols):
            x = 340 + k * (sw + gap)
            d.rectangle([x, y, x + sw, y + sw * 0.55], fill=hexrgb(hx), outline=(20, 12, 20, 255), width=4)
            if nm != hx:
                d.text((x, y + sw * 0.55 + 4), nm, font=font(13), fill=BONE)
            d.text((x, y + sw * 0.55 + (20 if nm != hx else 4)), hx, font=font(13), fill=MUTED)
        y += sw * 0.55 + 70
    im.save(os.path.join(OUT, "palette.png"))
    return im


BEFORE_AFTER = [
    ("PLAYER SHIP", "Art/Resources/Prefabs/Ships/Retro80s/NeonComet_intact.png", None, "player_ship"),
    ("ENEMY FIGHTER", "Art/Resources/Prefabs/Enemies/Kenney/enemyBlack1.png", None, "enemy_fighter_0"),
    ("ALIEN", "Art/invader32x32x4.png", (0, 0, 32, 32), "enemy_alien_0"),
    ("RAIL MINE", "Art/Resources/Vfx/rail_bomb_themes_atlas.png", (0, 0, 313, 313), "enemy_mine_0"),
    ("ASTEROID", "Art/Aestroids/aestroid_brown.png", None, "asteroid"),
    ("EXPLOSION", "Art/RedExplosion/1_6.png", None, "explosion_1"),
    ("STAR DUST", "Art/Retro80s/Pickups/StarDustSmall.png", None, "pickup_stardust_0"),
    ("HEAL ATOM", "Art/Resources/Pickups/heal_atom_green.png", (200, 140, 1060, 1120), "pickup_heal_0"),
    ("TUTORIAL ROBOT", "Art/contra2.png", None, "robot_2"),
    ("REPLAY ICON", "Art/Resources/QuickActions/QuickAction_replay.png", None, "ui_icon_replay"),
    ("FROST WORLD", "Art/Resources/Worlds/Frost/backdrop.png", (0, 0, 1024, 2218), "world_frost"),
    ("DEATH PANEL", "Art/Resources/DeathPanel/dp_panel.png", None, "ui_death_panel"),
]


def build_before_after():
    cell, lab = 300, 46
    cols = 2
    pw = 2 * cell + 120
    rows = (len(BEFORE_AFTER) + cols - 1) // cols
    W = cols * pw + 60 * (cols + 1)
    H = 120 + rows * (cell + lab + 30)
    im = Image.new("RGBA", (W, H), SHEET_BG)
    d = ImageDraw.Draw(im)
    d.text((60, 36), "BEFORE  ->  AFTER", font=font(44), fill=BONE)
    for k, (label, path, crop, new) in enumerate(BEFORE_AFTER):
        c, r = k % cols, k // cols
        x0 = 60 + c * (pw + 60)
        y0 = 120 + r * (cell + lab + 30)
        d.text((x0, y0), label, font=font(22), fill=MUTED)
        old = Image.open(os.path.join(ASSETS, path)).convert("RGBA")
        if crop:
            old = old.crop(crop)
        nw = load(new)
        for j, (img, near) in enumerate([(old, max(old.size) < 140), (nw, False)]):
            bx = x0 + j * (cell + 120)
            d.rectangle([bx, y0 + lab, bx + cell, y0 + lab + cell], fill=BG)
            t = fit(img, cell - 24, cell - 24, near)
            im.alpha_composite(t, (bx + (cell - t.width) // 2, y0 + lab + (cell - t.height) // 2))
        d.text((x0 + cell + 34, y0 + lab + cell // 2 - 22), "->", font=font(40), fill=RED)
    im.save(os.path.join(OUT, "before_after.png"))
    return im


def build_sheet(anims, palette):
    W = 3600
    items = [  # (title, image, note) -- flowed left to right, wrapping
        ("PLAYER - NEON COMET", fit(on_bg(load("player_ship"), pad=10), h=440), "128u canvas; 0.58 world units in game"),
        ("TAIL-LIGHT EXHAUST - 6 FRAMES, ticks 2,2,1,2,2,2 (frame 3 = smear)", fit(on_bg(strip(anims["player_exhaust"])), h=440), ""),
        ("ENEMY FIGHTER IDLE - hold, anticipation, snap, settle", fit(on_bg(strip(anims["enemy_fighter_idle"])), h=250), "ticks 6,2,3,3"),
        ("ASTEROID", fit(on_bg(load("asteroid")), h=250), "amber city-glow rim on the lit edge"),
        ("ALIEN IDLE - crouch, mandible snap, blink", fit(on_bg(strip(anims["enemy_alien_idle"])), h=250), "ticks 4,2,3,3"),
        ("RAIL MINE - retract, then spike pop", fit(on_bg(strip(anims["enemy_mine_pulse"])), h=250), "ticks 6,2,2,4"),
        ("EXPLOSION - impact, burst, fireball, breakup, smoke cels, out", fit(on_bg(strip(anims["explosion"])), h=320), "ticks 1,2,2,2,3,3"),
        ("STAR DUST CELL - spin, edge-on smear", fit(on_bg(strip(anims["pickup_stardust_spin"])), h=210), "ticks 4,2,1,2"),
        ("HEAL CELL - squash / stretch pulse", fit(on_bg(strip(anims["pickup_heal_pulse"])), h=210), "ticks 5,2,2,3"),
        ("TUTORIAL ROBOT - 3-frame talk", fit(on_bg(strip(anims["robot_talk"])), h=330), "ticks 3,2,3"),
        ("QUICK ACTIONS", fit(on_bg(strip([load("ui_icon_replay"), load("ui_icon_home")]), pad=10), h=250), ""),
        ("HUD", fit(on_bg(load("ui_hud"), pad=10), h=330), ""),
        ("DEATH PANEL", fit(on_bg(load("ui_death_panel"), pad=10), h=720), ""),
    ]
    worlds = [("WORLD - SPACE", fit(load("world_space"), h=1560)), ("WORLD - FROST", fit(load("world_frost"), h=1560))]
    ww = worlds[0][1].width
    right_w = 2 * ww + 60
    left_w = W - right_w - 3 * 60
    hdr = 200
    x, y, rh = 60, hdr + 50, 0
    placed = []
    for title, img, note in items:
        if img.width > left_w:
            img = fit(img, w=left_w)
        if x + img.width > 60 + left_w:
            x, y, rh = 60, y + rh + 120, 0
        placed.append((x, y, title, img, note))
        x += img.width + 60
        rh = max(rh, img.height)
    left_h = y + rh + 120
    rx = 60 + left_w + 60
    pal = fit(palette, w=right_w)
    pal_y = hdr + 50 + worlds[0][1].height + 120
    H = max(left_h, pal_y + pal.height + 60)
    im = Image.new("RGBA", (W, int(H)), SHEET_BG)
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W, hdr - 40], fill=RED)
    d.rectangle([0, hdr - 46, W, hdr - 40], fill=(20, 12, 20, 255))
    d.text((60, 40), "PAUSE  //  80s ANIME - AKIRA DIRECTION", font=font(64), fill=BONE)
    d.text((62, 118), "SAMPLE SHEET v1 - flat cartoon cels, thick ink, 1 shadow + 1 highlight tone, frame-by-frame SVG flipbooks. For approval.", font=font(24), fill=BONE)
    for x, yy, title, img, note in placed:
        d.text((x, yy - 46), title, font=font(22), fill=BONE)
        if note:
            d.text((x, yy + img.height + 10), note, font=font(17), fill=MUTED)
        im.alpha_composite(img, (x, int(yy)))
    for k, (title, img) in enumerate(worlds):
        wx = rx + k * (ww + 60)
        d.text((wx, hdr + 4), title, font=font(22), fill=BONE)
        im.alpha_composite(img, (wx, hdr + 50))
        d.text((wx, hdr + 60 + img.height), "true game scale; parallax L0 sky .. L5 walls", font=font(17), fill=MUTED)
    im.alpha_composite(pal, (rx, int(pal_y)))
    im.convert("RGB").save(os.path.join(OUT, "sample-sheet.png"), optimize=True)


def main():
    anims = build_anims()
    build_singles()
    pal = build_palette()
    build_before_after()
    build_sheet(anims, pal)
    print("composed into", OUT)


if __name__ == "__main__":
    main()
