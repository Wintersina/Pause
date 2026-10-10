"""Compose the image-edit seam strips and export the Tide rail."""

from pathlib import Path

import numpy as np
from PIL import Image


HERE = Path(__file__).resolve().parent
OUT = HERE.parents[2] / "Resources/Worlds/Tide/rail_tide_wide_v1.png"


def rgb_to_hsv(rgb):
    rgb = rgb.astype(np.float32) / 255.0
    hi = rgb.max(axis=2)
    lo = rgb.min(axis=2)
    delta = hi - lo
    sat = np.divide(delta, hi, out=np.zeros_like(hi), where=hi > 0)
    hue = np.zeros_like(hi)
    live = delta > 0
    r = (hi == rgb[:, :, 0]) & live
    g = (hi == rgb[:, :, 1]) & live & ~r
    b = live & ~r & ~g
    hue[r] = ((rgb[:, :, 1][r] - rgb[:, :, 2][r]) / delta[r]) % 6
    hue[g] = (rgb[:, :, 2][g] - rgb[:, :, 0][g]) / delta[g] + 2
    hue[b] = (rgb[:, :, 0][b] - rgb[:, :, 1][b]) / delta[b] + 4
    return (hue * 60) % 360, sat, hi


def hsv_to_rgb(hue, sat, val):
    h = hue / 60.0
    chroma = val * sat
    second = chroma * (1 - np.abs(h % 2 - 1))
    low = val - chroma
    out = np.empty((*hue.shape, 3), dtype=np.float32)
    for sector, channels in enumerate(((chroma, second, 0), (second, chroma, 0),
                                       (0, chroma, second), (0, second, chroma),
                                       (second, 0, chroma), (chroma, 0, second))):
        use = np.floor(h).astype(np.int8) == sector
        for channel, part in enumerate(channels):
            out[:, :, channel][use] = part[use] if isinstance(part, np.ndarray) else part
    return np.uint8(np.clip(np.rint((out + low[:, :, None]) * 255), 0, 255))


def main():
    first = np.asarray(Image.open(HERE / "tide_first_edit.png").convert("RGBA"))
    seam = np.asarray(Image.open(HERE / "tide_seam_edit.png").convert("RGBA"))
    assert first.shape == (2169, 725, 4)
    assert seam.shape == (2170, 725, 4)

    # The image edit changed some center texels. Restore the first edit's middle
    # 90% exactly, while retaining the generated seam strips at both ends.
    result = seam.copy()
    result[109:2060] = first[109:2060]

    # Re-map warm copper to muted rust brown, algae away from lime, and a few
    # accidental cyan flecks into the intended mint family. Preserve violet and pink.
    hue, sat, val = rgb_to_hsv(result[:, :, :3])
    active = (result[:, :, 3] > 0) & (sat > 0.08) & (val > 0.08)
    brown = active & (hue < 60)
    hue[brown] = np.clip(hue[brown], 20, 24)
    sat[brown] = np.minimum(sat[brown], 0.43)
    val[brown] = np.minimum(val[brown], 0.69)
    algae = active & (hue >= 60) & (hue < 125)
    hue[algae] = 132
    sat[algae] = np.minimum(sat[algae], 0.54)
    cyan = active & (hue >= 170) & (hue <= 186)
    hue[cyan] = 159
    result[:, :, :3] = hsv_to_rgb(hue, sat, val)

    # One-pixel stepped edge, no partially transparent dark exterior halo.
    result[:, :, 3] = np.where(result[:, :, 3] >= 128, 255, 0)
    result[result[:, :, 3] == 0, :3] = 0

    # The edit kept the vent holes visually black but opaque. Open their dark
    # interiors to the backdrop, retaining the 1-2 px metal lips and bevels.
    cutouts = (
        (236, 132, 253, 214), (260, 132, 277, 214), (284, 132, 301, 214),
        (198, 548, 304, 568), (198, 571, 304, 591), (198, 595, 304, 614),
        (275, 964, 308, 985), (275, 988, 308, 1012), (275, 1016, 308, 1041),
        (240, 2059, 255, 2132), (266, 2059, 282, 2132), (291, 2059, 307, 2132),
    )
    for x0, y0, x1, y1 in cutouts:
        area = result[y0:y1, x0:x1]
        dark = area[:, :, :3].max(axis=2) < 31
        area[dark] = 0

    # Force equal wrap scanlines. Only the final few rows are affected; the
    # generated seam edit already supplies the broader top/bottom treatment.
    for offset in range(4):
        weight = (offset + 1) / 4
        y = result.shape[0] - 4 + offset
        target = result[0].astype(np.float32)
        source = result[y].astype(np.float32)
        result[y, :, :3] = np.rint(source[:, :3] * (1 - weight) + target[:, :3] * weight).astype(np.uint8)
        if offset >= 2:
            result[y, :, 3] = result[0, :, 3]
        result[y, result[y, :, 3] == 0, :3] = 0
    tail_hue, tail_sat, tail_val = rgb_to_hsv(result[-4:, :, :3])
    tail_brown = (tail_hue >= 15) & (tail_hue <= 28) & (tail_sat > .44)
    tail_sat[tail_brown] = .42
    result[-4:, :, :3] = hsv_to_rgb(tail_hue, tail_sat, tail_val)
    result[-1] = result[0]

    OUT.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(result, "RGBA").save(OUT, optimize=True)
    print(OUT)


if __name__ == "__main__":
    main()
