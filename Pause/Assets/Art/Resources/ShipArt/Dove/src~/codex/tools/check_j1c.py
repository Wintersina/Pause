from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
SIZE = (1024, 1024)
GREEN = (0, 255, 0)


def load(name):
    image = Image.open(ROOT / name).convert("RGB")
    if image.size != SIZE:
        raise ValueError(f"{name}: expected 1024x1024, got {image.size}")
    return image


def solid(pixel):
    return pixel != GREEN


def near_black_or_orange(pixel):
    r, g, b = pixel
    near_black = max(pixel) <= 72
    orange = r >= 110 and r > g * 1.18 and 28 <= g <= 190 and b <= 125
    return near_black or orange


def stats(base, other):
    base_px = base.load()
    other_px = other.load()
    hull = 0
    silhouette_changes = 0
    changed = 0
    damage_like = 0
    state1_preserved = 0
    for y in range(SIZE[1]):
        for x in range(SIZE[0]):
            a = base_px[x, y]
            b = other_px[x, y]
            a_solid = solid(a)
            b_solid = solid(b)
            if a_solid:
                hull += 1
                if a_solid != b_solid:
                    silhouette_changes += 1
                if a != b:
                    changed += 1
                    if near_black_or_orange(b):
                        damage_like += 1
    silhouette_pct = 100.0 * silhouette_changes / max(1, hull)
    colour_pct = 100.0 * changed / (SIZE[0] * SIZE[1])
    clean_share = 100.0 * (hull - damage_like) / max(1, hull)
    return silhouette_pct, colour_pct, clean_share, changed


def bbox(image):
    px = image.load()
    points = [(x, y) for y in range(image.height) for x in range(image.width) if solid(px[x, y])]
    return (min(x for x, _ in points), min(y for _, y in points),
            max(x for x, _ in points), max(y for _, y in points)) if points else None


def main():
    images = {name: load(name) for name in ("key_state0.png", "key_state1.png", "key_state2.png")}
    base = images["key_state0.png"]
    base_px = base.load()
    for name, image in images.items():
        px = image.load()
        if any(base_px[x, y] == GREEN and px[x, y] != GREEN
               for y in range(1024) for x in range(1024)):
            raise AssertionError(f"{name}: changed a green background pixel")

    print("file          size        non-green bbox              hull pixels")
    for name, image in images.items():
        hull_pixels = sum(solid(pixel) for pixel in image.getdata())
        print(f"{name:14} {image.width}x{image.height}   {bbox(image)}   {hull_pixels}")

    print("\ncomparison                         silhouette-diff     colour-diff     non-damage hull")
    results = {}
    for name in ("key_state1.png", "key_state2.png"):
        results[name] = stats(base, images[name])
        silhouette_pct, colour_pct, clean_share, changed = results[name]
        print(f"state0 -> {name:10}                  {silhouette_pct:7.3f}%       {colour_pct:7.3f}%       {clean_share:7.3f}%")

    s1, c1, _, _ = results["key_state1.png"]
    s2, c2, _, _ = results["key_state2.png"]
    ok = (
        s1 == 0.0 and s2 == 0.0 and
        3.0 <= c1 <= 10.0 and
        8.0 <= c2 <= 22.0 and
        c2 > c1
    )
    print(f"\nPASS: {ok}")
    if not ok:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
