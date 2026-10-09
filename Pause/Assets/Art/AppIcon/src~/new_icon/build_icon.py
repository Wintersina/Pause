"""Compose Orbital Rail from the game's unmodified title, ship, and rail art."""
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageEnhance, ImageFilter, ImageFont, ImageOps


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[5]
NEAREST = Image.Resampling.NEAREST


def source_art():
    logo = Image.open(ROOT / "Pause/Assets/Art/UI/Title/pause_title_2.png").convert("RGBA")
    logo = logo.crop(logo.getchannel("A").getbbox())
    sheet = Image.open(ROOT / "Pause/Assets/Art/Resources/ShipArt/Skins/GoldWarden_Regent.bytes").convert("RGBA")
    hull = sheet.crop((0, 0, 256, 256))
    hull = hull.crop(hull.getchannel("A").getbbox())
    atlas = Image.open(ROOT / "Pause/Assets/Art/Resources/ShipArt/Exhaust/exhaust_atlas.png").convert("RGBA")
    # GoldWarden PlumeBoost, frame 2: the longest readable flame in its strip.
    flame = atlas.crop((400, 388, 448, 580))
    flame = flame.crop(flame.getchannel("A").getbbox())
    return logo, hull, flame


def scaled(im, width):
    height = round(im.height * width / im.width)
    return im.resize((width, height), NEAREST)


def paste(layer, art, xy):
    layer.alpha_composite(art, tuple(map(round, xy)))


def hard_glow(sprite, color, sizes):
    a = sprite.getchannel("A").point(lambda v: 255 if v > 40 else 0)
    result = Image.new("RGBA", sprite.size)
    for diameter, opacity in sizes:
        expanded = a.filter(ImageFilter.MaxFilter(diameter))
        tint = Image.new("RGBA", sprite.size, color + (0,))
        tint.putalpha(expanded.point(lambda v, o=opacity: o if v else 0))
        result = Image.alpha_composite(result, tint)
    return result


def planet(width):
    source = Image.open(HERE / "concept_rail_planet_source.png").convert("RGBA")
    source = source.crop((80, 760, 390, 1070))
    disk = Image.new("L", source.size)
    ImageDraw.Draw(disk).ellipse((4, 4, 305, 305), fill=255)
    source.putalpha(disk)
    return ImageEnhance.Brightness(source).enhance(.72).resize((width, width), NEAREST)


def muted_rail(crop):
    rail = Image.open(ROOT / "Pause/Assets/Art/Resources/Worlds/Space/rail_space_wide_v1.png").convert("RGBA")
    rail = rail.crop(crop)
    rail = ImageEnhance.Color(rail).enhance(.58)
    rail = ImageEnhance.Brightness(rail).enhance(.37)
    rail.putalpha(rail.getchannel("A").point(lambda a: round(a * .68)))
    return rail


def rail_background():
    """Keep real station metal at the outer edges of a quiet painted lane."""
    space = Image.open(HERE / "concept_rail_refined_base.png").convert("RGBA")
    bg = space.resize((1024, 1024), NEAREST)
    bg.alpha_composite(planet(132), (275, 62))
    # A 1:1 pixel crop is intentionally used; the game's actual rail texture
    # stays recognizable while most of its width falls outside the icon.
    rail = muted_rail((145, 180, 465, 1204))
    left = Image.new("RGBA", bg.size)
    left.alpha_composite(rail, (-139, 0))
    right = ImageOps.mirror(left)
    bg = Image.alpha_composite(bg, left)
    bg = Image.alpha_composite(bg, right)
    bg.putalpha(255)
    return bg


def adaptive_background():
    space = Image.open(HERE / "concept_rail_refined_base.png").convert("RGBA")
    bg = space.resize((432, 432), NEAREST)
    bg.alpha_composite(planet(54), (100, 105))
    # On Android the launcher may show any crop of the 432 px canvas. Keep the
    # rails within its central 288 px viewport without tiling the background.
    rail = muted_rail((340, 180, 460, 612))
    side = Image.new("RGBA", bg.size)
    side.alpha_composite(rail, (0, 0))
    bg = Image.alpha_composite(bg, side)
    bg = Image.alpha_composite(bg, ImageOps.mirror(side))
    bg.putalpha(255)
    return bg


def background(size, bg_name):
    if bg_name == "rail":
        return rail_background().resize((size, size), NEAREST)
    return Image.open(HERE / f"concept_{bg_name}.png").convert("RGBA").resize((size, size), NEAREST)


def scene(size, bg_name, adaptive=False):
    logo_src, hull_src, flame_src = source_art()
    bg = background(size, bg_name)
    fg = Image.new("RGBA", (size, size))
    logo_layer = Image.new("RGBA", (size, size))
    hull_layer = Image.new("RGBA", (size, size))
    flame_layer = Image.new("RGBA", (size, size))

    if adaptive:
        # The launcher shows the middle 288 of 432 px. Both source artworks fit
        # the 132 px safe circle as distinct silhouettes with a slight overlap.
        logo = scaled(logo_src, 204)
        hull = scaled(hull_src, 142)
        flame = scaled(flame_src, 33)
        paste(logo_layer, logo, (216 - logo.width / 2, 232))
        paste(hull_layer, hull, (216 - hull.width / 2, 88))
        paste(flame_layer, flame, (216 - flame.width / 2, 207))
        glow_sizes = ((19, 20), (9, 38), (3, 60))
    else:
        logo = scaled(logo_src, 820)
        hull = scaled(hull_src, 642)  # exact 3x original hull pixels
        flame = scaled(flame_src, 96)  # exact 2x original exhaust pixels
        paste(logo_layer, logo, (512 - logo.width / 2, 604))
        paste(hull_layer, hull, (512 - hull.width / 2, 34))
        paste(flame_layer, flame, (512 - flame.width / 2, 665))
        glow_sizes = ((49, 15), (23, 28), (7, 45))

    # The flame and hull rise from behind the intact title artwork. The glow
    # follows the sprite outline without any Gaussian or resampling blur.
    fg = Image.alpha_composite(fg, hard_glow(hull_layer, (29, 214, 239), glow_sizes))
    fg = Image.alpha_composite(fg, flame_layer)
    fg = Image.alpha_composite(fg, hull_layer)
    fg = Image.alpha_composite(fg, logo_layer)

    if bg_name == "chasm":
        # Reproduce the previous icon for the before/after preview.
        chips = Image.new("RGBA", (size, size))
        d = ImageDraw.Draw(chips)
        if adaptive:
            specs = [(111, 183, 3, "#6fe7ee"), (318, 173, 2, "#ffd36a"),
                     (119, 226, 2, "#ffe4a0"), (311, 217, 2, "#6fe7ee")]
        else:
            specs = [(107, 360, 10, "#6fe7ee"), (918, 373, 7, "#ffd36a"),
                     (155, 531, 8, "#ffe4a0"), (872, 545, 6, "#6fe7ee"),
                     (85, 602, 4, "#ffd36a"), (936, 607, 4, "#6fe7ee")]
        for x, y, r, col in specs:
            d.polygon(((x, y-r), (x+r, y), (x, y+r), (x-r, y)), fill=col)
        fg = Image.alpha_composite(fg, chips)
    if adaptive:
        safe = Image.new("L", (size, size))
        ImageDraw.Draw(safe).ellipse((85, 85, 347, 347), fill=255)
        fg.putalpha(ImageChops.multiply(fg.getchannel("A"), safe))
    return bg, fg, logo_layer


def mask(size, shape):
    out = Image.new("L", (size, size))
    d = ImageDraw.Draw(out)
    if shape == "circle":
        d.ellipse((0, 0, size-1, size-1), fill=255)
    elif shape == "squircle":
        # A rounded square at ~22% corner radius.
        d.rounded_rectangle((0, 0, size-1, size-1), radius=round(size*.22), fill=255)
    else:
        d.rectangle((0, 0, size-1, size-1), fill=255)
    return out


def masked(image, shape):
    out = image.convert("RGBA")
    out.putalpha(mask(out.width, shape))
    return out


def font(size):
    for name in ("/System/Library/Fonts/Supplemental/Arial.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"):
        if Path(name).exists():
            return ImageFont.truetype(name, size)
    return ImageFont.load_default()


def label(draw, xy, text, size=22, fill="#e5ebff"):
    draw.text(xy, text, font=font(size), fill=fill)


def preview(master, afg, abg):
    # Each launcher treatment is shown on both wallpapers at three sizes.
    W, H = 1720, 2180
    sheet = Image.new("RGB", (W, H), "#0b1028")
    d = ImageDraw.Draw(sheet)
    label(d, (40, 25), "PAUSE  /  ORBITAL RAIL", 34)
    label(d, (40, 75), "Exact menu wordmark + GoldWarden Regent hull", 20, "#a9badd")
    adaptive = Image.alpha_composite(abg, afg).crop((72, 72, 360, 360))
    tiles = [("DARK WALLPAPER / MASTER", master, (15, 19, 49), 112),
             ("DARK WALLPAPER / ADAPTIVE", adaptive, (15, 19, 49), 535),
             ("LIGHT WALLPAPER / MASTER", master, (225, 231, 241), 958),
             ("LIGHT WALLPAPER / ADAPTIVE", adaptive, (225, 231, 241), 1381)]
    for title, source, color, top in tiles:
        d.rounded_rectangle((35, top, 1685, top+385), radius=24, fill=color)
        light_ink = top < 958
        label(d, (58, top+17), title, 23, "#f1f5ff" if light_ink else "#273149")
        for i, shape in enumerate(("circle", "squircle", "square")):
            x = 80 + i*535
            label(d, (x, top+68), shape.upper(), 20, "#cfd9f1" if light_ink else "#273149")
            for j, s in enumerate((192, 96, 48)):
                icon = source.resize((s, s), NEAREST)
                icon = masked(icon, shape)
                sheet.paste(icon, (x + [0, 228, 355][j], top+112), icon)
                label(d, (x + [0, 228, 355][j], top+320), str(s) + " px", 18,
                      "#cfd9f1" if light_ink else "#273149")

    label(d, (40, 1800), "BEFORE / AFTER  +  BACKGROUND ALONE", 22)
    old_bg, old_fg, _ = scene(1024, "chasm")
    examples = [("Before: Chasm breakout", Image.alpha_composite(old_bg, old_fg)),
                ("After: Orbital Rail", master),
                ("Adaptive background only", abg.crop((72, 72, 360, 360)))]
    for i, (title, source) in enumerate(examples):
        x = 160 + i*510
        icon = source.resize((290, 290), NEAREST)
        sheet.paste(icon.convert("RGB"), (x, 1840))
        label(d, (x, 2135), title, 20)
    sheet.convert("RGBA").save(HERE / "preview.png")


def main():
    bg, fg, logo = scene(1024, "rail")
    master = Image.alpha_composite(bg, fg)
    master.putalpha(255)
    master.save(HERE / "master_1024.png")
    master.convert("RGB").save(HERE / "ios_1024.png")
    for shape, name in (("squircle", "legacy_192.png"), ("circle", "round_192.png")):
        masked(master.resize((192, 192), NEAREST), shape).save(HERE / name)
    abg = adaptive_background()
    abg.save(HERE / "adaptive_bg_432.png")
    _, afg, _ = scene(432, "rail", adaptive=True)
    afg.save(HERE / "adaptive_fg_432.png")
    preview(master, afg, abg)


if __name__ == "__main__":
    main()
