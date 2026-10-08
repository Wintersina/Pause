"""Build three painted Frost ground sets with the v1 tile-finishing method.

Sources are image-generation paintings kept beside this script. All resampling
is nearest-neighbour; seam repair selects source pixels rather than blurring.
"""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
V1 = ROOT.parent
W, H = 512, 1024
BACKGROUND = (5, 9, 19, 255)
ORDER = ("sky", "far", "mid", "flow")

SETS = {
    "v2": {
        "name": "Coastline and harbour",
        "description": "Ice-cliff headlands frame a working harbour with breakwaters, docks, cranes, tanks and moored icebreakers.",
        "tiles": {
            "sky":  ("v2_sky_a.png", .50, 20, "A blue-violet hazy continental coast and bay"),
            "far":  ("v2_far_b.png", .52, 24, "Distant enclosed harbour, breakwaters and shoreline"),
            "mid":  ("v2_mid_b.png", .53, 28, "Working docks, cranes, tank farms, icebreakers and cliff shore"),
            "flow": ("v2_flow_b.png", .47, 22, "Sparse harbour floes and dark water-current contours"),
        },
    },
    "v3": {
        "name": "Inland frozen industrial tundra",
        "description": "An inland refinery plain: diagonal rail lines, pipeline corridors, tank farms, towers, frozen lakes and snowdrifts.",
        "tiles": {
            "sky":  ("v3_sky_a.png", .55, 20, "Faint tundra ridges, frozen lakes and roads under mist"),
            "far":  ("v3_far_b.png", .55, 24, "Remote pipeline routes, relay sites and lake basins"),
            "mid":  ("v3_mid_b.png", .68, 28, "Rail sidings, refinery blocks, tanks and snowy ridges"),
            "flow": ("v3_flow_b.png", .49, 22, "Thin separated wind-sculpted snowdrift ribbons"),
        },
    },
    "v4": {
        "name": "Night glacier and crevasses",
        "description": "Long deep crevasses cut broken glacier slabs; faint cyan fissure glow and muted aurora reflections surround survey platforms.",
        "tiles": {
            "sky":  ("v4_sky_a.png", .58, 20, "Distant fractured glacier under blue-violet haze"),
            "far":  ("v4_far_b.png", .54, 24, "Long branched fissure and dim blue-green ice"),
            "mid":  ("v4_mid_a.png", .55, 28, "Deep ice walls, ladders, bridges and small survey rigs"),
            "flow": ("v4_flow_a.png", .62, 22, "Creep cracks, angular shards and low fissure mist"),
        },
    },
}

BAYER8 = np.array([
    [0, 48, 12, 60, 3, 51, 15, 63],
    [32, 16, 44, 28, 35, 19, 47, 31],
    [8, 56, 4, 52, 11, 59, 7, 55],
    [40, 24, 36, 20, 43, 27, 39, 23],
    [2, 50, 14, 62, 1, 49, 13, 61],
    [34, 18, 46, 30, 33, 17, 45, 29],
    [10, 58, 6, 54, 9, 57, 5, 53],
    [42, 26, 38, 22, 41, 25, 37, 21],
], dtype=np.float32) / 64.0


def load_crop(source_name: str) -> np.ndarray:
    im = Image.open(SRC / source_name).convert("RGB")
    crop_w = min(im.width, im.height // 2)
    crop_h = min(im.height, im.width * 2)
    x0 = (im.width - crop_w) // 2
    y0 = (im.height - crop_h) // 2
    im = im.crop((x0, y0, x0 + crop_w, y0 + crop_h))
    return np.asarray(im.resize((W, H), Image.Resampling.NEAREST)).copy()


def seam_splice(source: np.ndarray) -> np.ndarray:
    """Bring all four source borders inside and repair the central cross."""
    rolled = np.roll(source, (H // 2, W // 2), axis=(0, 1))
    yy, xx = np.indices((H, W))
    dx = np.abs(xx - W // 2)
    dy = np.abs(yy - H // 2)
    wx = np.clip((92 - dx) / 72, 0, 1)
    wy = np.clip((144 - dy) / 112, 0, 1)
    probability = 1 - (1 - wx) * (1 - wy)
    chosen = probability > BAYER8[yy % 8, xx % 8]
    return np.where(chosen[..., None], source, rolled)


def alpha_for(variant: str, layer: str, source: np.ndarray) -> np.ndarray:
    value = source.max(axis=2).astype(np.float32) / 255
    if layer == "sky":
        return np.full((H, W), 255, np.uint8)
    if layer == "far":
        return np.where(value < .23, 255, 164).astype(np.uint8)
    if layer == "mid":
        return np.where(value < .24, 255, 186).astype(np.uint8)
    # The source paintings provide floes, snowdrift bands or fissure ice.
    # Lower values represent dark water/ground and reveal lower parallax planes.
    thresholds = {
        "v2": (.14, .19, .26, .34, .44, .55),
        "v3": (.18, .27, .36, .45, .54, .64),
        "v4": (.16, .23, .31, .39, .49, .60),
    }[variant]
    return np.select(
        [value < t for t in thresholds],
        [0, 32, 64, 112, 168, 216],
        default=255,
    ).astype(np.uint8)


def build_tile(variant: str, layer: str, source_name: str,
               gain: float, colors: int) -> Image.Image:
    painted = seam_splice(load_crop(source_name))
    dark = np.clip(np.rint(painted.astype(np.float32) * gain), 0, 255).astype(np.uint8)
    quantized = Image.fromarray(dark, "RGB").quantize(
        colors=colors, method=Image.Quantize.MEDIANCUT,
        dither=Image.Dither.NONE,
    ).convert("RGB")
    rgb = np.asarray(quantized).copy()
    if layer != "flow":
        p = painted.astype(np.float32) / 255
        hi = p.max(axis=2)
        lo = p.min(axis=2)
        sat = (hi - lo) / np.maximum(hi, .001)
        cyan = ((p[:, :, 2] > p[:, :, 0] * 1.45) &
                (p[:, :, 1] > p[:, :, 0] * 1.35) &
                (hi > .80) & (sat > .48))
        magenta = ((p[:, :, 0] > p[:, :, 1] * 1.30) &
                   (p[:, :, 2] > p[:, :, 1] * 1.30) &
                   (hi > .50) & (sat > .35))
        rgb[cyan] = (35, 81, 111) if layer == "sky" else (46, 108, 152)
        rgb[magenta] = (88, 54, 104) if layer == "sky" else (117, 75, 134)
    alpha = alpha_for(variant, layer, painted)
    rgba = np.dstack((rgb, alpha))
    # Matching border samples make the repeat exact in both directions.
    rgba[:, -1] = rgba[:, 0]
    rgba[-1] = rgba[0]
    tile = Image.fromarray(rgba, "RGBA")
    tile.save(ROOT / variant / f"{layer}.png", optimize=True)
    return tile


def tile_on_canvas(tile: Image.Image, width: int, height: int,
                   offset_x: int = 0, offset_y: int = 0,
                   scale: int = 1) -> Image.Image:
    if scale != 1:
        tile = tile.resize((W * scale, H * scale), Image.Resampling.NEAREST)
    tw, th = tile.size
    canvas = Image.new("RGBA", (width, height))
    for y in range(-th + offset_y % th, height, th):
        for x in range(-tw + offset_x % tw, width, tw):
            canvas.alpha_composite(tile, (x, y))
    return canvas


def make_preview(all_tiles: dict[str, dict[str, Image.Image]]) -> None:
    """Four columns: 1:1 layer tiles above a 1080x2400 phone composite."""
    column_w, pad, gap = 1080, 34, 22
    top = 72
    cell_h = H + 34
    phone_top = top + cell_h * 4 + 38
    height = phone_top + 2400 + pad
    preview = Image.new("RGB", (column_w * 4 + gap * 3, height), BACKGROUND[:3])
    draw = ImageDraw.Draw(preview)
    font = ImageFont.load_default()
    for index, variant in enumerate(("v1", "v2", "v3", "v4")):
        x = index * (column_w + gap)
        title = "Frozen ocean" if variant == "v1" else SETS[variant]["name"]
        draw.text((x + 18, 17), f"{variant.upper()}  /  {title}", fill=(156, 195, 213), font=font)
        tiles = all_tiles[variant]
        for row, layer in enumerate(ORDER):
            y = top + row * cell_h
            tx = x + (column_w - W) // 2
            flat = Image.new("RGBA", (W, H), BACKGROUND)
            flat.alpha_composite(tiles[layer])
            preview.paste(flat.convert("RGB"), (tx, y))
            draw.rectangle((tx - 1, y - 1, tx + W, y + H), outline=(71, 100, 128))
            draw.text((x + 18, y + 8), layer.upper(), fill=(156, 195, 213), font=font)
        phone = Image.new("RGBA", (1080, 2400), BACKGROUND)
        for layer, offset in (("sky", (0, 0)), ("far", (-109, 216)),
                              ("mid", (179, -420)), ("flow", (-244, 781))):
            sheet = tile_on_canvas(tiles[layer], 1080, 2400, *offset, scale=2)
            phone.alpha_composite(sheet)
        preview.paste(phone.convert("RGB"), (x, phone_top))
        draw.text((x + 18, phone_top + 8), "1080 x 2400 PHONE COMPOSITE", fill=(156, 195, 213), font=font)
    preview.save(ROOT / "preview_variants.png", optimize=True)


def make_manifest() -> None:
    variants = [{
        "id": "v1", "name": "Frozen ocean", "path": "../",
        "description": "Original run A: scattered ocean ice, dark leads, pressure ridges and offshore industry.",
        "files": [f"../{layer}.png" for layer in ORDER],
    }]
    for variant, config in SETS.items():
        variants.append({
            "id": variant, "name": config["name"],
            "path": f"{variant}/", "description": config["description"],
            "size": [W, H], "mode": "RGBA", "scroll": "repeat in both axes",
            "tiles": [{
                "file": f"{variant}/{layer}.png", "layer": layer,
                "role": config["tiles"][layer][3],
                "intended_parallax_depth": dict(sky=0.0, far=.25, mid=.60, flow=1.0)[layer],
                "source_candidate": f"src~/{config['tiles'][layer][0]}",
                "palette_colors_max": config["tiles"][layer][2] + (0 if layer == "flow" else 2),
                "alpha": "opaque foundation" if layer == "sky" else
                         "stepped transparency revealing lower layers" if layer == "flow" else
                         "opaque deep regions, translucent ice and industry",
            } for layer in ORDER],
        })
    manifest = {
        "world": "Frost", "view": "Orthographic atmosphere-level surface beneath clouds",
        "build": "python3 src~/build.py", "verify": "python3 src~/verify.py",
        "preview": "preview_variants.png", "variants": variants,
    }
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")


if __name__ == "__main__":
    all_tiles = {"v1": {layer: Image.open(V1 / f"{layer}.png").convert("RGBA")
                        for layer in ORDER}}
    for variant, config in SETS.items():
        all_tiles[variant] = {}
        for layer in ORDER:
            source_name, gain, colors, _ = config["tiles"][layer]
            all_tiles[variant][layer] = build_tile(variant, layer, source_name, gain, colors)
    make_preview(all_tiles)
    make_manifest()
