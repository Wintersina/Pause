#!/usr/bin/env python3
"""Build seven-cell review strips from the four high-resolution Space concepts."""
from pathlib import Path

from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent.parent
CELL = 192
NAMES = ("rift_lancer", "eventide_bastion", "orbit_reaver", "singularity_hauler")
BOOSTERS = {
    "rift_lancer": ((84, 156), (108, 156)),
    "eventide_bastion": ((59, 142), (79, 142), (113, 142), (133, 142)),
    "orbit_reaver": ((80, 153), (96, 158), (112, 153)),
    "singularity_hauler": ((70, 148), (91, 151), (111, 151), (132, 148)),
}


def master(name):
    art = Image.open(HERE / f"space_elite_{name}_concept.png").convert("RGBA")
    alpha = art.getchannel("A").point(lambda value: 0 if value < 72 else min(255, round((value - 72) * 255 / 183)))
    art.putalpha(alpha)
    bounds = alpha.point(lambda value: 255 if value > 100 else 0).getbbox()
    art = art.crop(bounds)
    scale = min(158 / art.width, 154 / art.height)
    art = art.resize((round(art.width * scale), round(art.height * scale)), Image.Resampling.LANCZOS)
    cell = Image.new("RGBA", (CELL, CELL))
    cell.alpha_composite(art, ((CELL - art.width) // 2, (CELL - art.height) // 2))
    return cell


def engine_state(cell, strength):
    result = cell.copy()
    px = result.load()
    for y in range(CELL // 2, CELL):
        for x in range(CELL):
            r, g, b, a = px[x, y]
            if b > 120 and b > r * 1.35 and b > g * 1.08:
                px[x, y] = (r, g, b, round(a * strength))
    return result


def damaged(cell):
    result = cell.copy()
    draw = ImageDraw.Draw(result)
    draw.line(((91, 61), (99, 69), (95, 77), (105, 84)), fill=(255, 66, 183, 215), width=2)
    draw.line(((84, 93), (90, 99), (86, 105)), fill=(81, 205, 255, 175), width=1)
    draw.point((106, 82), fill=(255, 200, 236, 255))
    return result


def boosted(cell, name):
    result = Image.new("RGBA", (CELL, CELL))
    flames = Image.new("RGBA", (CELL, CELL))
    draw = ImageDraw.Draw(flames)
    for x, y in BOOSTERS[name]:
        y -= 12
        draw.polygon(((x - 8, y), (x + 8, y), (x, min(CELL - 3, y + 42))),
                     fill=(15, 87, 255, 175))
        draw.polygon(((x - 5, y), (x + 5, y), (x, min(CELL - 7, y + 29))),
                     fill=(22, 215, 255, 235))
        draw.polygon(((x - 2, y), (x + 2, y), (x, min(CELL - 10, y + 18))),
                     fill=(227, 255, 255, 255))
    result.alpha_composite(flames)
    result.alpha_composite(cell, (0, -12))
    return result


def build(name):
    base = master(name)
    states = (
        engine_state(base, 0.08),
        engine_state(base, 0.35),
        engine_state(base, 0.78),
        base,
        base.rotate(8, Image.Resampling.BICUBIC),
        base.rotate(-8, Image.Resampling.BICUBIC),
        damaged(engine_state(base, 0.65)),
    )
    strip = Image.new("RGBA", (CELL * len(states), CELL))
    for i, frame in enumerate(states):
        strip.alpha_composite(frame, (i * CELL, 0))
    strip.save(HERE / f"space_elite_{name}_strip_candidate.png")

    motion = (
        engine_state(base, 0.35),
        base.rotate(8, Image.Resampling.BICUBIC),
        base.rotate(-8, Image.Resampling.BICUBIC),
        boosted(base, name),
    )
    motion_strip = Image.new("RGBA", (CELL * len(motion), CELL))
    for i, frame in enumerate(motion):
        motion_strip.alpha_composite(frame, (i * CELL, 0))
    motion_strip.save(HERE / f"space_elite_{name}_motion_candidate.png")


if __name__ == "__main__":
    for ship in NAMES:
        build(ship)
