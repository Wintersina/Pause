#!/usr/bin/env python3
"""Contact sheets: every ship at true relative scale (S px per world unit) next to the player hull,
current drawing vs the proposed tier size. Usage: sheet.py ASSETS OUTDIR"""
import json, glob, os, sys, re, math
from PIL import Image, ImageDraw, ImageFont
import numpy as np

ROOT, OUT = sys.argv[1], sys.argv[2]
ART = ROOT + '/Art/Resources/'
S = 150                       # sheet px per world unit
PLAYER_U = .58 * 1.35
m = json.load(open(OUT + '/measure.json'))
tiers = json.load(open(OUT + '/tiers.json'))

try:
    F = ImageFont.truetype('/System/Library/Fonts/Menlo.ttc', 13)
    FS = ImageFont.truetype('/System/Library/Fonts/Menlo.ttc', 11)
except Exception:
    F = FS = ImageFont.load_default()

def crop_cell(path, idx, world_cell):
    im = Image.open(path).convert('RGBA')
    h = im.size[1]
    c = im.crop((idx * h, 0, idx * h + h, h))
    bb = c.getchannel('A').point(lambda v: 255 if v > 40 else 0).getbbox()
    return c, h, bb

def sprite(path, idx, world_cell, scale):
    c, h, bb = crop_cell(path, idx, world_cell)
    px = int(round(world_cell * scale * S))
    return c.resize((px, px), Image.LANCZOS), bb, h

player_img = Image.open(ART + 'ShipArt/Dove/Dove.png').convert('RGBA').crop((0, 0, 768, 768))
pbb = player_img.getchannel('A').point(lambda v: 255 if v > 40 else 0).getbbox()
player_img = player_img.crop(pbb)
pl = max(player_img.size)
player_img = player_img.resize((int(player_img.size[0] * PLAYER_U * S / pl), int(player_img.size[1] * PLAYER_U * S / pl)), Image.LANCZOS)

def sheet(items, name, cols, cellw, cellh, title):
    rows = math.ceil(len(items) / cols)
    W, H = cols * cellw, rows * cellh + 40
    img = Image.new('RGBA', (W, H), (22, 18, 44, 255))
    d = ImageDraw.Draw(img)
    d.text((10, 10), title, fill=(230, 230, 255), font=F)
    for n, it in enumerate(items):
        cx, cy = (n % cols) * cellw, (n // cols) * cellh + 40
        d.rectangle([cx, cy, cx + cellw - 1, cy + cellh - 1], outline=(50, 44, 90))
        base = cy + cellh - 46      # ship baseline centre line is the cell middle
        mid_y = cy + (cellh - 46) // 2
        # left: now, right: proposed
        for side, (spr, bb, h, wc, scale) in enumerate(it['sprites']):
            ox = cx + cellw * (0.27 + 0.46 * side)
            bx = (bb[0] + bb[2]) / 2 / h - .5
            by = (bb[1] + bb[3]) / 2 / h - .5
            px = spr.size[0]
            img.alpha_composite(spr, (int(ox - px / 2 - bx * px), int(mid_y - px / 2 - by * px)))
            L = max(bb[2] - bb[0], bb[3] - bb[1]) / h * wc * scale
            d.rectangle([ox - L * S / 2, mid_y - L * S / 2, ox + L * S / 2, mid_y + L * S / 2], outline=(90, 200, 120) if side else (120, 120, 160))
            d.text((ox - 28, mid_y + L * S / 2 + 3), '%.2fu' % L, fill=(200, 200, 230), font=FS)
        # player reference at left bottom
        img.alpha_composite(player_img, (cx + 6, cy + 6))
        d.text((cx + 6, cy + 8 + player_img.size[1]), 'player', fill=(150, 150, 190), font=FS)
        d.text((cx + 8, cy + cellh - 18), it['label'][:34], fill=(255, 255, 255), font=FS)
        d.text((cx + 8, cy + cellh - 32), it['sub'], fill=(160, 220, 170), font=FS)
    img.convert('RGB').save(OUT + '/' + name)

# elites
els = []
for key, mm in m['elites'].items():
    t = tiers['elites'][key]
    wname = mm['world'].capitalize()
    path = ART + 'Elites/%s/%s.png' % (wname, key)
    d = json.load(open(ART + 'Elites/Defs/%s.json' % key))
    idx = ((d.get('cells') or {}).get('flight') or [0])[0]
    wc = mm['cell_world']
    s0 = sprite(path, idx, wc, 1.0)
    s1 = sprite(path, idx, wc, t['k'])
    els.append(dict(label=mm['name'] + ' (%s)' % mm['world'][:3], sub='x%.2f  %.2f -> %.2f u' % (t['k'], mm['long_u'], mm['long_u'] * t['k']),
                    sprites=[(s0[0], s0[1], s0[2], wc, 1.0), (s1[0], s1[1], s1[2], wc, t['k'])]))
# sort by world order then size
sheet(els, 'sheet_elites.png', 4, 540, 420, 'ELITES: left current (grey box), right proposed (green box); player flown hull = %.2f u shown bottom-left; %d px per world unit' % (PLAYER_U, S))

ens = []
for key, mm in m['enemies'].items():
    t = tiers['enemies'][key]
    path = ART + 'Enemies/%s.png' % key
    wc = mm['cell_world']
    s0 = sprite(path, 0, wc, 1.0)
    s1 = sprite(path, 0, wc, t['k'])
    ens.append(dict(label=mm['name'] + ' (%s %s)' % (key.split('_')[0][:4], mm['role']), sub='x%.2f  %.2f -> %.2f u' % (t['k'], mm['long_u'], mm['long_u'] * t['k']),
                    sprites=[(s0[0], s0[1], s0[2], wc, 1.0), (s1[0], s1[1], s1[2], wc, t['k'])]))
sheet(ens, 'sheet_enemies.png', 5, 430, 300, 'ENEMIES: left current, right proposed; player hull bottom-left; %d px per world unit' % S)
print('ok')
