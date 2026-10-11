"""Compose generated Desert worm paintings into ground-anchored flipbooks.

The four source PNGs are image-generation outputs. This file only crops, moves,
recolours and nearest-neighbour resamples their painted pixels.
"""
from __future__ import annotations

import json
import math
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src~"
NN = Image.Resampling.NEAREST
SOURCES = {
    "s": ("candidate_arc_a.png", (820, 1080), 0.110),
    "m": ("candidate_arc_c.png", (565, 1090), 0.150),
    "l": ("candidate_arc_b.png", (720, 1085), 0.350),
}
SPECS = {
    "s": (256, 8, (("ridge", 8), ("emerge", 5), ("arc", 5), ("dive", 5), ("spray", 5), ("cascade", 4))),
    "m": (256, 8, (("ridge", 8), ("emerge", 5), ("arc", 5), ("dive", 5), ("spray", 5), ("cascade", 4))),
    "l": (512, 4, (("ridge", 4), ("emerge", 3), ("arc", 3), ("dive", 3), ("spray", 3))),
}
FPS = {"ridge": 10, "emerge": 12, "arc": 8, "dive": 12, "spray": 14, "cascade": 10}


def clean(im: Image.Image, material: str) -> Image.Image:
    """Preserve generated local texture while pushing it into the Desert ramps."""
    p = np.asarray(im.convert("RGBA"), dtype=np.uint8).copy()
    rgb = p[:, :, :3].astype(np.float32) / 255
    mx, mn = rgb.max(2), rgb.min(2)
    delta = mx - mn
    val = mx
    sat = np.divide(delta, mx, out=np.zeros_like(mx), where=mx > 0)
    hue = np.zeros_like(mx)
    nz = delta > 1e-5
    r, g, b = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
    z = nz & (mx == r)
    hue[z] = (60 * ((g[z] - b[z]) / delta[z])) % 360
    z = nz & (mx == g)
    hue[z] = 60 * ((b[z] - r[z]) / delta[z] + 2)
    z = nz & (mx == b)
    hue[z] = 60 * ((r[z] - g[z]) / delta[z] + 4)
    # The generator adds bright red/yellow fringes around transparency. Remove
    # those first; they are neither painted sand nor allowed Desert pixels.
    fringe = ((hue >= 345) | (hue <= 16) | ((hue >= 290) & (hue <= 345))) & (sat > .48)
    fringe |= ((hue >= 48) & (hue <= 80) & (sat > .70))
    p[:, :, 3][fringe] = 0
    p[:, :, 3][p[:, :, 3] < 28] = 0
    # Match dark background values, keeping all image-generated shading bands.
    copper = (material == "body") & (hue >= 16) & (hue < 39) & (sat > .29)
    indigo = (hue >= 205) & (hue <= 270) & (sat > .11)
    dark = (val < .18) & ~copper
    hh = np.full_like(hue, 44.0)
    ss = np.minimum(.29, .16 + sat * .29)
    vv = np.clip(.10 + val * .44, .10, .54)
    if material == "sand":
        crest = (val > .90) & (sat < .50)
        vv[crest] = np.minimum(.62, .04 + val[crest] * .62)
    if material == "body":
        # Copper stays matte; mouth plates retain the lightest bone tones.
        hh[copper], ss[copper] = 25.0, np.minimum(.58, .36 + sat[copper] * .27)
        vv[copper] = np.minimum(.55, .09 + val[copper] * .50)
        pale = (sat < .37) & (val > .63) & ~copper
        vv[pale] = np.minimum(.60, .17 + val[pale] * .50)
    hh[indigo], ss[indigo] = 238.0, np.clip(sat[indigo], .28, .43)
    vv[indigo] = np.minimum(vv[indigo], .29)
    hh[dark], ss[dark] = 238.0, .32
    vv[dark] = np.minimum(vv[dark], .20)
    # Small, opaque throat glints remain pale sun-white, never amber.
    hi = hh / 60
    c = vv * ss
    x = c * (1 - np.abs(hi % 2 - 1))
    m = vv - c
    out = np.zeros_like(rgb)
    for k, (a, b, d) in enumerate(((c, x, 0), (x, c, 0), (0, c, x), (0, x, c), (x, 0, c), (c, 0, x))):
        z = (hi >= k) & (hi < k + 1)
        out[:, :, 0][z] = a if isinstance(a, int) else a[z]
        out[:, :, 1][z] = b if isinstance(b, int) else b[z]
        out[:, :, 2][z] = d if isinstance(d, int) else d[z]
    p[:, :, :3] = np.clip((out + m[:, :, None]) * 255, 0, 255).astype(np.uint8)
    return Image.fromarray(p, "RGBA")


def alpha_composite_at(dst: Image.Image, src: Image.Image, x: int, y: int) -> None:
    dst.alpha_composite(src, (x, y))


def warp_top(src: Image.Image, amplitude: float, phase: float, anchor_y: int) -> Image.Image:
    """Move the visible upper mass while the crater origin stays fixed."""
    a = np.asarray(src)
    out = np.zeros_like(a)
    h = a.shape[0]
    for y in range(h):
        weight = max(0., (anchor_y - y) / max(1, anchor_y - 20)) ** 1.35
        dx = round(amplitude * math.sin(phase) * weight)
        dy = round(amplitude * .42 * math.cos(phase) * weight)
        source_y = y - dy
        if not (0 <= source_y < h):
            continue
        if dx >= 0:
            out[y, dx:] = a[source_y, :a.shape[1] - dx]
        else:
            out[y, :dx] = a[source_y, -dx:]
    return Image.fromarray(out, "RGBA")


def body(scale: str, cell: int, height_factor: float = 1., width_factor: float = 1., phase: float | None = None) -> Image.Image:
    filename, origin, ratio = SOURCES[scale]
    base = BODY[scale]
    # Crop the low outer sand apron, retaining the grounded creature painting.
    base = base.crop((0, 0, base.width, 1110))
    w = round(base.width * ratio * width_factor)
    h = round(base.height * ratio * height_factor)
    art = base.resize((w, h), NN)
    target = Image.new("RGBA", (cell, cell))
    anchor = (cell // 2, cell - 20)
    x = round(anchor[0] - origin[0] * ratio * width_factor)
    y = round(anchor[1] - origin[1] * ratio * height_factor)
    alpha_composite_at(target, art, x, y)
    if phase is not None:
        target = warp_top(target, 3 if cell == 256 else 7, phase, anchor[1])
    return target


def source_fit(im: Image.Image, cell: int, width: int, height: int, bottom: int, shift_x: int = 0) -> Image.Image:
    alpha = np.asarray(im.getchannel("A"))
    ys, xs = np.where(alpha > 45)
    crop = im.crop((max(0, int(xs.min()) - 4), max(0, int(ys.min()) - 4),
                    min(im.width, int(xs.max()) + 5), min(im.height, int(ys.max()) + 5)))
    art = crop.resize((width, height), NN)
    out = Image.new("RGBA", (cell, cell))
    alpha_composite_at(out, art, (cell - width) // 2 + shift_x, bottom - height)
    return out


def ridge(scale: str, cell: int, i: int, n: int) -> Image.Image:
    wh = {"s": (112, 148), "m": (166, 205), "l": (332, 390)}[scale]
    base = source_fit(RIDGE, cell, *wh, bottom=cell - 7)
    return warp_top(base, 4 if cell == 256 else 8, 2 * math.pi * i / n, cell - 20)


def spray(scale: str, cell: int, factor: float, opacity: float = .70) -> Image.Image:
    width = round({"s": 134, "m": 168, "l": 360}[scale] * factor)
    height = round({"s": 111, "m": 139, "l": 292}[scale] * factor)
    out = source_fit(SPRAY, cell, width, height, bottom=cell - 7)
    a = np.asarray(out).copy()
    a[:, :, 3] = np.round(a[:, :, 3].astype(np.float32) * opacity).astype(np.uint8)
    return Image.fromarray(a, "RGBA")


def over(base: Image.Image, top: Image.Image) -> Image.Image:
    dst = base.copy()
    dst.alpha_composite(top)
    return dst


def frame(scale: str, seq: str, i: int, n: int, cell: int) -> Image.Image:
    if seq == "ridge":
        return ridge(scale, cell, i, n)
    if seq == "arc":
        return body(scale, cell, phase=2 * math.pi * i / n)
    if seq == "emerge":
        t = i / (n - 1)
        if i == 0:
            return over(ridge(scale, cell, 0, 8), spray(scale, cell, .42, .28))
        return over(body(scale, cell, .23 + .77 * t, .73 + .27 * t),
                    spray(scale, cell, .55 + .35 * math.sin(math.pi * t), .35))
    if seq == "dive":
        if i == n - 1:
            return over(ridge(scale, cell, 0, 8), spray(scale, cell, .42, .28))
        t = i / (n - 1)
        return over(body(scale, cell, 1.0 - .78 * t, 1.0 - .13 * t,
                         phase=math.pi * (.2 + .55 * t)),
                    spray(scale, cell, .57 + .37 * t, .22 + .36 * t))
    if seq == "spray":
        factors = [(.42, .45), (.72, .64), (1., .70), (.82, .50), (.54, .30)] if n == 5 else [(.45, .45), (1., .70), (.56, .32)]
        f, a = factors[i]
        return spray(scale, cell, f, a)
    if seq == "cascade":
        # The generated ridge supplies painted sand; only the tail is shown.
        art = ridge(scale, cell, i, n)
        a = np.asarray(art).copy()
        yy = np.arange(cell)[:, None]
        fade = np.clip((yy - cell * .30) / (cell * .38), 0, 1)
        a[:, :, 3] = (a[:, :, 3] * fade * .62).astype(np.uint8)
        return Image.fromarray(a, "RGBA")
    raise ValueError(seq)


def align_anchor(im: Image.Image, cell: int) -> Image.Image:
    """Center the visible ground contact band on the shared origin."""
    anchor = cell - 20
    a = np.asarray(im)
    xx = np.where(a[anchor - 3:anchor + 4, :, 3] > 96)[1]
    if not len(xx):
        return im
    dx = round(cell / 2 - float(np.median(xx)))
    if dx == 0:
        return im
    out = Image.new("RGBA", (cell, cell))
    out.alpha_composite(im, (dx, 0))
    return out


def make_atlas(scale: str) -> tuple[Image.Image, dict, dict]:
    cell, cols, groups = SPECS[scale]
    atlas = Image.new("RGBA", (2048, 2048))
    sprites = []
    sequence_data = {}
    idx = 0
    for seq, n in groups:
        names = []
        for i in range(n):
            img = align_anchor(frame(scale, seq, i, n, cell), cell)
            x = (idx % cols) * cell
            y = (idx // cols + (4 if scale == "m" else 0)) * cell
            atlas.alpha_composite(img, (x, y))
            name = f"worm_{scale}_{seq}_{i:02d}"
            sprites.append({"n": name, "x": x, "y": 2048 - y - cell, "w": cell, "h": cell})
            names.append(name)
            idx += 1
        sequence_data[seq] = {"frames": names, "fps": FPS[seq], "anchor": [cell // 2, cell - 20]}
    return atlas, {"sprites": sprites}, sequence_data


def fix_blended_indigo(im: Image.Image) -> Image.Image:
    # The translucent sand burst can shift a few deep-blue pixels to violet.
    # Keep that blended shadow in the permitted dusty-indigo band.
    a = np.asarray(im).copy()
    r, g, b = a[:, :, 0], a[:, :, 1], a[:, :, 2]
    violet = (b > r) & (r > g) & (b.astype(np.int16) - g.astype(np.int16) > 7) & (a[:, :, 3] > 0)
    a[:, :, 1][violet] = r[violet]
    return Image.fromarray(a, "RGBA")


def main() -> None:
    global BODY, RIDGE, SPRAY
    BODY = {s: clean(Image.open(SRC / v[0]), "body") for s, v in SOURCES.items()}
    RIDGE = clean(Image.open(SRC / "candidate_ridge.png"), "sand")
    SPRAY = clean(Image.open(SRC / "candidate_spray.png"), "sand")
    small, sj, sd = make_atlas("s")
    med, mj, md = make_atlas("m")
    small.alpha_composite(med)
    giant, gj, gd = make_atlas("l")
    fix_blended_indigo(small).save(ROOT / "worm.png", optimize=True)
    fix_blended_indigo(giant).save(ROOT / "worm_giant.png", optimize=True)
    (ROOT / "worm.json").write_text(json.dumps({"sprites": sj["sprites"] + mj["sprites"]}, indent=2) + "\n")
    (ROOT / "worm_giant.json").write_text(json.dumps(gj, indent=2) + "\n")
    manifest = {"format": "Pause Desert ambient worm v1", "draw_order": "ground, behind gameplay",
                "recommended_render_alpha": {"body": .85, "spray": .70},
                "anchor_convention": "top-left cell coordinates, emergence origin at bottom center",
                "sources": {"s": "src~/candidate_arc_a.png", "m": "src~/candidate_arc_c.png",
                            "l": "src~/candidate_arc_b.png", "ridge": "src~/candidate_ridge.png",
                            "spray": "src~/candidate_spray.png"},
                "scales": {"s": sd, "m": md, "l": gd}}
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")


if __name__ == "__main__":
    main()
