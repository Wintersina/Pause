"""Audit the delivered strip and make its contact sheet and flight GIF."""
from pathlib import Path
import colorsys
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
ART = ROOT.parents[3]  # Assets/Art
STRIP = ROOT / "frost_elite_rimebreaker.png"
OLD = ART / "Resources/Elites/Frost/frost_elite_rimebreaker.png"
FLOE = ART / "Resources/Elites/Frost/frost_elite_floe_harrower.png"
ROD = ART / "Resources/Elites/Storm/storm_elite_rod_dancer.png"
BG = (11, 11, 26, 255)
N = Image.Resampling.NEAREST


def centroid(im):
    a = im.getchannel("A")
    data = a.load()
    points = [(x, y) for y in range(im.height) for x in range(im.width) if data[x, y] >= 128]
    return (sum(x for x, _ in points) / len(points), sum(y for _, y in points) / len(points))


def band_shares(im):
    pixels = [p for p in im.get_flattened_data() if p[3] >= 128]
    red = pink = 0
    for r, g, b, _ in pixels:
        h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
        deg = h * 360
        if s < .35 or v < .18:
            continue
        red += deg >= 345 or deg <= 15
        pink += 290 <= deg < 345
    return red / len(pixels), pink / len(pixels)


def composite_scaled(path):
    im = Image.open(path).convert("RGBA")
    ground = Image.new("RGBA", im.size, BG)
    ground.alpha_composite(im)
    return ground.resize((im.width * 2, im.height * 2), N)


def make_sheet():
    rows = [composite_scaled(p) for p in (STRIP, OLD, FLOE, ROD)]
    gutter = 24
    sheet = Image.new("RGBA", (2688, 384 * 4 + gutter * 3), BG)
    for i, row in enumerate(rows):
        sheet.alpha_composite(row, (0, i * (384 + gutter)))
    sheet.convert("RGB").save(ROOT / "contact_sheet.png")


def make_gif(cells):
    frames = []
    for i in (3, 4, 3, 5):
        ground = Image.new("RGBA", (192, 192), BG)
        ground.alpha_composite(cells[i])
        frames.append(ground.resize((384, 384), N).convert("RGB"))
    frames[0].save(ROOT / "preview.gif", save_all=True, append_images=frames[1:], duration=[180, 130, 180, 130], loop=0, optimize=False)


def main():
    im = Image.open(STRIP)
    assert im.size == (1344, 192), im.size
    assert im.mode == "RGBA", im.mode
    assert set(im.getchannel("A").get_flattened_data()) <= {0, 255}, "solid sprite must have binary alpha"
    cells = [im.crop((i * 192, 0, (i + 1) * 192, 192)) for i in range(7)]
    centres = [centroid(c) for c in cells]
    print(f"strip size={im.width}x{im.height} mode={im.mode}; cell=192x192")
    print("cell  hull/art bbox       margins L T R B  centroid x,y    Δ vs hover  colours  red%    pink%")
    for i, c in enumerate(cells):
        box = c.getbbox()
        assert box is not None
        x0, y0, x1, y1 = box
        margins = (x0, y0, 192 - x1, 192 - y1)
        assert min(margins) >= 16, (i, margins)
        dx, dy = centres[i][0] - centres[3][0], centres[i][1] - centres[3][1]
        colors = len(c.getcolors(37000) or [])
        red, pink = band_shares(c)
        print(f"{i:>4}  {str(box):<19}  {str(margins):<16}  ({centres[i][0]:5.1f},{centres[i][1]:5.1f})  ({dx:+4.1f},{dy:+4.1f})  {colors:>6}  {100*red:5.3f}  {100*pink:6.3f}")
        assert colors >= 1000, (i, colors)
        assert red < .001 and pink < .001, (i, red, pink)
    for i in (4, 5):
        assert abs(centres[i][0] - centres[3][0]) <= 4
        assert abs(centres[i][1] - centres[3][1]) <= 4
    hull_box = cells[0].getbbox()
    assert hull_box == cells[3].getbbox(), (hull_box, cells[3].getbbox())
    print(f"cell 3 hull bbox: {hull_box} = {hull_box[2]-hull_box[0]}x{hull_box[3]-hull_box[1]} (from opaque landed hull)")
    print("cell 3 gameplay points: prow muzzle=(98,43); nozzle L=(85,133), R=(109,133)")
    make_sheet()
    make_gif(cells)
    print("wrote contact_sheet.png and preview.gif")


if __name__ == "__main__":
    main()
