#!/usr/bin/env python3
"""Trims the user's two gun clips (kept untouched beside this script) into the short in-game cues:
   Resources/Audio/Attack/EnemyShot/laser.wav    single shots (enemy / elite / boss / rail mine)
   Resources/Audio/Attack/EnemyShot/gunshot.wav  rapid-fire attackers, retriggered in a chain by PewSound
Both are mono 48 kHz 16-bit. Leading silence cut, a few-ms fade-in, a fade-out so a cut never clicks. Re-run after editing the constants."""
import wave, sys, os
import numpy as np
here = os.path.dirname(os.path.abspath(__file__))
out = os.path.join(here, "..", "..", "Resources", "Audio", "Attack", "EnemyShot")
def load(name):
    w = wave.open(os.path.join(here, name)); r = w.getframerate()
    x = np.frombuffer(w.readframes(w.getnframes()), '<i2').reshape(-1, w.getnchannels()).astype(float) / 32768
    return x.mean(1), r
def cut(x, r, start, length, fin, fout, peak):
    s = x[int(start * r):int((start + length) * r)].copy()
    n = len(s); a = int(fin * r); b = int(fout * r)
    s[:a] *= np.linspace(0, 1, a); s[n - b:] *= np.linspace(1, 0, b) ** 1.5
    return s * (peak / np.abs(s).max())
def save(name, x, r):
    with wave.open(os.path.join(out, name), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(r)
        w.writeframes((np.clip(x, -1, 1) * 32767).astype('<i2').tobytes())
x, r = load("lazer.wav");     save("laser.wav",   cut(x, r, .055, .26, .004, .10, .8), r)   # onset at ~60 ms
x, r = load("gun shot.wav");  save("gunshot.wav", cut(x, r, .30,  .20, .004, .08, .8), r)   # the loud body, after its slow swell-in
