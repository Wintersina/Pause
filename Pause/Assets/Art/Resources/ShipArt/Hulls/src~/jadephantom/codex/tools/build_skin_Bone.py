"""Assemble the Bone sheets from the approved references and AI repaint candidates.

The candidates supply the hand-painted Bone material ramps. The approved sheets remain
the geometry authority: their exact green mask and every non-armour pixel are retained,
then the finished 1536px sheet is enlarged 2x with nearest-neighbour.
"""

from __future__ import annotations

import colorsys
from pathlib import Path

from PIL import Image


HERE = Path(__file__).resolve().parents[1]
BG = (0, 255, 0)

# Bone ceramic ramp, dark to hot. The anchor colours are kept exact.
BONE_RAMP = (
    (185, 174, 152),  # #B9AE98 shadow
    (213, 202, 181),
    (244, 234, 212),  # #F4EAD4 base
    (255, 247, 231),
    (255, 253, 245),  # near-white highlight
)
RED_CORE = (255, 91, 69)  # #FF5B45


def hsv(rgb: tuple[int, int, int]) -> tuple[float, float, float]:
    r, g, b = (v / 255.0 for v in rgb)
    return colorsys.rgb_to_hsv(r, g, b)


def is_armour(rgb: tuple[int, int, int]) -> bool:
    """Recognise jade armour while excluding blue canopy and warm damage FX."""

    if rgb == BG:
        return False
    r, g, b = rgb
    h, s, v = hsv(rgb)
    if not (0.20 <= h <= 0.52 and s >= 0.16 and v >= 0.08):
        return False
    if b > g * 1.08 and b > r * 1.35:
        return False
    if max(rgb) < 30 and s < 0.35:
        return False
    return True


def is_blue(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    if rgb == BG or max(rgb) < 35:
        return False
    h, s, _ = hsv(rgb)
    return 0.52 <= h <= 0.78 and s >= 0.25 and b > g * 1.15 and b > r * 1.20


def is_candidate_red(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    return r > 135 and r > g * 1.45 and r > b * 1.30


def is_neutral_ceramic(rgb: tuple[int, int, int]) -> bool:
    """Identify pale hit-flash ceramic, without touching ink or coloured FX."""

    if max(rgb) < 70:
        return False
    _, saturation, _ = hsv(rgb)
    return saturation < 0.34


def luma(rgb: tuple[int, int, int]) -> float:
    r, g, b = rgb
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def bone_from_candidate(candidate: tuple[int, int, int], source: tuple[int, int, int]) -> tuple[int, int, int]:
    """Quantise the AI repaint's local light/shadow into the requested ceramic ramp."""

    value = luma(candidate)
    # Keep candidate's painted light placement, but never allow dark green/black
    # candidate contamination to become an armour colour.
    if value < 92:
        return BONE_RAMP[0]
    if value < 150:
        return BONE_RAMP[1]
    if value < 210:
        return BONE_RAMP[2]
    if value < 245:
        return BONE_RAMP[3]
    return BONE_RAMP[4]


def repaint(state: int) -> Path:
    ref = Image.open(HERE / f"skinref_state{state}.png").convert("RGB")
    candidate = Image.open(HERE / "tools" / "ai_candidates" / f"state{state}.png").convert("RGB")
    if ref.size != (1536, 1536):
        raise ValueError(f"reference state{state}: expected 1536x1536, got {ref.size}")
    candidate = candidate.resize(ref.size, Image.Resampling.NEAREST)

    ref_px = ref.load()
    cand_px = candidate.load()
    out = ref.copy()
    out_px = out.load()

    for y in range(ref.height):
        for x in range(ref.width):
            source = ref_px[x, y]
            if source == BG:
                out_px[x, y] = BG
                continue

            # The blue canopy, ink, glints, damage FX and nozzle glow are protected.
            if is_blue(source):
                continue

            painted = cand_px[x, y]
            in_hit_flash = x >= 1024 and y >= 1024
            if is_armour(source):
                out_px[x, y] = bone_from_candidate(painted, source)
            elif in_hit_flash and is_neutral_ceramic(source) and not is_blue(source):
                # The reference hit frame is already pale, but the Bone repaint
                # still needs to be a real colourway there: quantise its ceramic
                # light/shadow ramp rather than copying the old white flash.
                out_px[x, y] = bone_from_candidate(painted, source)
            elif is_candidate_red(painted) and source != BG and max(source) > 65:
                # The candidate's aligned red pixels identify the spine bar, star,
                # and wingtip diamonds without recolouring the blue canopy.
                out_px[x, y] = RED_CORE if luma(painted) > 180 else (216, 35, 44)

            # The bottom-right hit-flash is intentionally left bone-white except for
            # the red emissive marks identified above.

    target = HERE / f"skin_Bone_state{state}.png"
    out.resize((3072, 3072), Image.Resampling.NEAREST).save(target, format="PNG", optimize=False)
    return target


def main() -> None:
    for state in range(3):
        print(repaint(state).name)


if __name__ == "__main__":
    main()
