"""Render art-only previews from the verified atlases."""
from __future__ import annotations

import json
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
WORKTREE = ROOT.parents[5]
BG = (11, 11, 26, 255)
NN = Image.Resampling.NEAREST
GROUPS = {
    "s": [("ridge", 8), ("emerge", 5), ("arc", 5), ("dive", 5)],
    "m": [("ridge", 8), ("emerge", 5), ("arc", 5), ("dive", 5)],
    "l": [("ridge", 4), ("emerge", 3), ("arc", 3), ("dive", 3)],
}


def mat(im: Image.Image, size: tuple[int, int] | None = None) -> Image.Image:
    if size:
        im = im.resize(size, NN)
    out = Image.new("RGBA", im.size, BG)
    out.alpha_composite(im)
    return out


def atlas_cells(im: Image.Image, cell: int, start_row: int, rows: int) -> Image.Image:
    return im.crop((0, start_row * cell, 2048, (start_row + rows) * cell))


def make_static() -> None:
    small = Image.open(ROOT / "worm.png").convert("RGBA")
    giant = Image.open(ROOT / "worm_giant.png").convert("RGBA")
    panels = [
        mat(atlas_cells(small, 256, 0, 4)),
        mat(atlas_cells(small, 256, 0, 4), (4096, 2048)),
        mat(atlas_cells(small, 256, 4, 4)),
        mat(atlas_cells(small, 256, 4, 4), (4096, 2048)),
        mat(giant),
        mat(giant, (4096, 4096)),
    ]
    tile_path = WORKTREE / "Pause/Assets/Art/Worlds/Desert/backdrop_v3~/v1/mid.png"
    if tile_path.exists():
        tile = Image.open(tile_path).convert("RGBA")
        overlay = Image.new("RGBA", (4096, 1024))
        for y in range(0, 1024, tile.height):
            for x in range(0, 4096, tile.width):
                overlay.alpha_composite(tile, (x, y))
        for scale, sheet, cell, row, x in (("s", small, 256, 0, 0), ("m", small, 256, 4, 1024), ("l", giant, 512, 0, 2048)):
            a = sheet.crop((row * 0, row * cell, cell, (row + 1) * cell))
            a = a.resize((512 if scale != "l" else 1024,) * 2, NN)
            overlay.alpha_composite(a, (x, 1024 - a.height))
        panels.append(overlay)
    out = Image.new("RGBA", (4096, sum(p.height for p in panels)), BG)
    y = 0
    for panel in panels:
        out.alpha_composite(panel, (0, y))
        y += panel.height
    out.convert("RGB").save(ROOT / "preview.png", optimize=True)
    manifest = json.loads((ROOT / "manifest.json").read_text())
    manifest["preview"] = {"small_medium_giant": "all cells at 1x and nearest-neighbour 2x, in that order",
                            "tile_overlay": str(tile_path.relative_to(WORKTREE)) if tile_path.exists() else None}
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")


def get_cell(scale: str, seq: str, i: int, atlas: Image.Image, giant: Image.Image) -> Image.Image:
    names = json.loads((ROOT / ("worm_giant.json" if scale == "l" else "worm.json")).read_text())["sprites"]
    n = f"worm_{scale}_{seq}_{i:02d}"
    e = next(e for e in names if e["n"] == n)
    im = giant if scale == "l" else atlas
    return im.crop((e["x"], 2048 - e["y"] - e["h"], e["x"] + e["w"], 2048 - e["y"]))


def make_gif() -> None:
    atlas = Image.open(ROOT / "worm.png").convert("RGBA")
    giant = Image.open(ROOT / "worm_giant.png").convert("RGBA")
    sequence = [(s, i, n) for s, n in GROUPS["s"] for i in range(n)]
    gif_frames = []
    durations = []
    fps = {"ridge": 10, "emerge": 12, "arc": 8, "dive": 12}
    for seq, i, n in sequence:
        canvas = Image.new("RGBA", (1536, 512), BG)
        for j, scale in enumerate(("s", "m", "l")):
            ln = dict(GROUPS[scale])[seq]
            mapped = min(ln - 1, round(i * (ln - 1) / max(1, n - 1)))
            f = get_cell(scale, seq, mapped, atlas, giant)
            if scale != "l":
                f = f.resize((512, 512), NN)
            canvas.alpha_composite(f, (j * 512, 0))
        gif_frames.append(canvas.convert("RGB"))
        durations.append(round(1000 / fps[seq]))
    gif_frames[0].save(ROOT / "preview.gif", save_all=True, append_images=gif_frames[1:],
                       duration=durations, loop=0, optimize=False, disposal=2)


if __name__ == "__main__":
    make_static()
    make_gif()
