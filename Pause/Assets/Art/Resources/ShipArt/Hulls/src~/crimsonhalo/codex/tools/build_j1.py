from collections import deque
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
TOOLS = ROOT / "tools"
GREEN = (0, 255, 0)
SIZE = (1024, 1024)


def green_background_mask(image: Image.Image):
    """Return generated-image background pixels connected to the canvas border."""
    rgb = image.convert("RGB")
    px = rgb.load()
    w, h = rgb.size
    candidate = [[False] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            r, g, b = px[x, y]
            candidate[y][x] = g > 120 and g > r * 1.20 and g > b * 1.20

    background = [[False] * w for _ in range(h)]
    queue = deque()
    for x in range(w):
        if candidate[0][x]:
            background[0][x] = True
            queue.append((x, 0))
        if candidate[h - 1][x] and not background[h - 1][x]:
            background[h - 1][x] = True
            queue.append((x, h - 1))
    for y in range(h):
        if candidate[y][0] and not background[y][0]:
            background[y][0] = True
            queue.append((0, y))
        if candidate[y][w - 1] and not background[y][w - 1]:
            background[y][w - 1] = True
            queue.append((w - 1, y))

    while queue:
        x, y = queue.popleft()
        for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            if 0 <= nx < w and 0 <= ny < h and candidate[ny][nx] and not background[ny][nx]:
                background[ny][nx] = True
                queue.append((nx, ny))
    return background


def old_silhouette():
    old = Image.open(ROOT / "ref_old_state0.png").convert("RGB")
    px = old.load()
    mask = Image.new("1", SIZE, 0)
    out = mask.load()
    for y in range(1024):
        for x in range(1024):
            out[x, y] = px[x, y] != GREEN
    return mask


def crop_foreground(image: Image.Image):
    image = image.resize(SIZE, Image.Resampling.NEAREST).convert("RGB")
    bg = green_background_mask(image)
    points = [(x, y) for y in range(1024) for x in range(1024) if not bg[y][x]]
    if not points:
        raise RuntimeError("generated candidate has no foreground")
    left = min(x for x, _ in points)
    top = min(y for _, y in points)
    right = max(x for x, _ in points) + 1
    bottom = max(y for _, y in points) + 1
    return image.crop((left, top, right, bottom)), bg, (left, top, right, bottom)


def compose(candidate_path: Path, silhouette: Image.Image, base: Image.Image | None = None):
    old_px = silhouette.load()
    candidate, _, _ = crop_foreground(Image.open(candidate_path))

    old_points = [(x, y) for y in range(1024) for x in range(1024) if old_px[x, y]]
    left = min(x for x, _ in old_points)
    top = min(y for _, y in old_points)
    right = max(x for x, _ in old_points) + 1
    bottom = max(y for _, y in old_points) + 1
    target_size = (right - left, bottom - top)
    candidate = candidate.resize(target_size, Image.Resampling.NEAREST)

    out = Image.new("RGB", SIZE, GREEN)
    out.paste(candidate, (left, top))
    out_px = out.load()
    base_px = base.load() if base else None
    cand_px = candidate.load()
    for y in range(1024):
        for x in range(1024):
            if not old_px[x, y]:
                out_px[x, y] = GREEN
            elif base_px is not None:
                r, g, b = out_px[x, y]
                if g > 120 and g > r * 1.20 and g > b * 1.20:
                    out_px[x, y] = base_px[x, y]
    if base_px is None:
        # Generated candidates may leave a few edge pixels green after scaling.
        # Seed those pixels with a deep local crimson rather than changing the mask.
        for y in range(1024):
            for x in range(1024):
                if old_px[x, y]:
                    r, g, b = out_px[x, y]
                    if g > 120 and g > r * 1.20 and g > b * 1.20:
                        out_px[x, y] = (42, 10, 24)
    return out


def main():
    silhouette = old_silhouette()
    intact = compose(TOOLS / "candidate_intact.png", silhouette)
    damaged = compose(TOOLS / "candidate_damaged.png", silhouette, intact)
    wreck = compose(TOOLS / "candidate_wreck.png", silhouette, damaged)
    intact.save(ROOT / "key_state0.png")
    damaged.save(ROOT / "key_state1.png")
    wreck.save(ROOT / "key_state2.png")
    preview = Image.new("RGB", (3072, 1024), (11, 11, 26))
    for offset, image in ((0, intact), (1024, damaged), (2048, wreck)):
        panel = image.copy()
        panel_px = panel.load()
        for y in range(1024):
            for x in range(1024):
                if panel_px[x, y] == GREEN:
                    panel_px[x, y] = (11, 11, 26)
        preview.paste(panel, (offset, 0))
    preview.save(ROOT / "preview_j1.png")


if __name__ == "__main__":
    main()
