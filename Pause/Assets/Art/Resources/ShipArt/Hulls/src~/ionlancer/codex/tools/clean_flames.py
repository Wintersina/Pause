#!/usr/bin/env python3
"""Codex filled each flame cell with a navy (#1C2A80, the 'dark' band) backdrop rectangle.
Flood-fill every navy/green pixel connected to the cell border back to the green key so only the
flame body stays. Originals are kept as flame_*_raw.png (first run only)."""
import os, shutil
from collections import deque
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
CODEX = os.path.dirname(HERE)
NAVY = (0x1C, 0x2A, 0x80)

for name in ("flame_cruise", "flame_boost"):
    p = os.path.join(CODEX, name + ".png")
    raw = os.path.join(CODEX, name + "_raw.png")
    if not os.path.exists(raw):
        shutil.copy(p, raw)
    a = np.array(Image.open(raw).convert("RGB"))
    H, W, _ = a.shape
    cw = W // 6
    out = a.copy()
    isnavy = (a == np.array(NAVY)).all(-1)
    isgreen = (a == np.array((0, 255, 0))).all(-1)
    bg = isnavy | isgreen
    for c in range(6):
        x0, x1 = c * cw, (c + 1) * cw
        seen = np.zeros((H, cw), bool)
        q = deque()
        for x in range(cw):
            for y in (0, H - 1):
                if bg[y, x0 + x]:
                    q.append((y, x)); seen[y, x] = True
        for y in range(H):
            for x in (0, cw - 1):
                if bg[y, x0 + x] and not seen[y, x]:
                    q.append((y, x)); seen[y, x] = True
        while q:
            y, x = q.popleft()
            for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                ny, nx = y + dy, x + dx
                if 0 <= ny < H and 0 <= nx < cw and not seen[ny, nx] and bg[ny, x0 + nx]:
                    seen[ny, nx] = True; q.append((ny, nx))
        sub = out[:, x0:x1]
        sub[seen] = (0, 255, 0)
    Image.fromarray(out).save(p)
    cols = [int((~(out[:, c * cw:(c + 1) * cw] == np.array((0, 255, 0))).all(-1)).any(0).sum()) for c in range(6)]
    print(name, "flame width px per cell (of", cw, "):", cols)
