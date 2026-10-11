"""Verify the staged Desert worm atlas contract and record measured results."""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
GROUPS = {
    "s": (("ridge", 8), ("emerge", 5), ("arc", 5), ("dive", 5), ("spray", 5), ("cascade", 4)),
    "m": (("ridge", 8), ("emerge", 5), ("arc", 5), ("dive", 5), ("spray", 5), ("cascade", 4)),
    "l": (("ridge", 4), ("emerge", 3), ("arc", 3), ("dive", 3), ("spray", 3)),
}


def hsv(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    q = rgb.astype(np.float32) / 255
    mx, mn = q.max(2), q.min(2)
    d = mx - mn
    s = np.divide(d, mx, out=np.zeros_like(mx), where=mx > 0)
    h = np.zeros_like(mx)
    r, g, b = q[:, :, 0], q[:, :, 1], q[:, :, 2]
    z = (d > 0) & (mx == r)
    h[z] = (60 * (g[z] - b[z]) / d[z]) % 360
    z = (d > 0) & (mx == g)
    h[z] = 60 * ((b[z] - r[z]) / d[z] + 2)
    z = (d > 0) & (mx == b)
    h[z] = 60 * ((r[z] - g[z]) / d[z] + 4)
    return h, s, mx


def diff(a: np.ndarray, b: np.ndarray) -> float:
    ma, mb = a[:, :, 3] > 32, b[:, :, 3] > 32
    union = ma | mb
    if not union.any():
        return 0.
    changed = (ma != mb) | ((np.max(np.abs(a[:, :, :3].astype(np.int16) - b[:, :, :3].astype(np.int16)), axis=2) > 7) & ma & mb)
    return float(np.mean(changed[union]))


def measure_head(img: np.ndarray) -> list[int] | None:
    h, s, _ = hsv(img[:, :, :3])
    # The head is the uppermost matte copper plate. Ignore taller sand spray.
    copper = (img[:, :, 3] > 96) & (h >= 21) & (h <= 31) & (s >= .35)
    yy, xx = np.where(copper)
    if not len(yy):
        return None
    top = int(yy.min())
    near = xx[yy <= top + 3]
    return [int(np.median(near)), top]


def main() -> None:
    atlas = np.asarray(Image.open(ROOT / "worm.png"))
    giant = np.asarray(Image.open(ROOT / "worm_giant.png"))
    assert atlas.shape == giant.shape == (2048, 2048, 4), "size/RGBA"
    assert Image.open(ROOT / "worm.png").mode == Image.open(ROOT / "worm_giant.png").mode == "RGBA"
    manifest = json.loads((ROOT / "manifest.json").read_text())
    js = json.loads((ROOT / "worm.json").read_text())["sprites"]
    jg = json.loads((ROOT / "worm_giant.json").read_text())["sprites"]
    assert len(js) == 64 and len(jg) == 16
    failures = []
    print("scale  cells  margin  sand_s95  amber%  red  pink  violet  loop_min  seam/adj  anchor")
    overall_hues = {}
    for scale in ("s", "m", "l"):
        cell = 512 if scale == "l" else 256
        cols = 4 if scale == "l" else 8
        row0 = 4 if scale == "m" else 0
        sheet = giant if scale == "l" else atlas
        entries = jg if scale == "l" else js
        entries = [e for e in entries if e["n"].startswith("worm_" + scale + "_")]
        frames = {}
        count = 0
        min_margin = 999
        sample_rgb = []
        sample_a = []
        min_loop_change = 1.
        max_seam_ratio = 0.
        anchor_max = 0
        for seq, n in GROUPS[scale]:
            group = []
            metrics = []
            for i in range(n):
                idx = count + i
                x = idx % cols * cell
                y = (idx // cols + row0) * cell
                e = entries[idx]
                assert (e["x"], e["y"], e["w"], e["h"]) == (x, 2048 - y - cell, cell, cell), "JSON coordinates"
                assert e["n"] == f"worm_{scale}_{seq}_{i:02d}"
                img = sheet[y:y + cell, x:x + cell]
                group.append(img)
                opaque = img[:, :, 3] > 32
                assert opaque.any(), e["n"] + " empty"
                yy, xx = np.where(opaque)
                margins = (int(xx.min()), int(yy.min()), int(cell - 1 - xx.max()), int(cell - 1 - yy.max()))
                margin = min(margins)
                min_margin = min(min_margin, margin)
                if margin < (10 if seq == "arc" else 6):
                    failures.append(f"{e['n']} margin {margins}")
                sample_rgb.append(img[:, :, :3].reshape(-1, 3))
                sample_a.append(img[:, :, 3].reshape(-1))
                # Measure the visible contact band, not only the declared point.
                declared = manifest["scales"][scale][seq]["anchor"]
                band_x = np.where(img[cell - 23:cell - 16, :, 3] > 96)[1]
                measured_x = int(round(float(np.median(band_x)))) if len(band_x) else cell // 2
                offset = [measured_x - cell // 2, declared[1] - (cell - 20)]
                anchor_max = max(anchor_max, *map(abs, offset))
                m = {"anchor_offset": offset, "opaque_bbox": [int(xx.min()), int(yy.min()), int(xx.max()), int(yy.max())]}
                if seq in ("emerge", "arc", "dive"):
                    m["head_tip"] = None if (seq == "emerge" and i == 0) or (seq == "dive" and i == n - 1) else measure_head(img)
                metrics.append(m)
            manifest["scales"][scale][seq]["measurements"] = metrics
            if seq == "ridge":
                manifest["scales"][scale][seq]["approx_ridge_length_px"] = int(np.median([m["opaque_bbox"][3] - m["opaque_bbox"][1] + 1 for m in metrics]))
            if seq in ("ridge", "arc", "cascade"):
                steps = [diff(group[i], group[(i + 1) % n]) for i in range(n)]
                min_loop_change = min(min_loop_change, min(steps))
                seam_ratio = steps[-1] / max(.001, np.median(steps[:-1]))
                max_seam_ratio = max(max_seam_ratio, seam_ratio)
                manifest["scales"][scale][seq]["loop_changes"] = [round(v, 4) for v in steps]
                if min(steps) < .04:
                    failures.append(f"{scale} {seq} motion {min(steps):.3f}")
                if seam_ratio > 2.25:
                    failures.append(f"{scale} {seq} seam {seam_ratio:.2f}")
            frames[seq] = group
            count += n
        assert count == (16 if scale == "l" else 32)
        rgb = np.concatenate(sample_rgb)
        alpha = np.concatenate(sample_a)
        h, s, v = hsv(rgb.reshape(-1, 1, 3))
        h, s, v = h[:, 0], s[:, 0], v[:, 0]
        mask = alpha > 32
        npx = max(1, int(mask.sum()))
        sand = mask & (h >= 40) & (h <= 48) & (s < .45)
        sand95 = float(np.quantile(s[sand], .95)) if sand.any() else 0.
        amber = mask & (h >= 30) & (h <= 48) & (s > .45)
        red = mask & ((h >= 345) | (h <= 15)) & (s > .5)
        pink = mask & (h >= 312) & (h <= 326) & (s > .25)
        violet = mask & (h >= 250) & (h <= 300) & (s > .2)
        neon_cool = mask & (h >= 70) & (h <= 205) & (s > .40)
        copper = mask & (h >= 22) & (h <= 30) & (s > .30)
        shadow = mask & (h >= 232) & (h <= 248) & (s > .20)
        amber_pct = 100 * int(amber.sum()) / npx
        copper_vmax = float(v[copper].max()) if copper.any() else 0.
        copper_smax = float(s[copper].max()) if copper.any() else 0.
        shadow_vmax = float(v[shadow].max()) if shadow.any() else 0.
        shadow_smax = float(s[shadow].max()) if shadow.any() else 0.
        if (sand95 > .335 or amber_pct > .3 or red.any() or pink.any() or violet.any()
                or neon_cool.any() or copper_vmax > .601 or copper_smax > .621
                or shadow_vmax > .301 or shadow_smax > .451):
            failures.append(f"{scale} hue rules")
        overall_hues[scale] = {"sand_saturation_p95": round(sand95, 4), "forbidden_amber_percent": round(amber_pct, 4),
                               "red_pixels": int(red.sum()), "pink_pixels": int(pink.sum()), "violet_pixels": int(violet.sum()),
                               "saturated_green_cyan_pixels": int(neon_cool.sum()),
                               "copper_saturation_max": round(copper_smax, 4),
                               "copper_value_max": round(copper_vmax, 4),
                               "shadow_saturation_max": round(shadow_smax, 4),
                               "shadow_value_max": round(shadow_vmax, 4),
                               "shadow_coverage_percent": round(100 * int(shadow.sum()) / npx, 3),
                               "opaque_pixels": npx, "value_p90": round(float(np.quantile(v[mask], .9)), 4)}
        manifest["scales"][scale]["hue_rules"] = overall_hues[scale]
        print(f"{scale:5} {count:5} {min_margin:7} {sand95:9.3f} {amber_pct:7.3f} {red.sum():4} {pink.sum():5} {violet.sum():7} {min_loop_change:9.3f} {max_seam_ratio:9.2f} {anchor_max:7}")
        if anchor_max > 1:
            failures.append(f"{scale} anchor")
        # The exact settling pose is shared with the ridge start.
        if diff(frames["dive"][-1], frames["emerge"][0]) > .001:
            failures.append(f"{scale} A-B-D handoff")
        if diff(frames["ridge"][0], frames["arc"][0]) < .10:
            failures.append(f"{scale} sequence distinction")
    if failures:
        print("FAIL:", "; ".join(failures))
        raise SystemExit(1)
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print("PASS: RGBA sizes, 80 cells, margins, loops, anchor, hue rules, and sequence distinction")


if __name__ == "__main__":
    main()
