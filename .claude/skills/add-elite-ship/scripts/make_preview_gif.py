#!/usr/bin/env python3
"""Turns ElitePreview's frame folders into GIFs and contact sheets.

  python3 make_preview_gif.py DIR [--fps 15] [--sheet-every 6]

For every DIR/elite-<key>/NNN.png folder writes DIR/elite-<key>.gif and
DIR/elite-<key>-sheet.png (every Nth frame in a grid).  Needs Pillow.
"""
import argparse
import glob
import os

from PIL import Image


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dir")
    ap.add_argument("--fps", type=float, default=15)
    ap.add_argument("--sheet-every", type=int, default=6)
    ap.add_argument("--cols", type=int, default=10)
    args = ap.parse_args()
    for folder in sorted(glob.glob(os.path.join(args.dir, "elite-*"))):
        if not os.path.isdir(folder):
            continue
        files = sorted(glob.glob(os.path.join(folder, "*.png")))
        if not files:
            continue
        frames = [Image.open(f).convert("RGB") for f in files]
        name = os.path.basename(folder)
        gif = os.path.join(args.dir, name + ".gif")
        small = [f.resize((f.width * 2 // 3, f.height * 2 // 3), Image.LANCZOS) for f in frames]
        pal = [f.quantize(colors=128, method=Image.Quantize.MEDIANCUT) for f in small]
        pal[0].save(gif, save_all=True, append_images=pal[1:], duration=int(1000 / args.fps), loop=0, optimize=True)
        picks = frames[::args.sheet_every]
        w, h = frames[0].width // 2, frames[0].height // 2
        cols = min(args.cols, len(picks))
        rows = (len(picks) + cols - 1) // cols
        sheet = Image.new("RGB", (cols * w, rows * h), (10, 8, 20))
        for i, f in enumerate(picks):
            sheet.paste(f.resize((w, h), Image.LANCZOS), ((i % cols) * w, (i // cols) * h))
        sheet.save(os.path.join(args.dir, name + "-sheet.png"))
        print("%s: %d frames -> %s" % (name, len(frames), gif))


if __name__ == "__main__":
    main()
