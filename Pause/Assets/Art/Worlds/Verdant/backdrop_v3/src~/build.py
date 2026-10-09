"""Build four painted Verdant parallax sets from selected imagegen sources.

Run from any directory with: python3 path/to/src~/build.py
The source paintings are kept here so the assets can be rebuilt and reviewed.
"""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
W, H = 512, 1024
DAY_TARGET = {"sky": .415, "far": .445, "mid": .475, "flow": .445}
NIGHT_TARGET = {"sky": .335, "far": .365, "mid": .395, "flow": .375}
PLACES = {
    1: "Canopy sea and rivers; cliff waterfalls and rusted relay trellises",
    2: "Swamp delta; braided channels, mangroves and stilt refineries",
    3: "Overgrown ruined city; broken road and rail grid, canals and rooftop vines",
    4: "Bioluminescent night forest; luminous roots, fungus and distant lamps",
}
FLOW_SOURCE = {
    1: "v1_flow_source.png",
    2: "v2_flow_source.png",
    3: "v3_flow_candidate_b.png",
    4: "v4_flow_candidate_b.png",
}
FLOW_SECONDARY = {3: "v3_flow_source.png", 4: "v4_flow_source.png"}
ROLE_NOTES = {
    "sky": "Deepest hazy ground plane; lowest contrast and green-teal atmospheric fade",
    "far": "Distant painted terrain; reduced contrast and more haze than mid",
    "mid": "Nearest detailed painted terrain; restrained highlights for action readability",
    "flow": "Sparse transparent painted scrolling overlay; ground shows through",
}


def source(name: str) -> np.ndarray:
    im = Image.open(SRC / name).convert("RGBA").resize((W, H), Image.Resampling.NEAREST)
    return np.asarray(im).copy()


def wrap_blend(a: np.ndarray, band: int = 68) -> np.ndarray:
    """Offset-and-blend treatment on the two toroidal join lines.

    Pair equal-distance pixels across each edge. This is equivalent to moving
    the edge to the centre, blending that join, and moving it back. The edge
    pixels are identical after treatment; the band tapers to the painted source.
    """
    out = a.astype(np.float32).copy()
    for axis in (1, 0):
        old = out.copy()
        limit = W if axis == 1 else H
        for d in range(band):
            t = .5 * (1 - d / (band - 1)) ** 1.45
            lo, hi = d, limit - 1 - d
            if axis == 1:
                out[:, lo] = old[:, lo] * (1 - t) + old[:, hi] * t
                out[:, hi] = old[:, hi] * (1 - t) + old[:, lo] * t
            else:
                out[lo] = old[lo] * (1 - t) + old[hi] * t
                out[hi] = old[hi] * (1 - t) + old[lo] * t
    # Corner pairs can accumulate sub-byte rounding differently, so lock the
    # exact wrap pixels before palette indexing.
    out[:, -1] = out[:, 0]
    out[-1] = out[0]
    return np.uint8(np.clip(np.rint(out), 0, 255))


def palette_and_value(rgb: np.ndarray, target: float, contrast: float,
                      haze: float = 0, cap: float = .65, colors: int = 48,
                      mask: np.ndarray | None = None) -> np.ndarray:
    f = rgb.astype(np.float32) / 255
    if haze:
        # Teal-green pollen atmosphere keeps source geography visible.
        fog = np.array([.12, .32, .30], dtype=np.float32)
        f = f * (1 - haze) + fog * haze
    old_v = f.max(axis=2)
    p90 = float(np.percentile(old_v if mask is None else old_v[mask], 90))
    new_v = np.clip(target + (old_v - p90) * contrast, .055, cap)
    f *= (new_v / np.maximum(old_v, .001))[..., None]
    indexed = Image.fromarray(np.uint8(np.rint(np.clip(f, 0, 1) * 255)), "RGB").quantize(
        colors=colors, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE
    ).convert("RGB")
    q = np.asarray(indexed).astype(np.float32) / 255
    # Correct indexed palette steps while retaining the same number of colors.
    qv = q.max(axis=2)
    qp90 = float(np.percentile(qv if mask is None else qv[mask], 90))
    q *= np.clip(target / max(qp90, .001), .5, 1.5)
    q = np.clip(q, 0, cap)
    return np.uint8(np.rint(q * 255))


def make_ground(variant: int, role: str) -> Image.Image:
    a = source(f"v{variant}_terrain_a.png")[:, :, :3]
    b = source(f"v{variant}_terrain_b.png")[:, :, :3]
    # Both generated candidates inform each depth. Keeping one predominant
    # preserves each painting's readable river, island, or street structures.
    weights = {"sky": .32, "far": .72, "mid": .16}
    w = weights[role]
    rgb = np.uint8(np.rint(a.astype(np.float32) * (1 - w) + b.astype(np.float32) * w))
    target = (NIGHT_TARGET if variant == 4 else DAY_TARGET)[role]
    contrast = {"sky": .27, "far": .41, "mid": .57}[role]
    haze = {"sky": .38, "far": .18, "mid": .025}[role]
    cap = .53 if variant == 4 else .62
    rgb = palette_and_value(rgb, target, contrast, haze, cap, colors=42 if role == "sky" else 56)
    rgba = np.dstack((rgb, np.full((H, W), 255, np.uint8)))
    rgba = wrap_blend(rgba)
    # Blend can introduce intermediate colors, so index once more, then tune
    # values. Opaque alpha remains untouched.
    rgb = palette_and_value(rgba[:, :, :3], target, 1.0, 0, cap, colors=56)
    rgba = np.dstack((rgb, np.full((H, W), 255, np.uint8)))
    rgba[:, -1] = rgba[:, 0]
    rgba[-1] = rgba[0]
    return Image.fromarray(rgba, "RGBA")


def make_flow(variant: int) -> Image.Image:
    a = source(FLOW_SOURCE[variant])
    if variant in FLOW_SECONDARY:
        secondary = source(FLOW_SECONDARY[variant])
        a = np.asarray(Image.alpha_composite(Image.fromarray(secondary, "RGBA"),
                                             Image.fromarray(a, "RGBA"))).copy()
    # The generated source supplies every mark. Stepped alpha keeps a soft
    # scrolling overlay without baked-in animation or soft raster blur.
    alpha = a[:, :, 3].astype(np.float32)
    alpha = np.where(alpha < 105, 0, np.where(alpha < 170, 72,
                     np.where(alpha < 225, 112, 152))).astype(np.uint8)
    target = (NIGHT_TARGET if variant == 4 else DAY_TARGET)["flow"]
    rgb = palette_and_value(a[:, :, :3], target, .42, .08,
                            .58 if variant == 4 else .62, 36, mask=alpha > 0)
    rgba = np.dstack((rgb, alpha))
    rgba = wrap_blend(rgba, band=80)
    rgba[:, :, :3] = palette_and_value(rgba[:, :, :3], target, 1.0, 0,
                                       .58 if variant == 4 else .62, 36,
                                       mask=rgba[:, :, 3] > 0)
    levels = np.array([0, 72, 112, 152], dtype=np.uint8)
    rgba[:, :, 3] = levels[np.abs(rgba[:, :, 3, None].astype(np.int16) - levels).argmin(axis=2)]
    rgba[:, -1] = rgba[:, 0]
    rgba[-1] = rgba[0]
    return Image.fromarray(rgba, "RGBA")


def stats(im: Image.Image) -> dict:
    a = np.asarray(im)
    mask = a[:, :, 3] > 0
    v = a[:, :, :3].max(axis=2)[mask] / 255
    xdiff = np.abs(a[:, 0].astype(np.int16) - a[:, -1].astype(np.int16))
    ydiff = np.abs(a[0].astype(np.int16) - a[-1].astype(np.int16))
    return {
        "p90_hsv_value_opaque": round(float(np.percentile(v, 90)), 4),
        "opaque_fraction": round(float(mask.mean()), 4),
        "wrap_edge_mean_abs_channel_difference": round(float(np.r_[xdiff.flat, ydiff.flat].mean()), 4),
        "wrap_edge_max_channel_difference": int(max(xdiff.max(), ydiff.max())),
    }


def tiled(im: Image.Image, size: tuple[int, int], offset: tuple[int, int]) -> Image.Image:
    out = Image.new("RGBA", size)
    ox, oy = offset
    for y in range(oy - H, size[1] + H, H):
        for x in range(ox - W, size[0] + W, W):
            out.alpha_composite(im, (x, y))
    return out


def composite_phone(layers: dict[str, Image.Image]) -> Image.Image:
    # 540 native-looking pixels across, then integer x2 export to 1080x2400.
    size = (540, 1200)
    base = tiled(layers["sky"], size, (0, 0))
    for role, shift, opacity in (("far", (30, 135), .43),
                                 ("mid", (75, 340), .68),
                                 ("flow", (10, 85), .90)):
        layer = tiled(layers[role], size, shift)
        if opacity < 1:
            alpha = layer.getchannel("A").point(lambda v: round(v * opacity))
            layer.putalpha(alpha)
        base = Image.alpha_composite(base, layer)
    return base.resize((1080, 2400), Image.Resampling.NEAREST)


def preview(all_layers: dict[int, dict[str, Image.Image]]) -> None:
    sheet = Image.new("RGB", (4 * 1080, 4510), (10, 20, 22))
    d = ImageDraw.Draw(sheet)
    for variant in range(1, 5):
        x0 = (variant - 1) * 1080
        layers = all_layers[variant]
        d.text((x0 + 12, 9), f"V{variant}  {PLACES[variant]}", fill=(198, 227, 183))
        for role, x, y in (("sky", 0, 32), ("far", 542, 32),
                           ("mid", 0, 1065), ("flow", 542, 1065)):
            # Show alpha flow on a dark check field at exact 512x1024 pixels.
            tile = Image.new("RGBA", (W, H), (21, 48, 46, 255))
            tile.alpha_composite(layers[role])
            sheet.paste(tile.convert("RGB"), (x0 + x, y))
            d.text((x0 + x + 5, y + 5), role, fill=(226, 238, 206))
        sheet.paste(composite_phone(layers).convert("RGB"), (x0, 2105))
    sheet.save(ROOT / "preview_variants.png", optimize=True)


def main() -> None:
    entries = []
    all_layers: dict[int, dict[str, Image.Image]] = {}
    for variant in range(1, 5):
        layers = {}
        (ROOT / f"v{variant}").mkdir(exist_ok=True)
        for role in ("sky", "far", "mid", "flow"):
            im = make_flow(variant) if role == "flow" else make_ground(variant, role)
            path = ROOT / f"v{variant}" / f"{role}.png"
            im.save(path, optimize=True)
            s = stats(im)
            low, high = (.32, .42) if variant == 4 else (.40, .50)
            assert im.size == (W, H) and im.mode == "RGBA"
            assert low <= s["p90_hsv_value_opaque"] <= high, (path, s)
            assert s["wrap_edge_max_channel_difference"] == 0, (path, s)
            if role == "flow":
                assert s["opaque_fraction"] < .5, (path, s)
            entries.append({"file": str(path.relative_to(ROOT)), "size": [W, H],
                            "role": role, "variant": f"v{variant}",
                            "notes": f"{PLACES[variant]}. {ROLE_NOTES[role]}.", **s})
            layers[role] = im
            print(path.relative_to(ROOT), s)
        all_layers[variant] = layers
    preview(all_layers)
    manifest = {
        "world": "Verdant",
        "size": [W, H],
        "format": "RGBA PNG",
        "source_method": "Built-in image generation: two painted terrain candidates and two painted transparent flow candidates per variant; inspected flow selection, nearest-neighbor resize, palette indexing, depth haze, offset-and-blend wrap join",
        "selected_flow_sources": {f"v{k}": v for k, v in FLOW_SOURCE.items()},
        "secondary_flow_sources": {f"v{k}": v for k, v in FLOW_SECONDARY.items()},
        "preview": "preview_variants.png",
        "files": entries,
    }
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")


if __name__ == "__main__":
    main()
