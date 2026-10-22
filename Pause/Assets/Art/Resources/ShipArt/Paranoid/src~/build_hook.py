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
