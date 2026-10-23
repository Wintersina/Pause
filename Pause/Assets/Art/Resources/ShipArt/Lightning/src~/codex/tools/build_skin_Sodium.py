#!/usr/bin/env python3
"""Build the Sodium flipbook from generated repaint candidates.

The approved sheets are the geometry authority.  The generated candidates contribute local
lighting/value cues; the final raster copies the approved drawing and replaces only the armour,
wing-bolt, and bone-white hit-flash material ramps.  The export is enlarged with nearest-neighbour.
"""

from __future__ import annotations

import colorsys
import sys
from collections import deque
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
CELL = 512
SOURCES = [ROOT / f"skinref_state{i}.png" for i in range(3)]
OUTPUTS = [ROOT / f"skin_Sodium_state{i}.png" for i in range(3)]

# Two candidates were generated for each approved sheet.  The builder chooses the candidate
# with the closest nearest-neighbour silhouette, so the selected repaint is deterministic.
CANDIDATE_PAIRS = [
    (
        Path("/Users/sina/.codex/generated_images/01a16493-0d5b-7763-879e-2f2352fd9e2e/exec-d18c2d90-a3dd-4aa8-ae83-52d034320cff.png"),
        Path("/Users/sina/.codex/generated_images/01a16493-0d5b-7763-879e-2f2352fd9e2e/exec-3141a4eb-d1af-4e41-a291-ea340a248596.png"),
    ),
    (
        Path("/Users/sina/.codex/generated_images/01a16493-0d5b-7763-879e-2f2352fd9e2e/exec-8b593dbf-3724-4fbc-85d7-fb60f0be3693.png"),
        Path("/Users/sina/.codex/generated_images/01a16493-0d5b-7763-879e-2f2352fd9e2e/exec-274d08c9-0295-4bea-a178-5e9a31932cf1.png"),
    ),
    (
        Path("/Users/sina/.codex/generated_images/01a16493-0d5b-7763-879e-2f2352fd9e2e/exec-ba8c04bd-0519-4b07-b3cc-7028916f4cae.png"),
        Path("/Users/sina/.codex/generated_images/01a16493-0d5b-7763-879e-2f2352fd9e2e/exec-00dd49e1-27ff-4e41-b082-ffdcf7416ba3.png"),
    ),
]

GREEN = (0, 255, 0)

# Sodium-lamp material ramps: seam/deep shadow -> warm base -> hot edge.
ARMOUR_RAMP = (
    (82, 27, 16),       # dark seam shade
    (169, 72, 26),      # specified shadow
    (207, 105, 31),     # lower mid
    (242, 134, 43),     # specified base
    (255, 180, 60),     # specified highlight
    (255, 224, 145),    # pale yellow-white hot edge
)
BOLT_RAMP = (
    (75, 12, 17),
    (158, 20, 28),      # specified deep crimson
    (205, 35, 37),
    (255, 91, 69),      # specified hot core
)
BONE_RAMP = (
    (198, 190, 176),
    (222, 214, 197),
    (239, 232, 215),
    (249, 244, 229),
    (255, 251, 238),
)


def is_green(rgb: tuple[int, int, int]) -> bool:
    return rgb == GREEN


def luma(rgb: tuple[int, int, int]) -> float:
    return (0.2126 * rgb[0] + 0.7152 * rgb[1] + 0.0722 * rgb[2]) / 255.0


def is_armour_yellow(rgb: tuple[int, int, int]) -> bool:
    """Select the lemon armour, excluding blue glass, red bolts, and amber damage/nozzles."""
    if is_green(rgb):
        return False
    r, g, b = rgb
    h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    return 0.085 < h < 0.205 and s > 0.24 and v > 0.16 and (r - g) < 58


def is_red(rgb: tuple[int, int, int]) -> bool:
    if is_green(rgb):
        return False
    r, g, b = rgb
    h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    return (h < 0.045 or h > 0.97) and s > 0.40 and v > 0.15


def is_forbidden_magenta(rgb: tuple[int, int, int]) -> bool:
    """Catch saturated purple legacy damage accents, while leaving the teal canopy untouched."""
    if is_green(rgb):
        return False
    r, g, b = rgb
    h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    return s > 0.50 and v > 0.15 and 0.86 < h < 0.97


def candidate_sample(candidate: Image.Image, x: int, y: int) -> tuple[int, int, int]:
    """Sample the corresponding normalized sheet coordinate from a generated 3x3 candidate."""
    sx = min(candidate.width - 1, max(0, int((x + 0.5) * candidate.width / 1536)))
    sy = min(candidate.height - 1, max(0, int((y + 0.5) * candidate.height / 1536)))
    return candidate.getpixel((sx, sy))


def candidate_luma(candidate: Image.Image, x: int, y: int, fallback: tuple[int, int, int]) -> float:
    rgb = candidate_sample(candidate, x, y)
    if rgb[1] > 235 and rgb[0] < 40 and rgb[2] < 40:
        return luma(fallback)
    return luma(rgb)


def sheet_mask(im: Image.Image) -> list[bool]:
    def green_field(rgb: tuple[int, int, int]) -> bool:
        # Generated previews can encode the requested chroma field as a near-green value.
        return rgb[1] > 210 and rgb[0] < 45 and rgb[2] < 45

    return [not green_field(rgb) for rgb in im.getdata()]


def candidate_score(ref: Image.Image, candidate: Image.Image) -> float:
    """Compare coarse nearest-neighbour silhouettes; lower is truer to the approved sheet."""
    scaled = candidate.resize(ref.size, Image.Resampling.NEAREST)
    rm = sheet_mask(ref)
    cm = sheet_mask(scaled)
    return sum(a != b for a, b in zip(rm, cm)) / max(1, sum(rm))


def bolt_components(source: Image.Image, col: int, row: int) -> set[tuple[int, int]]:
    """Return large red components (the two wing bolts), not small red tip diamonds or damage."""
    x0, y0 = col * CELL, row * CELL
    red = {
        (x, y)
        for y in range(y0, y0 + CELL)
        for x in range(x0, x0 + CELL)
        if is_red(source.getpixel((x, y)))
    }
    selected: set[tuple[int, int]] = set()
    while red:
        start = red.pop()
        queue = deque([start])
        component = [start]
        while queue:
            x, y = queue.popleft()
            for neighbour in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if neighbour in red:
                    red.remove(neighbour)
                    queue.append(neighbour)
                    component.append(neighbour)
        xs = [p[0] for p in component]
        ys = [p[1] for p in component]
        # Wing bolts are broad connected shapes.  This excludes red/white diamonds and sparks.
        if len(component) >= 600 and max(xs) - min(xs) >= 35:
            selected.update(component)
    return selected


def ramp_index(value: float, count: int) -> int:
    if count == 6:
        thresholds = (0.16, 0.30, 0.46, 0.64, 0.82)
    elif count == 4:
        thresholds = (0.22, 0.42, 0.68)
    else:
        thresholds = tuple((i + 1) / count for i in range(count - 1))
    for index, threshold in enumerate(thresholds):
        if value < threshold:
            return index
    return count - 1


def repaint_hit_flash(source: Image.Image, candidate: Image.Image, col: int, row: int, out: Image.Image) -> None:
    """Keep the hit frame bone-white while changing its warm bone ramp enough to be a repaint."""
    for y in range(row * CELL, (row + 1) * CELL):
        for x in range(col * CELL, (col + 1) * CELL):
            rgb = source.getpixel((x, y))
            if is_green(rgb):
                continue
            r, g, b = rgb
            if min(rgb) < 105 or max(rgb) - min(rgb) > 95:
                continue
            value = 0.50 * luma(rgb) + 0.50 * candidate_luma(candidate, x, y, rgb)
            out.putpixel((x, y), BONE_RAMP[ramp_index(value, len(BONE_RAMP))])


def build_one(source_path: Path, candidates: tuple[Path, Path], output_path: Path, state: int) -> None:
    source = Image.open(source_path).convert("RGB")
    if source.size != (1536, 1536):
        raise ValueError(f"{source_path.name}: expected 1536x1536, got {source.size}")
    loaded = [Image.open(path).convert("RGB") for path in candidates]
    scores = [candidate_score(source, candidate) for candidate in loaded]
    chosen_index = min(range(len(scores)), key=scores.__getitem__)
    candidate = loaded[chosen_index]
    print(f"state{state}: selected candidate {chosen_index + 1} silhouette_score={scores[chosen_index] * 100:.3f}%")

    out = source.copy()
    for row in range(3):
        for col in range(3):
            if (row, col) == (2, 2):
                repaint_hit_flash(source, candidate, col, row, out)
                continue
            bolts = bolt_components(source, col, row)
            for y in range(row * CELL, (row + 1) * CELL):
                for x in range(col * CELL, (col + 1) * CELL):
                    rgb = source.getpixel((x, y))
                    if (x, y) in bolts:
                        value = 0.40 * luma(rgb) + 0.60 * candidate_luma(candidate, x, y, rgb)
                        out.putpixel((x, y), BOLT_RAMP[ramp_index(value, len(BOLT_RAMP))])
                    elif is_armour_yellow(rgb):
                        value = 0.42 * luma(rgb) + 0.58 * candidate_luma(candidate, x, y, rgb)
                        out.putpixel((x, y), ARMOUR_RAMP[ramp_index(value, len(ARMOUR_RAMP))])

    # Enforce the requested flat chroma field exactly after all repaint operations.
    for y in range(source.height):
        for x in range(source.width):
            source_rgb = source.getpixel((x, y))
            output_rgb = out.getpixel((x, y))
            if is_green(source_rgb):
                out.putpixel((x, y), GREEN)
            elif is_forbidden_magenta(output_rgb):
                # Preserve the dark/light damage value, but keep it in the approved warm ramp.
                out.putpixel((x, y), BOLT_RAMP[ramp_index(luma(output_rgb), len(BOLT_RAMP))])

    final = out.resize((3072, 3072), Image.Resampling.NEAREST)
    final.save(output_path, format="PNG", optimize=False)
    print(f"wrote {output_path.name}: {final.size}")


def main() -> int:
    for state, (source, candidates, output) in enumerate(zip(SOURCES, CANDIDATE_PAIRS, OUTPUTS)):
        if not source.exists():
            raise FileNotFoundError(source)
        for candidate in candidates:
            if not candidate.exists():
                raise FileNotFoundError(candidate)
        build_one(source, candidates, output, state)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
