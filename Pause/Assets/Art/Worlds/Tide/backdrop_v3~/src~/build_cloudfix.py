"""Build the Tide weather-cloud candidate without touching the live atlas.

The generated candidate supplies the four bank silhouettes and lobe texture.
This script samples it with nearest-neighbour pixels, maps its colours to the
existing Tide weather palette, and retains every other atlas pixel verbatim.
"""

from __future__ import annotations

import colorsys
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[6]
BASE = ROOT / "Pause/Assets/Art/Backgrounds/Resources/Worlds/Tide/Backdrop3"
OUT = HERE.parent / "cloudfix"
SOURCE = HERE / "cloud_candidate_atlas.png"
PALETTE = np.array(
    [
        (27, 41, 37),
        (43, 60, 57),
        (65, 85, 80),
        (91, 114, 107),
        (128, 155, 146),
        (35, 105, 72),
        (57, 147, 101),
        (88, 194, 138),
    ],
    dtype=np.uint8,
)


def generated_bank(generated: Image.Image, index: int, target_box: tuple[int, ...]) -> np.ndarray:
    """Fit a generated lobe silhouette to its original cell footprint."""
    side = generated.width / 4
    x0, x1 = round(index * side), round((index + 1) * side)
    y1 = round(generated.height / 4)
    src = np.asarray(generated.crop((x0, 0, x1, y1)).convert("RGBA"))
    ys, xs = np.where(src[:, :, 3] > 30)
    if len(xs) == 0:
        raise ValueError(f"Generated cloud {index} is empty")
    crop = Image.fromarray(src[ys.min() : ys.max() + 1, xs.min() : xs.max() + 1])
    left, top, right, bottom = target_box
    crop = crop.resize((right - left + 1, bottom - top + 1), Image.Resampling.NEAREST)
    raw = np.asarray(crop, dtype=np.uint8)
    canvas = np.zeros((256, 256, 4), dtype=np.uint8)
    canvas[top : bottom + 1, left : right + 1] = raw
    return canvas


def recolour(raw: np.ndarray, footprint: tuple[int, ...]) -> np.ndarray:
    """Quantise to the exact eight Tide weather colours and stepped alpha."""
    rgb = raw[:, :, :3].astype(np.float32)
    red, green, blue = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
    value = red * 0.2126 + green * 0.7152 + blue * 0.0722
    mint = (green - red > 30) & (green - blue > 10)
    gray_step = np.digitize(value, [51, 68, 112, 182])
    mint_step = 5 + np.digitize(value, [90, 160])
    indices = np.where(mint, mint_step, gray_step)
    out = np.zeros_like(raw)
    out[:, :, :3] = PALETTE[indices]

    # The original atlas uses translucent, stepped clouds with alpha <= 105.
    alpha = np.minimum(105, np.rint(raw[:, :, 3].astype(np.float32) * 105 / 205))
    yy, xx = np.indices((256, 256))
    border_distance = np.minimum.reduce([xx, yy, 255 - xx, 255 - yy])
    edge_limit = np.clip((border_distance - 8) * 15, 0, 105)
    left, top, right, bottom = footprint
    footprint_distance = np.minimum.reduce([xx - left, yy - top, right - xx, bottom - yy])
    footprint_limit = np.clip(footprint_distance * 15, 0, 105)
    alpha = np.minimum.reduce([alpha, edge_limit, footprint_limit])
    out[:, :, 3] = (np.rint(alpha / 15) * 15).astype(np.uint8)
    out[out[:, :, 3] == 0] = 0
    return out


def composite(bank: np.ndarray, backdrop: Image.Image, left: int, top: int) -> Image.Image:
    result = backdrop.convert("RGBA").copy()
    result.alpha_composite(Image.fromarray(bank), (left, top))
    return result


def make_previews(before: np.ndarray, after: np.ndarray) -> None:
    """Every bank, at true size, before and after over each Tide mid tile."""
    for tile_index in range(4):
        mid = Image.open(BASE / f"v{tile_index + 1}/mid.png").convert("RGBA")
        panel = Image.new("RGBA", (1024, 1120), (5, 21, 22, 255))
        draw = ImageDraw.Draw(panel)
        for bank_index in range(4):
            y = 80 + bank_index * 190
            crop = mid.crop((0, y, 512, y + 256))
            old = before[:256, bank_index * 256 : (bank_index + 1) * 256]
            new = after[:256, bank_index * 256 : (bank_index + 1) * 256]
            row_y = bank_index * 280
            panel.alpha_composite(composite(old, crop, 128, 0), (0, row_y + 24))
            panel.alpha_composite(composite(new, crop, 128, 0), (512, row_y + 24))
            draw.text((8, row_y + 5), f"v{tile_index + 1} bank {bank_index:02d} BEFORE", fill=(190, 229, 217))
            draw.text((520, row_y + 5), f"v{tile_index + 1} bank {bank_index:02d} AFTER", fill=(190, 229, 217))
        panel.convert("RGB").save(HERE / f"preview_v{tile_index + 1}_before_after.png")


def metrics(before: np.ndarray, after: np.ndarray) -> dict:
    unchanged = np.array_equal(before[256:], after[256:])
    source_json = (BASE / "weather.json").read_bytes()
    output_json = (OUT / "weather.json").read_bytes()
    contract = json.loads(source_json)
    expected_names = [f"cloud_bank_{i:02d}" for i in range(4)]
    assert [s["n"] for s in contract["sprites"][:4]] == expected_names
    unchanged_sprites = []
    for sprite in contract["sprites"][4:]:
        x = sprite["x"]
        y = 1024 - sprite["y"] - sprite["h"]
        rect = (slice(y, y + sprite["h"]), slice(x, x + sprite["w"]))
        if before[rect].tobytes() == after[rect].tobytes():
            unchanged_sprites.append(sprite["n"])
    changed_banks = []
    for index in range(4):
        a = after[:256, index * 256 : (index + 1) * 256]
        old = before[:256, index * 256 : (index + 1) * 256]
        border = np.minimum.reduce(np.indices((256, 256)).tolist() + [
            255 - np.indices((256, 256))[1],
            255 - np.indices((256, 256))[0],
        ])
        ys, xs = np.where(a[:, :, 3] > 24)
        rgb = a[:, :, :3]
        alpha = a[:, :, 3]
        visible = alpha > 0
        hsv = np.array([colorsys.rgb_to_hsv(*(p / 255.0)) for p in rgb[visible]], dtype=float)
        premul_value = np.max(rgb.astype(float) * alpha[:, :, None] / (255 * 255), axis=2)
        changed_banks.append(
            {
                "name": f"cloud_bank_{index:02d}",
                "changed_pixels": int(np.count_nonzero(np.any(a != old, axis=2))),
                "bbox_alpha_gt_24": [int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())],
                "max_border_8px_alpha": int(alpha[border <= 8].max()),
                "max_alpha": int(alpha.max()),
                "p90_alpha": float(np.percentile(alpha[visible], 90)),
                "p90_raw_value": float(np.percentile(hsv[:, 2], 90)),
                "p90_premultiplied_value": float(np.percentile(premul_value, 90)),
                "hues_degrees": sorted(set(round(h * 360, 1) for h in hsv[:, 0])),
                "palette_rgb": sorted({tuple(map(int, p)) for p in rgb[visible].reshape(-1, 3)}),
            }
        )
    return {
        "atlas_size": [int(after.shape[1]), int(after.shape[0])],
        "json_byte_identical": source_json == output_json,
        "other_12_sprites_pixel_identical": unchanged and len(unchanged_sprites) == 12,
        "unchanged_sprite_names": unchanged_sprites,
        "banks": changed_banks,
    }


def main() -> None:
    original = Image.open(BASE / "weather.png").convert("RGBA")
    generated = Image.open(SOURCE).convert("RGBA")
    before = np.asarray(original).copy()
    after = before.copy()
    for index in range(4):
        orig = before[:256, index * 256 : (index + 1) * 256]
        ys, xs = np.where(orig[:, :, 3] > 24)
        footprint = (int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max()))
        after[:256, index * 256 : (index + 1) * 256] = recolour(
            generated_bank(generated, index, footprint), footprint
        )
    OUT.mkdir(exist_ok=True)
    Image.fromarray(after).save(OUT / "weather.png")
    (OUT / "weather.json").write_bytes((BASE / "weather.json").read_bytes())
    exported = np.asarray(Image.open(OUT / "weather.png").convert("RGBA"))
    assert np.array_equal(after, exported)
    make_previews(before, exported)
    report = metrics(before, exported)
    (HERE / "verification.json").write_text(json.dumps(report, indent=2) + "\n")
    assert report["other_12_sprites_pixel_identical"]
    assert report["json_byte_identical"]
    assert all(b["max_border_8px_alpha"] <= 24 and b["max_alpha"] <= 110 for b in report["banks"])
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
