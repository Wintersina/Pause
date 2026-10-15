# Boss music and bullet timing

## Song connections

`WorldMusic.BeginBoss` selects `WorldMusic/Boss_<world>`, starts the song at the entrance, and attaches its row from `Resources/BossBeatMaps.json`. All seven bosses have distinct songs and maps.

| Boss | Song resource | BPM | Map | Decoded duration |
| --- | --- | ---: | --- | ---: |
| Void Archon | `WorldMusic/Boss_Space` | 150.005 | Steady | 137.200 s |
| Hoarfrost Leviathan | `WorldMusic/Boss_Frost` | 150.005 | Steady | 159.720 s |
| Bloom Queen | `WorldMusic/Boss_Verdant` | 145.005 | Steady | 238.400 s |
| Cinder Drake | `WorldMusic/Boss_Ember` | 150.025 | Steady | 92.640 s |
| Iron Kraken | `WorldMusic/Boss_Tide` | 146.000 | Steady | 165.000 s |
| Tempest Engine | `WorldMusic/Boss_Storm` | 150.015 | Steady | 119.880 s |
| Dune-Eater | `WorldMusic/Boss_Desert` | 146.030 nominal | Explicit drifting beat list | 184.800 s |

Audio audit: decoded all seven MP3s with ffmpeg at 22,050 Hz and compared mapped beats with the mapper's librosa onset-strength envelope (256-sample hop). Best phase adjustments were Space +4 ms, Frost -2 ms, Verdant/Ember/Tide/Storm 0 ms, Desert +2 ms. Decoded durations matched the baked maps. This verifies consistency with the detector; it is not a listening-based verification of bar accents. The maps still assume downbeat index zero; `downbeatGuess` remains informational.

Unity imports these clips with an additional 48–64 ms of padding. The clock now uses `AudioClip.length` for the actual loop boundary while retaining the map's beat positions.

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
