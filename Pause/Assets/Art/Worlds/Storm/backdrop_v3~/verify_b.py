"""Independent geometry, readability, and palette audit for Storm piece atlases."""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent
ATLASES = ("landmarks", "sites", "weather", "lightning")
EXPECTED = (16, 12, 16, 16)


def point_margin(a: np.ndarray) -> int:
    ys, xs = np.nonzero(a > 0)
    if not len(xs):
        return -1
    return int(min(xs.min(), ys.min(), 255 - xs.max(), 255 - ys.max()))


def brightness(a: np.ndarray) -> float:
    luma = a[:, :, :3].astype(float) @ np.array([.2126, .7152, .0722])
    return float(np.sum(luma * a[:, :, 3] / 255))


def audit() -> int:
    failed = []
    opaque_rgb = []
    print("atlas      count dimensions  RGBA  rects margin wrap p90   max-alpha special")
    for atlas, count in zip(ATLASES, EXPECTED):
        im = Image.open(ROOT / f"{atlas}.png")
        cfg = json.loads((ROOT / f"{atlas}.json").read_text())["sprites"]
        arr = np.array(im.convert("RGBA"))
        dims = im.size == (1024, 1024)
        rgba = im.mode == "RGBA"
        rects_ok = len(cfg) == count
        margins = []
        weather_alpha = 0
        specials = []
        p90 = None
        for i, rec in enumerate(cfg):
            col, row = i % 4, i // 4
            expected = {"x": col * 256, "y": 1024 - (row + 1) * 256, "w": 256, "h": 256}
            rects_ok &= all(rec.get(k) == v for k, v in expected.items())
            rects_ok &= 0 <= rec["x"] <= 768 and 0 <= rec["y"] <= 768
            cell = arr[row * 256:(row + 1) * 256, col * 256:(col + 1) * 256]
            alpha = cell[:, :, 3]
            if not rec["n"].startswith("strike_"):
                margins.append(point_margin(alpha))
                if point_margin(alpha) < 14:
                    failed.append(f"{rec['n']} margin {point_margin(alpha)} < 14")
            if atlas == "weather":
                mx = int(alpha.max())
                weather_alpha = max(weather_alpha, mx)
                limit = 150 if rec["n"].startswith("cloud_bank") else 105
                if mx > limit:
                    failed.append(f"{rec['n']} max alpha {mx} > {limit}")
            if rec["n"].startswith("spire_"):
                p = (128, 40)
                ok = int(alpha[p[1], p[0]]) == 255
                specials.append(f"{rec['n']} tip={p} alpha={int(alpha[p[1],p[0]])}")
                if not ok:
                    failed.append(f"{rec['n']} rod tip missing")
            if rec["n"].startswith("strike_"):
                p = (128, 226)
                ok = int(alpha[p[1], p[0]]) == 255
                top_touch = np.flatnonzero(alpha[0] > 0).tolist()
                specials.append(f"{rec['n']} end={p} alpha={int(alpha[p[1],p[0]])} top={top_touch}")
                if not ok or top_touch != [128]:
                    failed.append(f"{rec['n']} endpoint/top entry mismatch")
                if np.count_nonzero(alpha[:14, :128]) or np.count_nonzero(alpha[:14, 129:]) or np.count_nonzero(alpha[:, :14]) or np.count_nonzero(alpha[:, 242:]) or np.count_nonzero(alpha[242:, :]):
                    failed.append(f"{rec['n']} strikes protected 14px margin")
            opaque_rgb.append(cell[alpha >= 128, :3])
        if atlas == "landmarks":
            v = arr[:, :, :3].max(axis=2)[arr[:, :, 3] >= 128] / 255
            p90 = float(np.percentile(v, 90))
            if p90 > .55:
                failed.append(f"landmarks p90 {p90:.3f} > .55")
        if not dims or not rgba or not rects_ok:
            failed.append(f"{atlas} dimensions/mode/rect failure")
        wrap_edges = int(np.count_nonzero(arr[0, :, 3]) + np.count_nonzero(arr[-1, :, 3]) +
                         np.count_nonzero(arr[:, 0, 3]) + np.count_nonzero(arr[:, -1, 3]))
        if wrap_edges:
            failed.append(f"{atlas} nontransparent wrap seam: {wrap_edges} edge pixels")
        print(f"{atlas:10s} {len(cfg):2d}/{count:2d}  {im.width}x{im.height}  {str(rgba):5s} {str(rects_ok):5s} "
              f"{min(margins) if margins else '-':>6} {wrap_edges:>4} {f'{p90:.3f}' if p90 is not None else '-':>5} "
              f"{weather_alpha if atlas == 'weather' else '-':>9} {', '.join(specials)}")
    sites = np.array(Image.open(ROOT / "sites.png"))
    off = sites[2 * 256:3 * 256, 2 * 256:3 * 256]
    on = sites[2 * 256:3 * 256, 3 * 256:4 * 256]
    ratio = brightness(on) / max(brightness(off), 1)
    print(f"lights on/off alpha-weighted luma ratio: {ratio:.2f} (minimum 4.00)")
    if ratio < 4:
        failed.append(f"lights ratio {ratio:.2f} < 4")
    rgb = np.concatenate(opaque_rgb, axis=0)
    hsv = np.array(Image.fromarray(rgb.reshape(1, -1, 3).astype(np.uint8), "RGB").convert("HSV"))[0]
    h = hsv[:, 0].astype(float) * 360 / 255
    s = hsv[:, 1].astype(float) / 255
    shares = {
        "green/teal/cyan": float(np.mean((h >= 95) & (h <= 195) & (s > .18))),
        "saturated violet": float(np.mean((h >= 250) & (h <= 300) & (s > .30))),
        "saturated red": float(np.mean(((h < 15) | (h > 340)) & (s > .5))),
    }
    for key, share in shares.items():
        limit = .002 if key == "saturated red" else .005
        print(f"hue {key:18s}: {share * 100:.3f}% (maximum {limit * 100:.1f}%)")
        if share >= limit:
            failed.append(f"hue {key} {share * 100:.3f}% >= {limit * 100:.1f}%")
    manifest = json.loads((ROOT / "manifest_b.json").read_text())
    if len(manifest.get("pieces", [])) != sum(EXPECTED):
        failed.append("manifest_b piece count")
    print("RESULT:", "PASS" if not failed else "FAIL")
    for failure in failed:
        print(" -", failure)
    return int(bool(failed))


if __name__ == "__main__":
    raise SystemExit(audit())
