"""Writes every enemy flipbook frame as an SVG template into svg/.

    python3 build.py && ./render.sh

One strip per enemy id (art key), FRAME_COUNT frames: 0-3 idle, 4-5 tell,
6 hit flash. The ids and frame timings here are mirrored in
Scripts/Gameplay/Enemies/EnemyRoster.cs; EnemyArtTest checks every roster
entry has its strip.
"""
import os
import sys

from common import FRAME_COUNT, HIT, Parts, svg_doc
import mine
import rocks
import big
import fighter
import chaser
import alien

WORLDS = ("space", "frost", "verdant", "ember")
MINES = False


def roster():
    """art key -> (draw(i) -> Parts, idle ticks, tell ticks)"""
    out = {}
    for w in WORLDS:
        for variant, fn in rocks.DRAW[w].items():
            out[f"{w}_rock_{variant}"] = (fn, rocks.IDLE_TICKS, rocks.TELL_TICKS)
        # The rail mines are no longer drawn here: they play the original
        # neon pixel-art atlas (Art/Resources/Enemies/Mines/rail_mines_neon.png,
        # RailMineArt). mine.py stays for reference; add MINES to draw them.
        if MINES:
            out[f"{w}_mine"] = (lambda i, w=w: mine.draw(w, i), mine.IDLE_TICKS, mine.TELL_TICKS)
        out[f"{w}_big"] = (lambda i, w=w: big.draw(w, i), big.IDLE_TICKS, big.TELL_TICKS)
        for tier in (1, 2, 3, 4):
            out[f"{w}_fighter_{tier}"] = (lambda i, w=w, t=tier: fighter.draw(w, t, i), fighter.IDLE_TICKS, fighter.TELL_TICKS)
        out[f"{w}_chaser"] = (lambda i, w=w: chaser.draw(w, i), chaser.IDLE_TICKS, chaser.TELL_TICKS)
        out[f"{w}_alien"] = (lambda i, w=w: alien.draw(w, i), alien.IDLE_TICKS, alien.TELL_TICKS)
    return out


HIT_TICKS = 2


def main(only=None):
    here = os.path.dirname(os.path.abspath(__file__))
    out_dir = os.path.join(here, "svg")
    os.makedirs(out_dir, exist_ok=True)
    if not only:
        for old in os.listdir(out_dir):
            if old.endswith(".svg"):
                os.remove(os.path.join(out_dir, old))
    n = 0
    for key, (fn, idle, tell) in roster().items():
        if only and not any(key.startswith(o) for o in only):
            continue
        ticks = list(idle) + list(tell) + [HIT_TICKS]
        for i in range(FRAME_COUNT):
            parts = fn(0 if i == HIT else i)
            body = parts.render(flash=(i == HIT))
            role = "idle" if i < 4 else "tell" if i < 6 else "hit"
            comment = f"{key} frame {i} ({role}), hold {ticks[i]} ticks @24fps; colours are palette.env tokens"
            with open(os.path.join(out_dir, f"{key}_{i}.svg"), "w") as fh:
                fh.write(svg_doc(body, comment))
            n += 1
    print(f"wrote {n} SVG frames into svg/")


if __name__ == "__main__":
    main(sys.argv[1:])
