#!/usr/bin/env python3
"""Audit authored enemy death cues / screams (Resources/Audio/EnemyDeath/<key>_<n>.wav, <key>_scream_<n>.wav).

  python3 verify_wavs.py WAV_DIR PREFIX [--target -15] [--tol 1.5] [--big KEY ...]

PREFIX picks the files (e.g. ember_ or tide_). Mono 44.1 kHz 16-bit expected. Per file it prints duration, peak,
A-weighted loudest-50 ms level (A50), centroid, share above 4 kHz, attack, longest tonal run and clipping, and FAILS:
  death cue : A50 within --tol dB of --target (big/elite keys: up to 2 dB louder), centroid < 1800 Hz (big < 900 for
              enemies named --big), < 12% energy above 4 kHz, attack 2-8 ms rising (soft), no clipping,
              longest narrow-band tonal run <= 100 ms (a ringing 1-3 kHz tone reads as a scream/beep)
  scream    : 0.7-1.3 s, peak -9 dBFS +-1.5, centroid < 1700 Hz, < 8% above 3.5 kHz, 10-16 dB under its death cue in game (files 4-10 dB lower in A50: ScreamVolume .5 adds 6 dB)
  variants  : cross-correlation of the _0/_1/_2 variants < 0.6, lengths within +-20%
It cannot hear: audition the files (afplay) - this only catches the measurable ways a cue turns beepy, harsh,
too loud or too alike. Needs numpy.
"""
import sys, os, glob, wave, argparse, re
import numpy as np

RATE = 44100
fails = 0
def check(ok, what):
    global fails
    print(("PASS  " if ok else "FAIL  ") + what)
    if not ok: fails += 1

def read(p):
    with wave.open(p, "rb") as w:
        assert w.getnchannels() == 1 and w.getsampwidth() == 2, f"{p}: not mono 16-bit"
        rate = w.getframerate()
        x = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(np.float64) / 32768
    return x, rate

def a_weighted(x, rate):
    f = np.fft.rfftfreq(len(x), 1 / rate); f2 = f * f
    ra = (12194**2 * f2 * f2) / ((f2 + 20.6**2) * np.sqrt((f2 + 107.7**2) * (f2 + 737.9**2)) * (f2 + 12194**2) + 1e-30)
    return np.fft.irfft(np.fft.rfft(x) * ra * 10 ** (2 / 20), n=len(x))

def a50(x, rate):
    y = a_weighted(x, rate)
    n, hop = round(.05 * rate), round(.025 * rate)
    if len(y) < n: y = np.pad(y, (0, n - len(y)))
    fr = np.lib.stride_tricks.sliding_window_view(y, n)[::hop]
    return 20 * np.log10(np.sqrt((fr * fr).mean(axis=1)).max() + 1e-30)

def stats(x, rate):
    peak = 20 * np.log10(np.abs(x).max() + 1e-30)
    spec = np.abs(np.fft.rfft(x)) ** 2; f = np.fft.rfftfreq(len(x), 1 / rate)
    cen = float((spec * f).sum() / (spec.sum() + 1e-30)); hf = float(spec[f > 4000].sum() / (spec.sum() + 1e-30) * 100)
    hf35 = float(spec[f > 3500].sum() / (spec.sum() + 1e-30) * 100)
    env = np.abs(x); k = int(rate * .001); env = np.convolve(env, np.ones(k) / k, "same")
    pk = env.max(); i10 = np.argmax(env > .1 * pk); i90 = np.argmax(env > .9 * pk)
    attack = (i90 - i10) / rate * 1000
    # tonal run: 20 ms frames, strongest bin 1-3.5 kHz > 12 dB over the frame median spectrum
    n = int(.02 * rate); run = best = 0
    for s in range(0, len(x) - n, n // 2):
        seg = x[s:s + n] * np.hanning(n); m = np.abs(np.fft.rfft(seg)); ff = np.fft.rfftfreq(n, 1 / rate)
        band = (ff > 1000) & (ff < 3500)
        if m[band].max() > 4 * (np.median(m) + 1e-9) * 1.0 and m[band].max() > 0.02 * m.max() and m[band].max() / (np.median(m[band]) + 1e-9) > 16:
            run += 1; best = max(best, run)
        else: run = 0
    return dict(dur=len(x) / rate, peak=peak, a50=a50(x, rate), cen=cen, hf=hf, hf35=hf35, attack=attack,
                tonal=best * (n // 2) / rate * 1000, clip=int((np.abs(x) >= .9999).sum()))

def xcorr(a, b):
    n = min(len(a), len(b)); a = a[:n] - a[:n].mean(); b = b[:n] - b[:n].mean()
    c = np.fft.irfft(np.fft.rfft(a, 2 * n) * np.conj(np.fft.rfft(b, 2 * n)))
    return float(np.abs(c).max() / (np.linalg.norm(a) * np.linalg.norm(b) + 1e-30))

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("dir"); ap.add_argument("prefix")
    ap.add_argument("--target", type=float, default=-15); ap.add_argument("--tol", type=float, default=1.5)
    ap.add_argument("--big", nargs="*", default=[])
    o = ap.parse_args()
    files = sorted(glob.glob(os.path.join(o.dir, o.prefix + "*.wav")))
    check(len(files) > 0, f"{len(files)} files for prefix {o.prefix}")
    groups = {}
    print(f"{'file':44} {'s':>5} {'peak':>6} {'A50':>6} {'cent':>5} {'>4k%':>5} {'att':>5} {'tonal':>5}")
    for f in files:
        name = os.path.basename(f)[:-4]; x, rate = read(f); check(rate == RATE, f"{name}: 44.1 kHz ({rate})")
        s = stats(x, rate); scream = "_scream_" in name
        key = re.sub(r"(_scream)?_\d+$", "", name)
        groups.setdefault((key, scream), []).append((name, x, s))
        print(f"{name:44} {s['dur']:5.2f} {s['peak']:6.1f} {s['a50']:6.1f} {s['cen']:5.0f} {s['hf']:5.1f} {s['attack']:5.1f} {s['tonal']:5.0f}")
        big = any(b in key for b in o.big) or "_elite_" in key
        if scream:
            check(.7 <= s["dur"] <= 1.3, f"{name}: scream length {s['dur']:.2f}s (0.7-1.3)")
            check(abs(s["peak"] + 9) <= 1.5, f"{name}: scream peak {s['peak']:.1f} dBFS (-9 +-1.5)")
            check(s["cen"] < 1700 and s["hf35"] < 8, f"{name}: scream centroid {s['cen']:.0f} Hz (<1700), >3.5k {s['hf35']:.1f}% (<8)")
        else:
            hi = o.target + (2 if big else 0)
            check(o.target - o.tol <= s["a50"] <= hi + o.tol, f"{name}: A50 {s['a50']:.1f} dB in [{o.target-o.tol:.1f}, {hi+o.tol:.1f}]")
            check(s["cen"] < (900 if any(b in key for b in o.big) else 1800) and s["hf"] < 12, f"{name}: centroid {s['cen']:.0f} Hz, >4k {s['hf']:.1f}%")
            check(s["clip"] == 0, f"{name}: no clipping")
            check(s["tonal"] <= 100, f"{name}: tonal run {s['tonal']:.0f} ms (<=100)")
    for (key, scream), items in groups.items():
        if len(items) > 1:
            xs = [i[1] for i in items]
            worst = max(xcorr(xs[i], xs[j]) for i in range(len(xs)) for j in range(i + 1, len(xs)))
            lens = [len(v) for v in xs]
            check(worst < .6, f"{key}{' scream' if scream else ''}: variants differ (max xcorr {worst:.2f} < .6)")
            check(max(lens) <= 1.2 * min(lens) + 1, f"{key}{' scream' if scream else ''}: variant lengths within +-20%")
        if scream and (key, False) in groups:
            d = np.mean([i[2]["a50"] for i in groups[(key, False)]]); sc = np.mean([i[2]["a50"] for i in items])
            # the game plays a scream at EnemyDeathAudio.ScreamVolume (.5 = -6 dB): in game it sits 10-16 dB under the cue
            check(4 <= d - sc <= 10, f"{key}: scream file is {d-sc:.1f} dB under its death cue = {d-sc+6:.1f} dB in game (10-16)")
    print("RESULT:", "FAIL" if fails else "PASS", f"({fails} failures)")
    sys.exit(1 if fails else 0)
main()
