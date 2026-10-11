# Codex prompts: Tide attack art + Tide water explosion

Status 2026-10-11 (branch feature/gaps-cleanup): the Tide roster and boss themed attacks are wired procedurally (Lantern Angler thunder lure = `Strike`,
Hammerhead surf wave = `Wave`, Iron Kraken thunder strike + surf wave via `BossDef.themedAttacks`). Nothing below is delivered yet, so every piece is drawn by
the procedural fallbacks (`AttackHazardArt`, `ShotMotionArt`, `EliteFxArt`). These are the jobs that replace them. **Nothing here has been launched.**

## How to launch (one job at a time; max 2 `codex exec` machine-wide: check `pgrep -fl "codex exec"` first)

```
cd /Users/sina/Developer/Pause
git worktree add .claude/worktrees/codex-tide-attacks -b art/tide-attacks master        # once
cd .claude/worktrees/codex-tide-attacks
# assemble: preamble (docs/world-attacks-codex-prompts.md section 2, with the Tide slots below substituted) + the job's TASK + the verification list (section 5 there)
~/.local/bin/codex exec -C "$PWD" -s workspace-write -c model_reasoning_effort='"high"' \
    -i <abs ref png> [-i <abs ref png> ...] -o /abs/scratch/tide_<job>_last.txt - < /abs/scratch/tide_<job>.md > /abs/scratch/tide_<job>.log 2>&1
```

After a job: `python3 scripts/verify_attack_art.py <OUT> --sheet out.png --backdrops <a Tide mid tile>`, open every PNG for the user, **install nothing until the user has seen it**.
Pink on enemy art is rejected (user, 2026-10-11) but these are *hostile attacks*, where the pink cue is the fairness contract (PC1-PC5): keep it here.

## Tide slot values (substitute into the common preamble)

```
<WORLD>=Tide  <w>=tide  <OUT>=./Pause/Assets/Art/Attacks/Tide~/
<MATERIAL>=pressurised sea water, foam, brine, barnacled brass, pearl, lightning
<BODY_RAMP>=sea-foam white (#F0FFFA), pale teal (hue 165-190, sat <= .4: #BFEFE4, #8FD0C4), deep teal-black (#0E2A30), brass #8A6A2E, ink outline #0B0B1A
<PINK>=pink-white pressure cores, pink foam crests, pearl nacre: #FFE0F8 / #FF8AE6 / #FF4FD8
<AVOID>=shield cyan 178 (water is WHITE-teal, never a saturated cyan), repair green 82, violet 259, amber 37, any red
<REFS>=Pause/Assets/Art/Resources/Bosses/Tide_shots.png, Pause/Assets/Art/Resources/Bosses/Frost_shots.png, one Tide mid backdrop tile, Pause/Assets/Art/Attacks/Tide~/tide_attack_laser.png (the delivered Tide boss kit: match its water language)
```

## Queue (ordered; the first three change the most on screen)

### T1. Strike atlas (Lantern Angler thunder lure, Kraken thunder strike) -- `tide_attack_strike.png`

Prompt = preamble + design `P3` with `<STRIKE_BODY>` = *THUNDER: a vertical jagged lightning bolt, blue-white glow with a pink-white core line, branching twigs*; glyph =
*a ripple / wave ring* motif; burst = a short ground flash with spray. 768 x 512, geometry exactly as `docs/world-attacks-art.md` 3.3. Loaded by `AttackArt.StrikeBody/StrikeGlyph/StrikeBurst(4, ..)`
(a Tide strike picks it up the moment the file is in `Resources/Attacks/Tide/`; no code).

### T2. Wave atlas (Hammerhead surf wave, Kraken surf wave) -- `tide_attack_wave.png`

Prompt = preamble + design `P5` verbatim (512 x 480, four seamless 512 x 96 bodies, then capL, capR, gapMarker a,b at 96 x 96). `AttackArt.WaveBody/WaveCapL/WaveCapR/WaveGapMarker(4, ..)`.

### T3. Shots atlas (water gun, pearl fan, depth charge, ink, net) -- `tide_attack_shots.png`

Prompt = preamble + design `P1` with `<SHOTS_TABLE>`:

```
 bolt  = SPOUT DROP: a teardrop of white water pointing down, a pink-white core, three spray dots.
 shard = PEARL: a scalloped clam cup holding a pink pearl, two bubbles (NOT a smooth disc: a spiked or scalloped pink rim).
 shell = DEPTH CHARGE: a horned sea mine inside a pink-rimmed spiky bubble.
 slag  = INK BLOB: black-teal ink with a pink rim.
 pool  = NET: a knotted mesh pool, pink knots, 2 beats.
 sigA  = INK POOL: a flat spreading ink stain, pink rim.   sigB = reserved.   sigC = BUBBLE RING: a ring of spiked pink-rimmed bubbles.
```

Wiring afterwards (code, not Codex): `ShotSkins.Enable(4)` in `EnableDeliveredWorlds`, `ShotMotionArt.Painted` for Tide if a motion body is wanted, then `ShotSkinTest` / `HostileProjectileTest` / `ReadabilitySweep`.
**Do not call `ShotSkins.Enable(4)` before this file exists.**

### T4. Jet atlas (limpet mine pressure jet / any Tide `Jet`) -- `tide_attack_jet.png`

Design `P4`, Tide line: 768 x 448; row 0 six frames 128 x 320 = a PRESSURE JET column .5 u wide, 2.5 u long, white-water body with a pink-white core line, spray beads and a foam end, seamless 12 fps loop;
row 1 nozzle x3 (the flare growing through the tell) + splash x3. `AttackArt.JetBody/JetNozzle/JetTip(4, ..)`.

### T5. FX atlas -- `tide_attack_fx.png`

Design `P2`: impact 1-4, special 1-4 = *splash crown: a ring of water droplets and spray, pink-white in frames 1-2*, trail 1-4 = *bubbles and foam flecks*, glyph a,b = *a ripple / wave ring*, flash a,b.

### T6. Beam atlas (limpet mine laser skin) -- `tide_attack_beam.png`

Design `P7`: `<BEAM_AIM>` = *a dotted white-water line*, `<BEAM_BODY>` = *a WHITE-WATER JET: spray beads, pink-white core line, foam edges*. `AttackArt.Beam(4)` is what `MineLaserArt` prefers.

### T7. Lash atlas (Kraken tentacle arcs, Tide elites) -- `tide_attack_lash.png`

Design `P8`: `<LASH_BODY>` = *a barnacled brass-and-steel TENTACLE section*, `<LASH_TIP>` = *a pink-cored sucker pad*, `<LASH_ROOT>` = *a hatch collar*; dash a,b = dotted arc preview.

### T8. Tell and end frames (U7 of `docs/ultimates-codex-prompts.md`, Tide half)

Prompt = ultimates preamble + U7 for Tide only:

```
TASK: (a) tide_attack_strike_fx.png, exactly 768 x 256 RGBA (two rows of six 128 x 128 cells): row 0 TELL 1-6 (the lane marker building over 1 s at 12 fps: the dotted ripple ring closes, water motes
and static gather, the last frame flashes white-pink); row 1 END 1-4 (the column's impact dissolving: spray, steam, afterimage of the bolt) + 2 reserved.
(b) tide_attack_jet_fx.png, exactly 768 x 256 RGBA: row 0 NOZZLE 1-6 (the muzzle flare growing across the 0.9-1 s tell), row 1 END 1-4 (the jet tearing off at the nozzle and shredding to spray) + 2 reserved.
```

## Tide water explosion (Explosions.png has no Water row)

`Pause/Assets/Art/Resources/Weapons/Explosions.png` is 2048 x 768 = 16 columns x 6 rows of 128 (metal, rock, mine, ice, spore, magma); `TargetExplosion.KindForWorld("tide")` returns `Kind.Ice` as a
stand-in. A seventh row needs one painted-key candidate sheet (the same method as the other six, `Art/Weapons/ExplosionsV2~/src~/PROMPTS.md`), the row builder (`build_explosions.py`: add `"water"` to `ROWS`,
`PALETTES`, `GLOW`, `SELECT`), and the matching code change (`TargetExplosion.Kind.Water = 6`, `WeaponArt.ExplosionKinds = 7`, `ExplosionCell` uses 7 rows, `KindForWorld("tide") -> Water`,
`ExplosionV2Test` row count). The atlas becomes 2048 x 896.

Codex prompt (image generation, then `build_explosions.py` keeps the staged-frame pipeline; one job, `-i` = `ice_key_candidates.png`, `magma_key_candidates.png`, `candidate_contact.png`):

```
Use case: stylized-concept. Asset type: painted keyframe source for a small top-down 2D shooter pixel-art explosion animation. Make a precise 2 by 2 grid of FOUR separate isolated effects with roomy empty
gutters and no dividing lines: top left = compact initial peak burst; top right = different peak candidate; bottom left = dispersed material debris after the core cools; bottom right = different debris
candidate. Each effect circular and undirected, centered in its quadrant, occupies no more than half its quadrant width. Transparent background with true alpha. Professional realistic-leaning neon pixel art:
tiny hand-placed square pixels, richly faceted material, hard luminous pixel core and stepped translucent halo, subtle vapor. Painted, tactile, intricate, like high-quality enemy death sprites.
Restrained luminosity, small white-hot point only. NO comic-book burst, NO jagged star polygon, NO black outline around FX, NO radial spokes, NO thick ring, NO text, NO UI, NO checkerboard.
Material variant: water. white-teal pressurised sea-water burst with a bright white core, a crown of thrown droplets and spray, pale foam clumps, scattered barnacled-brass and dark steel fragments, a few
pink-white pearl sparks; no red; no lava, ice crystals or leaves. Colour separation and debris texture should make this material immediately recognizable.
```

Palette for the row (sea-foam, not saturated cyan, like Tide's other art): `#0B1E28 #123642 #1E5560 #2F7C82 #55A9A5 #8FD0C4 #BFEFE4 #E8FFF8` + brass `#463A22 #8A6A2E` + pearl `#FF8AE6 #FFE0F8`; glow `#8FD0C4`.
Acceptance: `ExplosionV2Test` (frame counts, silhouettes, no red, per-row distinct) with the new row; the burst must read over the Tide backdrops (`ExplosionPreview`).
