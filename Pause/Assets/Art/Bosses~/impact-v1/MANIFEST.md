# Boss impact v1

All PNGs have straight alpha, hard pixel edges, and no pink. Keep Point filtering in Unity.

## Job 1

- `crack_overlays/crack_01.png` through `crack_04.png`: 1080 × 2400 RGBA, one full-screen overlay each; fade over approximately 0.5 s.
- `crack_overlays/crack_growth.png`: 4096 × 512 RGBA, 8 horizontal cells of 512 × 512, stages 0–7.
- `boss_hit_burst.png`: 4608 × 384 RGBA, 12 horizontal cells of 384 × 384, frames 0–11.
- `boss_hit_burst_small.png`: 2304 × 192 RGBA, 12 horizontal cells of 192 × 192, frames 0–11.
- `contact_job1.png`: downscaled review sheet.

Pass 2: The four overlays use distinct impact points, 6–9 px jagged primary fractures with dark rims and 1–3 px white cores, branching hairlines, broken concentric rings, and highlighted glass chips. The growth strip spreads and thickens across all eight cells. Both burst strips use a white-hot center, yellow and orange flame spikes, angular sparks and debris, an expanding shock ring, and fading dark smoke. `python3 build.py` now regenerates Job 1 only; Job 2 outputs remain as delivered.

## Job 2

The shipped atlases in `Pause/Assets/Art/Resources/Bosses` each have four damage stages (1–4), two idle cells per stage. Stage 0 is the pristine body atlas. Every attack output below is a standalone 384 × 384 RGBA frame. `build.py` transfers registered damage from the stage's first idle cell onto each pristine attack frame, preserving attack alpha and pose, with bright fracture marks copied pixel exactly. These are derived previews and need an in-game frame-selection change to fix the bug: `BossActor.SetFrame` currently selects damaged art only for idle frames.

| Boss | Existing attack/telegraph frames | Damage stages | Gap filled |
| --- | --- | --- | --- |
| Space | tell0_0 (body 26), tell0_1 (body 27), tell0_2 (body 28), tell1_0 (body 29), tell1_1 (body 30), tell1_2 (body 31), tell2_0 (body 32), tell2_1 (body 33), tell2_2 (body 34) | 0 pristine, 1–4 damaged | `attack_damage/space_<attack>_stage<N>.png` for every listed attack and N=1–4 (36 files) |
| Frost | tell0_0 (body 5), tell0_1 (body 6), tell1_0 (body 7), tell1_1 (body 8), tell2_0 (body 10), tell2_1 (body 11), fire (body 9) | 0 pristine, 1–4 damaged | `attack_damage/frost_<attack>_stage<N>.png` for every listed attack and N=1–4 (28 files) |
| Verdant | tell0_0 (body 5), tell0_1 (body 6), tell1_0 (body 7), tell1_1 (body 8), tell2_0 (body 10), tell2_1 (body 11), fire (body 9) | 0 pristine, 1–4 damaged | `attack_damage/verdant_<attack>_stage<N>.png` for every listed attack and N=1–4 (28 files) |
| Ember | tell0_0 (body 5), tell0_1 (body 6), tell1_0 (body 7), tell1_1 (body 8), tell2_0 (body 10), tell2_1 (body 11), fire (body 9) | 0 pristine, 1–4 damaged | `attack_damage/ember_<attack>_stage<N>.png` for every listed attack and N=1–4 (28 files) |
| Tide | tell0_0 (body 26), tell0_1 (body 27), tell0_2 (body 28), tell1_0 (body 29), tell1_1 (body 30), tell1_2 (body 31), tell2_0 (body 32), tell2_1 (body 33), tell2_2 (body 34) | 0 pristine, 1–4 damaged | `attack_damage/tide_<attack>_stage<N>.png` for every listed attack and N=1–4 (36 files) |
| Storm | tell0_0 (body 5), tell0_1 (body 6), tell1_0 (body 7), tell1_1 (body 8), tell2_0 (body 10), tell2_1 (body 11), fire (body 9) | 0 pristine, 1–4 damaged | `attack_damage/storm_<attack>_stage<N>.png` for every listed attack and N=1–4 (28 files) |
| Desert | tell0_0 (body 5), tell0_1 (body 6), tell1_0 (body 7), tell1_1 (body 8), tell2_0 (body 10), tell2_1 (body 11), fire (body 9) | 0 pristine, 1–4 damaged | `attack_damage/desert_<attack>_stage<N>.png` for every listed attack and N=1–4 (28 files) |

- `contact_job2.png`: one representative tell pose at stages 1–4 for each boss.
- Source of truth: `BossArt.TellFrame`, `BossArt.FireFrame`, and `BossActor.SetFrame`.
- Limitation: the attack damage PNGs are staged art only; no runtime code or Unity import settings were changed under the requested write boundary. The automated damage transfer should receive an artist pass for pose-specific occlusion before final integration.
