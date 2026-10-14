#!/usr/bin/env python3
"""Assemble generated violet paintings at native 1024-cell resolution.

Pass four original transparent imagegen PNGs (round, arc, early, peak). Sources
must be at least 1024 square. Every resize is a nearest-neighbour reduction.
Large raw paintings stay outside this worktree; only final game art is saved.
"""
from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image


SIZE = 1024
OUT = Path(__file__).resolve().parent.parent


def fit(path: Path, side: int, xy: tuple[int, int]) -> np.ndarray:
    source = Image.open(path).convert("RGBA")
    if min(source.size) < SIZE or side > min(source.size):
        raise ValueError(f"source must be >= 1024 and reduced, never enlarged: {path} {source.size}")
    source = source.resize((side, side), Image.Resampling.NEAREST)
    canvas = Image.new("RGBA", (SIZE, SIZE))
    canvas.alpha_composite(source, xy)
    a = np.asarray(canvas).copy()
    a[a[..., 3] < 20] = 0
    a[:16] = a[-16:] = 0
    a[:, :16] = a[:, -16:] = 0
    return a


def corona(disc: np.ndarray, center: tuple[int, int], radius: int) -> np.ndarray:
    y, x = np.ogrid[:SIZE, :SIZE]
    r = np.hypot(x - center[0], y - center[1])
    # Retain the painted rim, spicules and the second sun's large prominence.
    outer = (r > radius - 27).astype(np.float32)
    alpha = disc[..., 3].astype(np.float32) * outer * .25
    # A very faint, irregular halo: integer-pixel stipple, never filtered.
    theta = np.arctan2(y - center[1], x - center[0])
    warped = radius + 7 * np.sin(theta * 13) + 4 * np.cos(theta * 23)
    band = (r > warped - 10) & (r < warped + 48)
    noise = ((x * 37 + y * 71 + (x * y) % 19) % 29).astype(np.float32) / 29
    halo = np.maximum(0, 16 - np.abs(r - (warped + 10)) * .48) * (0.35 + .65 * noise)
    alpha = np.maximum(alpha, np.where(band, halo, 0))
    alpha[:16] = alpha[-16:] = 0
    alpha[:, :16] = alpha[:, -16:] = 0
    out = np.zeros_like(disc)
    out[..., :3] = disc[..., :3]
    bare = (disc[..., 3] == 0) & (alpha > 0)
    out[bare, :3] = (255, 111, 208)
    out[..., 3] = np.clip(alpha, 0, 150).astype(np.uint8)
    out[out[..., 3] < 5] = 0
    return out


def flare_frames(early: np.ndarray, peak: np.ndarray) -> list[np.ndarray]:
    frames = []
    for k in range(8):
        growth = .5 - .5 * np.cos(2 * np.pi * k / 8)
        # The early painted ribbons contract while the peak ribbons emerge.
        ea = early[..., 3].astype(np.float32) * (.23 - .13 * growth)
        pa = peak[..., 3].astype(np.float32) * (.055 + .30 * growth)
        total = ea + pa
        rgb = (early[..., :3].astype(np.float32) * ea[..., None]
               + peak[..., :3].astype(np.float32) * pa[..., None])
        out = np.zeros((SIZE, SIZE, 4), dtype=np.uint8)
        out[..., :3] = np.clip(rgb / np.maximum(total, 1)[..., None], 0, 255).astype(np.uint8)
        out[..., 3] = np.clip(total, 0, 165).astype(np.uint8)
        out[out[..., 3] < 5] = 0
        out[:8] = out[-8:] = 0
        out[:, :8] = out[:, -8:] = 0
        frames.append(out)
    return frames


def sheet(images: list[np.ndarray], cols: int) -> Image.Image:
    out = Image.new("RGBA", (SIZE * cols, SIZE * ((len(images) + cols - 1) // cols)))
    for i, a in enumerate(images):
        out.paste(Image.fromarray(a, "RGBA"), ((i % cols) * SIZE, (i // cols) * SIZE))
    return out


def additive(base: np.ndarray, layer: np.ndarray) -> np.ndarray:
    contribution = (layer[..., :3].astype(np.uint16) * layer[..., 3:4].astype(np.uint16) + 127) // 255
    return np.minimum(base.astype(np.uint16) + contribution, 255).astype(np.uint8)


def gif(disc: np.ndarray, cor: np.ndarray, flares: list[np.ndarray]) -> None:
    bg = np.empty((SIZE, SIZE, 3), dtype=np.uint8)
    bg[:] = (11, 11, 26)
    # Standard alpha composite for the opaque disc; additive for corona/flares.
    a = disc[..., 3:4].astype(np.float32) / 255
    base = np.rint(disc[..., :3] * a + bg * (1 - a)).astype(np.uint8)
    base = additive(base, cor)
    thumbs = [Image.fromarray(additive(base, f), "RGB").resize((512, 512), Image.Resampling.NEAREST)
              for f in flares]
    # GIF stores delays in 10 ms units. 1330 ms per eight-frame loop is 6.02 fps.
    thumbs[0].save(OUT / "flares_violet.gif", save_all=True, append_images=thumbs[1:],
                   duration=[170, 170, 160, 170, 170, 160, 170, 160],
                   loop=0, disposal=2, optimize=False)


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    for name in ("round", "arc", "early", "peak"):
        p.add_argument(name, type=Path)
    args = p.parse_args()
    round_disc = fit(args.round, 900, (62, 62))
    arc_disc = fit(args.arc, 960, (32, 32))
    round_cor = corona(round_disc, (512, 512), 385)
    arc_cor = corona(arc_disc, (532, 553), 405)
    early = fit(args.early, 900, (80, 100))
    peak = fit(args.peak, 900, (80, 100))
    frames = flare_frames(early, peak)
    sheet([round_disc, arc_disc], 2).save(OUT / "sun_violet.png", optimize=True)
    sheet([round_cor, arc_cor], 2).save(OUT / "sun_violet_corona.png", optimize=True)
    sheet(frames, 4).save(OUT / "flares_violet.png", optimize=True)
    gif(arc_disc, arc_cor, frames)


if __name__ == "__main__":
    main()
