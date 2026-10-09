"""Finish the painted Ember sources into four repeatable parallax sets.

Run: python3 Pause/Assets/Art/Worlds/Ember/backdrop_v3/src~/build.py
Only the imagegen paintings in this src~ directory supply scene content.
"""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
W, H = 512, 1024
ROLES = ("sky", "far", "mid", "flow")
NAMES = {
    1: "Lava caldera fields",
    2: "Forge city",
    3: "Ash plains and ember rivers",
    4: "Obsidian night volcano",
}
NOTES = {
    "sky": "Deepest hazy far-ground plane, warm brown-violet ash haze, lowest contrast",
    "far": "Distant top-down ground with reduced contrast and warm atmospheric haze",
    "mid": "Near detailed top-down ground with restrained highlights for action readability",
    "flow": "Sparse transparent painted scrolling marks revealing layers beneath",
}
TARGETS = {
    1: dict(sky=.415, far=.445, mid=.475, flow=.445),
    2: dict(sky=.415, far=.445, mid=.475, flow=.445),
    3: dict(sky=.415, far=.445, mid=.475, flow=.445),
    4: dict(sky=.335, far=.365, mid=.395, flow=.375),
}


def load(name: str) -> np.ndarray:
    im = Image.open(SRC / name).convert("RGBA")
    # Generated paintings are portrait, but their exact pixel dimensions vary.
    width = min(im.width, im.height // 2)
    x = (im.width - width) // 2
    y = (im.height - 2 * width) // 2
    im = im.crop((x, y, x + width, y + 2 * width))
    return np.asarray(im.resize((W, H), Image.Resampling.NEAREST)).copy()


def wrap_blend(a: np.ndarray, band: int) -> np.ndarray:
    """Blend opposing edge bands, then lock the last sample to the first."""
    out = a.astype(np.float32)
    for axis, length in ((1, W), (0, H)):
        old = out.copy()
        for d in range(band):
            t = .5 * (1 - d / (band - 1)) ** 1.5
            lo, hi = d, length - 1 - d
            if axis == 1:
                out[:, lo] = old[:, lo] * (1 - t) + old[:, hi] * t
                out[:, hi] = old[:, hi] * (1 - t) + old[:, lo] * t
            else:
                out[lo] = old[lo] * (1 - t) + old[hi] * t
                out[hi] = old[hi] * (1 - t) + old[lo] * t
    out = np.uint8(np.clip(np.rint(out), 0, 255))
    out[:, -1] = out[:, 0]
    out[-1] = out[0]
    return out


def palette(rgb: np.ndarray, target: float, contrast: float,
            haze: float, colors: int, cap: float,
            mask: np.ndarray | None = None) -> np.ndarray:
    f = rgb.astype(np.float32) / 255
    if haze:
        fog = np.array([.32, .245, .265], dtype=np.float32)
        f = f * (1 - haze) + fog * haze
    old_v = f.max(axis=2)
    sample = old_v if mask is None else old_v[mask]
    p90 = float(np.percentile(sample, 90))
    new_v = np.clip(target + (old_v - p90) * contrast, .065, cap)
    f *= (new_v / np.maximum(old_v, .001))[..., None]
    indexed = Image.fromarray(np.uint8(np.rint(np.clip(f, 0, 1) * 255)), "RGB").quantize(
        colors=colors, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE
    ).convert("RGB")
    q = np.asarray(indexed).copy()
    # Keep yellow-orange and muted brown-violet, and rotate any generated
    # crimson or green pixels into the Ember hue range. Greys have no hue.
    hsv = np.asarray(Image.fromarray(q, "RGB").convert("HSV")).copy()
    hue = hsv[:, :, 0].astype(np.float32) * 360 / 255
    sat = hsv[:, :, 1]
    grey = sat < 12
    violet = (hue >= 270) & (hue <= 325) & ~grey
    amber = (hue >= 25) & (hue <= 65) & ~grey
    hsv[:, :, 0][~(violet | amber | grey)] = np.uint8(round(35 * 255 / 360))
    hsv[:, :, 1][violet] = np.minimum(sat[violet], 82)
    hsv[:, :, 1][grey] = 0
    q = np.asarray(Image.fromarray(hsv, "HSV").convert("RGB")).astype(np.float32)
    value = q.max(axis=2) / 255
    p90 = float(np.percentile(value if mask is None else value[mask], 90))
    q *= target / max(p90, .001)
    q = np.clip(q, 0, cap * 255)
    return np.uint8(np.rint(q))


def ground(variant: int, role: str) -> Image.Image:
    a = load(f"v{variant}_terrain_a.png")[:, :, :3]
    b = load(f"v{variant}_terrain_b.png")[:, :, :3]
    if role == "mid":
        painted = a
    elif role == "far":
        painted = np.roll(b, (143, 61), axis=(0, 1))
    else:
        # The two independent paintings make a quiet deepest plane.
        painted = np.uint8(np.rint(a.astype(np.float32) * .20 +
                                    np.roll(b, (241, -49), axis=(0, 1)).astype(np.float32) * .80))
    target = TARGETS[variant][role]
    contrast = dict(sky=.29, far=.44, mid=.65)[role]
    haze = dict(sky=.36, far=.18, mid=.025)[role]
    cap = .59 if variant == 4 else .69
    rgb = palette(painted, target, contrast, haze, 44 if role == "sky" else 58, cap)
    rgba = np.dstack((rgb, np.full((H, W), 255, np.uint8)))
    rgba = wrap_blend(rgba, 78)
    rgba[:, :, :3] = palette(rgba[:, :, :3], target, 1, 0, 58, cap)
    if role == "mid":
        # Restore only the tiny hottest painted furnace/lava cores. The broad
        # lava fields stay in the restrained ground-value range.
        cores = (painted[:, :, 0] > 245) & (painted[:, :, 1] > 205)
        rgba[:, :, :3][cores] = (216, 172, 77) if variant == 4 else (228, 192, 101)
    elif role == "far":
        cores = (painted[:, :, 0] > 250) & (painted[:, :, 1] > 225)
        rgba[:, :, :3][cores] = (169, 122, 53)
    rgba[:, -1] = rgba[:, 0]
    rgba[-1] = rgba[0]
    return Image.fromarray(rgba, "RGBA")


def flow(variant: int) -> Image.Image:
    a = Image.fromarray(load(f"v{variant}_flow_a.png"), "RGBA")
    b = Image.fromarray(np.roll(load(f"v{variant}_flow_b.png"), (153, 47), axis=(0, 1)), "RGBA")
    painted = np.asarray(Image.alpha_composite(a, b)).copy()
    alpha = painted[:, :, 3]
    alpha = np.select([alpha < 45, alpha < 110, alpha < 190],
                      [0, 64, 112], default=160).astype(np.uint8)
    mask = alpha > 0
    target = TARGETS[variant]["flow"]
    cap = .59 if variant == 4 else .69
    rgb = palette(painted[:, :, :3], target, .48, .02, 40, cap, mask)
    rgba = wrap_blend(np.dstack((rgb, alpha)), 82)
    levels = np.array([0, 64, 112, 160], dtype=np.uint8)
    rgba[:, :, 3] = levels[np.abs(rgba[:, :, 3, None].astype(np.int16) - levels).argmin(axis=2)]
    mask = rgba[:, :, 3] > 0
    rgba[:, :, :3] = palette(rgba[:, :, :3], target, 1, 0, 40, cap, mask)
    rgba[:, :, :3][~mask] = 0
    rgba[:, -1] = rgba[:, 0]
    rgba[-1] = rgba[0]
    return Image.fromarray(rgba, "RGBA")


def stats(im: Image.Image) -> dict:
    a = np.asarray(im)
    mask = a[:, :, 3] > 0
    v = a[:, :, :3].max(axis=2)[mask] / 255
    dx = np.abs(a[:, 0].astype(np.int16) - a[:, -1].astype(np.int16))
    dy = np.abs(a[0].astype(np.int16) - a[-1].astype(np.int16))
    return {
        "p90_hsv_value_opaque": round(float(np.percentile(v, 90)), 4),
        "opaque_fraction": round(float(mask.mean()), 4),
        "wrap_edge_mean_abs_channel_difference": round(float(np.r_[dx.flat, dy.flat].mean()), 4),
        "wrap_edge_max_channel_difference": int(max(dx.max(), dy.max())),
    }


def tiled(im: Image.Image, size: tuple[int, int], offset: tuple[int, int]) -> Image.Image:
    out = Image.new("RGBA", size)
    ox, oy = offset
    for y in range(oy % H - H, size[1], H):
        for x in range(ox % W - W, size[0], W):
            out.alpha_composite(im, (x, y))
    return out


def phone(layers: dict[str, Image.Image]) -> Image.Image:
    base = tiled(layers["sky"], (540, 1200), (0, 0))
    for role, shift, opacity in (("far", (30, 135), .42),
                                 ("mid", (75, 340), .66),
                                 ("flow", (10, 85), .90)):
        layer = tiled(layers[role], (540, 1200), shift)
        layer.putalpha(layer.getchannel("A").point(lambda x: round(x * opacity)))
        base = Image.alpha_composite(base, layer)
    return base.resize((1080, 2400), Image.Resampling.NEAREST)


def preview(all_layers: dict[int, dict[str, Image.Image]]) -> None:
    sheet = Image.new("RGB", (4320, 4510), (25, 18, 25))
    d = ImageDraw.Draw(sheet)
    for variant in range(1, 5):
        x0 = (variant - 1) * 1080
        layers = all_layers[variant]
        d.text((x0 + 12, 9), f"V{variant}  {NAMES[variant]}", fill=(226, 187, 136))
        for role, x, y in (("sky", 0, 32), ("far", 542, 32),
                           ("mid", 0, 1065), ("flow", 542, 1065)):
            tile = Image.new("RGBA", (W, H), (38, 28, 35, 255))
            tile.alpha_composite(layers[role])
            sheet.paste(tile.convert("RGB"), (x0 + x, y))
            d.text((x0 + x + 5, y + 5), role.upper(), fill=(237, 203, 148))
        sheet.paste(phone(layers).convert("RGB"), (x0, 2105))
    sheet.save(ROOT / "preview_variants.png", optimize=True)


def main() -> None:
    entries = []
    all_layers = {}
    for variant in range(1, 5):
        out_dir = ROOT / f"v{variant}"
        out_dir.mkdir(exist_ok=True)
        layers = {}
        for role in ROLES:
            im = flow(variant) if role == "flow" else ground(variant, role)
            path = out_dir / f"{role}.png"
            im.save(path, optimize=True)
            s = stats(im)
            low, high = (.32, .42) if variant == 4 else (.40, .50)
            assert im.size == (W, H) and im.mode == "RGBA"
            assert low <= s["p90_hsv_value_opaque"] <= high, (path, s)
            assert s["wrap_edge_max_channel_difference"] == 0, (path, s)
            if role == "flow":
                assert s["opaque_fraction"] < .25, (path, s)
            entries.append({"file": str(path.relative_to(ROOT)), "size": [W, H],
                            "role": role, "variant": f"v{variant}",
                            "notes": f"{NAMES[variant]}. {NOTES[role]}.", **s})
            layers[role] = im
            print(path.relative_to(ROOT), s)
        all_layers[variant] = layers
    preview(all_layers)
    manifest = {
        "world": "Ember",
        "view": "Orthographic atmosphere-level surface beneath ash clouds",
        "size": [W, H], "format": "RGBA PNG",
        "source_method": "Sixteen built-in imagegen paintings: two terrain and two transparent flow candidates per place; nearest-neighbor crop, discrete palette, depth haze, color limit, two-axis wrap blending",
        "preview": "preview_variants.png",
        "files": entries,
    }
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")


if __name__ == "__main__":
    main()
