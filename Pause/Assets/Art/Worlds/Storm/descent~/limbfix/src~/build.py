"""Build the Storm limb from image-generated candidate B using nearest-pixel projection."""

from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont


HERE = Path(__file__).resolve().parent
OUT = HERE.parent
W, H = 2048, 1024


def horizon(x):
    """Circle through (centre, 355) and (both edges, 720)."""
    mid = (W - 1) / 2
    span = mid
    rise = 365.0
    radius = (span * span + rise * rise) / (2 * rise)
    return 355.0 + radius - np.sqrt(radius * radius - (x - mid) ** 2)


def build():
    source = np.asarray(Image.open(HERE / "candidate_b.png").convert("RGBA"))
    sh, sw = source.shape[:2]
    xx = np.arange(W, dtype=np.float32)
    yy = np.arange(H, dtype=np.float32)[:, None]
    sx = np.rint(xx * (sw - 1) / (W - 1)).astype(np.int32)
    # Smooth fit to candidate B's true main rim. Ignore its isolated cyan flecks.
    t = (sx - (sw - 1) / 2) / ((sw - 1) / 2)
    src_horizon = 310.0 + 384.0 * t**2 - 80.0 * t**4
    dest_horizon = horizon(xx)
    radial = np.clip((yy - dest_horizon[None, :]) / (H - 1 - dest_horizon[None, :]), 0, 1)
    # s = v^1.33: a given source feature is nearer the horizon after projection.
    source_radial = radial ** (1 / 1.33)
    sy = np.rint(src_horizon[None, :] + source_radial * (sh - 1 - src_horizon[None, :])).astype(np.int32)
    rgba = source[sy, sx[None, :]].copy()
    inside = yy >= np.ceil(dest_horizon)[None, :]

    # Restrict chromatic hues while retaining the generated painting's value detail.
    hsv = np.asarray(Image.fromarray(rgba[:, :, :3], "RGB").convert("HSV")).copy()
    hue = hsv[:, :, 0].astype(np.int16) * 360.0 / 256.0
    sat = hsv[:, :, 1].astype(np.int16)
    val = hsv[:, :, 2].astype(np.int16)
    blue = (hue >= 165) & (hue <= 285)
    warm = (~blue) & (sat > 14)
    lightning = warm & (hue >= 42) & (hue <= 74) & (val >= 180)
    # Blue occupies 205-220 degrees; low-chroma warm wisps occupy 28-40.
    blue_hue = np.clip(hue, 205, 220)
    hsv[:, :, 0] = np.where(blue, np.rint(blue_hue * 256 / 360), hsv[:, :, 0]).astype(np.uint8)
    hsv[:, :, 0] = np.where(warm & ~lightning, np.rint(34 * 256 / 360), hsv[:, :, 0]).astype(np.uint8)
    hsv[:, :, 1] = np.where(warm & ~lightning, np.minimum(sat, 76), hsv[:, :, 1]).astype(np.uint8)
    hsv[:, :, 0] = np.where(lightning, np.rint(55 * 256 / 360), hsv[:, :, 0]).astype(np.uint8)
    hsv[:, :, 1] = np.where(lightning, np.minimum(sat, 52), hsv[:, :, 1]).astype(np.uint8)
    # Hue has no visual meaning for near-neutral pixels. Set it consistently.
    hsv[:, :, 0] = np.where(sat <= 14, np.rint(211 * 256 / 360), hsv[:, :, 0]).astype(np.uint8)
    rgb = np.asarray(Image.fromarray(hsv, "HSV").convert("RGB")).astype(np.float32)

    # A blue-white layered atmosphere follows radial distance from the exact rim.
    distance = yy - dest_horizon[None, :]
    bayer = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]], dtype=np.float32)
    ordered = bayer[np.arange(H)[:, None] % 4, np.arange(W)[None, :] % 4] / 16
    haze = 0.08 + 0.42 * np.exp(-np.maximum(distance, 0) / 32.0)
    haze *= distance < 105
    haze = np.clip(np.floor(haze * 16 + ordered) / 16, 0, 0.55)
    pale = np.array([171, 188, 204], dtype=np.float32)
    rgb = rgb * (1 - haze[:, :, None]) + pale * haze[:, :, None]
    # The dark atmospheric layer immediately under the haze increases depth.
    shade = 0.085 * np.exp(-((distance - 92) / 58) ** 2)
    rgb *= 1 - shade[:, :, None]
    # A final HSV pass cleans rounding shifts introduced by RGB haze blending.
    mixed = np.clip(np.rint(rgb), 0, 255).astype(np.uint8)
    final_hsv = np.asarray(Image.fromarray(mixed, "RGB").convert("HSV")).copy()
    final_hue = final_hsv[:, :, 0].astype(np.float32) * 360 / 256
    final_sat = final_hsv[:, :, 1].astype(np.int16)
    neutral = final_sat < 16
    final_hsv[:, :, 1] = np.where(neutral, 0, final_hsv[:, :, 1]).astype(np.uint8)
    final_blue = (final_hue >= 165) & (final_hue <= 285) & ~neutral
    final_yellow = (final_hue >= 45) & (final_hue <= 75) & ~neutral
    final_bronze = ~final_blue & ~final_yellow & ~neutral
    final_hsv[:, :, 0] = np.where(final_blue, np.rint(np.clip(final_hue, 208, 216) * 256 / 360), final_hsv[:, :, 0]).astype(np.uint8)
    final_hsv[:, :, 0] = np.where(final_yellow, np.rint(55 * 256 / 360), final_hsv[:, :, 0]).astype(np.uint8)
    final_hsv[:, :, 0] = np.where(final_bronze, np.rint(34 * 256 / 360), final_hsv[:, :, 0]).astype(np.uint8)
    final_hsv[:, :, 1] = np.where(final_bronze, np.minimum(final_hsv[:, :, 1], 72), final_hsv[:, :, 1]).astype(np.uint8)
    rgba[:, :, :3] = np.asarray(Image.fromarray(final_hsv, "HSV").convert("RGB"))
    rgba[:, :, 3] = np.where(inside, 255, 0).astype(np.uint8)
    rgba[~inside, :3] = 0
    Image.fromarray(rgba, "RGBA").save(OUT / "storm_planet_limb.png", optimize=True)
    make_preview()


def make_preview():
    old = Image.open(HERE / "old_limb.png").convert("RGBA")
    new = Image.open(OUT / "storm_planet_limb.png").convert("RGBA")
    deck = Image.open(HERE / "cloud_deck.png").convert("RGBA")
    s = (1024, 512)
    panels = [Image.new("RGBA", s, (0, 0, 0, 255)) for _ in range(3)]
    panels[0].alpha_composite(old.resize(s, Image.Resampling.NEAREST))
    panels[1].alpha_composite(new.resize(s, Image.Resampling.NEAREST))
    panels[2].alpha_composite(deck.resize(s, Image.Resampling.NEAREST))
    panels[2].alpha_composite(new.resize(s, Image.Resampling.NEAREST))
    preview = Image.new("RGB", (3096, 556), (9, 14, 22))
    draw = ImageDraw.Draw(preview)
    font = ImageFont.load_default(size=22)
    for i, (label, panel) in enumerate(zip(("OLD LIMB", "NEW LIMB", "NEW OVER STORM CLOUD DECK"), panels)):
        px = i * 1036
        preview.paste(panel.convert("RGB"), (px, 44))
        draw.text((px + 12, 9), label, fill=(198, 211, 222), font=font)
    preview.save(OUT / "preview.png", optimize=True)


if __name__ == "__main__":
    build()
