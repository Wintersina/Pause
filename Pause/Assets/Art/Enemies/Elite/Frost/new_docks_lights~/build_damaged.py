#!/usr/bin/env python3
"""Build Frost's six-frame weak, ragged damaged-engine loops."""
from pathlib import Path
import importlib.util
import random
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
CELL = 384
COUNT = 6
# the strip cell that holds each ship's damaged hull (EliteDef cells.damaged): NOT cell 9 for every ship
DAMAGED_CELL = {"rimebreaker": 9, "cryo_siren": 9, "floe_harrower": 6, "glacier_tender": 6, "whiteout_sentinel": 6}
SHIPS = {
    "rimebreaker": {"erase": True, "nozzles": [(164, 233), (219, 233)], "flame": ((30, 130, 228), (72, 196, 255), (205, 250, 255)), "half": 20, "lengths": (18, 27, 13, 24, 11, 21)},
    "floe_harrower": {"nozzles": [(166, 286), (218, 286)], "flame": ((24, 135, 232), (70, 205, 255), (210, 252, 255)), "half": 19, "lengths": (17, 26, 12, 23, 10, 20)},
    "cryo_siren": {"flame_dy": -3, "nozzles": [(183, 299), (202, 300)], "flame": ((25, 145, 235), (90, 222, 255), (224, 255, 255)), "half": 13, "lengths": (16, 25, 11, 22, 9, 19)},
    "glacier_tender": {"nozzles": [(154, 316), (230, 316)], "flame": ((22, 128, 219), (66, 192, 250), (202, 246, 255)), "half": 22, "lengths": (18, 28, 13, 24, 10, 22)},
    "whiteout_sentinel": {"nozzles": [(168, 302), (216, 302)], "flame": ((28, 137, 224), (70, 202, 255), (215, 253, 255)), "half": 18, "lengths": (17, 27, 12, 23, 10, 21)},
}

spec = importlib.util.spec_from_file_location("frost_build_art", HERE / "build_art.py")
art = importlib.util.module_from_spec(spec); spec.loader.exec_module(art)


def clean(name, cfg):
    source = Image.open(ROOT / f"frost_elite_{name}.png").convert("RGBA")
    parked = source.crop((0, 0, CELL, CELL))
    c = DAMAGED_CELL[name]
    damaged = source.crop((c * CELL, 0, (c + 1) * CELL, CELL)).copy()
    for x, y in cfg["nozzles"]:
        h = cfg["half"]
        if cfg.get("erase"):
            # the damaged hull is drawn differently from the parked one (nothing to restore from): clear the painted flames under the bells
            damaged.paste((0, 0, 0, 0), (x - h, y + 4, x + h + 1, CELL))
            continue
        box = (max(0, x - h), max(0, y - 22), min(CELL, x + h + 1), CELL)
        damaged.paste(parked.crop(box), box)
    return damaged


def flame(draw, x, y, length, width, colors, rng, dead=False):
    outer, mid, core = colors
    if dead:
        draw.rectangle((x - 3, y, x + 3, y + 3), fill=(*outer, 185))
        if rng.random() < .52:
            draw.rectangle((x - 1, y + 5, x + 1, y + 7), fill=(*mid, 185))
        return
    for row in range(length):
        t = row / max(1, length - 1)
        taper = max(.28, 1.0 - .80 * t)
        jitter = rng.choice((-2, -1, 0, 0, 0, 1, 2)) if row > 3 else 0
        half = max(1, round(width * taper * (.70 if row > length * .58 else 1.0)))
        if row > length * .43 and rng.random() < .19 + .16 * t:
            continue
        color = outer if row < 2 else mid if row < length * .68 else core
        draw.rectangle((x - half + jitter, y + row, x + half + jitter, y + row + 2), fill=(*color, 205 if row < length * .63 else 165))
    for _ in range(1 + rng.randrange(2)):
        yy = y + length + rng.randrange(2, 9)
        if yy < CELL - 2:
            xx = x + rng.randrange(-max(2, width // 2), max(3, width // 2 + 1))
            draw.rectangle((xx, yy, xx + 2, yy + 2), fill=(*mid, 150))


def build(name, cfg):
    hull = clean(name, cfg)
    frames = []
    for f in range(COUNT):
        layer = Image.new("RGBA", (CELL, CELL)); draw = ImageDraw.Draw(layer); rng = random.Random(4100 + f * 47)
        for i, (x, y) in enumerate(cfg["nozzles"]):
            phase = (f * 2 + i * 3) % COUNT
            flame(draw, x, y + cfg.get("flame_dy", 5), cfg["lengths"][phase], 8 + (i % 2) * 2, cfg["flame"], rng, dead=(f == 2 and i == 0) or (f == 3 and i == 1))   # (drawing 0 is the damaged cell the tests read: both engines burn there)
        out = hull.copy(); out.alpha_composite(layer); frames.append(out)
    strip = Image.new("RGBA", (CELL * COUNT, CELL))
    for i, im in enumerate(frames): strip.alpha_composite(im, (i * CELL, 0))
    outdir = HERE / "damaged"; outdir.mkdir(exist_ok=True)
    strip.save(outdir / f"frost_elite_{name}_damaged.png")

    source = Image.open(ROOT / f"frost_elite_{name}.png").convert("RGBA")
    points = art.feature_points(source.crop((0, 0, CELL, CELL)))
    lights = Image.new("RGBA", (CELL * COUNT, CELL))
    for f in range(COUNT):
        lights.alpha_composite(art.glow_layer((CELL, CELL), points, f + 7, damaged=True), (f * CELL, 0))
    lights.save(outdir / f"frost_elite_{name}_damaged_lights.png")
    preview = Image.new("RGBA", strip.size)
    for i, im in enumerate(frames): preview.alpha_composite(Image.alpha_composite(im, lights.crop((i * CELL, 0, (i + 1) * CELL, CELL))), (i * CELL, 0))
    preview.save(outdir / f"frost_elite_{name}_damaged_preview.png")
    cells = [preview.crop((i * CELL, 0, (i + 1) * CELL, CELL)) for i in range(COUNT)]
    cells[0].save(outdir / f"frost_elite_{name}_damaged.gif", save_all=True, append_images=cells[1:], duration=130, loop=0, disposal=2)
    return strip


def main():
    results = {name: build(name, cfg) for name, cfg in SHIPS.items()}
    sheet = Image.new("RGBA", (1440, 960), (5, 14, 27, 255)); d = ImageDraw.Draw(sheet)
    for j, (name, strip) in enumerate(results.items()):
        x, y = (j % 3) * 480, (j // 3) * 480
        d.text((x + 12, y + 10), name.replace("_", " ") + " damaged", fill=(205, 245, 255, 255))
        for f in range(COUNT):
            im = strip.crop((f * CELL, 0, (f + 1) * CELL, CELL)).resize((145, 145), Image.Resampling.NEAREST)
            sheet.alpha_composite(im, (x + 12 + (f % 3) * 155, y + 42 + (f // 3) * 175))
    sheet.convert("RGB").save(HERE / "damaged" / "contact_sheet.png")


if __name__ == "__main__": main()
