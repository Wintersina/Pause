#!/usr/bin/env python3
"""Build the Bone sheets from the approved sheets and selected repaint guides.

The repaint guides are AI-painted candidates.  The approved sheets supply the
locked geometry; only material colours are replaced, then the result is kept
at nearest-neighbour pixel boundaries for Unity.
"""

from pathlib import Path
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GUIDES = {
    0: Path("/Users/sina/.codex/generated_images/01a15f6f-8158-7383-bfc1-8bc69a17eddd/exec-57ac820c-c7b8-4fb3-b539-c91bec4d869f.png"),
    1: Path("/Users/sina/.codex/generated_images/01a15f6f-8158-7383-bfc1-8bc69a17eddd/exec-390be2a8-c2b6-46c2-9fbc-af24606faa93.png"),
    2: Path("/Users/sina/.codex/generated_images/01a15f6f-8158-7383-bfc1-8bc69a17eddd/exec-426b6069-93fe-44de-ad81-1c5c34a8aa64.png"),
}


def is_green(px):
    r, g, b = px
    return g > 200 and r < 80 and b < 80


def is_source_red(px):
    r, g, b = px
    return r > 5 and r > g * 1.03 and r > b * 1.08 and g < 180


def is_guide_bone(px):
    r, g, b = px
    return r > 110 and g > 90 and b > 70 and r >= g * 0.92 and g >= b * 0.92


def is_guide_red(px):
    r, g, b = px
    return r > 130 and r > g * 1.22 and r > b * 1.18 and g < 155


def is_guide_damage_warm(px):
    r, g, b = px
    return r > 110 and g > 45 and r > g * 1.18 and g > b * 1.35


def is_source_damage_warm(px):
    r, g, b = px
    return r > 80 and g > 20 and g > r * 0.22 and r > b * 1.35


def is_source_cream_light(px):
    r, g, b = px
    return r > 180 and g > 150 and b > 115 and r >= g * 0.94 and g >= b * 0.94


def bone_ramp(guide_px, source_px):
    gr, gg, gb = guide_px
    # The guide's value carries the painted candidate's local ramp; the source
    # red channel is the fallback where the guide kept an ink seam.
    value = (0.2126 * gr) + (0.7152 * gg) + (0.0722 * gb)
    if value < 85:
        value = source_px[0] * 0.46
    if value < 70:
        return (139, 130, 113)
    if value < 125:
        return (185, 174, 152)       # #B9AE98
    if value < 175:
        return (212, 199, 174)
    if value < 225:
        return (244, 234, 212)       # #F4EAD4
    return (255, 253, 245)


def red_light(guide_px):
    value = max(guide_px)
    return (255, 91, 69) if value > 220 else (216, 35, 44)


def guide_cell(guide, row, col):
    # Generated candidates are 1254 square, so their cells are 418/419 px.
    w, h = guide.size
    x0, x1 = round(col * w / 3), round((col + 1) * w / 3)
    y0, y1 = round(row * h / 3), round((row + 1) * h / 3)
    return guide.crop((x0, y0, x1, y1)).resize((512, 512), Image.Resampling.NEAREST)


def build_sheet(state):
    ref = Image.open(ROOT / f"skinref_state{state}.png").convert("RGB")
    guide = Image.open(GUIDES[state]).convert("RGB")
    out = Image.new("RGB", (3072, 3072), (0, 255, 0))

    for row in range(3):
        for col in range(3):
            ref_cell = ref.crop((col * 512, row * 512, (col + 1) * 512, (row + 1) * 512))
            gcell = guide_cell(guide, row, col)
            src = ref_cell.load()
            gsrc = gcell.load()
            dst = Image.new("RGB", (512, 512), (0, 255, 0))
            dp = dst.load()
            for y in range(512):
                for x in range(512):
                    sp = src[x, y]
                    gp = gsrc[x, y]
                    if is_green(sp):
                        dp[x, y] = sp
                        continue

                    # Candidate red lights identify the former cream slash,
                    # diamond, and star marks without touching the hit-flash.
                    if is_guide_red(gp):
                        dp[x, y] = red_light(gp)
                        continue

                    # Repaint only armour red that the candidate also reads as
                    # ceramic. Warm damage effects are intentionally retained.
                    if (is_source_red(sp) and not is_source_damage_warm(sp)
                            and not is_guide_damage_warm(gp)):
                        dp[x, y] = bone_ramp(gp, sp)
                    elif (row, col) != (2, 2) and is_source_cream_light(sp):
                        # Former cream slash/diamond/star pixels are the red
                        # emissive markings in the Bone colourway.
                        dp[x, y] = (255, 91, 69) if max(sp) > 235 else (216, 35, 44)
                    elif row == 2 and col == 2:
                        # The approved hit-flash is already bone-white; warm
                        # the ceramic itself so this colourway is still a
                        # visibly new sheet while retaining the flash state.
                        r, g, b = sp
                        if r > 145 and g > 135 and b > 120 and max(sp) - min(sp) < 65:
                            if r + g + b > 690:
                                dp[x, y] = (244, 234, 212)
                            else:
                                dp[x, y] = (185, 174, 152)
                        else:
                            dp[x, y] = sp
                    else:
                        dp[x, y] = sp

            out.paste(dst.resize((1024, 1024), Image.Resampling.NEAREST), (col * 1024, row * 1024))

    out.save(ROOT / f"skin_Bone_state{state}.png", format="PNG", optimize=False)


if __name__ == "__main__":
    for state in range(3):
        build_sheet(state)
        print(f"built skin_Bone_state{state}.png")
