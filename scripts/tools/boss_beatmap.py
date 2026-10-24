#!/usr/bin/env python3
"""Offline beat analysis for the boss tracks (Audio/Resources/WorldMusic/Boss_*.mp3).

Writes Pause/Assets/Resources/BossBeatMaps.json, read by BeatMaps (Scripts/Audio/BeatMap.cs).

Usage (needs ffmpeg on PATH and: pip install librosa numpy scipy, in a venv):
    python scripts/tools/boss_beatmap.py            # all bosses
    python scripts/tools/boss_beatmap.py Space Tide # some
    python scripts/tools/boss_beatmap.py --arrangement-only # preserve beat grids, refresh musical features

Method: decode to mono 22.05 kHz, librosa beat tracking from several start
tempos, then fit beat_k = first + k*period by robust least squares. A track is
"steady" when the fitted grid explains the tracked beats (median residual
< 30 ms) and the first/second half tempos agree (< 1 %). Steady tracks store
bpm + firstBeat; others store the explicit beat list. Downbeat (bar phase) is a
low-frequency-energy guess over 4/4 and is the LEAST reliable field: check it
by ear. Times are seconds from clip start (clip time == AudioSource.time).

The half-beat phase (which eighth is the beat) is decided by the KICK band when
it can tell the two apart, and by the full band only as a tie-break: Boss_Space
has its snare / stab layer on the off-beats, and the full-band comb (which that
layer dominates) once put the whole map half a beat late (firstBeat .228 for
kicks at .028; the shots landed between the kicks). A map already in the file
keeps its half-beat phase and its downbeatBeat when re-analysed (both were
checked by ear); delete the entry to re-decide them from scratch.

clipLead: ffmpeg drops the MP3's declared priming (the LAME header's encoder
delay + the decoder's 529 samples: 1105 samples) and reports it as the stream's
start_time, and it skips the Xing / Info header frame; Unity's importer decodes
everything, the header frame included (1152 samples of silence: every imported
clip is exactly (frames + 1) * 1152 samples long), so in the game's
AudioSource.time every beat sits 2257 samples = 47 ms later than the numbers
here. The map carries it and BeatMap applies it (Scripts/Audio/BeatMap.cs).
"""
import hashlib, json, os, subprocess, sys, tempfile
import numpy as np
import librosa
from scipy.signal import butter, sosfilt

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MUSIC = os.path.join(ROOT, "Pause/Assets/Audio/Resources/WorldMusic")
OUT = os.path.join(ROOT, "Pause/Assets/Resources/BossBeatMaps.json")
BOSSES = ["Space", "Frost", "Verdant", "Ember", "Tide", "Storm", "Desert"]
SR = 22050
HOP = 256


def load(path):
    with tempfile.TemporaryDirectory() as d:
        wav = os.path.join(d, "a.wav")
        subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-ac", "1", "-ar", str(SR), wav], check=True)
        y, _ = librosa.load(wav, sr=SR)
    return y


def header_frame_seconds(path):
    """1152 / rate when the MP3 opens with a Xing / Info header frame (Unity decodes it as a silent frame), else 0."""
    with open(path, "rb") as f:
        d = f.read(1 << 16)
    i = 10 + ((d[6] << 21) | (d[7] << 14) | (d[8] << 7) | d[9]) if d[:3] == b"ID3" else 0
    while i < len(d) - 4 and not (d[i] == 0xFF and d[i + 1] & 0xE0 == 0xE0):
        i += 1
    if i >= len(d) - 4 or (d[i + 1] >> 3) & 3 != 3:   # MPEG-1 layer III only
        return 0.0
    rate = {0: 44100, 1: 48000, 2: 32000}.get((d[i + 2] >> 2) & 3)
    off = i + 4 + (17 if (d[i + 3] >> 6) & 3 == 3 else 32)
    return 1152.0 / rate if rate and d[off:off + 4] in (b"Xing", b"Info") else 0.0


def clip_lead(path):
    """Seconds the imported clip runs behind this decode: the priming ffmpeg trims (its start_time) plus the header
    frame it skips and Unity decodes; 0 for a plain clip."""
    out = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "a:0", "-show_entries", "stream=start_time",
                          "-of", "csv=p=0", path], capture_output=True, text=True, check=True).stdout.strip()
    try:
        start = max(0.0, float(out.splitlines()[0]))
    except (ValueError, IndexError):
        start = 0.0
    return round(start + header_frame_seconds(path), 6)


def comb(env, ft, bpm, off, t0, t1):
    p = 60.0 / bpm
    g = np.arange(off + np.ceil((t0 - off) / p) * p, t1, p)
    return float(np.interp(g, ft, env).mean()) if len(g) else 0.0


def best_offset(env, ft, bpm, t0, t1, step=0.004):
    p = 60.0 / bpm
    offs = np.arange(0, p, step)
    sc = [comb(env, ft, bpm, o, t0, t1) for o in offs]
    i = int(np.argmax(sc))
    return float(offs[i]), float(sc[i])


KICK_MARGIN = .12   # the kick band decides the half-beat phase when the weaker candidate scores under 88 % of the stronger


def analyse(name, old=None):
    path = os.path.join(MUSIC, "Boss_%s.mp3" % name)
    y = load(path)
    dur = len(y) / SR
    env = librosa.onset.onset_strength(y=y, sr=SR, hop_length=HOP)
    env = env / env.max()
    low = sosfilt(butter(4, 200, "low", fs=SR, output="sos"), y)
    kenv = librosa.onset.onset_strength(y=low, sr=SR, hop_length=HOP)
    kenv = kenv / kenv.max()
    ft = librosa.frames_to_time(np.arange(len(env)), sr=SR, hop_length=HOP)

    # 1. windowed tempo (20 s windows, 10 s hop): does the tempo move?
    wt = []
    for t0 in np.arange(0, dur - 20, 10):
        best = (-1, 0)
        for bpm in np.arange(138, 156, 0.1):
            for off in np.arange(0, 60 / bpm, 0.01):
                s = comb(env, ft, bpm, off + t0, t0, t0 + 20)
                if s > best[0]:
                    best = (s, bpm)
        wt.append(best[1])
    wt = np.array(wt)
    # global tempo (whole track), refined
    best = (-1, 0)
    for bpm in np.arange(138, 156, 0.05):
        off, s = best_offset(env, ft, bpm, 0, dur, 0.01)
        if s > best[0]:
            best = (s, bpm)
    bpm = best[1]
    for b2 in np.arange(bpm - 0.1, bpm + 0.1, 0.005):
        off, s = best_offset(env, ft, b2, 0, dur, 0.004)
        if s > best[0]:
            best = (s, b2)
    bpm = best[1]
    period = 60.0 / bpm
    # 2. per-window phase at the global tempo, folded mod half a beat (accent
    #    sections on the off-beat are not drift); then which half-beat?
    half = period / 2
    offs = []
    for t0 in np.arange(0, dur - 10, 10):
        o, _ = best_offset(env, ft, bpm, t0, t0 + 10)
        offs.append(((o + half / 2) % half) - half / 2)
    offs = np.array(offs)
    med = float(np.median(offs))
    drift_ms = float(np.percentile(np.abs(offs - med), 90) * 1000)
    # 3. full-grid phase: which of the two half-beats is the beat. The kick band decides when it is clear about it
    #    (the weaker candidate under 1 - KICK_MARGIN of the stronger); otherwise kick + full band together, as before.
    cands = [(med + k * half) % period for k in (0, 1)]
    sk = [comb(kenv, ft, bpm, c, 0, dur) for c in cands]
    sf = [comb(env, ft, bpm, c, 0, dur) for c in cands]
    both = [k + f for k, f in zip(sk, sf)]
    kick_clear = min(sk) / max(sk) <= 1 - KICK_MARGIN
    first = cands[int(np.argmax(sk if kick_clear else both))]
    ambiguity = float(min(both) / max(both))  # near 1.0 = half-beat phase is a coin flip
    if old is not None and old.get("steady"):
        # a map that is already in the file was checked by ear: keep its half-beat phase (the new analysis only refines
        # the position by a few ms), and say so
        o = old["firstBeat"] % period
        d = ((first - o + period / 2) % period) - period / 2   # signed, within half a period
        if abs(d) > period / 4:   # nearer the other half-beat than the old one: a flip, not a refinement
            other = cands[1 - cands.index(first)]
            print("%s: keeping the existing half-beat phase (firstBeat %.3f, analysis preferred %.3f); delete the entry to re-decide" % (name, other, first))
            first = other
    steady = drift_ms < 25 and (wt.max() - wt.min()) < 1.0
    nb = int((dur - first) / period) + 1
    out = {
        "bpm": round(float(bpm), 3), "firstBeat": round(float(first), 4), "clipLead": clip_lead(path), "beatsPerBar": 4,
        # the bar line stays what the file says (checked by ear); a new map starts at 0 and downbeatGuess is the hint
        "downbeatBeat": int(old["downbeatBeat"]) if old is not None else 0, "bossStartBeat": int(old.get("bossStartBeat", 0)) if old is not None else 0, "duration": round(float(dur), 3), "steady": bool(steady),
        "windowDriftMs90": round(drift_ms, 1),
        "windowBpmMin": round(float(wt.min()), 1), "windowBpmMax": round(float(wt.max()), 1),
        "halfBeatPhaseRatio": round(ambiguity, 2),
        "loopBeats": round((dur - first) / period, 2),
    }
    # downbeat guess (4/4): kick energy folded mod 4. Informational only.
    grid = first + period * np.arange(nb)
    fr = np.clip(librosa.time_to_frames(grid, sr=SR, hop_length=HOP), 0, len(kenv) - 1)
    fold = np.array([kenv[fr][i::4].mean() for i in range(4)])
    out["downbeatGuess"] = int(np.argmax(fold))
    out["downbeatRatio"] = round(float(np.sort(fold)[-1] / (np.sort(fold)[-2] + 1e-9)), 2)
    out["beats"] = []
    if not steady:
        # explicit list: tracker beats smoothed by a low-order polynomial in beat index
        _, bt = librosa.beat.beat_track(onset_envelope=env, sr=SR, hop_length=HOP,
                                        start_bpm=float(wt[0]), tightness=400, units="time")
        bt = np.array(bt)
        k = np.arange(len(bt))
        keep = np.abs(((bt - first + half / 2) % period) - half / 2) < 0.06
        coef = np.polyfit(k[keep], bt[keep], 4)
        res = bt[keep] - np.polyval(coef, k[keep])
        smooth = np.polyval(coef, np.arange(0, int(dur / 0.38)))
        smooth = smooth[(smooth >= 0) & (smooth < dur)]
        out["beatFitResidualMs"] = round(float(np.median(np.abs(res)) * 1000), 1)
        out["beats"] = [round(float(x), 4) for x in smooth]
    return out


def arrangement(name, beatmap):
    """Beat-indexed musical controls, independent of combat timers.

    Energy is robust-normalized RMS; bass is low-band RMS; accent is spectral
    onset strength. Section labels are heuristic, not hand-authored song form.
    Eight-beat blocks avoid frame-to-frame state flicker; a preceding rise marks
    a build and a strong energy/bass return marks a drop.
    """
    y = load(os.path.join(MUSIC, "Boss_%s.mp3" % name))
    rms = librosa.feature.rms(y=y, hop_length=HOP)[0]
    low = sosfilt(butter(4, 200, "low", fs=SR, output="sos"), y)
    bass = librosa.feature.rms(y=low, hop_length=HOP)[0]
    onset = librosa.onset.onset_strength(y=y, sr=SR, hop_length=HOP)
    times = np.arange(len(rms)) * HOP / SR
    grid = (np.asarray(beatmap["beats"]) if not beatmap["steady"] else
            np.arange(beatmap["firstBeat"], beatmap["duration"], 60 / beatmap["bpm"]))
    def perbeat(signal):
        result = []
        for i, t in enumerate(grid):
            end = grid[i + 1] if i + 1 < len(grid) else beatmap["duration"]
            chunk = signal[(times[:len(signal)] >= t) & (times[:len(signal)] < end)]
            result.append(float(np.mean(chunk)) if len(chunk) else 0.)
        result = np.asarray(result)
        lo, hi = np.percentile(result, [10, 90])
        return np.clip((result - lo) / max(hi - lo, 1e-8), 0, 1)
    energy, low_energy, accents = perbeat(rms), perbeat(bass), perbeat(onset)
    sections = []
    blocks = [(i, float(np.mean(energy[i:i+8])), float(np.mean(low_energy[i:i+8])))
              for i in range(0, len(grid), 8)]
    for j, (i, e, b) in enumerate(blocks):
        prev = blocks[j-1][1] if j else e
        following = blocks[j+1][1] if j+1 < len(blocks) else e
        if e < .28: kind = "breakdown"
        elif e - prev > .18 and b > .45: kind = "drop"
        elif following - e > .14 or (e - prev > .08 and e < .7): kind = "build"
        elif e > .65: kind = "peak"
        else: kind = "drive"
        if not sections or sections[-1]["kind"] != kind:
            sections.append({"beat": i, "kind": kind, "energy": round(e, 3)})
    return {"energy": np.round(energy, 3).tolist(), "bass": np.round(low_energy, 3).tolist(),
            "accents": np.round(accents, 3).tolist(), "sections": sections,
            "analysisVersion": 1, "sourceSha256": hashlib.sha256(open(os.path.join(MUSIC, "Boss_%s.mp3" % name), "rb").read()).hexdigest()}


def main():
    features_only = "--arrangement-only" in sys.argv
    names = [n for n in sys.argv[1:] if n != "--arrangement-only"] or BOSSES
    # the maps already in the file: the ones not named are kept as they are, the named ones keep their ear-checked
    # half-beat phase and bar line through the re-analysis (see analyse)
    old = json.load(open(OUT))["bosses"] if os.path.exists(OUT) else []
    keep = {b["name"]: b for b in old}
    for n in names:
        r = keep[n] if features_only else {"name": n, "clip": "WorldMusic/Boss_%s" % n, **analyse(n, keep.get(n))}
        if features_only:
            r["clipLead"] = clip_lead(os.path.join(MUSIC, "Boss_%s.mp3" % n))
        r.update(arrangement(n, r))
        keep[n] = r
        print(n, {k: v for k, v in r.items() if k not in ("beats", "energy", "bass", "accents", "sections")})
    data = {"bosses": [keep[n] for n in BOSSES if n in keep]}
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    json.dump(data, open(OUT, "w"), indent=2)


if __name__ == "__main__":
    main()
