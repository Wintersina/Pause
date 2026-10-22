from __future__ import annotations

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = (0, 255, 0)
FILES = [ROOT / "key_state0.png", ROOT / "key_state1.png", ROOT / "key_state2.png"]


def mask_and_bbox(im: Image.Image) -> tuple[list[bool], tuple[int, int, int, int]]:
    pix = im.convert("RGB")
    mask: list[bool] = []
    xs: list[int] = []
    ys: list[int] = []
    for y in range(pix.height):
        for x in range(pix.width):
            value = pix.getpixel((x, y)) != GREEN
            mask.append(value)
            if value:
                xs.append(x)
                ys.append(y)
    return mask, (min(xs), min(ys), max(xs), max(ys)) if xs else (0, 0, 0, 0)


def main() -> int:
    images = [Image.open(path).convert("RGB") for path in FILES]
    masks: list[list[bool]] = []
    print("file             size        non-green bbox")
    print("---------------- ----------- ------------------------")
    ok = True
    for path, image in zip(FILES, images):
        mask, box = mask_and_bbox(image)
        masks.append(mask)
        size_ok = image.size == (1024, 1024)
        ok &= size_ok
        print(f"{path.name:<16} {str(image.size):<11} {box} {'OK' if size_ok else 'FAIL'}")

    base = images[0].load()
    base_mask = masks[0]
    for index in (1, 2):
        other = images[index].load()
        other_mask = masks[index]
        union = sum(a or b for a, b in zip(base_mask, other_mask))
        silhouette_diff = 100.0 * sum(a != b for a, b in zip(base_mask, other_mask)) / max(1, union)
        colour_diff = 100.0 * sum(
            base[x, y] != other[x, y]
            for y in range(1024)
            for x in range(1024)
        ) / (1024 * 1024)
        colour_threshold = 3.0 if index == 1 else 8.0
        silhouette_ok = silhouette_diff < 1.5
        colour_ok = colour_diff > colour_threshold
        ok &= silhouette_ok and colour_ok
        print(
            f"state0 -> state{index}: silhouette pixels differ {silhouette_diff:.4f}% "
            f"({'OK' if silhouette_ok else 'FAIL'}); colour pixels differ {colour_diff:.4f}% "
            f"({'OK' if colour_ok else 'FAIL; need > ' + str(colour_threshold) + '%'})"
        )
    print("RESULT:", "PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
