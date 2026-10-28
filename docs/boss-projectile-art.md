# Boss projectile art: inventory and per-attack plan (branch `art/boss-projectiles`)

Goal (user, Oct 16): every boss attack gets its own custom art that matches what the attack is; no two attacks (within or across bosses) may read as re-skins. Desert laser first, then Storm electric beam, Verdant vine (no white outline), Ember flamethrower (from the jaw), then the rest. Pink only in Space. Hitboxes, pooling, timings and the dotted telegraph previews (PR #42) do not change.

## 1. How boss attacks are drawn today

| Piece | Where | Format |
| --- | --- | --- |
| Boss shots (every `Aimed/Fan/Ring/Spiral/Rain/Lattice/Seeds/Bomb/Mines/Pincer/Curtain` stream) | `Resources/Bosses/<Key>_shots.png` via `BossArt.Shot` (`BossProjectile` in `BossProjectiles.cs`) | 1024 x 128, 8 cells of 128 px (1 cell = 1 world unit at scale 1) |
| Shot cells | bolt 0..1 (cells 0-1), shard 0..1 (cells 2-3), lane telegraph (4), generic beam 0..1 (5-6), muzzle charge (7) | 2 frames each, 24 fps tick pairs (`ShotTicks`) |
| Shot size / hitbox | bolt drawn `BoltWorldSize` .46 u, hit radius .13; shard drawn .42 u, hit radius .12 (`BossConfig`) | pale rim built from the cells' alpha (`BossArt.ShotRim`) |
| Painted laser kit | `Resources/Attacks/<World>/<w>_attack_laser.png` 1024 x 384 (8 x 3 cells: windup 8, muzzle 4 + fade 4, impact 4 + spark 4), `_laserbody.png` 512 x 256 (4 frames of 128 x 256, root at TOP, tiles), `_lasertell.png` 512 x 128 (sight a,b + lock a,b) | `AttackArt.BossLaser`, `BossBeam`; body drawn so its opaque columns equal the hit width (`bodyOpaquePx`); only Space, Frost, Verdant, Ember have kits |
| Generic beam (Tide, Storm, Desert) | shot-sheet cells 5-6 (flat-colour bar) + cell 7 charge | no painted kit: this is the "plain laser" |
| Vine lash / trunk roll | `Attacks/Verdant/verdant_attack_lash.png` (1024 x 128, 8 cells), `_log.png` | Verdant only; other lash/strike/ring/jet/wave hazards are drawn by `BossThemed`/`BossAttackFx` from world colours (no painted files) |
| Themed shot skins | `ShotSkins`/`AttackArt.ShotCell` (`verdant_attack_shots.png` etc.) are for enemy and elite shots, not boss streams | |

Key finding: a boss stream only chooses between two styles, `Bolt` and `Shard` (`PatternStream.style`), so every stream of a boss (a fan, a rain, a lattice, a bomb) shares ONE bolt and ONE shard sprite pair, and only the colour differs between bosses. That is why the shots "look the same". Per-attack art therefore needs a per-stream art variant (see section 4).

## 2. Per boss, per attack (art status)

Status key: TODO = unchanged current art. Art column is the current look.

### Space: Void Archon (pink allowed)
| Attack | Kind / emitter | Current art | Needs | Status |
| --- | --- | --- | --- | --- |
| chin cannon | Aimed, Bolt, Chin | pink diamond bolt | rail dart | art wired (section 6) |
| ion arcs | Aimed, Shard, PodL/R | pink heart-shard | ion arc | art wired (section 6) |
| pulsar spiral | Spiral, Shard, Core | same shard | pulsar ring | art wired (section 6) |
| pod lasers | beam, painted kit | magenta kit (done earlier) | keep | kept |
| mirror sweep | Spiral, Bolt, PodL/R | same bolt | void orb | art wired (section 6) |

### Frost: Hoarfrost Leviathan
| Attack | Kind / emitter | Current art | Needs | Status |
| --- | --- | --- | --- | --- |
| icicle spray | Fan, Bolt, Jaw | blue ice spear | ice shard | art wired (section 6) |
| cold snap | Ring, Shard, Crown | blue crystal cluster | snow-crystal ring | art wired (section 6) |
| glare beams | beam, painted kit (EyeL/EyeR) | generic ice-white glowing column | CRYO LANCE (priority 5): jagged stacked-crystal ice-spike stream, pale-cyan to deep-blue with white-blue core, frost mist and snow flurry off the edges, drifting ice flakes, rime crystals growing outward; muzzle = frost gathering with cold vapour and forming crystals; impact = ice burst, snow puff, frost ring on the ground; 4-6 frames; no uniform pale keyline | art wired (section 6) |
| shatter | Aimed, Shard, EyeL/R | same crystal | frost orb | art wired (section 6) |
| frozen crown | Ring, Shard, Crown | same crystal | (reuses cold snap ring) | art wired (section 6) |

### Verdant: Bloom Queen
| Attack | Kind / emitter | Current art | Needs | Status |
| --- | --- | --- | --- | --- |
| stinger thorns | Aimed, Bolt, Stinger | green-white seed spike | thorn | art wired (section 6) |
| vine lash | lash hazard | `verdant_attack_lash.png`, pale outline | clean vine, NO white outline | DONE (section 6) |
| spore seeds | Seeds, Shard, Petals | green burst | spore cloud / sap glob | art wired (section 6) |
| trunk toss | roll hazard | `verdant_attack_log.png` | check pale rim | art wired (section 6) |
| overgrowth / thorn storm | Seeds/Fan, Bolt/Shard | same pair | thorn + sap | art wired (section 6) |
| acid cannons | beam (kit, used by pods) | lime kit | keep | kept |

### Ember: Cinder Drake
| Attack | Kind / emitter | Current art | Needs | Status |
| --- | --- | --- | --- | --- |
| fire breath | Fan, Bolt, Jaw | orange fireball | mouth flame jet | art wired (section 6) |
| cinder spit | Aimed, Shard, Furnace | red-orange shard | molten brass slug | art wired (section 6) |
| magma bombs | Bomb, Shard, Furnace | same shard | ember bomb | art wired (section 6) |
| brow laser | beam, painted kit, Brow emitter | plain amber beam | long FLAMETHROWER from the jaw (re-map emitter/pose), no rim | DONE (section 6) |
| eruption columns | strike hazard | themed tint | lava column art | art wired (section 6) |
| slag storm | Fan/Aimed | same pair | slag | art wired (section 6) |

### Tide: Iron Kraken
| Attack | Kind / emitter | Current art | Needs | Status |
| --- | --- | --- | --- | --- |
| port / starboard cluster | Fan, Bolt/Shard | teal orb / green spear | pressure-jet water bolt, brass harpoon | art wired (section 6) |
| tidal wall | Curtain, Shard | teal spear | bubble wall | art wired (section 6) |
| beak jet | beam, generic | flat teal bar (no kit) | water jet kit | art wired (section 6) |
| surf wave | wave hazard | themed | painted wave | art wired (section 6) |
| riptide / maelstrom | Aimed/Fan | same pair | bubble mine | art wired (section 6) |

### Storm: Tempest Engine
| Attack | Kind / emitter | Current art | Needs | Status |
| --- | --- | --- | --- | --- |
| core arc fan | Fan, Bolt, Core | white lightning ball | arc bolt | art wired (section 6) |
| crown fan / storm rain | Fan, Rain, Shard, Rods | white-blue spear | hail | art wired (section 6) |
| coil beams | beam, generic | flat grey bar (no kit) | ELECTRIC beam kit (priority 2) | art wired (section 6) |
| lightning lattice / cage storm | Lattice, Bolt, Core | same bolt | coil lightning chain | art wired (section 6) |
| thunderhead | Rain + Aimed + Fan | same pair | mix of the above | art wired (section 6) |

### Desert: Dune-Eater
| Attack | Kind / emitter | Current art | Needs | Status |
| --- | --- | --- | --- | --- |
| grit fan | Fan, Shard, Maw | sand-brown rock shard | sand spit | art wired (section 6) |
| dune fan | Fan, Bolt, Boom | same rock | glass shard | art wired (section 6) |
| buried mines | Mines, Shard, Boom | same | buried glass mine | art wired (section 6) |
| lens beam | beam, generic | flat bar, no kit | sunlight lens laser kit (priority 1) | DONE (see section 6) |
| closing jaws | Pincer, Bolt, Maw | same rock | sand spit | art wired (section 6) |
| quicksand / maw | Fan/Aimed | same | sand/glass mix | art wired (section 6) |

### Beam kinds (each must read as a different KIND of attack)
Priority order: Desert prismatic sunlight lens laser, Storm electric arc, Verdant vine, Ember jaw flamethrower, Frost cryo lance, then Space void/plasma rail, Tide pressure water jet, then the rest. Hitbox/width logic and the dotted telegraph stay unchanged. Codex resumes at 17:42 CDT.

## 3. Why the shots look the same
1. Two shared sprites per boss for every stream (bolt, shard).
2. Same silhouettes across worlds (vertical spear for bolt, small cluster for shard) with a hue change.
3. Tide, Storm, Desert beams are the same flat vertical bar; only Space/Frost/Verdant/Ember have painted kits and those kits are all "glowing column + round flare".

## 4. Implementation plan
* Per-stream art: add an optional `art` slot to `PatternStream` (0 = today's bolt/shard cells) selecting a variant from a new `<Key>_attackshots.png` (rows of 2-frame cells, each declared Bolt-size or Shard-size so hit radii stay as is); rim built per variant by the same `ShotRim` code.
* Beam kits: extend `AttackArt.LaserBodyOpaque/LaserImpactBase/lasers` and `KeyOf/WorldOfArtKey` from 5 to 7 worlds (Storm, Desert), files `storm_attack_laser*.png`, `desert_attack_laser*.png` in the existing 3-file format; `BossLaserArtTest` gains Kits for Storm and Desert (Tide stays generic until its turn).
* Ember brow laser becomes a jaw flamethrower: re-map emitter to `Jaw`, pose to the breath cell, update `BossEmitterTable` use and the attack-to-pose test.
* Tests: no two bosses share identical projectile sprites; per-attack variants distinct; readability (`ReadabilitySweep shots`), `NoPinkOutsideSpaceTest`, `BossLaserArtTest`.
* Pale-rim audit of all boss attack art (lashes, hazards, jets, waves, rolls, mines, beam tips) with the changes listed in the PR.

## 5. Codex work log
* Oct 16, 11:54 CDT: first Desert beam-body call failed with `usage_limit_reached`; limit resets 17:42 CDT. Prompt kept in the scratchpad (`bp/desert_body.txt`). No art produced yet.

* Oct 16, 18:00-18:52 CDT: after the limit reset the whole queue (32 sheets) ran clean, one image per `codex exec`, no retries needed. Raws are not committed (staged under `Art/BossProjectiles~/`); `assemble.py` (chroma key, premultiplied resize, loop cross-fade, hue-targeted de-pinking) turned them into the shipped files.

## 6. Status: what shipped on this branch

Format: `Resources/Bosses/<Key>_attackshots.png` (256 x 128*N, 2 frames per row), stream field `PatternStream.art` (also `BossWaveStyle.art` and `BossMusicVoices.VoiceArt` for the beat-locked waves and voices). Timing, widths, hit radii (style's Bolt / Shard) and the dotted telegraphs are untouched.

| Boss | Beam kit (laser / laserbody / lasertell) | Projectile rows (art id: look -> used by) |
| --- | --- | --- |
| Space: Void Archon | plasma RAIL: hard-edged mechanical stacked-ring beam, plasma muzzle ring, shockwave impact | 1 rail dart (chin cannon) / 2 ion arc (ion arcs) / 3 pulsar ring (pulsar spiral) / 4 void orb (mirror sweep) |
| Frost: Hoarfrost Leviathan | CRYO LANCE: stacked ice crystals, navy keyline (no pale rim), frost star muzzle with vapour, ice-burst impact | 1 ice shard (icicle spray) / 2 frost orb (shatter) / 3 snow crystal (cold snap, frozen crown) |
| Verdant: Bloom Queen | acid kit kept; NEW vine lash (`verdant_attack_lash.png`), clean painted vine with dark bark edges, no white outline | 1 thorn / 2 spore cloud / 3 sap glob |
| Ember: Cinder Drake | very long FLAMETHROWER from the Jaw (emitter moved Brow -> Jaw, breath pose), pilot flame windup, roaring muzzle, dispersing-fireball impact | 1 molten brass slug / 2 magma bomb / 3 flame jet puff |
| Tide: Iron Kraken | pressure WATER JET: foam-streaked column, spray, splash plume | 1 pressure water bolt / 2 brass harpoon / 3 bubble mine |
| Storm: Tempest Engine | ELECTRIC arc: jagged forking lightning body, per-frame forks, coil flash, ionised ring, ground-strike impact | 1 arc bolt / 2 hail / 3 coil chain |
| Desert: Dune-Eater | prismatic SUNLIGHT LENS beam: white-gold core, thin cyan/mint/gold fringes (no pink), glassy lens flare, heat-haze scorch with sand spray | 1 sand spit / 2 glass shard / 3 buried mine |

Tests: `BossAttackShotArtTest` (plumbing, table ids, file format, animation, no two attacks/bosses share a sprite, readability, pale-rim scan of beam bodies / vine lash / trunk), `BossLaserArtTest` now covers all 7 kits, `NoPinkOutsideSpaceTest` scans the new files.

Known gaps: the bomb burst shards, the themed hazards (surf wave, eruption columns, tidal wall rings) and the free-fall shots of the legacy tables still use the shared pair or procedural drawing; the Tide flare cells have the kraken's nozzle cut away (the boss draws its own beak).

## Verdant attack shots: visibility and calm (2026-10-27)

User: the three Bloom Queen shots (Verdant_attackshots.png: thorn row 1, spore cloud row 2, gourd row 3) were hard to see and "wiggled too much".

**Root cause of the sprite wiggle.** The two frames of every shot were drawn 40-44 px apart inside their 128 px cells (frame a right of centre, frame b left), so the drawing hopped about 0.2 u sideways on every flip (12 times a second). Now both frames are centred on the hit centre and share one silhouette; only the hot core flows down the shot.

**Path / steering wiggle (`VerdantShotTuning`, BossPatternTables.Verdant.cs):**

| knob | old | new |
|---|---|---|
| seeds homing (spore seeds / overgrowth / thorn storm) | 70 / 80 / 90 deg/s | 25 / 28 / 32 deg/s (~36 %) |
| homing steering | hard clamp, whips round the ship | eased over the last 12 deg, stops within 0.9 u of the ship (`BossProjectile.HomeTurn`) |
| fan tilt amp (overgrowth thorns / thorn storm gourds) | 8 / 7 deg | 2.8 / 2.4 deg (~35 %) |
| art flip | 2 ticks (12 swaps/s) | 6 ticks (4 swaps/s) |

Damage, rates, counts, speeds, the roster and the song's wave gap sweep are unchanged. No shot oscillates laterally on a sine; there is no spawn-angle jitter or camera shake on these shots.

**Visibility.** Bright mint-mist backdrops (mean luminance ~.35) only let near-black or near-white pixels hold 3:1, so the regrade (`Art/Resources/Bosses/src~/verdant_attackshots_regrade.py`, from `Verdant_attackshots_orig.png`) paints a near-black body with an amber / lime-yellow / orange core that climbs fast to cream, drawn 1.15-1.3x larger about the centre (hitbox unchanged). No outline, no pink. `VerdantShotVisibilityTest` measures pixels at 3:1 over 4 variants x 6 scroll offsets x 2 frames x 24 placements (worst case: thorn 16-24 -> 137-148 px, spore 208-222 -> 191-200, gourd 223-238 -> 163-176).
