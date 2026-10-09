"""Transfer selected image-generated hover-cell paint onto the seven live poses.

The generated art supplies the material, while each original cell supplies its
silhouette, alpha, rim, glow, muzzle and thruster pixels. Run from any directory.
"""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


HERE = Path(__file__).resolve().parent
ART = HERE.parents[2]
LIVE = ART / "Resources/Elites/Space"
NAMES = (
    "eventide_bastion",
    "orbit_reaver",
    "rift_lancer",
    "singularity_hauler",
)
SELECTED = {
    "eventide_bastion": "c",
    "orbit_reaver": "c",
    "rift_lancer": "b",
    "singularity_hauler": "c",
}
CELL = 192


def bbox(mask: np.ndarray) -> tuple[int, int, int, int]:
    ys, xs = np.nonzero(mask)
    return int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1


def glow_mask(rgb: np.ndarray, alpha: np.ndarray) -> np.ndarray:
    r, g, b = (rgb[:, :, i].astype(np.float32) for i in range(3))
    cyan = (b > 125) & (b > r * 1.30) & (g > r * 1.23)
    blue = (b > 160) & (b > r * 1.5) & (b > g * 1.12)
    magenta = (np.maximum(r, b) > 155) & (r > g * 1.55) & (b > g * 1.50)
    white_core = (np.minimum(g, b) > 180) & (r > 120)
    return (cyan | blue | magenta | white_core) & (alpha > 0)


def edge_mask(mask: np.ndarray) -> np.ndarray:
    interior = mask.copy()
    interior[1:] &= mask[:-1]
    interior[:-1] &= mask[1:]
    interior[:, 1:] &= mask[:, :-1]
    interior[:, :-1] &= mask[:, 1:]
    return mask & ~interior


def generated_to_cell(generated: Image.Image, original: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    target_alpha = original[:, :, 3]
    tx0, ty0, tx1, ty1 = bbox(target_alpha >= 64)
    source = np.asarray(generated)
    sx0, sy0, sx1, sy1 = bbox(source[:, :, 3] >= 64)
    cropped = generated.crop((sx0, sy0, sx1, sy1))
    resized = cropped.resize((tx1 - tx0, ty1 - ty0), Image.Resampling.NEAREST)
    canvas = Image.new("RGBA", (CELL, CELL))
    canvas.paste(resized, (tx0, ty0))
    mapped = np.asarray(canvas)
    return mapped[:, :, :3].copy(), mapped[:, :, 3] >= 64


def transfer(original: np.ndarray, generated: Image.Image, pose: int) -> np.ndarray:
    old = original[:, :, :3].astype(np.float32)
    alpha = original[:, :, 3]
    painted, valid = generated_to_cell(generated, original)
    painted = painted.astype(np.float32)

    # Neutralize the old purple finish where the candidate has a small cutout.
    value = old[:, :, 0] * .213 + old[:, :, 1] * .715 + old[:, :, 2] * .072
    fallback = np.stack((value * .75 + 8, value * .82 + 12, value * .93 + 20), axis=2)
    material = np.where(valid[:, :, None], painted * .91 + fallback * .09, fallback)

    # Light changes are on the paint, never on the alpha or attachment points.
    yy, xx = np.mgrid[:CELL, :CELL]
    if pose == 4:
        multiplier = 1.0 + .07 * (1.0 - xx / (CELL - 1))
    elif pose == 5:
        multiplier = 1.0 + .07 * xx / (CELL - 1)
    elif pose in (0, 1):
        multiplier = .965 + .012 * yy / (CELL - 1)
    elif pose == 2:
        multiplier = 1.015 + .01 * yy / (CELL - 1)
    elif pose == 6:
        multiplier = .94 + .02 * yy / (CELL - 1)
    else:
        multiplier = 1.0
    material *= multiplier[:, :, None] if isinstance(multiplier, np.ndarray) else multiplier

    # Push any true red rust toward aged copper/brass; magenta remains intact.
    r, g, b = material[:, :, 0], material[:, :, 1], material[:, :, 2]
    red_rust = (r > 70) & (r > g * 1.5) & (r > b * 1.55)
    material[:, :, 1] = np.where(red_rust, np.maximum(g, r * .68), g)

    # Keep original neon timing, engine flames, damage arcs and the 1 px edge.
    keep = glow_mask(original[:, :, :3], alpha) | edge_mask(alpha >= 64)
    output = np.empty_like(original)
    output[:, :, :3] = np.where(keep[:, :, None], old, np.clip(material, 0, 255)).astype(np.uint8)
    output[:, :, 3] = alpha
    rr, gg, bb = (output[:, :, i].astype(np.float32) for i in range(3))
    actual_red = (rr > 80) & (rr > gg * 1.55) & (rr > bb * 1.55) & (
        (bb < rr * .45) | (bb < gg * 1.4)
    )
    output[:, :, 1] = np.where(actual_red, np.maximum(gg, rr * .68), gg).astype(np.uint8)
    return output


def preview(before: dict[str, Image.Image], after: dict[str, Image.Image]) -> None:
    scale = 2
    label_width = 210
    width = label_width + 7 * CELL * scale
    height = len(NAMES) * 2 * CELL * scale
    board = Image.new("RGB", (width, height), (11, 11, 26))
    draw = ImageDraw.Draw(board)
    for row, name in enumerate(NAMES):
        for state, images in enumerate((before, after)):
            y = (row * 2 + state) * CELL * scale
            sprite = images[name].resize((7 * CELL * scale, CELL * scale), Image.Resampling.NEAREST)
            board.paste(sprite, (label_width, y), sprite)
            draw.text((12, y + 12), name.replace("_", " ").title(), fill=(210, 210, 225))
            draw.text((12, y + 33), "BEFORE" if state == 0 else "AFTER", fill=(125, 210, 230))
    board.save(HERE / "preview.png")


def main() -> None:
    before: dict[str, Image.Image] = {}
    after: dict[str, Image.Image] = {}
    for name in NAMES:
        filename = f"space_elite_{name}.png"
        source = Image.open(HERE / "original" / filename).convert("RGBA")
        generated = Image.open(HERE / f"candidate_{SELECTED[name]}_{name}.png").convert("RGBA")
        cells = []
        for pose in range(7):
            original = np.asarray(source.crop((pose * CELL, 0, (pose + 1) * CELL, CELL)))
            cells.append(Image.fromarray(transfer(original, generated, pose), "RGBA"))
        strip = Image.new("RGBA", source.size)
        for pose, cell in enumerate(cells):
            strip.paste(cell, (pose * CELL, 0))
        strip.save(LIVE / filename)
        before[name] = source
        after[name] = strip
    preview(before, after)


if __name__ == "__main__":
    main()
