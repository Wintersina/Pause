"""Check launcher assets and report legibility, safe area, and red ownership."""
import numpy as np
from PIL import Image

from build_icon import HERE, NEAREST, scene


EXPECTED = {
    "master_1024.png": ((1024, 1024), "RGBA"),
    "adaptive_bg_432.png": ((432, 432), "RGBA"),
    "adaptive_fg_432.png": ((432, 432), "RGBA"),
    "legacy_192.png": ((192, 192), "RGBA"),
    "round_192.png": ((192, 192), "RGBA"),
    "ios_1024.png": ((1024, 1024), "RGB"),
    "preview.png": ((1720, 2180), "RGBA"),
}


def red_band(im):
    rgb = np.asarray(im.convert("RGB"), dtype=np.float32) / 255
    hi = rgb.max(axis=2)
    lo = rgb.min(axis=2)
    delta = hi - lo
    hue = np.zeros(hi.shape, dtype=np.float32)
    r, g, b = np.moveaxis(rgb, 2, 0)
    mask = (hi == r) & (delta > 0)
    hue[mask] = ((g[mask] - b[mask]) / delta[mask]) % 6
    mask = (hi == g) & (delta > 0)
    hue[mask] = (b[mask] - r[mask]) / delta[mask] + 2
    mask = (hi == b) & (delta > 0)
    hue[mask] = (r[mask] - g[mask]) / delta[mask] + 4
    hue *= 60
    sat = np.divide(delta, hi, out=np.zeros_like(delta), where=hi > 0)
    return ((hue >= 345) | (hue <= 15)) & (sat >= .35) & (hi >= .2)


def main():
    failures = []
    for name, (size, mode) in EXPECTED.items():
        path = HERE / name
        if not path.exists():
            failures.append(f"missing {name}")
            continue
        im = Image.open(path)
        print(f"{name}: {im.size}, {im.mode}")
        if im.size != size or im.mode != mode:
            failures.append(f"{name}: expected {size} {mode}")

    master = Image.open(HERE / "master_1024.png")
    bg = Image.open(HERE / "adaptive_bg_432.png")
    fg = Image.open(HERE / "adaptive_fg_432.png")
    if np.asarray(master.getchannel("A")).min() != 255:
        failures.append("master has nonopaque pixels")
    if np.asarray(bg.getchannel("A")).min() != 255:
        failures.append("adaptive background has nonopaque pixels")
    alpha = np.asarray(fg.getchannel("A"))
    ys, xs = np.nonzero(alpha > 40)
    max_radius = np.hypot(xs + .5 - 216, ys + .5 - 216).max()
    print(f"adaptive foreground max radius (alpha > 40): {max_radius:.2f} px / 132 px")
    if max_radius > 132:
        failures.append("adaptive foreground exceeds safe circle")

    _, _, logo_master = scene(1024, "rail")
    _, _, logo_adaptive = scene(432, "rail", adaptive=True)
    def bbox_height(layer, crop=None):
        if crop:
            layer = layer.crop(crop)
        reduced = layer.resize((96, 96), NEAREST)
        a = np.asarray(reduced.getchannel("A")) > 40
        yy, xx = np.nonzero(a)
        return int(yy.max() - yy.min() + 1), int(xx.max() - xx.min() + 1)
    h, w = bbox_height(logo_master)
    ah, aw = bbox_height(logo_adaptive, (72, 72, 360, 360))
    print(f"logo bbox at 96 px: master {w}x{h} px; adaptive viewport {aw}x{ah} px")
    if h < 25 or ah < 20:
        failures.append("logo is too small at 96 px")
    logo_alpha = np.asarray(logo_adaptive.getchannel("A")) > 40
    logo_y, logo_x = np.nonzero(logo_alpha)
    logo_radius = np.hypot(logo_x + .5 - 216, logo_y + .5 - 216).max()
    print(f"unclipped adaptive title max radius: {logo_radius:.2f} px / 132 px")
    if logo_radius > 132:
        failures.append("adaptive title artwork is clipped by safe circle")

    red = red_band(master)
    logo_mask = np.asarray(logo_master.getchannel("A")) > 40
    total = int(red.sum())
    from_logo = int((red & logo_mask).sum())
    share = from_logo / total if total else 0
    print(f"red hue 345..15: {total} pixels; {from_logo} overlap logo ({share:.1%})")
    if share < .8:
        failures.append("less than 80% of red pixels overlap logo")

    for filename in ("legacy_192.png", "round_192.png"):
        im = Image.open(HERE / filename)
        a = np.asarray(im.getchannel("A"))
        if any(a[y, x] != 0 for x, y in ((0, 0), (191, 0), (0, 191), (191, 191))):
            failures.append(f"{filename} corners are not transparent")
    ios = Image.open(HERE / "ios_1024.png")
    if "A" in ios.getbands():
        failures.append("iOS asset contains alpha")
    if failures:
        print("FAIL: " + "; ".join(failures))
        raise SystemExit(1)
    print("PASS")


if __name__ == "__main__":
    main()
