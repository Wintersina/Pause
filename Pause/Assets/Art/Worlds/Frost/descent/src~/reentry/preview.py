# Planetfall entry preview: replays Planetfall.Step's entry layers (decks,
# streak strips, tint, vignette, shroud + its additive hot copy, the hull and
# its engine flames) on PlanetfallTimeline's clock, tl 1.4 .. 5.35.
# usage: python3 preview.py <world> <fx.png> <streaks.png> <out.gif> <mode: cur|new> [frames]
#   cur: today's code (6 cells, `deep` = Ramp(ShroudInTo, BreakAt), def.heat as is)
#   new: the proposed code (ShroudHeat ramp, ShroudFps 16 for 12 cells, Frost heat not pink)
import sys, math
import numpy as np
from PIL import Image

A = '/Users/sina/Developer/Pause-worktrees/enemy-art/Pause/Assets/'
R = A + 'Art/Backgrounds/Resources/Worlds/'
DEFS = {  # cue, heat, cold, shade (PlanetfallDef)
    'frost': ((0.45, 0.92, 1), (0.95, 0.30, 0.80), (0.35, 0.78, 1), (0.05, 0.03, 0.14)),
    'verdant': ((0.70, 1, 0.36), (0.86, 0.90, 0.22), (0.22, 0.80, 0.58), (0.02, 0.08, 0.05)),
    'ember': ((1, 0.74, 0.26), (1, 0.64, 0.20), (0.66, 0.50, 0.40), (0.07, 0.04, 0.03)),
    'tide': ((0.49, 0.95, 0.75), (0.55, 0.90, 0.85), (0.20, 0.45, 0.48), (0.02, 0.07, 0.08)),
    'storm': ((1, 0.95, 0.63), (0.80, 0.88, 1), (0.22, 0.29, 0.40), (0.03, 0.04, 0.09)),
    'desert': ((1, 0.945, 0.78), (1, 0.95, 0.80), (0.45, 0.40, 0.35), (0.10, 0.09, 0.17)),
}
NEW_HEAT = {'frost': (1.0, 0.62, 0.32)}   # proposed: Frost's entry glow orange, not pink

def ramp(t, a, b):
    if b <= a: return 1.0 if t >= b else 0.0
    x = min(1, max(0, (t - a) / (b - a)))
    return x * x * (3 - 2 * x)

# PlanetfallTimeline
def shroud_alpha(t): return ramp(t, 1.45, 2.0) * (1 - ramp(t, 5.0, 5.3))
def streak_alpha(t): return ramp(t, 2.2, 2.8) * (1 - ramp(t, 5.0, 5.3))
def tint_at(t): return .12 * ramp(t, 1.4, 2.3) * (1 - ramp(t, 5.0, 6.3))
def cool(t): return ramp(t, 2.8, 4.4)
def vig(t): return .7 * ramp(t, 1.4, 2.3) * (1 - ramp(t, 5.0, 6.3))
def dark_a(t): return ramp(t, 2.0, 2.7) * (1 - ramp(t, 5.0, 6.3))
def deck_a(t): return .78 * ramp(t, 2.3, 3.0) * (1 - ramp(t, 5.0, 5.9))
def zoom(t): return (1 + .45 * ramp(t, 2.0, 5.0)) * (1 + 1.6 * ramp(t, 5.0, 6.3))
# proposed: the plasma builds as the dive bites into denser air, peaks, then
# dims as the ship slows over the cloud deck, before the break blows it away
def shroud_heat(t): return ramp(t, 1.45, 2.6) * (1 - .55 * ramp(t, 3.7, 4.95))

S = 2                    # supersample (render at 2x, the GIF is 1x)
VW, VH = 486 * S, 864 * S
UNITS_W = 5.7            # the view's width, world units (CameraFit minHalfWidth 2.85)
PPU = VW / UNITS_W
SHIP_W = 0.85            # the hull's drawn width, units

def load(p): return np.asarray(Image.open(p).convert('RGBA')).astype(np.float32) / 255.

def affine_sample(tex, cx_tex, cy_tex, scale, out_w, out_h):
    """Nearest-free bilinear sample of a wrapping texture: canvas px (x,y) -> tex (cx + (x-W/2)/scale, cy + (y-H/2)/scale)."""
    th, tw = tex.shape[:2]
    xs = cx_tex + (np.arange(out_w) - out_w / 2) / scale
    ys = cy_tex + (np.arange(out_h) - out_h / 2) / scale
    x0 = np.floor(xs).astype(int); y0 = np.floor(ys).astype(int)
    fx = (xs - x0)[None, :, None]; fy = (ys - y0)[:, None, None]
    x0m, x1m = np.mod(x0, tw), np.mod(x0 + 1, tw); y0m, y1m = np.mod(y0, th), np.mod(y0 + 1, th)
    a = tex[y0m][:, x0m]; b = tex[y0m][:, x1m]; c = tex[y1m][:, x0m]; d = tex[y1m][:, x1m]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy

def over(dst, src_rgba, alpha_mul=1.0):
    a = src_rgba[..., 3:4] * alpha_mul
    dst[..., :3] = src_rgba[..., :3] * a + dst[..., :3] * (1 - a)

def paste_sprite(dst, spr, cx, cy, w, h, angle=0.0, tint=(1, 1, 1), alpha=1.0, additive=False, pivot=(.5, .5)):
    """Draws an RGBA float sprite scaled to w x h canvas px, its pivot (share of
    the sprite, y down) at (cx, cy), rotated `angle` deg (ccw on screen)."""
    im = Image.fromarray((np.clip(spr, 0, 1) * 255).astype(np.uint8), 'RGBA')
    im = im.resize((max(1, int(round(w))), max(1, int(round(h)))), Image.BILINEAR)
    px, py = pivot[0] * im.width, pivot[1] * im.height
    if abs(angle) > 1e-3:
        # rotate about the pivot: pad so the pivot is the centre, then rotate
        W2 = int(2 * max(px, im.width - px)) + 2; H2 = int(2 * max(py, im.height - py)) + 2
        pad = Image.new('RGBA', (W2, H2), (0, 0, 0, 0)); ox, oy = int(W2 / 2 - px), int(H2 / 2 - py)
        pad.paste(im, (ox, oy)); im = pad.rotate(angle, resample=Image.BILINEAR, expand=False)
        px, py = W2 / 2, H2 / 2
    s = np.asarray(im).astype(np.float32) / 255.
    x0, y0 = int(round(cx - px)), int(round(cy - py))
    H, W = dst.shape[:2]
    sx0, sy0 = max(0, -x0), max(0, -y0); dx0, dy0 = max(0, x0), max(0, y0)
    dx1, dy1 = min(W, x0 + s.shape[1]), min(H, y0 + s.shape[0])
    if dx1 <= dx0 or dy1 <= dy0: return
    sub = s[sy0:sy0 + dy1 - dy0, sx0:sx0 + dx1 - dx0]
    rgb = sub[..., :3] * np.array(tint, np.float32); a = sub[..., 3:4] * alpha
    if additive: dst[dy0:dy1, dx0:dx1, :3] += rgb * a
    else: dst[dy0:dy1, dx0:dx1, :3] = rgb * a + dst[dy0:dy1, dx0:dx1, :3] * (1 - a)

def main():
    world, fxp, stp, outp, mode = sys.argv[1:6]
    W_ = world.capitalize()
    cue, heat, cold, shade = [np.array(c, np.float32) for c in DEFS[world]]
    if mode in ('new', 'drop') and world in NEW_HEAT: heat = np.array(NEW_HEAT[world], np.float32)
    code = 'cur' if mode in ('cur', 'drop') else 'new'
    deck = load(R + f'{W_}/Planetfall/{world}_cloud_deck.png')
    dark = load(R + f'{W_}/Planetfall/{world}_cloud_deck_dark.png')
    fx = load(fxp); n = fx.shape[1] // 512
    cells = [fx[:, i * 512:(i + 1) * 512] for i in range(n)]
    streaks = load(stp)
    hull = load(A + 'Art/Resources/ShipArt/Paragon/Paragon.png')[25:231, 3:253]
    flame = load(A + 'Art/Resources/ShipArt/Paragon/Paragon_flame.png')
    flames = [flame[0:193, i * 49:(i + 1) * 49] for i in range(7)]
    fps = 14.0 if code == 'cur' else (16.0 if n >= 12 else 14.0)
    twist = 2.5 if code == 'cur' else 1.5
    # the shroud's fit (Planetfall.FitShroud): opening 147 px of 512 = the hull's width
    shroud_scale = min(5, max(1.6, SHIP_W / (147 / 512.)))
    ship_w = SHIP_W * PPU; ship_h = ship_w * 206 / 250
    vign = None
    frames = []
    darkS = deckS = strS = drift = 0.0
    dt = .05
    t = 1.40
    yy, xx = np.mgrid[0:VH, 0:VW].astype(np.float32)
    vx = (xx + .5) / VW * 2 - 1; vy = (yy + .5) / VH * 2 - 1
    vign = np.clip((np.sqrt(vx * vx * .8 + vy * vy) - .55) / .6, 0, 1); vign = vign * vign * (3 - 2 * vign)
    # the backdrop before the decks: the world's shade with a lighter band
    sky = np.zeros((VH, VW, 3), np.float32) + shade * 1.6 + np.array([.04, .05, .08], np.float32) * (1 - vy[..., None]) * .5
    while t <= 5.35 + 1e-6:
        rush = 1 + 2 * ramp(t, 5.0, 6.3)
        darkS += dt * .28 * rush; deckS += dt * .55 * rush; drift += dt * .015; strS += dt * 1.5
        img = np.concatenate([sky.copy(), np.ones((VH, VW, 1), np.float32)], -1)
        z = zoom(t)
        for tex, pix, sc, dr, al in ((dark, 1 / 110., darkS, -drift, dark_a(t)), (deck, 1 / 70., deckS, drift, deck_a(t))):
            if al <= .002: continue
            scale = PPU * pix * z     # canvas px per art px
            th, tw = tex.shape[:2]
            smp = affine_sample(tex, tw / 2 + dr * tw, th / 2 - sc * th, scale, VW, VH)
            over(img, smp, al)
        # streak strips (additive), outer 30% of the art on each side
        sk = streak_alpha(t)
        if sk > .002:
            share = .3; sw = int(VW * share)
            tile_h = VW * 2.0           # the art spans the view's width; 2048/1024 tall
            sc = VW / 1024.
            ys = (np.arange(VH) / sc - strS * 2048) % 2048
            col = np.clip((np.array(cue) * .65 + .35), 0, 1)
            for side in (-1, 1):
                xs = np.arange(sw) / sc + (0 if side < 0 else 1024 * (1 - share))
                smp = streaks[np.floor(ys).astype(int) % 2048][:, np.floor(xs).astype(int) % 1024]
                x0 = int(-VW * .06) if side < 0 else int(VW * 1.06) - sw
                x0c, x1c = max(0, x0), min(VW, x0 + sw)
                seg = smp[:, x0c - x0:x1c - x0]
                img[:, x0c:x1c, :3] += seg[..., :3] * col * seg[..., 3:4] * sk * .55
        # tint and vignette
        hc = heat * (1 - cool(t)) + cold * cool(t)
        ta = tint_at(t); img[..., :3] = hc * ta + img[..., :3] * (1 - ta)
        va = vig(t) * vign[..., None]; img[..., :3] = shade * va + img[..., :3] * (1 - va)
        # the ship (dive spot: 34% of the height from the bottom, buffeting)
        sa = shroud_alpha(t)
        cx = VW / 2 + sa * .02 * math.sin(t * 9.3) * PPU
        cy = VH - VH * .34 - sa * .03 * math.sin(t * 13.1) * PPU
        # shroud
        if sa > .002:
            deep = ramp(t, 2.0, 5.0) if code == 'cur' else shroud_heat(t)
            cell = int(math.floor(t * fps)) % n
            flick = .035 * (.6 * math.sin(t * 31) + .4 * math.sin(t * 53 + 1.3))
            size = shroud_scale * (1 + .05 * deep + flick)
            pulse = 1 - .12 * (.5 + .5 * math.sin(t * 19 + 2 * math.sin(t * 7)))
            atx = cx + .012 * size * math.sin(t * 37) * PPU
            aty = cy + .1 * SHIP_W * PPU          # HoleDrop: the pivot sits below the hull's middle
            wpx = size * PPU
            stretch = 1.0 if code == 'cur' else (.92 + .16 * deep)
            hpx = wpx * 2 * (1 + .025 * math.sin(t * 23)) * stretch
            alpha = sa if code == 'cur' else sa * (.55 + .45 * deep)
            piv = (256 / 512., 338 / 1024.)
            paste_sprite(img, cells[cell], atx, aty, wpx, hpx, twist * math.sin(t * 17 + 1), (pulse,) * 3, alpha, pivot=piv)
            gsize = size * 1.07 * (1 + .03 * math.sin(t * 41))
            g = heat * .65 + .35
            ga = sa * (.22 + (.55 - .22) * deep) * (.8 + 1.6 * (1 - pulse))
            paste_sprite(img, cells[(cell + n - 1) % n], atx + .01 * size * math.sin(t * 29 + 2) * PPU,
                         aty + .006 * size * math.sin(t * 43) * PPU, gsize * PPU, gsize * PPU * 2,
                         -twist * math.sin(t * 13), tuple(g), ga, additive=True, pivot=piv)
        # engine flames (under the hull), then the hull
        fi = int(t * 20) % 7
        for nx, nsc in ((125.5, 1.0), (24.5, .45), (225.5, .45)):
            fw = ship_w * .2 * nsc; fh = fw * 193 / 49 * (1 + .1 * math.sin(t * 40 + nx))
            fx_ = cx - ship_w / 2 + nx / 250 * ship_w; fy_ = cy + ship_h / 2 - 22 / 206 * ship_h
            paste_sprite(img, flames[fi], fx_, fy_, fw, fh, 0, (1, 1, 1), 1.0, additive=True, pivot=(.5, .04))
        paste_sprite(img, hull, cx, cy, ship_w, ship_h)
        out = Image.fromarray((np.clip(img[..., :3], 0, 1) * 255).astype(np.uint8), 'RGB')
        frames.append(out)
        t += dt
    import subprocess, tempfile, os
    ccx, ccy = VW // 2, int(VH - VH * .34)
    box = (ccx - 243, ccy - 260, ccx + 243, ccy + 520)
    close = [f.crop(box) for f in frames]
    def gif(seq, path, mp4=False):
        d = tempfile.mkdtemp(dir=os.path.dirname(os.path.abspath(path)))
        for i, f in enumerate(seq): f.save(f'{d}/{i:03d}.png')
        subprocess.run(['ffmpeg', '-v', 'error', '-y', '-framerate', '20', '-i', f'{d}/%03d.png', '-vf',
                        'split[a][b];[a]palettegen=max_colors=256:stats_mode=full[p];[b][p]paletteuse=dither=sierra2_4a',
                        '-loop', '0', path], check=True)
        if mp4:
            subprocess.run(['ffmpeg', '-v', 'error', '-y', '-framerate', '20', '-i', f'{d}/%03d.png', '-vf', 'scale=trunc(iw/2)*2:trunc(ih/2)*2', '-pix_fmt', 'yuv420p',
                            '-crf', '18', path.replace('.gif', '.mp4')], check=True)
        for x in os.listdir(d): os.remove(f'{d}/{x}')
        os.rmdir(d)
    gif([f.resize((VW * 5 // (6 * S), VH * 5 // (6 * S)), Image.LANCZOS) for f in frames], outp, mp4=True)
    gif(close, outp.replace('.gif', '_close.gif'))
    # contact sheet of a few moments for review
    picks = [int(i) for i in np.linspace(4, len(frames) - 3, 6)]
    sheet = Image.new('RGB', (6 * 243, 390))
    for k, i in enumerate(picks): sheet.paste(close[i].resize((243, 390)), (k * 243, 0))
    sheet.save(outp.replace('.gif', '_sheet.png'))
    print(outp, len(frames), 'frames')

if __name__ == '__main__':
    main()
