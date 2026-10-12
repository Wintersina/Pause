#!/usr/bin/env python3
"""Copies the runtime-needed impact-v1 art into Resources/BossImpact (run from anywhere)."""
import os, shutil
import os, shutil
here = os.path.dirname(os.path.abspath(__file__))
out = os.path.join(here, '..', '..', 'Resources', 'BossImpact')
os.makedirs(out, exist_ok=True)
os.makedirs(os.path.join(out, 'attack'), exist_ok=True)
# attack_damage/<boss>_<pose>_stage<N>.png -> Resources/BossImpact/attack/ (one 384 px file each: a packed
# per-boss atlas is 2-6 MB, over the repo's 1 MiB new-file limit). BossArt.AttackDamageBody loads them by name.
for f in sorted(os.listdir(os.path.join(here, 'attack_damage'))):
    if f.endswith('.png'):
        shutil.copy(os.path.join(here, 'attack_damage', f), os.path.join(out, 'attack', f))
for n in ['crack_01', 'crack_02', 'crack_03', 'crack_04', 'crack_growth']:
    shutil.copy(os.path.join(here, 'crack_overlays', n + '.png'), os.path.join(out, n + '.png'))
for n in ['boss_hit_burst', 'boss_hit_burst_small']:
    shutil.copy(os.path.join(here, n + '.png'), os.path.join(out, n + '.png'))
