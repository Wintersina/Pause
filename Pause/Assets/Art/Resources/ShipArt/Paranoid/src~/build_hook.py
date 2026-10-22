# exec'd by ShipArt/src~/build_restyle.py (in its globals) when this file exists.
# Paranoid's hull is green, so all of its Codex paintings are on a flat #FF00FF key instead of #00FF00.
# Nothing on the ship is pink, so the key needs no despill.
KEYRGB = np.array([255, 0, 255], dtype=np.uint8)


def key_magenta(img):
    a = np.array(img).astype(int)
    dist = np.sqrt((a[..., 0] - 255) ** 2 + a[..., 1] ** 2 + (a[..., 2] - 255) ** 2)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    bg = (dist < 120) | ((r - g > 70) & (b - g > 70))
    out = np.dstack([np.clip(a, 0, 255).astype(np.uint8), np.where(bg, 0, 255).astype(np.uint8)])
    out[bg] = 0
    return Image.fromarray(out)


def is_green(rgb):          # used by hit_flash(): "is background"
    v = rgb.astype(np.int16)
    r, g, b = v[..., 0], v[..., 1], v[..., 2]
    return ((r - g > 70) & (b - g > 70)) | ((r > 200) & (b > 200) & (g < 90))


bp.key_green = key_magenta


# Sprite rect padding. measure() crops the rect 1 px outside the art; the old Paranoid sprite had ~5 px of
# clear margin per side (rect 243 for a 232 px hull). Game code normalises every hull sprite to a fixed size
# (shopingShips.NormalizedHullScale) and the spawn-row gap is sized against the widest hull hit polygon, so a tight
# rect made this wide, rectangular-podded hull 4% bigger on screen than before and tipped RailsShipSizeTest
# (widest 1.35x hitbox 0.756 u > gap 0.754 u). Pad back to the old margin; nozzle points are rect-relative.
_measure_tight = measure
PAD = 5


def measure(sheet, cfg):
    d = _measure_tight(sheet, cfg)
    x, y, w, h = d["rect"]
    x2, y2 = max(0, x - PAD), max(0, y - PAD)
    w2 = min(CELL, x + w + PAD) - x2
    h2 = min(CELL, y + h + PAD) - y2
    dx, dy = x - x2, y - y2
    d["rect"] = [x2, y2, w2, h2]
    d["ppu"] = round(w2 / cfg["world_w"], 4)
    d["nozzles"] = [(round(a + dx, 1), round(b + dy, 1), k) for a, b, k in d["nozzles"]]
    return d
