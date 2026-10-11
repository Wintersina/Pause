#!/usr/bin/env python3
"""Build the boss HURT cues: boss_<world>_hurt_0/_1.wav (Resources/Audio/EnemyDeath).

A boss losing a heart (EnemyDeathAudio.PlayBossHurt, from BossActor.SetHearts) is a heavy blow on its own material, not its death: ~0.5-0.7 s,
one low thump, a short body of filtered noise, a few dull plate fragments and that world's detail. No oscillators, no pings, no chirps (the
user: nothing that sounds like a beep or a coin). Same toolkit as build_boss_gate_sounds.py (filtered noise, low thump, modal plates at a
very low level, a short dark room), deterministic, mono 44.1 kHz 16-bit.

Level: A-weighted loudest 50 ms at -17 dB (the boss death cues sit at about -15), because it plays on every heart lost and must not wall off
the death cue it leads to. Verification mirrors verify_wavs.py: centroid < 900 Hz (bigs), < 12 % above 4 kHz, soft 2-8 ms attack, no tonal
run > 100 ms, no clipping, the two variants of a boss correlate < 0.6 and differ in length by < 20 %.

    python3 build_boss_hurt_sounds.py
"""
from __future__ import annotations

import zlib
from pathlib import Path

import numpy as np

import build_verdant_ember_sounds as b
import build_boss_gate_sounds as g

ROOT = Path(__file__).resolve().parent
DEATH = ROOT.parent / 'Resources' / 'Audio' / 'EnemyDeath'
RATE = b.RATE
TARGET_A50 = -17.0
ORDER = [
    ('boss_space_hurt', .62, 'Void Archon hull strike', 'dull plate boom, torn armour, plasma crackle'),
    ('boss_frost_hurt', .66, 'Leviathan ice-steel crack', 'hull groan, ice fissure, rime shards'),
    ('boss_verdant_hurt', .60, 'Bloom Queen petal-armour rupture', 'wet tear, brass plate, seed pops'),
    ('boss_ember_hurt', .66, 'Cinder Drake scale fracture', 'rock thud, obsidian crack, steam vent'),
    ('boss_tide_hurt', .70, 'Iron Kraken pressure-hull dent', 'deep boom, iron groan, trapped bubbles'),
]


def build(key, dur, rng, v):
    x = np.zeros(round(dur * RATE))
    noise, pulse, bubbles, scatter = g.noise, g.pulse, g.bubbles, g.scatter
    if key == 'boss_space_hurt':
        noise(x, rng, 0, .16, 40, 420, .50, .08, 9)
        pulse(x, rng, .02, 1.0)
        noise(x, rng, .03, .45, 60, 1000, .62, .17, 6)
        scatter(x, rng, .05, .46, 15 + v * 4, 120, 1500, .12)
        for at in (.06, .15): b.add_modal(x, rng, at, .05, 250)
        noise(x, rng, .22, .30, 70, 800, .20, .16)
    elif key == 'boss_frost_hurt':
        noise(x, rng, 0, .20, 45, 450, .46, .10, 7)
        pulse(x, rng, .03, 1.05)
        noise(x, rng, .02, .50, 55, 950, .64, .20, 7)
        noise(x, rng, .05, .22, 150, 1900, .30, .10)
        scatter(x, rng, .07, .56, 22 + v * 5, 130, 2200, .12)
        b.add_modal(x, rng, .08, .045, 190)
        noise(x, rng, .30, .30, 300, 1500, .13, .18)
    elif key == 'boss_verdant_hurt':
        noise(x, rng, 0, .18, 50, 600, .44, .09, 14)
        pulse(x, rng, .02, .95)
        noise(x, rng, .02, .38, 65, 1000, .66, .15, 19)
        bubbles(x, rng, .04, .42, 9 + v * 3, .15)
        scatter(x, rng, .10, .50, 18 + v * 4, 130, 1700, .09)
        b.add_modal(x, rng, .07, .05, 225)
        noise(x, rng, .22, .30, 90, 900, .16, .14, 17)
    elif key == 'boss_ember_hurt':
        noise(x, rng, 0, .18, 35, 400, .50, .09, 10)
        pulse(x, rng, .02, 1.1)
        noise(x, rng, .02, .52, 40, 900, .70, .21, 12)
        scatter(x, rng, .06, .56, 20 + v * 5, 80, 1500, .12)
        noise(x, rng, .14, .40, 200, 1700, .17, .20)
        bubbles(x, rng, .20, .60, 6 + v * 2, .11)
    elif key == 'boss_tide_hurt':
        noise(x, rng, 0, .20, 30, 320, .52, .10, 9)
        pulse(x, rng, .03, 1.1)
        noise(x, rng, .03, .62, 35, 700, .70, .24, 6)
        bubbles(x, rng, .08, .62, 14 + v * 4, .13)
        b.add_modal(x, rng, .09, .05, 180)
        noise(x, rng, .24, .40, 90, 1100, .22, .22, 11)
        scatter(x, rng, .15, .64, 10 + v * 3, 80, 1100, .07)
    else:
        raise ValueError(key)
    return x


def finish(x, rng):
    x = b.short_room(x, rng, .12, .04)
    x = b.fft_filter(x, 27, 2350)
    n = len(x)
    t = np.arange(n) / RATE
    edge = np.sin(np.minimum(t / .008, 1) * np.pi / 2) ** 2
    edge *= np.sin(np.minimum((n - 1 - np.arange(n)) / (RATE * .030), 1) * np.pi / 2) ** 2
    x = x * edge
    x -= np.mean(x) * edge
    # a sharp transient can push the matched peak past the limit: soften the crest (power law) until it fits
    for gamma in (1.0, .9, .8, .72, .65, .58, .5):
        y = np.sign(x) * np.abs(x) ** gamma
        y -= np.mean(y) * edge
        try:
            return b.match_loudness(y, TARGET_A50)
        except ValueError:
            continue
    raise ValueError('cannot fit the level')


def stats(x):
    p = np.abs(np.fft.rfft(x)) ** 2
    f = np.fft.rfftfreq(len(x), 1 / RATE)
    s = g.read_stats(x)
    s['a50'] = b.short_a_level(x)
    s['hf'] = float(np.sum(p[f > 4000]) / (np.sum(p) + 1e-30))
    return s


def main():
    out = {}
    notes = ['# Boss hurt cues', '',
             'Run `python3 build_boss_hurt_sounds.py` (deterministic, NumPy only). `boss_<world>_hurt_0/_1.wav` in Resources/Audio/EnemyDeath, played by',
             '`EnemyDeathAudio.PlayBossHurt` on every heart a boss loses (never the last: that is its death cue). A-weighted loudest 50 ms at -17 dB.', '',
             '| File | s | Peak dBFS | A50 dB | Centroid Hz | >4 kHz % | Attack ms | Tonal ms | Clips |', '|---|---:|---:|---:|---:|---:|---:|---:|---:|']
    fails = []
    print('File                         s    peak   A50   centroid   HF%  attack tonal clips xcorr')
    for key, dur, _, _ in ORDER:
        pair = []
        for v in range(2):
            rng = np.random.default_rng(zlib.crc32(key.encode()) + v * 1000003 + 5521)
            x = finish(build(key, dur * (1, .94)[v], rng, v), rng)
            path = DEATH / f'{key}_{v}.wav'
            b.write_wav(path, x)
            pair.append(b.read_wav(path))
        corr = b.max_xcorr(pair[0], pair[1])
        if corr >= .6:
            fails.append(f'{key}: xcorr {corr:.3f}')
        if abs(len(pair[0]) / len(pair[1]) - 1) > .2:
            fails.append(f'{key}: length pair')
        for v, x in enumerate(pair):
            m = stats(x)
            name = f'{key}_{v}.wav'
            good = (abs(m['a50'] - TARGET_A50) <= 1.5 and m['centroid'] < 900 and m['hf'] < .12 and 2 <= m['attack'] <= 8 and
                    m['tonal'] <= 100 and m['clips'] == 0 and .4 <= m['duration'] <= .9)
            if not good:
                fails.append(name)
            print(f"{name:<28} {m['duration']:4.2f} {m['peak']:6.2f} {m['a50']:6.2f} {m['centroid']:8.0f} {m['hf']*100:6.2f} {m['attack']:6.1f} {m['tonal']:5.0f} {m['clips']:5d} {corr:5.2f}")
            notes.append(f"| `{name}` | {m['duration']:.3f} | {m['peak']:.2f} | {m['a50']:.2f} | {m['centroid']:.0f} | {m['hf']*100:.3f} | {m['attack']:.1f} | {m['tonal']:.0f} | {m['clips']} |")
        out[key] = pair
    if fails:
        raise SystemExit('Verification failed: ' + ', '.join(fails))
    gap = np.zeros(round(.9 * RATE))
    seq = [out[k][v] for k, *_ in ORDER for v in range(2)]
    parts = [p for i, s in enumerate(seq) for p in ((s, gap) if i < len(seq) - 1 else (s,))]
    b.write_wav(ROOT / 'audition_boss_hurt.wav', np.concatenate(parts))
    notes += ['', f'**Result:** PASS: {len(seq)} WAVs. Audition: `audition_boss_hurt.wav` (space, frost, verdant, ember, tide; variants 0 then 1).',
              'No listening monitor was available: check on a phone speaker.', '']
    (ROOT / 'sound_notes_boss_hurt.md').write_text('\n'.join(notes))
    print('PASS: 10 WAVs, audition, notes')


if __name__ == '__main__':
    main()
