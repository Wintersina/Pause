"""Build the high-resolution Space planet atlases from generated source paintings.

Run from the repository root with Python 3, Pillow and NumPy installed.
All outputs remain in the Unity-ignored hires_planets~ folder.
"""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


HERE = Path(__file__).resolve().parent
OUT = HERE.parent
REPO = HERE.parents[6]
BACKDROP = REPO / "Pause/Assets/Art/Backgrounds/Resources/Worlds/Space/Backdrop"
OLD_JSON = BACKDROP / "anim.json"
OLD_SHEET = BACKDROP / "anim.png"
SKY = BACKDROP / "sky_02.png"
MASTER_SCALE = 4
CELL = 1024
PADDING = 10  # 2.5 old-art pixels on each side at 4x.


def font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    for path in ("/System/Library/Fonts/Supplemental/Arial.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"):
        if Path(path).exists():
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def paint_disc(name: str, width: int, height: int) -> Image.Image:
    source = Image.open(HERE / f"{name}_source.png").convert("RGBA")
    alpha = source.getchannel("A").point(lambda value: 255 if value >= 128 else 0)
    bounds = alpha.getbbox()
    if bounds is None:
        raise ValueError(f"Empty source: {name}")
    # The generated paintings are new source art, roughly 1254 px across.
    # Their natural silhouette is fitted once into the exact old-art footprint.
    body = source.crop(bounds).resize((width, height), Image.Resampling.NEAREST)
    rgb = Image.new("RGB", body.size, (7, 10, 28))
    rgb.paste(body, (0, 0), body.getchannel("A"))
    # A per-world compact palette creates deliberate color islands, while the
    # drawn cloud and crater structures remain from the generated paintings.
    rgb = rgb.quantize(colors=128, method=Image.Quantize.MEDIANCUT,
                       dither=Image.Dither.NONE).convert("RGB")
    # Keep saturated highlights at least 28 degrees away from the player's
    # red (#FF3E4E, about 354 degrees). Warm cracks become gold; hot pink
    # becomes violet-magenta.
    hsv = np.asarray(rgb.convert("HSV"), dtype=np.uint8).copy()
    saturated = (hsv[:, :, 1] > 115) & (hsv[:, :, 2] > 51)
    hue = hsv[:, :, 0]
    # Leave extra room because 2x box filtering can blend neighboring warm
    # and violet pixels into red-adjacent intermediate colors.
    hue[saturated & (hue <= 25)] = 33    # 47 degrees, gold side
    hue[saturated & (hue >= 225)] = 212  # 299 degrees, violet side
    rgb = Image.fromarray(hsv, "HSV").convert("RGB")
    pixels = np.asarray(rgb, dtype=np.float32).copy()
    yy, xx = np.mgrid[:height, :width]
    nx = (xx + 0.5 - width / 2) / (width / 2)
    ny = (yy + 0.5 - height / 2) / (height / 2)
    radius = np.sqrt(nx * nx + ny * ny)
    # One-pixel antialiased silhouette. Every glow pixel stays inside the disc.
    feather = 2 / min(width, height)
    mask = np.clip((1 - radius) / feather, 0, 1)
    # Preserve the hand-painted illumination and reinforce only the outer 4%
    # of the lit limb, safely inside the shader's fixed 14% rim.
    lit = np.clip((nx + 0.08) * 1.5, 0, 1)
    rim = np.clip((radius - 0.958) / 0.025, 0, 1) * np.clip((1 - radius) / 0.018, 0, 1) * lit
    pixels += rim[..., None] * np.array([6, 38, 48], dtype=np.float32)
    # A thin indigo definition on the shadowed limb controls the silhouette.
    outline = ((radius > 0.993) & (nx < 0)).astype(np.float32)
    pixels *= 1 - outline[..., None] * 0.35
    result = Image.fromarray(np.uint8(np.clip(pixels, 0, 255)), "RGB").convert("RGBA")
    result.putalpha(Image.fromarray(np.uint8(np.round(mask * 255)), "L"))
    return result


def json_sheet(sprites: list[dict], scale: int, cell: int) -> dict:
    entries = []
    for i, old in enumerate(sprites):
        width, height = old["w"] * scale, old["h"] * scale
        x = (i % 4) * cell + (cell - width) // 2
        y = (3 - i // 4) * cell + (cell - height) // 2
        entries.append({"n": old["n"], "x": x, "y": y,
                        "w": width, "h": height})
    return {"pixelScale": scale, "sheetW": cell * 4,
            "sheetH": cell * 4, "sprites": entries}


def render_sheet(sprites: list[dict]) -> tuple[Image.Image, dict]:
    sheet = Image.new("RGBA", (4096, 4096), (0, 0, 0, 0))
    data = json_sheet(sprites, 4, 1024)
    for entry in data["sprites"]:
        width, height = entry["w"] - 2 * PADDING, entry["h"] - 2 * PADDING
        disc = paint_disc(entry["n"], width, height)
        col = entry["x"] + PADDING
        top = 4096 - (entry["y"] + entry["h"]) + PADDING
        sheet.alpha_composite(disc, (col, top))
    return sheet, data


def space_background(size: tuple[int, int]) -> Image.Image:
    base = Image.open(SKY).convert("RGB")
    w, h = size
    scale = max(w / base.width, h / base.height)
    base = base.resize((round(base.width * scale), round(base.height * scale)), Image.Resampling.LANCZOS)
    x = (base.width - w) // 2
    y = (base.height - h) // 2
    base = base.crop((x, y, x + w, y + h))
    return Image.blend(base, Image.new("RGB", size, (3, 5, 19)), 0.38)


def preview(master: Image.Image, old: Image.Image) -> None:
    new = master.resize((2048, 2048), Image.Resampling.BOX)
    before = old.resize((2048, 2048), Image.Resampling.NEAREST)
    canvas = space_background((4096, 2192)).convert("RGBA")
    shade = Image.new("RGBA", (4096, 2048), (2, 3, 14, 155))
    canvas.alpha_composite(shade, (0, 144))
    canvas.alpha_composite(new, (0, 144))
    canvas.alpha_composite(before, (2048, 144))
    draw = ImageDraw.Draw(canvas)
    title = font(58)
    label = font(36)
    draw.text((56, 32), "NEW  •  2048 runtime sheet", fill=(221, 240, 255), font=title)
    draw.text((2100, 32), "BEFORE  •  original 1x enlarged 2x", fill=(221, 240, 255), font=title)
    names = [f"giant_{i:02d}" for i in range(12)] + [f"rocky_{i:02d}" for i in range(4)]
    for i, name in enumerate(names):
        x, y = (i % 4) * 512, 144 + (i // 4) * 512
        draw.text((x + 22, y + 8), name, fill=(245, 238, 255), font=label,
                  stroke_width=3, stroke_fill=(3, 4, 17))
        draw.text((2048 + x + 22, y + 8), name, fill=(245, 238, 255), font=label,
                  stroke_width=3, stroke_fill=(3, 4, 17))
    canvas.convert("RGB").save(OUT / "preview.png", optimize=True)


def phone_preview(master: Image.Image, sheet_data: dict) -> None:
    phone = space_background((1170, 2532)).convert("RGBA")
    for name, x, y in (("giant_00", 60, 160), ("giant_05", 510, 940), ("giant_10", 90, 1720)):
        entry = next(e for e in sheet_data["sprites"] if e["n"] == name)
        top = 4096 - (entry["y"] + entry["h"])
        art = master.crop((entry["x"], top, entry["x"] + entry["w"], top + entry["h"]))
        art = art.resize((600, round(600 * entry["h"] / entry["w"])), Image.Resampling.NEAREST)
        phone.alpha_composite(art, (x, y))
    phone.convert("RGB").save(OUT / "preview_phone.png", optimize=True)


def main() -> None:
    old_data = json.loads(OLD_JSON.read_text())
    sprites = old_data["sprites"]
    assert len(sprites) == 16
    master, data4 = render_sheet(sprites)
    data2 = json_sheet(sprites, 2, 512)
    master.save(OUT / "anim_hires_4096.png", optimize=True)
    (OUT / "anim_hires_4096.json").write_text(json.dumps(data4, indent=2) + "\n")
    runtime = master.resize((2048, 2048), Image.Resampling.BOX)
    runtime.save(OUT / "anim_hires.png", optimize=True)
    (OUT / "anim_hires.json").write_text(json.dumps(data2, indent=2) + "\n")
    preview(master, Image.open(OLD_SHEET).convert("RGBA"))
    phone_preview(master, data4)


if __name__ == "__main__":
    main()
