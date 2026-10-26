from pathlib import Path
import math
from PIL import Image


ROOT = Path(__file__).resolve().parent
LOOP_SRC = Path('/Users/sina/.codex/generated_images/01a1765d-383d-7331-96e8-f634be940dfc/exec-5cbc9477-8d00-4f72-92a4-c69da38b7417.png')
BURST_SRC = Path('/Users/sina/.codex/generated_images/01a1765d-383d-7331-96e8-f634be940dfc/exec-66d05048-02a8-4d59-8f3f-b73cf5b8c4c0.png')

PALETTE = [
    (17, 29, 43),    # deep outline
    (42, 64, 83),    # dark slate
    (77, 106, 132),  # slate
    (107, 139, 164), # light slate
    (23, 116, 177),  # blue accent
    (37, 186, 230),  # cyan accent
    (182, 202, 211), # bone
    (231, 233, 222), # cream
    (255, 250, 231), # hot cream
    (223, 54, 43),   # restrained red spark
]


def hard_clean(im: Image.Image) -> Image.Image:
    im = im.convert('RGBA')
    px = []
    for r, g, b, a in im.getdata():
        if a < 32:
            px.append((0, 0, 0, 0))
            continue
        # The generated artwork contains a few cyan/blue glow pixels. Keep the
        # hard pixel cluster but collapse every visible color to the small VFX palette.
        best = min(PALETTE, key=lambda c: (c[0] - r) ** 2 + (c[1] - g) ** 2 + (c[2] - b) ** 2)
        px.append((*best, 255))
    out = Image.new('RGBA', im.size)
    out.putdata(px)
    return out


def tile_frames(src: Path, cols: int, rows: int, target_span: int):
    clean = hard_clean(Image.open(src))
    w, h = clean.size
    frames = []
    for row in range(rows):
        for col in range(cols):
            x0 = (w * col) // cols
            x1 = (w * (col + 1)) // cols
            y0 = (h * row) // rows
            y1 = (h * (row + 1)) // rows
            tile = clean.crop((x0, y0, x1, y1))
            bbox = tile.getchannel('A').getbbox()
            if bbox is None:
                frames.append(Image.new('RGBA', (256, 256)))
                continue
            art = tile.crop(bbox)
            frames.append(art)

    max_span = max(max(frame.size) for frame in frames)
    scale = target_span / max_span
    out = []
    for art in frames:
        nw = max(1, round(art.width * scale))
        nh = max(1, round(art.height * scale))
        art = art.resize((nw, nh), Image.Resampling.NEAREST)
        cell = Image.new('RGBA', (256, 256))
        cell.alpha_composite(art, ((256 - nw) // 2, (256 - nh) // 2))
        out.append(cell)
    return out


def place_strip(frames):
    strip = Image.new('RGBA', (256 * len(frames), 256))
    for i, frame in enumerate(frames):
        strip.alpha_composite(frame, (256 * i, 0))
    return strip


def clear_ship_center(cell: Image.Image, phase: float) -> Image.Image:
    """Keep the hull area transparent with an organic, slightly wavy round opening."""
    out = cell.copy()
    alpha = out.getchannel('A')
    pix = alpha.load()
    cx = cy = 127.5
    for y in range(256):
        for x in range(256):
            dx = x - cx
            dy = y - cy
            radius = math.hypot(dx, dy)
            angle = math.atan2(dy, dx)
            inner_edge = 57.0 + 3.0 * math.sin(3.0 * angle + phase) + 2.0 * math.sin(5.0 * angle - phase)
            if radius < inner_edge:
                pix[x, y] = 0
    out.putalpha(alpha)
    return out


loop = tile_frames(LOOP_SRC, 4, 2, target_span=224)
burst = tile_frames(BURST_SRC, 4, 2, target_span=224)
all_frames = loop + burst
all_frames = [clear_ship_center(frame, i * 0.71) for i, frame in enumerate(all_frames)]

strip = place_strip(all_frames)
strip.save(ROOT / 'B_Ninja_dash.png', 'PNG', optimize=True)

preview = Image.new('RGBA', (256 * 8, 256 * 2), (20, 22, 42, 255))
for i, frame in enumerate(all_frames):
    preview.alpha_composite(frame, ((i % 8) * 256, (i // 8) * 256))
preview.convert('RGB').save(ROOT / 'preview.png', 'PNG', optimize=True)

print('wrote', ROOT / 'B_Ninja_dash.png', strip.size, strip.mode)
print('wrote', ROOT / 'preview.png', preview.size, preview.mode)
