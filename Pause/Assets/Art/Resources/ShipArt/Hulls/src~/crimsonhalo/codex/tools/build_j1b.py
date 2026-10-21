from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
TOOLS = ROOT / "tools"
GREEN = np.array([0, 255, 0], dtype=np.uint8)
DARK = (11, 11, 26)
SIZE = (1024, 1024)


def clean_candidate(path: Path) -> Image.Image:
    """Normalize the generated flat-green plate with point operations only."""
    image = Image.open(path).convert("RGB")
    if image.size != SIZE:
        image = image.resize(SIZE, Image.Resampling.NEAREST)
    pixels = np.asarray(image).copy()
    red, green, blue = pixels[..., 0], pixels[..., 1], pixels[..., 2]
    # The generator's green plate is slightly noisy (usually around 2,249,4).
    # Keep the test tight so cyan/cream ship pixels are never mistaken for plate.
    background = (green >= 220) & (red <= 20) & (blue <= 20)
    pixels[background] = GREEN
    return Image.fromarray(pixels, "RGB")


def make_sheet(state: int) -> Image.Image:
    names = [
        f"candidate_s{state}_lights.png",
        f"candidate_s{state}_glint1.png",
        f"candidate_s{state}_glint2.png",
        f"candidate_s{state}_bank_left.png",
        f"candidate_s{state}_bank_right.png",
    ]
    sheet = Image.new("RGB", (3072, 2048), tuple(GREEN.tolist()))
    for cell, name in enumerate(names):
        sheet.paste(clean_candidate(TOOLS / name), (cell % 3 * 1024, cell // 3 * 1024))
    return sheet


def main() -> None:
    sheets = []
    for state in range(3):
        sheet = make_sheet(state)
        output = ROOT / f"hull_redo_state{state}.png"
        sheet.save(output, format="PNG", optimize=False)
        sheets.append(sheet)

    preview = Image.new("RGB", (9216, 2048), DARK)
    for state, sheet in enumerate(sheets):
        preview_sheet = np.asarray(sheet).copy()
        preview_sheet[np.all(preview_sheet == GREEN, axis=2)] = np.array(DARK, dtype=np.uint8)
        preview.paste(Image.fromarray(preview_sheet, "RGB"), (state * 3072, 0))
    preview.save(ROOT / "preview_j1b.png", format="PNG", optimize=False)


if __name__ == "__main__":
    main()
