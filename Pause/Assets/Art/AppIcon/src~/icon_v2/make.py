from PIL import Image, ImageDraw, ImageFilter
import numpy as np

SIZE = 4096
BG = 'out3/assets/starfield.png'
SHIP = 'out3/assets/ship.png'

background = Image.open(BG).convert('RGB')
moon = Image.open('ref/moon.png').convert('RGBA')
# Shift the source's tiny violet crater glints toward cool blue without
# changing its crater detail, silhouette, or lighting.
mp = np.array(moon)
mr, mg, mb = [mp[:, :, i].astype(np.int16) for i in range(3)]
violet = (mb > mg * 1.18) & (mr > mg * 1.20) & (mb > 45) & (mp[:, :, 3] > 8)
mp[violet, 0] = np.clip(mg[violet] * 0.85, 0, 255)
mp[violet, 1] = np.maximum(mg[violet], mb[violet] * 0.58).clip(0, 255)
moon = Image.fromarray(mp, 'RGBA')
logo = Image.open('ref/title_logo.png').convert('RGBA').crop((69, 272, 990, 706))
ship = Image.open(SHIP).convert('RGBA').crop((259, 5, 994, 1243))

# Keep the red hull and blue highlights, shifting only the pink exhaust to cyan.
pix = np.array(ship)
r, g, b = [pix[:, :, i].astype(np.int16) for i in range(3)]
pink = (r > b * 0.70) & (b > g * 1.25) & (r > g * 1.4) & (pix[:, :, 3] > 16)
pix[pink, 0] = np.clip(g[pink] * 0.75, 0, 255)
pix[pink, 1] = np.maximum(g[pink], b[pink] * 0.88).clip(0, 255)
pix[pink, 2] = np.maximum(b[pink], r[pink] * 0.8).clip(0, 255)
ship = Image.fromarray(pix, 'RGBA')

variants = [
    dict(angle=0, moon=(2510, -275, 1910), ship=(1768, 2790, 750)),
]

for number, cfg in enumerate(variants, 1):
    canvas = background.rotate(cfg['angle']).resize((SIZE, SIZE), Image.Resampling.LANCZOS).convert('RGBA')

    # Deepen the title field while retaining visible stars and dust around it.
    shade = Image.new('RGBA', (SIZE, SIZE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(shade)
    draw.ellipse((50, 1050, 4050, 3280), fill=(0, 2, 9, 104))
    shade = shade.filter(ImageFilter.GaussianBlur(240))
    canvas = Image.alpha_composite(canvas, shade)

    mx, my, ms = cfg['moon']
    m = moon.resize((ms, ms), Image.Resampling.LANCZOS)
    canvas.alpha_composite(m, (mx, my))

    sx, sy, sw = cfg['ship']
    sh = ship.resize((sw, round(sw * ship.height / ship.width)), Image.Resampling.LANCZOS)
    canvas.alpha_composite(sh, (sx, sy))

    lw = round(SIZE * 0.92 * 1.10)
    lh = round(lw * logo.height / logo.width)
    mark = logo.resize((lw, lh), Image.Resampling.LANCZOS)
    canvas.alpha_composite(mark, ((SIZE - lw) // 2, 1150 - (lh - round(round(SIZE*0.92)*logo.height/logo.width))//2))

    canvas.convert('RGB').save(f'out4/v{number}.png', optimize=True)
