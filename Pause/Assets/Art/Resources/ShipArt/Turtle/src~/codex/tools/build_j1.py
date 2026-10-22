from pathlib import Path

from PIL import Image, ImageChops, ImageDraw


OUT = Path(__file__).resolve().parent.parent
GREEN = (0, 255, 0)
SIZE = (1024, 1024)

BASE_SRC = Path('/Users/sina/.codex/generated_images/01a16483-09dd-76d3-a547-2f7abda6a7b9/exec-0ab3bb8d-a3b0-4efa-a9c2-df2090c1542e.png')
MODEST_SRC = Path('/Users/sina/.codex/generated_images/01a16483-09dd-76d3-a547-2f7abda6a7b9/exec-ad081bbd-7b1e-4fb1-ab74-1e2a8dcba115.png')
HEAVY_SRC = Path('/Users/sina/.codex/generated_images/01a16483-09dd-76d3-a547-2f7abda6a7b9/exec-921e0c07-769c-4f04-9859-9caa94318f59.png')


def normalize(path: Path) -> Image.Image:
    image = Image.open(path).convert('RGB').resize(SIZE, Image.Resampling.NEAREST)
    pixels = image.load()
    # Image generation gives the requested green backdrop a small amount of
    # colour variation. Key only that backdrop; ship pixels contain no green.
    for y in range(SIZE[1]):
        for x in range(SIZE[0]):
            r, g, b = pixels[x, y]
            if g >= 120 and g >= r + 70 and g >= b + 70:
                pixels[x, y] = GREEN
    return image


def silhouette(image: Image.Image) -> Image.Image:
    mask = Image.new('L', SIZE, 0)
    src = image.load()
    dst = mask.load()
    for y in range(SIZE[1]):
        for x in range(SIZE[0]):
            dst[x, y] = 255 if src[x, y] != GREEN else 0
    return mask


def region_mask(polygons, base, damage):
    mask = Image.new('L', SIZE, 0)
    draw = ImageDraw.Draw(mask)
    for polygon in polygons:
        draw.polygon(polygon, fill=255)
    # Never let a generated edit create or erase silhouette pixels.
    mask = ImageChops.multiply(mask, silhouette(base))
    mask = ImageChops.multiply(mask, silhouette(damage))
    return mask


def compose(base, damage, polygons):
    return Image.composite(damage, base, region_mask(polygons, base, damage))


def save(image, name):
    image.save(OUT / name, format='PNG', optimize=False)


base = normalize(BASE_SRC)
modest = normalize(MODEST_SRC)
heavy = normalize(HEAVY_SRC)

# State 1: one cavity on each side, two upper-scute cracks, soot, and nozzle
# scorch. The polygons are deliberately tight; all other pixels remain base.
modest_regions = [
    [(305, 250), (415, 235), (465, 315), (452, 430), (395, 485), (315, 455), (286, 350)],
    [(614, 565), (757, 560), (790, 650), (756, 780), (684, 826), (615, 764)],
    [(458, 389), (578, 392), (605, 520), (565, 570), (465, 562), (430, 480)],
    [(476, 920), (586, 914), (618, 969), (456, 980)],
]
state1 = compose(base, modest, modest_regions)

# State 2: use the generated heavy paint only in distinct damage zones. The
# intact mid-shell armour and lower side plates deliberately remain untouched.
heavy_regions = [
    [(292, 220), (447, 210), (486, 335), (455, 470), (327, 505), (278, 384)],
    [(625, 216), (782, 205), (820, 350), (806, 470), (684, 490), (625, 388)],
    [(440, 388), (602, 380), (610, 572), (438, 575)],
    [(694, 356), (840, 320), (872, 650), (783, 760), (688, 665)],
    [(420, 656), (618, 640), (650, 835), (426, 840)],
    [(454, 104), (604, 104), (620, 236), (446, 244)],
    [(700, 170), (850, 155), (920, 300), (922, 474), (834, 520), (758, 455)],
    [(457, 905), (623, 892), (658, 985), (428, 987)],
]
state2 = compose(base, heavy, heavy_regions)

save(base, 'key_state0.png')
save(state1, 'key_state1.png')
save(state2, 'key_state2.png')

# Three-up review preview on the requested dark background.
preview = Image.new('RGB', (3072, 1024), (11, 11, 26))
for index, image in enumerate((base, state1, state2)):
    preview.paste(image, (index * 1024, 0), silhouette(image))
preview.save(OUT / 'preview_j1.png', format='PNG', optimize=False)
