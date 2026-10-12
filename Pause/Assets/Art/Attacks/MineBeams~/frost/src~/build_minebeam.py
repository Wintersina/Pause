"""Build Frost's rail-mine cryo laser from the generated pixel paintings.

Every atlas cell is composed at native 64 px resolution, then the sheet is
exported with one exact x2 nearest-neighbour upscale. No image is shifted to
make another beam frame: the six beam crops are six separate painted studies.
"""
from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
OUT = HERE.parent
S = 64
NEAREST = Image.Resampling.NEAREST

NAVY = (10, 21, 48)
ICE = np.array([
    (10, 21, 48),     # crevasse ink
    (27, 48, 76),     # dark steel blue
    (52, 83, 119),    # ice shadows
    (100, 137, 173),  # faceted blue
    (166, 196, 218),  # glacier face
    (222, 238, 247),  # frosted white
    (250, 252, 255),  # laser core
], dtype=np.uint8)
PINK = np.array([(131, 36, 112), (255, 79, 216), (255, 138, 230), (255, 224, 248)], dtype=np.uint8)

# Tight boxes follow independent generated paintings, never shifted frames.
BEAM_BOXES = [
    (34, 0, 264, 708), (326, 0, 632, 708), (682, 0, 1032, 708),
    (1074, 0, 1408, 708), (1455, 0, 1810, 708), (1845, 0, 2140, 708),
]
PILOT_BOXES = [
    (48, 208, 252, 388), (320, 101, 618, 390),
    (678, 54, 1020, 391), (1073, 0, 1493, 392),
]
BURST_BOXES = [
    (0, 402, 312, 680), (316, 402, 688, 688),
    (681, 397, 1118, 693), (1110, 386, 1536, 705),
]
IMPACT_BOXES = [
    (0, 662, 639, 1002), (616, 684, 1128, 1002),
    (1117, 712, 1536, 1002),
]


def painted_crop(src: Image.Image, box: tuple[int, int, int, int], size: tuple[int, int]) -> Image.Image:
    """Retain source facets while removing anti-aliased alpha fringe."""
    raw = np.array(src.crop(box).resize(size, NEAREST).convert("RGBA"))
    alpha = raw[:, :, 3]
    visible = alpha >= 153
    rgb = raw[:, :, :3].astype(np.float32)
    hi = rgb.max(axis=2)
    lo = rgb.min(axis=2)
    sat = np.divide(hi - lo, np.maximum(hi, 1), out=np.zeros_like(hi), where=hi > 0)
    # Generated magenta lives in the R/B dominant pixels. Dark purple shadows
    # remain navy; cyan flecks are remapped into the safe glacier ramp.
    magenta = (rgb[:, :, 0] > rgb[:, :, 1] * 1.32) & (rgb[:, :, 2] > rgb[:, :, 1] * 1.15) & (sat > .19) & (hi > 85)
    lum = rgb[:, :, 0] * .23 + rgb[:, :, 1] * .55 + rgb[:, :, 2] * .22
    ice_idx = np.digitize(lum, [31, 67, 105, 150, 195, 233])
    pink_idx = np.digitize(lum, [95, 170, 226])
    out = np.zeros_like(raw)
    out[:, :, :3] = ICE[ice_idx]
    out[magenta, :3] = PINK[pink_idx[magenta]]
    out[:, :, 3] = np.where(visible, 255, 0).astype(np.uint8)
    return Image.fromarray(out, "RGBA")


def neighbors(mask: np.ndarray, radius: int = 1) -> np.ndarray:
    p = np.pad(mask, radius, constant_values=False)
    return np.logical_or.reduce([
        p[radius + dy:radius + dy + mask.shape[0], radius + dx:radius + dx + mask.shape[1]]
        for dy in range(-radius, radius + 1) for dx in range(-radius, radius + 1)
    ])


def crystallize_edges(art: Image.Image, frame: int, *, rim: bool = True) -> Image.Image:
    """A one-pixel navy contour plus pink crystal tips and glints."""
    a = np.array(art).copy()
    mask = a[:, :, 3] > 0
    if not mask.any():
        return art
    outside = neighbors(mask) & ~mask
    inside = mask & neighbors(~mask)
    yy, xx = np.indices(mask.shape)
    a[outside, :3] = NAVY
    a[outside, 3] = 255
    if rim:
        # A mostly continuous navy line keeps the ice visible on white snow.
        # Pink sits immediately inside it and flashes on selected outer tips.
        pink_inside = inside & (((xx * 3 + yy * 5 + frame * 7) % 11) < 6)
        a[pink_inside, :3] = PINK[1 + ((xx[pink_inside] + yy[pink_inside]) % 2)]
        tips = outside & (((xx * 5 + yy * 3 + frame * 7) % 13) < 3)
        a[tips, :3] = PINK[1 + ((xx[tips] + yy[tips]) % 2)]
    return Image.fromarray(a, "RGBA")


def place(cell: Image.Image, art: Image.Image, x: int, y: int) -> None:
    cell.alpha_composite(art, (x, y))


def glints(cell: Image.Image, frame: int, count: int, *, spread: int = 24, y0: int = 6, y1: int = 58) -> None:
    rng = np.random.default_rng(7201 + frame * 103)
    dr = ImageDraw.Draw(cell)
    for j in range(count):
        x = int(rng.integers(32 - spread, 32 + spread + 1))
        y = int(rng.integers(y0, y1))
        dr.point((x, y), fill=tuple(PINK[3] if j % 3 == 0 else PINK[1]) + (255,))


def make_beams(native: Image.Image, beams: Image.Image) -> None:
    for i, box in enumerate(BEAM_BOXES):
        cell = Image.new("RGBA", (S, S * 3))
        art = painted_crop(beams, box, (43 + (i % 3), 178))
        art = crystallize_edges(art, i)
        place(cell, art, (S - art.width) // 2, 7)
        # Sparse diamond dust follows the painted vertical flow. The large
        # crystalline forms and all distinct frame shapes come from painting.
        rng = np.random.default_rng(110 + i)
        dr = ImageDraw.Draw(cell)
        for j in range(29):
            y = int(rng.integers(9, 184))
            x = int(32 + rng.choice((-1, 1)) * rng.integers(18, 24))
            if 5 < x < 58:
                dr.point((x, y), fill=tuple(PINK[3] if j % 4 == 0 else PINK[1]) + (255,))
        native.alpha_composite(cell, (i * S, 0))


def make_pilots(native: Image.Image, effects: Image.Image) -> None:
    sizes = [(17, 18), (25, 27), (34, 37), (44, 48)]
    for i, (box, size) in enumerate(zip(PILOT_BOXES, sizes)):
        cell = Image.new("RGBA", (S, S))
        art = crystallize_edges(painted_crop(effects, box, size), 21 + i)
        place(cell, art, (S - art.width) // 2, 57 - art.height)
        glints(cell, 31 + i, i + 2, spread=6 + i * 5, y0=10, y1=57)
        native.alpha_composite(cell, (i * S, 3 * S))


def make_aim(native: Image.Image) -> None:
    for i in range(2):
        cell = Image.new("RGBA", (S, S))
        d = ImageDraw.Draw(cell)
        for y in range(-8 + i * 6, 72, 12):
            if y < 0 or y >= 64:
                continue
            # Two-pixel shard with navy spine, white facet and pink tip.
            d.polygon([(32, y), (34, y + 2), (32, y + 5), (30, y + 2)], fill=NAVY + (255,))
            d.line([(32, y + 1), (32, y + 4)], fill=tuple(ICE[5]) + (255,))
            d.point((33, y + 2), fill=tuple(PINK[1]) + (255,))
        native.alpha_composite(cell, ((4 + i) * S, 3 * S))


def make_bursts(native: Image.Image, effects: Image.Image) -> None:
    for i, box in enumerate(BURST_BOXES):
        cell = Image.new("RGBA", (S, S))
        size = (46 + i * 2, 45 + i * 2)
        art = crystallize_edges(painted_crop(effects, box, size), 41 + i)
        place(cell, art, (S - art.width) // 2, (S - art.height) // 2)
        if i == 0:
            # A bit of the neighbouring painting falls into this source crop.
            # Remove that isolated sliver without touching the starburst.
            arr = np.array(cell)
            arr[43:55, 49:56] = 0
            cell = Image.fromarray(arr, "RGBA")
        glints(cell, 51 + i, 4 + i, spread=24)
        native.alpha_composite(cell, (i * S, 4 * S))


def make_impacts(native: Image.Image, effects: Image.Image) -> None:
    for i, box in enumerate(IMPACT_BOXES):
        cell = Image.new("RGBA", (S, S))
        size = [(51, 37), (47, 33), (42, 27)][i]
        art = crystallize_edges(painted_crop(effects, box, size), 61 + i)
        place(cell, art, (S - art.width) // 2, 57 - art.height)
        glints(cell, 71 + i, 7 - i, spread=22 - i * 2, y0=13, y1=56)
        pos = ((4 + i) * S, 4 * S) if i < 2 else (0, 5 * S)
        native.alpha_composite(cell, pos)


def make_haze(native: Image.Image, haze: Image.Image) -> None:
    # Two separately painted cold-vapour veils, kept subtle and dithered.
    for i in range(2):
        box = ((63, 165, 1073, 566), (1100, 150, 2140, 574))[i]
        art = haze.crop(box).resize((49, 28 + 3 * i), NEAREST)
        a = np.array(art)
        yy, xx = np.indices(a.shape[:2])
        edge = np.minimum.reduce([xx, a.shape[1] - 1 - xx, yy, a.shape[0] - 1 - yy])
        atten = np.clip(edge / 5, 0, 1)
        a[:, :, :3] = ICE[6]
        a[:, :, 3] = np.where(a[:, :, 3] >= 100, (a[:, :, 3] / 255 * (14 + 48 * atten)).astype(np.uint8), 0)
        cell = Image.new("RGBA", (S, S))
        place(cell, Image.fromarray(a, "RGBA"), (S - art.width) // 2, 20 - i)
        native.alpha_composite(cell, ((1 + i) * S, 5 * S))


def preview(atlas: Image.Image) -> None:
    sheet = Image.new("RGBA", (1536, 1536), (11, 11, 26, 255))
    sheet.alpha_composite(atlas.resize((1536, 1536), NEAREST))
    out = Image.new("RGB", (1536, 1880), "#0b0b1a")
    out.paste(sheet.convert("RGB"), (0, 0))
    beam = atlas.crop((0, 0, 128, 384)).resize((42, 126), NEAREST)
    burst = atlas.crop((2 * 128, 512, 3 * 128, 640)).resize((62, 62), NEAREST)
    impact = atlas.crop((4 * 128, 512, 5 * 128, 640)).resize((62, 62), NEAREST)
    for i, bg in enumerate(((26, 46, 71), (216, 234, 244))):
        tile = Image.new("RGBA", (570, 240), bg + (255,))
        tile.alpha_composite(beam, (103, 30))
        tile.alpha_composite(burst, (260, 72))
        tile.alpha_composite(impact, (393, 72))
        out.paste(tile.convert("RGB"), (125 + i * 705, 1590))
    out.save(OUT / "preview.png")

    frames = []
    for i in range(6):
        panel = Image.new("RGBA", (450, 430), (11, 11, 26, 255))
        panel.alpha_composite(atlas.crop((i * 128, 0, (i + 1) * 128, 384)), (11, 9))
        p = atlas.crop(((i % 4) * 128, 384, (i % 4 + 1) * 128, 512))
        b = atlas.crop(((i % 4) * 128, 512, (i % 4 + 1) * 128, 640))
        aim = atlas.crop(((4 + i % 2) * 128, 384, (5 + i % 2) * 128, 512))
        imp_xy = ((4 + i % 2) * 128, 512) if i % 3 < 2 else (0, 640)
        impact = atlas.crop((*imp_xy, imp_xy[0] + 128, imp_xy[1] + 128))
        panel.alpha_composite(p.resize((84, 84), NEAREST), (170, 40))
        panel.alpha_composite(b.resize((110, 110), NEAREST), (167, 157))
        panel.alpha_composite(aim.resize((76, 76), NEAREST), (320, 47))
        panel.alpha_composite(impact.resize((104, 104), NEAREST), (305, 177))
        frames.append(panel.convert("RGB"))
    frames[0].save(OUT / "preview.gif", save_all=True, append_images=frames[1:], duration=83, loop=0, optimize=False)


def build() -> None:
    beams = Image.open(HERE / "candidate_beams.png").convert("RGBA")
    effects = Image.open(HERE / "candidate_effects.png").convert("RGBA")
    haze = Image.open(HERE / "candidate_haze.png").convert("RGBA")
    native = Image.new("RGBA", (S * 6, S * 6))
    make_beams(native, beams)
    make_pilots(native, effects)
    make_aim(native)
    make_bursts(native, effects)
    make_impacts(native, effects)
    make_haze(native, haze)
    atlas = native.resize((768, 768), NEAREST)
    atlas.save(OUT / "frost_attack_minebeam.png")
    preview(atlas)


if __name__ == "__main__":
    build()
