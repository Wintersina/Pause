from pathlib import Path
from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
IMAGE = ROOT / "wx_gust.png"
CELL = 512


def repeated_block_ratio(image: Image.Image, n: int) -> float:
    pix = image.load()
    total = repeated = 0
    for y in range(0, image.height - n + 1, n):
        for x in range(0, image.width - n + 1, n):
            block = [pix[x + dx, y + dy] for dy in range(n) for dx in range(n)]
            if max(p[3] for p in block) == 0:
                continue
            total += 1
            repeated += len(set(block)) == 1
    return repeated / total if total else 0.0


def main() -> None:
    image = Image.open(IMAGE).convert("RGBA")
    assert image.size == (2048, 1024), image.size
    print(f"size={image.size} mode={image.mode}")

    alpha = image.getchannel("A")
    for row in range(2):
        for col in range(4):
            cell = alpha.crop((col * CELL, row * CELL, (col + 1) * CELL, (row + 1) * CELL))
            bbox = cell.getbbox()
            assert bbox is not None, f"empty cell r{row}c{col}"
            print(f"cell r{row}c{col}: bbox={bbox} alpha_nonzero={sum(v > 0 for v in cell.getdata())}")

    edge_diffs = []
    for col in range(4):
        cell = alpha.crop((col * CELL, 0, (col + 1) * CELL, CELL))
        left = list(cell.crop((0, 0, 1, CELL)).getdata())
        right = list(cell.crop((CELL - 1, 0, CELL, CELL)).getdata())
        edge_diffs.append(max(abs(a - b) for a, b in zip(left, right)))
    assert max(edge_diffs) <= 2, edge_diffs
    print(f"row0_edge_alpha_diffs={edge_diffs}")

    ribbon_means = []
    for col in range(4):
        cell = alpha.crop((col * CELL, 0, (col + 1) * CELL, CELL))
        values = [v / 255.0 for v in cell.getdata() if v > 0]
        ribbon_means.append(sum(values) / len(values))
    print("row0_nonzero_alpha_means=" + ", ".join(f"{v:.3f}" for v in ribbon_means))
    assert all(0.35 <= v <= 0.47 for v in ribbon_means), ribbon_means

    warm = []
    for r, g, b, a in image.getdata():
        if a and (r > b or (r > 180 and g < 150)):
            warm.append((r, g, b, a))
    assert not warm, f"warm/red pixels found: {len(warm)}"
    print("warm_or_red_pixels=0")

    ratios = {n: repeated_block_ratio(image, n) for n in (2, 3, 4)}
    print("solid_block_repeat_ratios=" + ", ".join(f"{n}x{ratio:.3f}" for n, ratio in ratios.items()))
    assert ratios[2] < 0.70 and ratios[3] < 0.70 and ratios[4] < 0.70
    print("PASS")


if __name__ == "__main__":
    main()
