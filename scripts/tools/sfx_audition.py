#!/usr/bin/env python3
"""sfx_audition.py AUDIO_DIR OUT_DIR -- estimate Vorbis(0.6)/ADPCM sizes for all death/gate/shockwave wavs and
write before/after WAV pairs (after = encode then decode) for a representative set.
Policy mirrors EnemyDeathAudioImporter: clips <= 0.5 s -> ADPCM, longer -> Vorbis quality 0.6.
Vorbis here is libvorbis via libsndfile (Unity uses the same codec family; bitrates differ slightly)."""
import sys, os, io, glob
import numpy as np, soundfile as sf
root, out = sys.argv[1:3]
os.makedirs(out, exist_ok=True)
files = sorted(glob.glob(root + "/EnemyDeath/*.wav") + glob.glob(root + "/Gate/*.wav") + glob.glob(root + "/Shockwave/*.wav"))
def enc(path):
    x, sr = sf.read(path, dtype="int16")
    dur = len(x) / sr
    b = io.BytesIO()
    if dur <= 0.5:
        sf.write(b, x, sr, format="WAV", subtype="IMA_ADPCM"); kind = "adpcm"
    else:
        sf.write(b, x, sr, format="OGG", subtype="VORBIS", compression_level=0.6); kind = "vorbis"
    b.seek(0); y, _ = sf.read(b, dtype="int16")
    return kind, len(b.getvalue()), x, y, sr, dur
tot0 = tot1 = 0; kinds = {"adpcm": [0, 0, 0], "vorbis": [0, 0, 0]}
cache = {}
for f in files:
    kind, n, x, y, sr, dur = enc(f)
    s0 = os.path.getsize(f); tot0 += s0; tot1 += n
    k = kinds[kind]; k[0] += 1; k[1] += s0; k[2] += n
    cache[os.path.basename(f)] = (kind, n, s0, x, y, sr, dur)
print("files", len(files), "pcm", tot0, "encoded", tot1)
print(kinds)
pick = ["boss_ember_0", "boss_tide_0", "boss_frost_scream_0", "boss_verdant_scream_0", "ember_alien_scream_0", "space_elite_orbit_reaver_scream_0",
        "ember_fighter_1_0", "frost_mine_0", "space_rock_crater_0", "gate_smash_0", "gate_crack_0", "shield_release_0"]
for p in pick:
    k = p + ".wav"
    if k not in cache:
        c = [n for n in cache if n.startswith(p.rsplit("_", 1)[0])]
        if not c: print("missing", p); continue
        k = c[0]
    kind, n, s0, x, y, sr, dur = cache[k]
    b = k[:-4]
    sf.write(f"{out}/{b}__before_pcm.wav", x, sr, subtype="PCM_16")
    sf.write(f"{out}/{b}__after_{kind}.wav", y, sr, subtype="PCM_16")
    print(b, kind, f"{dur:.2f}s", s0, "->", n)
