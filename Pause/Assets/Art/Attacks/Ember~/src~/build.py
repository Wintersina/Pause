"""Build the Ember mine flame atlas from the selected generated paintings.

All composition happens at 64 px per cell; the sole export scale is x2 NEAREST.
"""
from __future__ import annotations

import colorsys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
OUT = HERE.parent
S = 64
NEAREST = Image.Resampling.NEAREST

ORANGE = ["#220C26", "#8A3A1A", "#B5541A", "#D04C0E", "#E0701C", "#FF8C18", "#FFF4D0"]
PINK = ["#220C26", "#7F216C", "#B72A9D", "#FF4FD8", "#FF8AE6", "#FFE0F8", "#FFF4F9"]
PLUM = "#220C26"


def rgba(hexcode: str, alpha: int = 255) -> tuple[int, int, int, int]:
    return tuple(bytes.fromhex(hexcode.lstrip("#"))) + (alpha,)


def clean(painting: Image.Image, *, edge_pink: bool = True, alpha_scale: float = 1) -> Image.Image:
    """Retain painted value structure, quantise to the safe Ember/pink ramps."""
    a = np.array(painting.convert("RGBA"), dtype=np.uint8)
    h, w = a.shape[:2]
    out = np.zeros_like(a)
    visible = a[:, :, 3] >= 85
    for y, x in zip(*np.where(visible)):
        r, g, b, aa = [int(v) for v in a[y, x]]
        hue, sat, val = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
        hue *= 360
        # The generated source's crimson artifacts are moved into orange or pink.
        is_pink = (285 <= hue <= 345 and sat >= .13) or (x / max(1, w - 1) < .21 and val > .24) or (x / max(1, w - 1) > .79 and val > .24)
        if sat < .18 and val > .80:
            is_pink = ((x + 2 * y) % 5) < 2
        index = 0 if val < .15 else 1 if val < .27 else 2 if val < .40 else 3 if val < .57 else 4 if val < .76 else 5 if val < .93 else 6
        if not is_pink and index == 6 and sat > .22:
            index = 5
        c = rgba((PINK if is_pink else ORANGE)[index])
        out[y, x, :3] = c[:3]
        out[y, x, 3] = min(255, int(aa * alpha_scale)) if aa >= 160 else min(255, int(aa * alpha_scale * .75))
    if edge_pink:
        mask = Image.fromarray((out[:, :, 3] > 100).astype("uint8") * 255)
        inner = np.array(mask.filter(ImageFilter.MinFilter(3))) > 0
        rim = (np.array(mask) > 0) & ~inner
        yy, xx = np.where(rim)
        for y, x in zip(yy, xx):
            bright = out[y, x, 0] > 200
            out[y, x, :3] = rgba("#FF8AE6" if bright else "#FF4FD8")[:3]
    return Image.fromarray(out, "RGBA")


def paste_center(canvas: Image.Image, art: Image.Image, y: int) -> None:
    canvas.alpha_composite(art, ((S - art.width) // 2, y))


def add_sparks(cell: Image.Image, seed: int, count: int, top: int = 5, bottom: int = 59) -> None:
    rng = np.random.default_rng(seed)
    draw = ImageDraw.Draw(cell)
    for _ in range(count):
        x = int(rng.integers(5, 59))
        y = int(rng.integers(top, bottom))
        draw.point((x, y), fill=rgba("#FFE0F8" if rng.random() < .45 else "#FF4FD8", 230))


def build() -> None:
    plume = Image.open(HERE / "candidate_plume_b.png").convert("RGBA")
    burst = Image.open(HERE / "candidate_burst.png").convert("RGBA")
    # Trim the generated images to their painted forms; black RGB at alpha zero stays transparent.
    plume = plume.crop((245, 25, 735, 1630))
    burst = burst.crop((12, 0, 1240, 1235))
    native = Image.new("RGBA", (S * 6, S * 6))

    # Six painted flame samples roll up and breathe slightly. Local y 0..191 is one 128x384 cell.
    scroll = [0, 1, 2, 3, 2, 1]
    for frame, shift in enumerate(scroll):
        body = Image.new("RGBA", (S, 3 * S))
        sample = plume.resize((46, 213), NEAREST).crop((0, shift, 46, shift + 186))
        sample = clean(sample)
        # Reanchor every frame; the changing source marks come from the generated painting.
        body.alpha_composite(sample, (9, 3))
        # Sparse square embers trace the rim, without turning the beam into a box.
        dr = ImageDraw.Draw(body)
        for j in range(13):
            y = 9 + ((j * 29 + frame * 7) % 172)
            x = 7 + ((j * 13 + frame * 5) % 50)
            if abs(x - 32) > 20:
                dr.point((x, y), fill=rgba("#FFE0F8" if j % 3 == 0 else "#FF4FD8", 210))
        native.alpha_composite(body, (frame * S, 0))

    # Tell: four rising pilots, followed by two dashed gas-jet tiles.
    for frame, (w, h) in enumerate(((13, 19), (17, 27), (21, 38), (25, 48))):
        cell = Image.new("RGBA", (S, S))
        art = clean(plume.resize((w, h), NEAREST))
        paste_center(cell, art, 59 - h)
        add_sparks(cell, 41 + frame, 2 + frame, max(4, 57 - h), 59)
        native.alpha_composite(cell, (frame * S, 3 * S))
    for frame in range(2):
        cell = Image.new("RGBA", (S, S))
        dr = ImageDraw.Draw(cell)
        for y in range(4 + frame * 3, 58, 13):
            dr.rectangle((30, y, 32, min(y + 5, 58)), fill=rgba("#FF8C18"))
            dr.line((29, y, 29, min(y + 5, 58)), fill=rgba("#FF4FD8"))
            dr.line((33, y, 33, min(y + 5, 58)), fill=rgba("#FF8AE6"))
            dr.point((31, min(y + 8, 58)), fill=rgba("#FFE0F8"))
            dr.point((29 + frame * 4, min(y + 9, 58)), fill=rgba("#FF4FD8"))
        native.alpha_composite(cell, ((4 + frame) * S, 3 * S))

    # Ignition: the generated burst supplies every ragged tongue and the center currents.
    for frame, size in enumerate((43, 50, 57, 54)):
        cell = Image.new("RGBA", (S, S))
        art = clean(burst.resize((size, size), NEAREST))
        paste_center(cell, art, (S - size) // 2)
        add_sparks(cell, 100 + frame, 3 + frame)
        native.alpha_composite(cell, (frame * S, 4 * S))

    # Impact: compress the painterly burst into the far-rail scorch fan; it then cools.
    for frame, (w, h, fade) in enumerate(((53, 35, 1), (49, 29, .78), (42, 22, .52))):
        cell = Image.new("RGBA", (S, S))
        art = clean(burst.resize((w, h), NEAREST), alpha_scale=fade)
        paste_center(cell, art, 9 + frame * 5)
        add_sparks(cell, 200 + frame, 5 - frame, 6, 50)
        xcell, ycell = ((4 + frame) * S, 4 * S) if frame < 2 else (0, 5 * S)
        native.alpha_composite(cell, (xcell, ycell))

    # Cooling haze stays below 25% opacity. It is sampled from the painted plume.
    for frame in range(2):
        cell = Image.new("RGBA", (S, S))
        art = clean(plume.resize((38 + 4 * frame, 53), NEAREST), edge_pink=False, alpha_scale=.20)
        ar = np.array(art)
        yy, xx = np.mgrid[:art.height, :art.width]
        side = np.minimum(xx, art.width - 1 - xx)
        ar[:, :, 3] = (ar[:, :, 3].astype(float) * np.clip(side / 5, 0, 1)).astype("uint8")
        art = Image.fromarray(ar, "RGBA")
        paste_center(cell, art, 5)
        native.alpha_composite(cell, ((1 + frame) * S, 5 * S))

    atlas = native.resize((768, 768), NEAREST)
    atlas.save(OUT / "ember_attack_mineflame.png")
    make_preview(atlas)


def make_preview(atlas: Image.Image) -> None:
    dark = Image.new("RGB", (1536, 1536), "#0b0b1a")
    dark.paste(atlas.resize((1536, 1536), NEAREST), mask=atlas.getchannel("A").resize((1536, 1536), NEAREST))
    # A small, game-scale cutout on dark and bright volcanic tiles.
    preview = Image.new("RGB", (1536, 1900), "#0b0b1a")
    preview.paste(dark, (0, 0))
    body = atlas.crop((0, 0, 128, 384)).resize((40, 120), NEAREST)
    burst = atlas.crop((2 * 128, 512, 3 * 128, 640)).resize((60, 60), NEAREST)
    for i, bg in enumerate(("#0b0b1a", "#c6782e")):
        tile = Image.new("RGB", (320, 180), bg)
        tile.paste(body, (80, 28), body)
        tile.paste(burst, (194, 60), burst)
        preview.paste(tile, (110 + i * 670, 1600))
    preview.save(OUT / "preview.png")

    frames = []
    for i in range(6):
        panel = Image.new("RGBA", (440, 420), rgba("#0B0B1A"))
        b = atlas.crop((128 * i, 0, 128 * (i + 1), 384))
        panel.alpha_composite(b, (0, 0))
        pilot = atlas.crop(((i % 4) * 128, 384, (i % 4 + 1) * 128, 512))
        panel.alpha_composite(pilot.resize((70, 70), NEAREST), (146, 174))
        strike = atlas.crop(((i % 4) * 128, 512, (i % 4 + 1) * 128, 640))
        panel.alpha_composite(strike.resize((82, 82), NEAREST), (238, 166))
        aim = atlas.crop(((4 + i % 2) * 128, 384, (5 + i % 2) * 128, 512))
        panel.alpha_composite(aim.resize((66, 66), NEAREST), (352, 75))
        impact_x, impact_y = ((4 + i % 2) * 128, 512) if i % 3 < 2 else (0, 640)
        impact = atlas.crop((impact_x, impact_y, impact_x + 128, impact_y + 128))
        panel.alpha_composite(impact.resize((70, 70), NEAREST), (350, 188))
        haze = atlas.crop(((1 + i % 2) * 128, 640, (2 + i % 2) * 128, 768))
        panel.alpha_composite(haze.resize((70, 70), NEAREST), (350, 286))
        frames.append(panel.convert("RGB"))
    frames[0].save(OUT / "preview.gif", save_all=True, append_images=frames[1:], duration=83, loop=0, optimize=False)


if __name__ == "__main__":
    build()
