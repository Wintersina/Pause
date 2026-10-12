"""Build four Storm parallax sets from image-generated painted sources.

Run from any directory: python3 path/to/build.py
Only this staged backdrop_v3~ directory is written.
"""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
W, H = 512, 1024
BG = (11, 11, 26)
SOURCES = {
    "v1": {"sky": "v1_b", "far": "v1_a", "mid": "v1_b"},
    "v2": {"sky": "v2_b", "far": "v2_b", "mid": "v2_a"},
    "v3": {"sky": "v3_b", "far": "v3_b", "mid": "v3_a"},
    "v4": {"sky": "v4_b", "far": "v4_b", "mid": "v4_a"},
}
P90_TARGETS = {
    "v1": {"sky": .35, "far": .40, "mid": .45},
    "v2": {"sky": .36, "far": .41, "mid": .46},
    "v3": {"sky": .34, "far": .39, "mid": .44},
    "v4": {"sky": .27, "far": .32, "mid": .37},
}
NOTES = {
    "v1": "Long diagonal sheared bands, hail-grey belt, small curl scars, faint buried lightning.",
    "v2": "Overhead cauliflower thunderhead crowns, deep canyons and hard crest rims.",
    "v3": "One immense eye and anvil spiral walls; wrapped arms carry the cyclone beyond the tile.",
    "v4": "Night-side graphite billows lit internally by blue-white sheet lightning.",
}


def painted(name: str, role: str, variant: str) -> np.ndarray:
    im = Image.open(HERE / f"{name}.png").convert("RGB")
    im = im.resize((W, H), Image.Resampling.NEAREST)
    a = np.asarray(im, dtype=np.float32) / 255.0
    source_value = a.max(2)
    q = np.percentile(source_value, [0, 25, 50, 75, 90, 99, 99.8, 99.97, 100])
    target = P90_TARGETS[variant][role]
    apex = {"sky": .68, "far": .79, "mid": .95}[role]
    if variant == "v4":
        apex = {"sky": .70, "far": .82, "mid": .98}[role]
    levels = np.array([
        max(.035, target * .13), target * .36, target * .56,
        target * .77, target, target + .12,
        min(apex - .11, target + .23), apex - .025, apex,
    ], dtype=np.float32)
    value = np.interp(source_value, q, levels).astype(np.float32)
    # Preserve the painted local tonal detail, while restricting chroma to slate-indigo.
    source_sat = np.where(source_value > 0, 1 - a.min(2) / np.maximum(source_value, .001), 0)
    sat = np.clip(.10 + source_sat * .35, .08, .30)
    sat *= np.where(value > .54, .75, 1.0)
    # HSV hue 225: R=V(1-S), G=V(1-.75S), B=V. Highlights grow neutral.
    rgb = np.stack((value * (1 - sat), value * (1 - .75 * sat), value), axis=2)
    hot = value > .88
    if np.any(hot):
        t = np.clip((value[hot] - .88) / .10, 0, 1)
        rgb[hot] = rgb[hot] * (1 - t[:, None] * .8) + np.array([.956, .973, 1.0]) * (t[:, None] * .8)
    return np.clip(np.rint(rgb * 255), 0, 255).astype(np.uint8)


def seal(a: np.ndarray, mx: int = 68, my: int = 100) -> np.ndarray:
    """Mix opposing edge strips; centre pixels remain untouched. No resampling or blur."""
    a = a.astype(np.float32).copy()
    for d in range(mx):
        weight = ((mx - d) / mx) ** 2
        l = a[:, d, :].copy()
        r = a[:, W - 1 - d, :].copy()
        common = (l + r) * .5
        a[:, d, :] = l * (1 - weight) + common * weight
        a[:, W - 1 - d, :] = r * (1 - weight) + common * weight
    for d in range(my):
        weight = ((my - d) / my) ** 2
        t = a[d, :, :].copy()
        b = a[H - 1 - d, :, :].copy()
        common = (t + b) * .5
        a[d, :, :] = t * (1 - weight) + common * weight
        a[H - 1 - d, :, :] = b * (1 - weight) + common * weight
    out = np.clip(np.rint(a), 0, 255).astype(np.uint8)
    # Quantized rounding at both ends can differ by one; match the exact outer row/column.
    out[:, -1, :] = out[:, 0, :]
    out[-1, :, :] = out[0, :, :]
    return out


def distant_lights(rgb: np.ndarray, role: str) -> np.ndarray:
    """A few tiny sodium-white glints glimpsed far below the night clouds."""
    count = {"sky": 0, "far": 3, "mid": 6}[role]
    if not count:
        return rgb
    rgb = rgb.copy()
    rng = np.random.default_rng(441 + count)
    value = rgb.max(2)
    valid = (value < np.percentile(value, 28))
    valid[:120] = False
    valid[-120:] = False
    valid[:, :85] = False
    valid[:, -85:] = False
    ys, xs = np.where(valid)
    chosen = rng.choice(len(ys), size=count * 12, replace=False)
    points = []
    for k in chosen:
        x, y = int(xs[k]), int(ys[k])
        if all((x - px) ** 2 + (y - py) ** 2 > 72 ** 2 for px, py in points):
            points.append((x, y))
        if len(points) == count:
            break
    for x, y in points:
        rgb[y, x] = (255, 243, 160)
        for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
            rgb[y + dy, x + dx] = np.maximum(rgb[y + dy, x + dx], (97, 101, 114))
    return rgb


def flow(mid: np.ndarray, seed: int, night: bool) -> np.ndarray:
    rng = np.random.default_rng(seed)
    arr = np.zeros((H, W, 4), dtype=np.uint8)
    # Wisps retain actual painted cloud crest pixels from the selected image source.
    v = mid[:, :, :3].max(2)
    threshold = np.percentile(v, 99.45 if night else 99.25)
    wisp = (v >= threshold)
    yy, xx = np.indices((H, W))
    wisp &= (xx > 18) & (xx < W - 18) & (yy > 26) & (yy < H - 26)
    arr[wisp, :3] = mid[wisp]
    arr[wisp, 3] = 75 if night else 43
    # Slanted wind/rain strokes are a separate sparse transparent overlay.
    sheet = Image.fromarray(arr, "RGBA")
    d = ImageDraw.Draw(sheet, "RGBA")
    count = 920 if night else 720
    for _ in range(count):
        x = int(rng.integers(-28, W + 28))
        y = int(rng.integers(-28, H + 28))
        length = int(rng.integers(7, 23))
        slant = int(round(length * (.33 if seed % 2 else -.32)))
        alpha = int(rng.integers(68, 150) if night else rng.integers(36, 105))
        color = (152, 176, 215, alpha) if night else (137, 155, 187, alpha)
        for ox in (-W, 0, W):
            for oy in (-H, 0, H):
                d.line((x + ox, y + oy, x + slant + ox, y + length + oy), fill=color, width=1)
    arr = np.asarray(sheet).copy()
    arr[:, -1] = arr[:, 0]
    arr[-1] = arr[0]
    return arr


def metric(a: np.ndarray) -> dict:
    opaque = a[:, :, 3] > 0
    v = a[:, :, :3].max(2).astype(np.float32) / 255
    dx = np.abs(a[:, 0].astype(np.int16) - a[:, -1].astype(np.int16))
    dy = np.abs(a[0].astype(np.int16) - a[-1].astype(np.int16))
    return {
        "size": [W, H],
        "p90_value_opaque": round(float(np.percentile(v[opaque], 90)), 4),
        "wrap_edge_max_channel_difference": int(max(dx.max(), dy.max())),
        "wrap_edge_mean_channel_difference": round(float((dx.mean() + dy.mean()) / 2), 5),
        "opaque_fraction": round(float(opaque.mean()), 5),
    }


def make_preview() -> None:
    # Four 1x tile columns (sky, far, mid, flow vertically), plus four real-size
    # 1080x2400 stacked phone composites. No UI or labels are painted into preview.
    preview = Image.new("RGB", (6368, 4096), BG)
    for i, variant in enumerate(SOURCES):
        p = ROOT / variant
        layers = {role: Image.open(p / f"{role}.png").convert("RGBA") for role in ("sky", "far", "mid", "flow")}
        for j, role in enumerate(("sky", "far", "mid", "flow")):
            preview.paste(layers[role], (i * W, j * H), layers[role])
        composite = Image.new("RGBA", (1080, 2400), BG + (255,))
        for role, opacity, xoff, yoff in (("sky", 255, 0, 0), ("far", 136, 150, 350), ("mid", 154, 310, 730), ("flow", 255, 50, 120)):
            src = layers[role].resize((1024, 2048), Image.Resampling.NEAREST)
            tiled = Image.new("RGBA", (1080, 2400))
            for x in range(xoff - 2048, 1080, 1024):
                for y in range(yoff - 4096, 2400, 2048):
                    tiled.alpha_composite(src, (x, y))
            if opacity != 255:
                alpha = tiled.getchannel("A").point(lambda a: a * opacity // 255)
                tiled.putalpha(alpha)
            composite.alpha_composite(tiled)
        preview.paste(composite.convert("RGB"), (2048 + i * 1080, 0))
    preview.save(ROOT / "preview_variants.png", optimize=True)


def main() -> None:
    manifest = {"description": "Storm atmosphere-level ground tiles, all source paintings generated with image_gen.", "files": {}}
    for i, (variant, roles) in enumerate(SOURCES.items(), 1):
        outdir = ROOT / variant
        outdir.mkdir(exist_ok=True)
        mid = None
        for role in ("sky", "far", "mid"):
            rgb = seal(painted(roles[role], role, variant))
            if variant == "v4":
                rgb = distant_lights(rgb, role)
            rgba = np.concatenate((rgb, np.full((H, W, 1), 255, dtype=np.uint8)), axis=2)
            Image.fromarray(rgba, "RGBA").save(outdir / f"{role}.png", optimize=True)
            manifest["files"][f"{variant}/{role}.png"] = {
                "role": role, "variant": variant, "source": f"src~/{roles[role]}.png",
                "notes": NOTES[variant] + f" {role} depth; value target {P90_TARGETS[variant][role]:.2f}.",
                **metric(rgba),
            }
            if role == "mid":
                mid = rgb
        rgba = flow(mid, 7919 + i * 101, variant == "v4")
        Image.fromarray(rgba, "RGBA").save(outdir / "flow.png", optimize=True)
        manifest["files"][f"{variant}/flow.png"] = {
            "role": "flow", "variant": variant, "source": f"src~/{roles['mid']}.png",
            "notes": "Sparse painted crest wisps plus static, slanted rain and wind streaks; transparent scrolling strip.",
            **metric(rgba),
        }
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    make_preview()


if __name__ == "__main__":
    main()
