"""Build the Frost ground parallax set from selected painted candidates.

The source paintings are the *_a.png / *_b.png files beside this script. The
offset splice uses ordered pixel selection so it introduces no blurred pixels.
"""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
W, H = 512, 1024

SETTINGS = {
    "sky": {"source": "sky_a.png", "gain": 0.52, "colors": 20, "depth": 0.0,
            "role": "Deepest frozen-ocean ground plane beneath blue-violet atmospheric haze"},
    "far": {"source": "far_a.png", "gain": 0.55, "colors": 24, "depth": 0.25,
            "role": "Distant ice sheets, open-water leads, coastal shelf and tiny industrial beacons"},
    "mid": {"source": "mid_b.png", "gain": 0.49, "colors": 28, "depth": 0.60,
            "role": "Nearer ocean and industrial coast with rigs, refineries, rail lines and ridges"},
    "flow": {"source": "flow_b.png", "gain": 0.41, "colors": 22, "depth": 1.0,
             "role": "Transparent drifting ice floes, water leads and current streaks"},
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


def load_crop(name: str) -> np.ndarray:
    im = Image.open(SRC / name).convert("RGB")
    # All selected sources are 2:3. Crop to the final 1:2 frame before
    # nearest-neighbour scaling; no interpolation enters the pipeline.
    crop_w = min(im.width, im.height // 2)
    crop_h = min(im.height, im.width * 2)
    x0 = (im.width - crop_w) // 2
    y0 = (im.height - crop_h) // 2
    im = im.crop((x0, y0, x0 + crop_w, y0 + crop_h))
    return np.asarray(im.resize((W, H), Image.Resampling.NEAREST)).copy()


def seam_splice(src: np.ndarray) -> np.ndarray:
    """Offset the source seam to the center, then repair it with source art.

    The outer edges sample neighbouring pixels from the source center. The
    center cross samples the intact source center. Ordered selection avoids
    soft, translucent or interpolated seam pixels.
    """
    rolled = np.roll(src, (H // 2, W // 2), axis=(0, 1))
    yy, xx = np.indices((H, W))
    dx = np.abs(xx - W // 2)
    dy = np.abs(yy - H // 2)
    wx = np.clip((92 - dx) / 72, 0, 1)
    wy = np.clip((144 - dy) / 112, 0, 1)
    probability = 1 - (1 - wx) * (1 - wy)
    take_source = probability > BAYER8[yy % 8, xx % 8]
    return np.where(take_source[..., None], src, rolled)


def alpha_for(name: str, original: np.ndarray) -> np.ndarray:
    v = original.max(axis=2).astype(np.float32) / 255
    if name == "sky":
        return np.full((H, W), 255, np.uint8)
    if name == "far":
        return np.where(v < .23, 255, 164).astype(np.uint8)
    if name == "mid":
        return np.where(v < .24, 255, 186).astype(np.uint8)
    # Deep water lets lower planes through. Floe surfaces have several hard
    # opacity steps; a few bright ice facets are fully opaque.
    alpha = np.select(
        [v < .18, v < .23, v < .32, v < .42, v < .56, v < .69],
        [0, 32, 64, 112, 168, 216],
        default=255,
    )
    return alpha.astype(np.uint8)


def build_tile(name: str, cfg: dict) -> Image.Image:
    source = load_crop(cfg["source"])
    painted = seam_splice(source)
    # Scale the painted palette into the safe backdrop range, then quantize
    # adaptively without diffusion. The source structure remains painted art.
    dark = np.clip(np.rint(painted.astype(np.float32) * cfg["gain"]), 0, 255).astype(np.uint8)
    rgb = Image.fromarray(dark, "RGB").quantize(
        colors=cfg["colors"], method=Image.Quantize.MEDIANCUT,
        dither=Image.Dither.NONE,
    ).convert("RGB")
    color = np.asarray(rgb).copy()
    if name != "flow":
        p = painted.astype(np.float32) / 255
        high = p.max(axis=2)
        low = p.min(axis=2)
        saturation = (high - low) / np.maximum(high, .001)
        cyan = ((p[:, :, 2] > p[:, :, 0] * 1.45) &
                (p[:, :, 1] > p[:, :, 0] * 1.35) &
                (high > .80) & (saturation > .48))
        magenta = ((p[:, :, 0] > p[:, :, 1] * 1.30) &
                   (p[:, :, 2] > p[:, :, 1] * 1.30) &
                   (high > .50) & (saturation > .35))
        # Pinpoint beacons retain the source painting's lighting cues. Their
        # combined coverage is far below one percent of any tile.
        color[cyan] = (35, 81, 111) if name == "sky" else (46, 108, 152)
        color[magenta] = (88, 54, 104) if name == "sky" else (117, 75, 134)
    alpha = alpha_for(name, painted)
    rgba = np.dstack((color, alpha))
    # A matching one-pixel wrap border makes edge tests deterministic. The
    # offset above puts these borders on continuous, adjacent source pixels.
    rgba[:, -1] = rgba[:, 0]
    rgba[-1] = rgba[0]
    out = Image.fromarray(rgba, "RGBA")
    out.save(ROOT / f"{name}.png", optimize=True)
    return out


def tile_on_canvas(tile: Image.Image, width: int, height: int,
                   offset_x: int = 0, offset_y: int = 0, scale: int = 1) -> Image.Image:
    if scale != 1:
        tile = tile.resize((W * scale, H * scale), Image.Resampling.NEAREST)
    tw, th = tile.size
    canvas = Image.new("RGBA", (width, height))
    for y in range(-th + (offset_y % th), height, th):
        for x in range(-tw + (offset_x % tw), width, tw):
            canvas.alpha_composite(tile, (x, y))
    return canvas


def make_preview(tiles: dict[str, Image.Image]) -> None:
    background = (5, 9, 19, 255)
    preview = Image.new("RGBA", (3380, 2500), background)
    draw = ImageDraw.Draw(preview)
    font = ImageFont.load_default()
    x = 32
    for name in SETTINGS:
        doubled = tile_on_canvas(tiles[name], W, H * 2)
        if name != "sky":
            base = Image.new("RGBA", doubled.size, background)
            base.alpha_composite(doubled)
            doubled = base
        preview.alpha_composite(doubled, (x, 62))
        draw.rectangle((x - 1, 61, x + W, 62 + H * 2), outline=(76, 107, 137, 255))
        draw.text((x, 30), f"{name.upper()}  /  1 x 2 WRAP", fill=(160, 203, 220), font=font)
        x += W + 20

    phone_x, phone_y = 2160, 62
    phone = Image.new("RGBA", (1080, 2400), background)
    for name, offset in (("sky", (0, 0)), ("far", (-109, 216)),
                         ("mid", (179, -420)), ("flow", (-244, 781))):
        sheet = tile_on_canvas(tiles[name], 1080, 2400, *offset, scale=2)
        phone.alpha_composite(sheet)
    preview.alpha_composite(phone, (phone_x, phone_y))
    draw.rectangle((phone_x - 1, phone_y - 1, phone_x + 1080, phone_y + 2400),
                   outline=(76, 107, 137, 255), width=2)
    draw.text((phone_x, 30), "COMPOSITE  /  1080 x 2400 PHONE VIEW", fill=(160, 203, 220), font=font)
    preview.convert("RGB").save(ROOT / "preview_a.png", optimize=True)


def make_manifest(tiles: dict[str, Image.Image]) -> None:
    manifest = {
        "world": "Frost",
        "view": "Orthographic directly overhead frozen ocean, coast and inland industry beneath clouds",
        "build": "python3 src~/build.py",
        "files": [
            {
                "file": f"{name}.png",
                "size": [W, H],
                "mode": "RGBA",
                "role": cfg["role"],
                "intended_parallax_depth": cfg["depth"],
                "notes": {
                    "source_candidate": f"src~/{cfg['source']}",
                    "scroll": "vertical; repeat in both axes",
                    "palette_colors_max": cfg["colors"] + (0 if name == "flow" else 2),
                    "alpha": "opaque foundation" if name == "sky" else
                             ("stepped translucent floes and transparent water" if name == "flow" else
                              "dark water opaque; ice and industry translucent for parallax compositing"),
                },
            }
            for name, cfg in SETTINGS.items()
        ],
        "preview": "preview_a.png",
    }
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")


if __name__ == "__main__":
    tiles = {name: build_tile(name, cfg) for name, cfg in SETTINGS.items()}
    make_preview(tiles)
    make_manifest(tiles)
