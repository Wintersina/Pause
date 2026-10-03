#!/usr/bin/env python3
"""Writes every sample SVG (one file per flipbook frame) next to this script.

    python3 build.py && ./render.sh && python3 compose.py
"""
import os
from akira import svg
import sprites as S
import scenes as W
import ui as U

HERE = os.path.dirname(os.path.abspath(__file__))


def write(name, w, h, body, comment):
    with open(os.path.join(HERE, name + ".svg"), "w") as fh:
        fh.write(svg(w, h, body, comment))


def main():
    write("player_ship", 128, 128, S.ship(), "Player default hull (Neon Comet). 128u canvas, nose up. Layers: base/shadow/highlight/ink/glow.")
    for i in range(len(S.EXHAUST_FRAMES)):
        write(f"player_exhaust_{i}", 128, 256, S.ship_with_exhaust(i),
              f"Player + tail-light exhaust, frame {i}/{len(S.EXHAUST_FRAMES)}. Hold {S.EXHAUST_TIMING[i]} tick(s) @24fps.")
    for i in range(len(S.FIGHTER_FRAMES)):
        write(f"enemy_fighter_{i}", 128, 104, S.fighter(i), f"Common enemy fighter idle, frame {i}. Hold {S.FIGHTER_TIMING[i]} ticks.")
    for i in range(len(S.ALIEN_FRAMES)):
        write(f"enemy_alien_{i}", 128, 128, S.alien(i), f"Alien idle, frame {i}. Hold {S.ALIEN_TIMING[i]} ticks.")
    for i in range(len(S.MINE_FRAMES)):
        write(f"enemy_mine_{i}", 128, 128, S.mine(i), f"Rail mine pulse, frame {i}. Hold {S.MINE_TIMING[i]} ticks.")
    write("asteroid", 128, 128, S.asteroid(), "Asteroid, faceted. 128u canvas.")
    for i in range(6):
        write(f"explosion_{i}", 128, 128, S.explosion(i), f"Explosion, frame {i}. Hold {S.EXPLOSION_TIMING[i]} ticks.")
    for i in range(len(S.STARDUST_FRAMES)):
        write(f"pickup_stardust_{i}", 64, 64, S.stardust(i), f"Star dust cell spin, frame {i}. Hold {S.STARDUST_TIMING[i]} ticks.")
    for i in range(len(S.HEAL_FRAMES)):
        write(f"pickup_heal_{i}", 64, 64, S.heal(i), f"Heal cell pulse, frame {i}. Hold {S.HEAL_TIMING[i]} ticks.")
    for i in range(3):
        write(f"robot_{i}", 128, 124, S.robot(i), f"Tutorial robot talk, frame {i}. Hold {S.ROBOT_TIMING[i]} ticks.")

    for name, body, comment in W.all_scenes():
        write(name, W.W, W.H, body, comment)
    for name, w, h, body, comment in U.all_ui():
        write(name, w, h, body, comment)


if __name__ == "__main__":
    main()
