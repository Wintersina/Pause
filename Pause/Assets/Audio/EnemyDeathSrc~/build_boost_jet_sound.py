#!/usr/bin/env python3
"""Blue-atom boost pickup: a jet-engine spool-up (Resources/Audio/Boost/boost_jet.wav).

Noise only: broadband noise through a low-pass whose cutoff rises (the ignition roar opening up), a low rumble bed, and a
turbine whine made of NARROW-BAND NOISE (a resonance band that climbs, ~6 % wide, random phase), never a sine. Peaks
mid-clip, then a roar tail that fades. Mono 44.1 kHz 16-bit. Run: python3 build_boost_jet_sound.py [--check]
"""
import sys, wave
from pathlib import Path
import numpy as np

RATE = 44100
DUR = 1.6
OUT = Path(__file__).resolve().parent.parent / 'Resources' / 'Audio' / 'Boost' / 'boost_jet.wav'


def synth(seed=7):
    rng = np.random.default_rng(seed)
    n = int(RATE * DUR)
    t = np.arange(n) / RATE
    x = rng.standard_normal(n)
    # STFT shaping: per frame magnitude = roar low-pass + whine band + rumble
    N, H = 2048, 512
    win = np.hanning(N)
    f = np.fft.rfftfreq(N, 1 / RATE)
    pad = np.concatenate([np.zeros(N), x, np.zeros(N)])
    out = np.zeros(len(pad))
    for s in range(0, len(pad) - N, H):
        tc = (s + N / 2 - N) / RATE
        u = np.clip(tc / 1.0, 0, 1)
        cutoff = 250 * (14 ** (u ** 0.8))            # 250 Hz -> ~3.5 kHz
        if tc > 1.0: cutoff = 3500 * (1 - .25 * min((tc - 1.0) / .6, 1))
        roar = 1 / (1 + (f / cutoff) ** 3)
        wf = 700 + 1900 * u ** 1.2 if tc <= 1.0 else 2600 - 500 * min((tc - 1) / .6, 1)
        whine = 1.6 * np.exp(-0.5 * ((f - wf) / (wf * .035)) ** 2) * min(u * 2, 1)
        whine += .7 * np.exp(-0.5 * ((f - wf * 1.5) / (wf * .05)) ** 2) * min(u * 2, 1)
        rumble = 1.4 * np.exp(-(f / 110) ** 2) * (.6 + .4 * u)
        mag = roar * (.35 + .65 * u) + .5 * whine * (f < 6000) + rumble
        seg = np.fft.irfft(np.fft.rfft(pad[s:s + N] * win) * mag)
        out[s:s + N] += seg * win
    y = out[N:N + n]
    # level envelope: spool up, peak ~0.95 s, roar tail, fade out
    env = np.where(t < .95, (t / .95) ** 1.6 * .85 + .15 * np.minimum(t / .04, 1), 1.0)
    env = np.where(t >= .95, np.exp(-(t - .95) / .55), env)
    env *= np.minimum((DUR - t) / .22, 1) ** 2
    env *= np.minimum(t / .02, 1)
    flutter = 1 + .07 * np.convolve(rng.standard_normal(n), np.ones(900) / 900, 'same') * 12
    y = (y - y.mean()) * env * flutter
    return y * (10 ** (-3.5 / 20)) / np.max(np.abs(y))


def write_wav(path, x):
    path.parent.mkdir(parents=True, exist_ok=True)
    pcm = np.round(np.clip(x, -1, 1) * 32767).astype('<i2')
    with wave.open(str(path), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE); w.writeframes(pcm.tobytes())


def peak_ratio(x, size=4096, hop=2048, half=64):
    """Worst (peak bin power / local mean power) over loud frames: ~5-12 for noise, >1000 for a tone."""
    worst = 0.0; w = np.hanning(size); rms_all = np.sqrt(np.mean(x * x))
    for s in range(0, len(x) - size, hop):
        fr = x[s:s + size]
        if np.sqrt(np.mean(fr * fr)) < .25 * rms_all: continue
        p = np.abs(np.fft.rfft(fr * w)) ** 2
        k = int(np.argmax(p[3:])) + 3
        lo, hi = max(k - half, 1), min(k + half, len(p))
        local = (p[lo:hi].sum() - p[max(k - 2, 0):k + 3].sum()) / (hi - lo - 5)
        worst = max(worst, p[k] / (local + 1e-30))
    return worst


if __name__ == '__main__':
    x = synth()
    if '--check' not in sys.argv:
        write_wav(OUT, x)
    print('peak %.1f dB rms %.1f dB ratio %.1f' % (20 * np.log10(np.max(np.abs(x))), 20 * np.log10(np.sqrt(np.mean(x * x))), peak_ratio(x)))
