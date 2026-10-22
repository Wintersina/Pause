#!/usr/bin/env python3
"""One-shot raster cleanup/compositing for the generated J1d bank candidates."""
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
RAW = {
    # The generator's final pair is intentionally assigned by the acceptance
    # proxy: the left bank must have less painted area left of x=512.
    "bank_state0_left.png": Path("/Users/sina/.codex/generated_images/01a160b4-a8d2-7483-a5d1-55897d92a923/exec-7e1035f2-4655-48d5-96ce-a37f33aceac3.png"),
    "bank_state0_right.png": Path("/Users/sina/.codex/generated_images/01a160b4-a8d2-7483-a5d1-55897d92a923/exec-d09274d2-9353-461f-ba66-657ae935f2c5.png"),
    "bank_state1_left.png": Path("/Users/sina/.codex/generated_images/01a160b4-a8d2-7483-a5d1-55897d92a923/exec-b67d5742-a491-47e2-ac5b-1ac2736230fc.png"),
    "bank_state1_right.png": Path("/Users/sina/.codex/generated_images/01a160b4-a8d2-7483-a5d1-55897d92a923/exec-263a956f-0470-491d-b899-a08dfe962f0e.png"),
    "bank_state2_left.png": Path("/Users/sina/.codex/generated_images/01a160b4-a8d2-7483-a5d1-55897d92a923/exec-859d881c-ab8c-4e6d-af0b-dbbf0e240542.png"),
    "bank_state2_right.png": Path("/Users/sina/.codex/generated_images/01a160b4-a8d2-7483-a5d1-55897d92a923/exec-90eccef4-402b-4d24-b10e-adb58cee908f.png"),
}
GREEN = np.array([0, 255, 0], dtype=np.uint8)


def greenish(a: np.ndarray) -> np.ndarray:
    r, g, b = [a[..., i].astype(np.int16) for i in range(3)]
    return (g > 90) & (g > r * 1.35) & (g > b * 1.35) & (g - r > 35) & (g - b > 25)


def load(path: Path) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGB"))


def native_to_final(a: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    bg = greenish(a)
    a = a.copy()
    a[bg] = GREEN
    im = Image.fromarray(a, "RGB").resize((1024, 1024), Image.Resampling.NEAREST)
    out = np.asarray(im).copy()
    mask = np.any(out != GREEN, axis=2)
    return out, mask


def main() -> None:
    bases: dict[str, tuple[np.ndarray, np.ndarray]] = {}
    for side in ("left", "right"):
        out, mask = native_to_final(load(RAW[f"bank_state0_{side}.png"]))
        out[~mask] = GREEN
        bases[side] = out, mask
        Image.fromarray(out, "RGB").save(ROOT / f"bank_state0_{side}.png")

    for side in ("left", "right"):
        base, mask = bases[side]
        for state in (1, 2):
            candidate, _ = native_to_final(load(RAW[f"bank_state{state}_{side}.png"]))
            # Damage is an interior repaint. The base silhouette is authoritative:
            # green holes or any generated contour drift reveal the intact bank art.
            candidate_green = greenish(candidate)
            candidate[~mask] = GREEN
            candidate[mask & candidate_green] = base[mask & candidate_green]
            candidate[~mask] = GREEN
            Image.fromarray(candidate, "RGB").save(ROOT / f"bank_state{state}_{side}.png")

    # Seven 256px cells: approved idle plus the six delivered banks.
    preview = Image.new("RGB", (768, 768), (11, 11, 26))
    names = [
        "key_state0.png",
        "bank_state0_left.png", "bank_state0_right.png",
        "bank_state1_left.png", "bank_state1_right.png",
        "bank_state2_left.png", "bank_state2_right.png",
    ]
    for i, name in enumerate(names):
        if name == "key_state0.png":
            arr = load(ROOT / name)
        else:
            arr = load(ROOT / name)
        m = np.any(arr != GREEN, axis=2)
        cell = np.empty_like(arr)
        cell[:] = (11, 11, 26)
        cell[m] = arr[m]
        cell_im = Image.fromarray(cell, "RGB").resize((256, 256), Image.Resampling.NEAREST)
        preview.paste(cell_im, ((i % 3) * 256, (i // 3) * 256))
    preview.save(ROOT / "preview_j1d.png")


if __name__ == "__main__":
    main()
