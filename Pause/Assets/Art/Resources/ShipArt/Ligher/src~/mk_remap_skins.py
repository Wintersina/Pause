"""Amber and Bone skins as controlled gradient-map remaps of the FINAL stock sheet (Ligher.png), so the drawing, the
damage on every frame and the alpha are identical to stock by construction.
The copper armour is found by hue (orange band, real saturation); the cyan gem, red lights, steel trim, white flash
column and the hot glows of the damage (saturated, near full value) are left alone. The armour's brightness (V) is
pushed through an explicit colour ramp per skin.
Run after the stock sheet is final:  python3 mk_remap_skins.py   (writes Ligher_Amber.bytes, Ligher_Bone.bytes)."""
import colorsys
import os
import numpy as np
from PIL import Image
S = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
CELL = 256


def hexrgb(h): return np.array([int(h[i:i + 2], 16) for i in (1, 3, 5)], float)


# (stop value V in 0..1, colour)
RAMPS = {
    # rich amber-gold / honey metal, dark chocolate trim in the seams, warm gold rim light on the plate edges
    'Amber': [(0.00, '#120A04'), (0.18, '#2C1A08'), (0.38, '#6B430F'), (0.60, '#B07A14'),
              (0.78, '#E0A826'), (0.92, '#F7CF5E'), (1.00, '#FFF0A8')],
    # clean warm ivory with tan trim
    'Bone': [(0.00, '#1C150E'), (0.18, '#4A3B2A'), (0.38, '#9C8868'), (0.58, '#D3C2A0'),
             (0.78, '#EADFC4'), (0.92, '#F7EFDC'), (1.00, '#FFFBEF')],
}


def remap(sheet, ramp):
    rgb = sheet[..., :3].astype(float) / 255.0
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx, mn = rgb.max(2), rgb.min(2)
    v = mx
    s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    d = np.maximum(mx - mn, 1e-6)
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60.0
    arm = (((h >= 11) | ((h >= 3) & (v < 0.8))) & (h <= 44) & (s > 0.30) & (v > 0.07))
    glow = (s > 0.75) & (v > 0.96)               # hot glows (damage tears, embers, nozzle core) stay as they are
    # glows are kept only where they belong to DAMAGE (pixels that differ from the intact row); the stock rim
    # lights on intact plating are remapped with the armour so no orange speckle is left on the new colourway
    dmg = np.zeros(glow.shape, bool)
    for rr in (1, 2):
        dmg[rr * CELL:(rr + 1) * CELL] = np.abs(sheet[rr * CELL:(rr + 1) * CELL, :, :3].astype(int)
                                                - sheet[0:CELL, :, :3].astype(int)).sum(2) > 24
    arm &= ~(glow & dmg)
    arm &= sheet[..., 3] > 0
    # the hit-flash column is bone white already
    arm[:, 8 * CELL:] = False
    stops = np.array([p for p, _ in ramp]); cols = np.array([hexrgb(c) for _, c in ramp])
    # the stock armour spans roughly V 0.10 .. 1.0; stretch so shadows reach the dark trim end
    vv = np.clip((v - 0.10) / 0.90, 0, 1)
    out = sheet.copy()
    mapped = np.stack([np.interp(vv, stops, cols[:, k]) for k in range(3)], axis=2)
    out[..., :3] = np.where(arm[..., None], np.round(mapped).astype(np.uint8), sheet[..., :3])
    return out


stock = np.array(Image.open(os.path.join(S, 'Ligher.png')).convert('RGBA'))
for name, ramp in RAMPS.items():
    out = remap(stock, ramp)
    assert (out[..., 3] == stock[..., 3]).all()
    Image.fromarray(out).save(os.path.join(S, 'Ligher_%s.bytes' % name), format='PNG', optimize=True)
    print(name, 'written')
