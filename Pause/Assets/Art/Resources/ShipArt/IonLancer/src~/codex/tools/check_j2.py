from __future__ import annotations

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = (0, 255, 0)
PALETTE = {
    (0x1C, 0x2A, 0x80),
    (0x3A, 0x5B, 0xE0),
    (0x6E, 0xF2, 0xEE),
    (0xF4, 0xEA, 0xD4),
    (0x9A, 0xB0, 0xFF),
}
ALLOWED = PALETTE | {GREEN}
FILES = [ROOT / "flame_cruise.png", ROOT / "flame_boost.png"]


def inspect(path: Path) -> tuple[Image.Image, bool]:
    image = Image.open(path).convert("RGB")
    colours = set(image.get_flattened_data())
    size_ok = image.size == (1536, 1024)
    palette_ok = colours <= ALLOWED
    equal_cells = image.width % 6 == 0 and all(
        image.crop((i * image.width // 6, 0, (i + 1) * image.width // 6, image.height)).size
        == (256, 1024)
        for i in range(6)
    )
    print(
        f"{path.name:<18} size={image.width}x{image.height} "
        f"cells={'6x256x1024' if equal_cells else 'FAIL'} "
        f"colours={len(colours):>2} allowed={'OK' if palette_ok else 'FAIL'} "
        f"result={'PASS' if size_ok and equal_cells and palette_ok else 'FAIL'}"
    )
    if not palette_ok:
        print("  unexpected:", sorted(colours - ALLOWED))
    return image, size_ok and equal_cells and palette_ok


def make_preview(images: list[Image.Image]) -> None:
    preview = Image.new("RGB", (1536, 2048), (0x0B, 0x0B, 0x1A))
    for index, image in enumerate(images):
        keyed = Image.new("RGB", image.size, (0x0B, 0x0B, 0x1A))
        src = image.load()
        dst = keyed.load()
        for y in range(image.height):
            for x in range(image.width):
                if src[x, y] != GREEN:
                    dst[x, y] = src[x, y]
        preview.paste(keyed, (0, index * 1024))
    preview.save(ROOT / "preview_j2.png")


def main() -> int:
    images: list[Image.Image] = []
    ok = True
    print("J2 engine flame sheet audit")
    print("--------------------------")
    print(f"{'file':<18} {'size':<12} {'cells':<14} {'colours':<8} {'palette':<8} result")
    for path in FILES:
        image, image_ok = inspect(path)
        images.append(image)
        ok &= image_ok
    make_preview(images)
    print("preview_j2.png      1536x2048 dark-background comparison saved")
    print("RESULT:", "PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
