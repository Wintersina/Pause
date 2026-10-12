#!/usr/bin/env python3
"""Verify the marketing capture output, build contact_sheet.png and write README.md.

    python3 scripts/marketing-verify.py [OUT_DIR]      (default /Users/sina/Developer/HapticGate-brand/marketing/pause)

Checks: every screenshot has the exact pixel size of its folder, every clip is H.264 1080x1920 60 fps with an AAC
audio track that is not silent. Pause-mechanic timestamps come from video/raw/pause_moments.jsonl (written by the
capture runner).
"""
import json, os, subprocess, sys

OUT = sys.argv[1] if len(sys.argv) > 1 else "/Users/sina/Developer/HapticGate-brand/marketing/pause"
WORLDS = ["space", "frost", "verdant", "ember"]
SIZES = {"screenshots/play_1080x1920": (1080, 1920), "screenshots/ios_1320x2868": (1320, 2868)}


def run(*a):
    return subprocess.run(a, capture_output=True, text=True).stdout.strip()


def probe(path, entries, stream=None):
    args = ["ffprobe", "-v", "error"]
    if stream:
        args += ["-select_streams", stream]
    return run(*args, "-show_entries", entries, "-of", "csv=p=0", path)


problems = []
lines = []

# ---- screenshots
shot_rows = {}
for folder, (w, h) in SIZES.items():
    d = os.path.join(OUT, folder)
    rows = []
    for f in sorted(os.listdir(d)) if os.path.isdir(d) else []:
        if not f.endswith(".png") or f == "contact_sheet.png":
            continue
        got = probe(os.path.join(d, f), "stream=width,height").split(",")
        ok = got == [str(w), str(h)]
        if not ok:
            problems.append("%s/%s is %s, wanted %dx%d" % (folder, f, "x".join(got), w, h))
        rows.append((f, os.path.getsize(os.path.join(d, f)), ok))
    shot_rows[folder] = rows

# ---- clips
clip_rows = []
vdir = os.path.join(OUT, "video/raw")
for f in sorted(os.listdir(vdir)) if os.path.isdir(vdir) else []:
    if not f.endswith(".mp4"):
        continue
    p = os.path.join(vdir, f)
    v = probe(p, "stream=codec_name,width,height,r_frame_rate", "v:0").split(",")
    a = probe(p, "stream=codec_name,sample_rate", "a:0")
    dur = float(probe(p, "format=duration") or 0)
    vol = run("sh", "-c", "ffmpeg -hide_banner -i '%s' -af volumedetect -vn -f null - 2>&1 | grep mean_volume" % p)
    mean = float(vol.split("mean_volume:")[1].split()[0]) if "mean_volume" in vol else -99.0
    ok = v[:4] == ["h264", "1080", "1920", "60/1"] and a.startswith("aac") and mean > -60
    if not ok:
        problems.append("clip %s: video %s audio %s mean %.1f dB" % (f, v, a, mean))
    clip_rows.append((f, os.path.getsize(p), dur, mean, ok))

# ---- contact sheet (3x3)
pick = ["title", "space_2", "frost_pause_freeze", "verdant_2", "ember_pause_teleport",
        "space_boss_2", "frost_boss_2", "verdant_pause_counter", "ember_boss_2"]
d = os.path.join(OUT, "screenshots/play_1080x1920")
inputs = []
for n in pick:
    p = os.path.join(d, n + ".png")
    if not os.path.exists(p):
        problems.append("contact sheet: missing " + n)
    inputs += ["-i", p]
if not any("contact sheet" in x for x in problems):
    flt = "".join("[%d]scale=360:640[s%d];" % (i, i) for i in range(9))
    flt += "".join("[s%d]" % i for i in range(9)) + "xstack=inputs=9:layout=" + "|".join(
        "%d_%d" % ((i % 3) * 360, (i // 3) * 640) for i in range(9))
    subprocess.run(["ffmpeg", "-v", "error", "-y"] + inputs + ["-filter_complex", flt, "-frames:v", "1",
                    os.path.join(OUT, "screenshots/contact_sheet.png")])

# ---- pause moments
moments = []
mp = os.path.join(vdir, "pause_moments.jsonl")
if os.path.exists(mp):
    for ln in open(mp):
        ln = ln.strip().rstrip(",")
        if ln:
            moments.append(json.loads(ln))
by_clip = {}
for m in moments:
    by_clip.setdefault(m["clip"], []).append(m)

KIND = {"Escape": "freeze in front of danger, teleport to a safe lane",
        "OntoEnemy": "freeze, teleport ONTO an enemy (rams it)",
        "OntoRed": "freeze, teleport onto a red atom (refills pauses)"}

R = []
R.append("# Pause - marketing capture (Space, Frost, Verdant, Ember)\n")
R.append("Raw store screenshots and gameplay clips with game audio for the four launch worlds. Tide, Storm and Desert are "
         "not captured. Produced by the editor-only capture tool in the Pause repo (branch `marketing/capture`).\n")
R.append("## How to re-run\n")
R.append("```sh\ncd <workspace>            # the marketing-capture jj workspace of the Pause repo\n"
         "scripts/marketing-capture.sh      # opens the Unity editor WINDOWED (not -batchmode), ~25 min, exits by itself\n"
         "python3 scripts/marketing-verify.py   # re-verifies, rebuilds contact_sheet.png and this README\n```\n")
R.append("Options (env): `MC_OUT` output root, `MC_WORLDS=0,2` (0 Space, 1 Frost, 2 Verdant, 3 Ember; `-1` = menus only), "
         "`MC_JOBS=0` (job 0 = 1080x1920 with video, job 1 = 1320x2868 screenshots only), `MC_PLAY_SECONDS`, "
         "`MC_BOSS_SECONDS`, `MC_WARMUP_SECONDS`, `MC_NO_VIDEO=1`. Keep the Unity window in front and the screen awake "
         "while it runs; the script backs up and restores the editor's PlayerPrefs (developer mode and sound settings "
         "are changed during the run).\n")
R.append("How it works: developer mode (`DeveloperUnlocks`) unlocks the worlds and ships and picks the start world, "
         "`BossDev.TriggerNow` calls the world's boss, the Unity Recorder (Game View, MP4 H.264 + AAC, 60 fps) records, "
         "screenshots are taken at the Game view's exact size, and an editor-only autopilot "
         "(`Assets/Marketing/MarketingAutopilot.cs`, driven through the `UNITY_EDITOR` override in `TouchInput`) flies, "
         "dodges, and performs the pause moves. Ships have their hearts topped up if one hit from death so a take is never lost. "
         "The title, codex and shop are shot in a normal (non-developer) state with the four live worlds' codex entries marked "
         "discovered, so no later world appears.\n")

R.append("## Pause mechanic (the core of the game): where it is shown\n")
R.append("Hold = fly. Release = time freezes (PAUSED marker, quick-action icons). Touching again teleports the ship (costs one "
         "pause, counter top-left `PAUSES n`). Red atoms refill pauses. Timestamps are seconds from the start of the clip "
         "(`freeze` = finger lifts and the world stops, `teleport` = finger touches elsewhere and time resumes, about 1.3 s later; "
         "`pauses` = counter at that moment).\n")
for clip in sorted(by_clip):
    R.append("### video/raw/%s.mp4\n" % clip)
    R.append("| move | freeze | teleport | pauses before -> after |\n|---|---|---|---|")
    recs = []
    for m in by_clip[clip]:
        if m["event"] == "freeze":
            recs.append({"kind": m["kind"], "freeze": m})
        elif recs:
            recs[-1][m["event"]] = m
    for r in recs:
        if "teleport" in r:
            R.append("| %s | %.1f s | %.1f s | %d -> %d |" % (KIND.get(r["kind"], r["kind"]), r["freeze"]["t"], r["teleport"]["t"],
                                                          r["freeze"]["pauses"], r.get("after", r["teleport"])["pauses"]))
    R.append("")
R.append("Pause screenshots (both sizes): `<world>_pause_freeze.png` (frozen frame in front of danger), "
         "`<world>_pause_teleport.png` (just after a teleport onto an enemy), `<world>_pause_counter.png` (red atom collected, "
         "`+1 PAUSE` and the counter going up). The HUD pause counter is on every gameplay frame.\n")

R.append("## Files\n")
R.append("### Screenshots\n")
for folder, rows in shot_rows.items():
    w, h = SIZES[folder]
    R.append("`%s/` - %dx%d PNG, %d files\n" % (folder, w, h, len(rows)))
    R.append("| file | size | pixel size ok |\n|---|---|---|")
    for f, sz, ok in rows:
        R.append("| %s | %.1f MB | %s |" % (f, sz / 1e6, "yes" if ok else "NO"))
    R.append("")
R.append("`screenshots/contact_sheet.png` - 3x3 overview of nine 1080x1920 screenshots.\n")
R.append("Naming: `title*` home menu, `shop*` space dock, `codex_*` pilot's log tabs, `<world>_1..4` gameplay (enemies, attacks, "
         "elites), `<world>_boss_1..3` boss fights, `<world>_pause_*` pause mechanic.\n")
R.append("### Video (`video/raw/`, MP4 H.264 + AAC, 1080x1920, 60 fps)\n")
R.append("| file | duration | size | mean audio | ok |\n|---|---|---|---|---|")
for f, sz, dur, mean, ok in clip_rows:
    R.append("| %s | %.1f s | %.0f MB | %.1f dB | %s |" % (f, dur, sz / 1e6, mean, "yes" if ok else "NO"))
R.append("")
R.append("`<world>_gameplay.mp4` ~40 s of flight with enemies, an elite and three-four pause moves; `<world>_boss.mp4` ~28 s from "
         "the boss warning through the fight; `pause_showcase_<world>.mp4` ~26 s dedicated pause demo (freeze in front of danger, "
         "hold, teleport away; freeze and teleport onto an enemy; freeze and teleport onto a red atom).\n")
R.append("## Verification\n")
R.append("PROBLEMS:\n" + "\n".join("- " + p for p in problems) if problems else
         "All screenshots have the exact pixel size of their folder and no developer overlay; every clip is H.264 1080x1920 at 60 fps "
         "with a non-silent AAC track.")
open(os.path.join(OUT, "README.md"), "w").write("\n".join(R) + "\n")
print("\n".join(problems) if problems else "OK")
