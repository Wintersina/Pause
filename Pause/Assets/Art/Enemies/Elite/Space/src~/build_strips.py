#!/usr/bin/env python3
"""Build twelve-cell review strips from the four high-resolution Space concepts."""
from pathlib import Path

from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent.parent
CELL = 192
NAMES = ("rift_lancer", "eventide_bastion", "orbit_reaver", "singularity_hauler")
BOOST_SIZE = {
    "rift_lancer": (54, 50),
    "eventide_bastion": (105, 53),
    "orbit_reaver": (72, 52),
    "singularity_hauler": (90, 53),
}
BOOST_FX = Image.open(HERE / "src~" / "boost_fx.png").convert("RGBA")
DEATH_FX = Image.open(HERE / "src~" / "death_fx.png").convert("RGBA")


def effect_cell(atlas, index, count):
    width = atlas.width // count
    cell = atlas.crop((index * width, 0, (index + 1) * width, atlas.height))
    return cell.crop(cell.getchannel("A").getbbox())


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


def boosted(cell, name, index):
    result = Image.new("RGBA", (CELL, CELL))
    width, height = BOOST_SIZE[name]
    if index == 0:
        width, height = round(width * .82), round(height * .78)
    plume = effect_cell(BOOST_FX, index, 2).resize((width, height), Image.Resampling.NEAREST)
    result.alpha_composite(plume, ((CELL - width) // 2, CELL - height))
    result.alpha_composite(cell)
    return result


def death_frame(cell, index):
    result = Image.new("RGBA", (CELL, CELL))
    if index < 2:
        hull = damaged(cell)
        if index == 1:
            hull.putalpha(hull.getchannel("A").point(lambda a: round(a * .38)))
        result.alpha_composite(hull)
    size = (130, 177, 168)[index]
    effect = effect_cell(DEATH_FX, index, 3).resize((size, size), Image.Resampling.NEAREST)
    effect.putalpha(effect.getchannel("A").point(lambda a: round(a * (.82, .94, .9)[index])))
    result.alpha_composite(effect, ((CELL - size) // 2, (CELL - size) // 2))
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
    frames = states + (boosted(base, name, 0), boosted(base, name, 1)) + tuple(death_frame(base, i) for i in range(3))
    strip = Image.new("RGBA", (CELL * len(frames), CELL))
    for i, frame in enumerate(frames):
        strip.alpha_composite(frame, (i * CELL, 0))
    strip.save(HERE / f"space_elite_{name}_strip_candidate.png")


if __name__ == "__main__":
    for ship in NAMES:
        build(ship)
