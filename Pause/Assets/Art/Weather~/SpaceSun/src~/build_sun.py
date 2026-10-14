#!/usr/bin/env python3
"""Assemble the selected generated sun paintings and a dithered additive corona.

Use a fresh output directory for each draft. This script refuses to overwrite files.
The painted inputs are candidate_warm_a.png and candidate_cool_a.png beside it.
"""
from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image


HERE = Path(__file__).resolve().parent
N = 256
SCALE = 4


def keep_painted_forms(alpha: np.ndarray) -> np.ndarray:
    """Remove the isolated generation flecks without altering connected plasma arcs."""
    opaque = alpha >= 104
    seen = np.zeros(opaque.shape, dtype=bool)
    retained = np.zeros(opaque.shape, dtype=bool)
    height, width = opaque.shape
    for y0, x0 in zip(*np.nonzero(opaque)):
        if seen[y0, x0]:
            continue
        queue = [(int(y0), int(x0))]
        seen[y0, x0] = True
        pixels = []
        while queue:
            y, x = queue.pop()
            pixels.append((y, x))
            for yy, xx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
                if 0 <= yy < height and 0 <= xx < width and opaque[yy, xx] and not seen[yy, xx]:
                    seen[yy, xx] = True
                    queue.append((yy, xx))
        if len(pixels) >= 8:
            for y, x in pixels:
                retained[y, x] = True
    return retained


def painted_cell(filename: str, native_side: int, tint: tuple[float, float, float]) -> Image.Image:
    source = Image.open(HERE / filename).convert("RGBA")
    # The 4x export follows the project's game-pixel convention. Every resize is nearest-neighbour.
    source = source.resize((native_side, native_side), Image.Resampling.NEAREST)
    canvas = Image.new("RGBA", (N, N))
    canvas.alpha_composite(source, ((N - native_side) // 2, (N - native_side) // 2))
    pixels = np.asarray(canvas).copy()
    opaque = keep_painted_forms(pixels[:, :, 3])
    rgb = pixels[:, :, :3].astype(np.float32)
    rgb *= np.array(tint, dtype=np.float32)
    pixels[:, :, :3] = np.clip(rgb, 0, 255).astype(np.uint8)
    pixels[:, :, 3] = np.where(opaque, 255, 0).astype(np.uint8)
    pixels[pixels[:, :, 3] == 0, :3] = 0
    return Image.fromarray(pixels, "RGBA").resize((1024, 1024), Image.Resampling.NEAREST)


def bayer8() -> np.ndarray:
    a = np.array([[0, 2], [3, 1]], dtype=np.int32)
    for _ in range(2):
        a = np.block([[4 * a, 4 * a + 2], [4 * a + 3, 4 * a + 1]])
    return a


def angular_distance(a: np.ndarray, b: np.ndarray) -> np.ndarray:
    return np.arctan2(np.sin(a - b), np.cos(a - b))


def corona_cell(radius: float, variant: int) -> Image.Image:
    yy, xx = np.mgrid[:N, :N].astype(np.float32)
    dx, dy = xx - 127.5, yy - 127.5
    r = np.hypot(dx, dy)
    angle = np.arctan2(dy, dx)

    # Low-energy radial haze and a narrow atmospheric ring. The taper ends before the cell edge.
    haze = 0.026 * np.exp(-((r - (radius + 9)) / 18) ** 2)
    halo_ring = 0.085 * np.exp(-((r - (radius + 2)) / 3.1) ** 2)
    ray_phase = 14 * angle + 1.1 * np.sin(3 * angle + variant)
    rays = 0.17 * np.maximum(0, np.cos(ray_phase)) ** 12
    rays *= np.exp(-((r - (radius + 17)) / 25) ** 2)
    streamers = np.zeros((N, N), dtype=np.float32)
    for direction, strength, turn in (
        (-2.48 + variant * 0.13, 0.18, 0.004),
        (-0.56 - variant * 0.14, 0.14, -0.003),
        (1.88 + variant * 0.10, 0.13, 0.005),
    ):
        curved_direction = direction + turn * (r - radius)
        wedge = np.exp(-(angular_distance(angle, curved_direction) / 0.10) ** 2)
        streamers += strength * wedge * np.exp(-((r - (radius + 19)) / 19) ** 2)
    strength = np.clip(haze + halo_ring + rays + streamers, 0, 0.34)
    strength *= np.clip((124 - r) / 9, 0, 1)
    strength[r < radius - 16] = 0

    # Ordered alpha dithering prevents visible concentric bands after 4x nearest upscale.
    scaled = strength * 255 / 12
    base = np.floor(scaled)
    threshold = (np.tile(bayer8(), (N // 8, N // 8)) + 0.5) / 64
    alpha = (base + ((scaled - base) > threshold)) * 12
    alpha = np.clip(alpha, 0, 255).astype(np.uint8)
    rgba = np.zeros((N, N, 4), dtype=np.uint8)
    rgba[:, :, :3] = (129, 32, 111) if variant == 0 else (94, 35, 143)
    rgba[:, :, 3] = alpha
    rgba[alpha == 0, :3] = 0
    return Image.fromarray(rgba, "RGBA").resize((1024, 1024), Image.Resampling.NEAREST)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("output_dir", type=Path)
    args = parser.parse_args()
    out = args.output_dir
    out.mkdir(parents=True, exist_ok=True)
    for name in ("sun.png", "sun_corona.png"):
        if (out / name).exists():
            raise FileExistsError(out / name)

    suns = Image.new("RGBA", (2048, 1024))
    suns.alpha_composite(painted_cell("candidate_warm_a.png", 216, (0.83, 0.80, 0.86)), (0, 0))
    suns.alpha_composite(painted_cell("candidate_cool_a.png", 244, (0.80, 0.75, 0.89)), (1024, 0))
    coronas = Image.new("RGBA", (2048, 1024))
    coronas.alpha_composite(corona_cell(94.5, 0), (0, 0))
    coronas.alpha_composite(corona_cell(96.5, 1), (1024, 0))
    suns.save(out / "sun.png")
    coronas.save(out / "sun_corona.png")


if __name__ == "__main__":
    main()
