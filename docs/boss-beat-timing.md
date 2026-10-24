# Boss music and bullet timing

This records the earlier beat-sync repair. Live mapped boss songs now use the continuous [musical director](boss-music-director.md), which replaces the timed rotation described below.

## Song connections

`WorldMusic.BeginBoss` selects `WorldMusic/Boss_<world>`, starts the song at the entrance, and attaches its row from `Resources/BossBeatMaps.json`. All seven bosses have distinct songs and maps.

| Boss | Song resource | BPM | Map | Decoded duration |
| --- | --- | ---: | --- | ---: |
| Void Archon | `WorldMusic/Boss_Space` | 150.005 | Steady | 137.200 s |
| Hoarfrost Leviathan | `WorldMusic/Boss_Frost` | 150.005 | Steady | 159.720 s |
| Bloom Queen | `WorldMusic/Boss_Verdant` | 148.3 nominal (147 → 148.7) | Explicit drifting beat list | 207.476 s |
| Cinder Drake | `WorldMusic/Boss_Ember` | 150.025 | Steady | 92.640 s |
| Iron Kraken | `WorldMusic/Boss_Tide` | 146.000 | Steady | 165.000 s |
| Tempest Engine | `WorldMusic/Boss_Storm` | 150.015 | Steady | 119.880 s |
| Dune-Eater | `WorldMusic/Boss_Desert` | 146.030 nominal | Explicit drifting beat list | 184.800 s |

Audio audit: decoded all seven MP3s with ffmpeg at 22,050 Hz and compared mapped beats with the mapper's librosa onset-strength envelope (256-sample hop). Best phase adjustments were Space +4 ms, Frost -2 ms, Verdant/Ember/Tide/Storm 0 ms, Desert +2 ms. Decoded durations matched the baked maps. This verifies consistency with the detector; it is not a listening-based verification of bar accents. The maps still assume downbeat index zero; `downbeatGuess` remains informational.

**Verdant: "jungle boss 3".** The Bloom Queen's song was replaced with the user's "jungle boss 3" (a 48 kHz stereo 16-bit WAV, 3:29.6). The source was encoded like the other boss songs (LAME CBR 256 kb/s, 48 kHz, stereo; the `.meta` / GUID unchanged) with three fixes: the 0.08 s of leading silence trimmed so beat 0 sits 0.026 s into the clip; the end trimmed at the last bar line before its 2 s fade-out (207.555 s of the source), so the clip is exactly 128 bars and `AudioSource.loop` wraps on a bar line (the seam has the same 0.03 s lead at both ends); and +1.7 dB of gain, from −16.9 to −15.2 LUFS integrated (the other six are −14.7…−15.4), true peak −1.4 dBFS, no clipping in the source (peak −2.9 dBFS). The tempo is not steady: it ramps from ~147 bpm in the intro to 148.7 bpm from ~105 s on (per-window 2-D comb scans, 12 s windows), so the map uses the explicit `beats` list like Desert: local tempos fitted with a cubic in time and integrated into beat times, the phase chosen by the comb optimum over that variable grid, the half-beat by the kick band (0.164 vs 0.144); per 15 s window the onset energy sits within ±6 ms of the grid in both bands. Through the per-band flux estimator used for the Space fix, kicks sit at −4 ms, mids −34 ms and highs −35 ms from the grid (the ear-checked maps: −6…−12 / −36…−40 / −42…−44). The bar line: the 14 biggest per-beat energy rises of the arrangement land on beats ≡ 0 mod 4 in 10 cases (bass: 7, with 6 on beat 3, the pickup before them) and on beats ≡ 4 mod 16 in 8, and the kick-band fold peaks at beat 0 mod 8 (mod 4 is a near tie), so `downbeatBeat` is 0: the first bar is a pickup before the phrase line at bar 1, and the loop seam lands on a bar line one bar before a phrase line at both ends.

**Space half-beat fix.** That audit measured the full band, and Boss_Space's snare / stab layer sits on the off-beats: the analyzer's full-band comb had put the map half a beat late (`firstBeat` .228) and the shots landed between the kicks, which was audible in the sparse first 30 s. Measured per band against the beat grid with one estimator on every map (spectral flux, 1024-sample window, 64-sample hop; the ear-checked maps calibrate the estimator's own latency): Frost / Ember / Tide / Storm kicks sit at −6…−12 ms and hats at −40…−44 ms from their grids; Space at .228 had its kicks at +194 ms (0.49 beat) and hats at +161 ms, and at .028 kicks −7 ms and hats −40 ms — the same signature as the others. `firstBeat` is now .028; the kick energy folded on that grid peaks at beat 0 mod 4 / 8 / 16 (ratio 1.15 / 1.29 / 1.42), so `downbeatBeat` stays 0 (bar lines 0.2 s earlier than before, the same bar numbering; the drops at beats 112 and 272 keep their numbers). The per-beat energy / bass / accents and the sections were regenerated on the new grid with a numpy re-implementation of the analyzer's arrangement step that reproduces its output to rounding on the unchanged maps (Frost: max difference 0.001, identical sections). Storm (`firstBeat` .008) measured kicks −11 ms / hats −42 ms: on its grid, unchanged. `scripts/tools/boss_beatmap.py` now lets the kick band decide the half-beat phase and keeps an existing map's phase and `downbeatBeat` through a re-analysis, so a regeneration does not revert this.

Unity imports these clips with an additional 48–64 ms of padding. The clock now uses `AudioClip.length` for the actual loop boundary while retaining the map's beat positions.

**Clip lead and output latency (Oct 24).** The owner heard the Void Archon off the beat through the first ~40 s of its fight (the song's sparse intro, beats 12–112, before the drop at 44.8 s). An end-to-end audit found the scheduling itself exact: `BossMusicDirector` fires every wave and plans every tell on a sixteenth of `BeatClock.BeatNow` (one current subdivision, no backlog, no free-running timer), the waves' flights are whole beats, the specials land on bar lines, and the Space map's grid holds through the intro (the per-bar bass hits at beats 32 / 40 / 48 / 56 / 64 sit within ±40 ms of the grid's bar lines; the stabs between them are the song's own off-beat layer). What was off was the clock's idea of *when the music is heard*, by a constant that only the sparse intro exposes — one lone hit a bar to judge a wave by, where the drop's sixteenth-note texture hides it:

- **Clip lead.** The maps are measured on ffmpeg's decode, which drops the MP3's declared priming (LAME header: encoder delay 576 + decoder delay 529 = 1105 samples; `ffprobe` reports it as `start_time 0.023`) and skips the Xing / Info header frame. Unity's importer decodes everything, the header frame included, as 1152 samples of silence: each imported clip is exactly `(frames + 1) × 1152 / 48000` s long (Space: 5718 header-counted frames, clip 137.256 s; the "48–64 ms of padding" noted above). So in `AudioSource.time` every beat sits 2257 samples = 47 ms later than the map said, and the clock ran 47 ms early. Each map now carries `clipLead` (0.047021); `BeatMap.BeatAt` / `TimeOfBeat` / `SecondsPerBeatAt` take and return AudioSource time and apply it once, for every reader. `boss_beatmap.py` writes it from `ffprobe` plus the header frame. (That the extra silent frame sits at the start, not the end, follows from how a plain decoder walks the stream; it is the one part of this not measured directly — a device's `beat_latency_ms` can still trim it.)
- **Output latency.** `AudioSettings.dspTime` and `AudioSource.timeSamples` are the mixer's clock; what the mixer renders leaves the speaker one DSP ring buffer later (`bufferLength × numBuffers / outputSampleRate`: 43–85 ms on phones). `BeatCalibration` defaulted to 0 and nothing in the game ever set it, so the whole fight fired that much early as well. With nothing saved the knob is now the mixer's own latency (`BeatCalibration.DspBufferLatency`, overridable through `OutputLatencyProbe` in tests); a saved calibration still replaces it. The hardware's tail (an Android HAL buffer) is still invisible from here and remains the calibration's job.
- `BossActor.StepArrival` ticks `BossBeat` through the entrance, so the clock's anchor is settled on the fight's first frame instead of nudging toward the source over its first second.

Together the two constants were about 130 ms (a third of a beat at 150 bpm — more than a sixteenth) of early fire on a phone. `BossBeatStartTest` parses every boss MP3's LAME header against its map's `clipLead` and the imported clip's length, checks the knob's default, and runs the Void Archon's real actor through an entrance and the first 60 s of its fight on a simulated clock (21 ms audio buffers, 85 ms of output latency): every wave leaves within a frame plus a buffer after the *heard* grid's sixteenth it was scheduled for (measured against `LastScheduledBeat`, not the nearest sixteenth: a clock a whole sixteenth ahead would otherwise look a few ms off), every tell starts on one, and the same run on the old clock (no lead, latency 0) reproduces the complaint — every wave about 95–130 ms early. `BossWaveTest`'s ricochet check no longer counts a big wave whose picks the fairness rule vetoed as "all or none": the grid's new phase against the frame clock surfaced two such waves in Frost's drift run.

## Intended behavior

The song continues while the player pauses. The actor ticks the audio clock even when gameplay `dt` is zero. A pattern's tell finishes on the next eligible bar/beat. Its volleys then follow the live song position, including Desert's changing tempo. Bullet travel and pattern duration use gameplay time, so they freeze with gameplay. Synchronization controls **emission**, not the later instant a bullet reaches the ship.

Lifting and resuming should skip missed volleys and wait for the next scheduled musical subdivision. A pause during a tell should select a new beat after the minimum gameplay tell has elapsed.

Only `BossAttackKind.Pattern` uses this scheduler. Legacy lasers and themed hazards retain their existing timers. Music absent/stopped or beat sync disabled uses the existing timer fallback.

## Faults and fixes

- An overdue volley was allowed to fire up to one whole beat late, so a short pause emitted between beats. A stream that had never emitted bypassed the lateness guard entirely and could dump its backlog. Allow only one rendered frame of lateness, using unscaled frame time, and skip expired events even before a stream's first shot.
- The actor selected a beat, entered its Pattern phase, then waited another frame before stepping the runner. The runner derived a new origin from that later frame. At 30 FPS this could move the whole pattern to another sixteenth. Pass the chosen beat into the runner and emit on the transition frame.
- A tell's target could expire while the song continued during a pause or slow motion. Re-select the next beat when the target is more than a frame old. Beat alignment no longer selects a beat before the tell ends.
- Loop rounding ignored the negative beat position before the first beat. Verdant could move backward at the seam. Round the complete beat span into whole bars, and wrap at the imported clip's actual length.

## Increased density

Each boss now uses `beatsScale = 0.5`: twice the requested volley cadence before musical subdivision rounding. Streams remain quantized to the beat grid and cannot become faster than a sixteenth. Existing bullet counts per volley, speeds, gaps, and telegraph times continue to define each pattern. Mines still place a single field. The full bounded projectile pool is warmed during the entrance because the older timer-only density measurement understates song-synced demand.

The regression simulation compares the old and new beat tuning over two complete enraged rotations for each boss, including cooldowns, tells, bullet travel, and bomb bursts. It also checks center/left/right player positions against the projectile cap. This is a capacity check, not a proof of human dodgeability; increased density needs gameplay review.

## Validation

`BeatSyncTest` covers short and long pauses, delayed streams, real actor starts at 30/60/120 FPS with and without pauses during tells, all seven loop seams, imported audio padding, density increases, and pool capacity. Existing map, jitter, pitch, fallback, and allocation checks remain.

Run:

```sh
scripts/unity-batch.sh -executeMethod AllTests.RunSuites \
  -suites BossPatternTest,BeatSyncTest,BossLadderTest,SpaceBossMusicTest
```

Before the fix, the new runner pause and Verdant seam regressions failed. A pre-existing process-wide allocation check also fluctuated in isolated runs; it passed in the combined validation run.
