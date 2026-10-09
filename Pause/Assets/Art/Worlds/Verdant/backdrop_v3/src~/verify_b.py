"""Validate Verdant run B atlas geometry, transparency, brightness and metadata."""

from __future__ import annotations

import json
import colorsys
from pathlib import Path

import numpy as np
from PIL import Image

from build_b import NAMES


ROOT = Path(__file__).resolve().parent.parent
FORBIDDEN_ASSET_WORDS = {"asteroid", "comet", "planet", "spaceship", "station", "starfield"}


def check_atlas(family: str, names: list[str]) -> None:
    image = Image.open(ROOT / f"{family}.png")
    assert image.size == (1024, 1024), (family, image.size)
    assert image.mode == "RGBA", (family, image.mode)
    pixels = np.asarray(image)
    data = json.loads((ROOT / f"{family}.json").read_text())
    assert list(data) == ["sprites"]
    sprites = data["sprites"]
    assert [s["n"] for s in sprites] == names
    occupied = np.zeros((1024, 1024), dtype=bool)
    margins = []
    for sprite in sprites:
        assert set(sprite) == {"n", "x", "y", "w", "h"}
        x, y, w, h = (sprite[k] for k in ("x", "y", "w", "h"))
        assert all(isinstance(v, int) for v in (x, y, w, h))
        assert (w, h) == (256, 256)
        assert 0 <= x <= 768 and 0 <= y <= 768
        top = 1024 - y - h  # JSON uses Unity's bottom-origin rects.
        assert not occupied[top:top + h, x:x + w].any(), sprite["n"]
        occupied[top:top + h, x:x + w] = True
        cell = pixels[top:top + h, x:x + w]
        opaque = cell[:, :, 3] > 0
        assert opaque.any(), sprite["n"]
        yy, xx = np.where(opaque)
        margin = min(int(xx.min()), int(yy.min()), 255 - int(xx.max()), 255 - int(yy.max()))
        assert margin >= 14, (sprite["n"], margin)
        margins.append(margin)
        assert np.all(cell[~opaque, :3] == 0), (sprite["n"], "dirty transparent RGB")
        assert not np.any(np.all(cell[opaque, :3] == [255, 62, 78], axis=1)), sprite["n"]
        if family != "weather":
            cell_values = cell[cell[:, :, 3] == 255, :3].max(axis=1) / 255
            assert float(np.percentile(cell_values, 90)) <= .55, (sprite["n"], "cell p90")
    if family != "weather":
        values = pixels[pixels[:, :, 3] == 255, :3].max(axis=1) / 255
        p90 = float(np.percentile(values, 90))
        assert p90 <= .55, (family, p90)
        print(f"{family}: {len(sprites)} sprites, min margin {min(margins)} px, opaque V p90 {p90:.3f}")
    else:
        assert pixels[:, :, 3].max() <= 88
        assert set(np.unique(pixels[:, :, 3])) <= set(range(0, 89, 8))
        assert np.count_nonzero(pixels[:, :, 3]) > 5000
        print(f"{family}: {len(sprites)} sprites, min margin {min(margins)} px, alpha max {pixels[:, :, 3].max()}")
    if family == "sites":
        assert not pixels[768:, :, 3].any(), "unused fourth row is not transparent"
    for color in np.unique(pixels[pixels[:, :, 3] > 0, :3], axis=0):
        hue, sat, _ = colorsys.rgb_to_hsv(*(float(c) / 255 for c in color))
        assert not (sat > .5 and (hue < 15 / 360 or hue > 345 / 360)), (family, color)


def check_manifest() -> None:
    manifest = json.loads((ROOT / "manifest.json").read_text())
    assert manifest["world"] == "Verdant"
    assert len(manifest["files"]) == 16, "run A ground entries changed"
    run = manifest["run_b"]
    pieces = run["pieces"]
    assert len(pieces) == sum(map(len, NAMES.values())) == 76
    expected = [(f"{family}.png", name) for family, names in NAMES.items() for name in names]
    assert [(p["atlas"], p["name"]) for p in pieces] == expected
    assert set(run["selected_sources"]) == set(NAMES)
    for source in run["selected_sources"].values():
        assert (ROOT / source).is_file(), source
    for piece in pieces:
        assert piece["layer_role"] in {"Landmark", "Pipe", "Fire", "Cloud", "Atmosphere", "Site"}
        assert len(piece["on_screen_size_px"]) == 2
        assert all(0 <= n < 256 for point in piece["emitter_points_px_from_top_left"] for n in point)
        assert all(0 <= n < 256 for point in piece["pipe_end_points_px_from_top_left"] for n in point)
        if piece["layer_role"] == "Fire":
            assert piece["emitter_points_px_from_top_left"]
        if piece["layer_role"] == "Pipe":
            assert len(piece["pipe_end_points_px_from_top_left"]) >= 2
        # Machine-checkable semantic metadata audit; the paintings are also
        # inspected visually in preview_b.png for forbidden sky/space motifs.
        words = set((piece["name"] + " " + piece["notes"]).lower().replace("-", " ").split())
        assert not words & FORBIDDEN_ASSET_WORDS, piece["name"]
    preview = Image.open(ROOT / run["preview"])
    assert preview.size == (4340, 2500)
    print("manifest: 76 documented pieces, source provenance and point bounds valid; no space-themed asset labels")


if __name__ == "__main__":
    for family, names in NAMES.items():
        check_atlas(family, names)
    check_manifest()
