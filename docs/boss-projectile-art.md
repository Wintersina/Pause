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
| chin cannon | Aimed, Bolt, Chin | pink diamond bolt | rail dart | TODO |
| ion arcs | Aimed, Shard, PodL/R | pink heart-shard | ion arc | TODO |
| pulsar spiral | Spiral, Shard, Core | same shard | pulsar ring | TODO |
| pod lasers | beam, painted kit | magenta kit (done earlier) | keep | kept |
| mirror sweep | Spiral, Bolt, PodL/R | same bolt | void orb | TODO |

### Frost: Hoarfrost Leviathan
| Attack | Kind / emitter | Current art | Needs | Status |
| --- | --- | --- | --- | --- |
| icicle spray | Fan, Bolt, Jaw | blue ice spear | ice shard | TODO |
| cold snap | Ring, Shard, Crown | blue crystal cluster | snow-crystal ring | TODO |
| glare beams | beam, painted kit (EyeL/EyeR) | generic ice-white glowing column | CRYO LANCE (priority 5): jagged stacked-crystal ice-spike stream, pale-cyan to deep-blue with white-blue core, frost mist and snow flurry off the edges, drifting ice flakes, rime crystals growing outward; muzzle = frost gathering with cold vapour and forming crystals; impact = ice burst, snow puff, frost ring on the ground; 4-6 frames; no uniform pale keyline | TODO |
| shatter | Aimed, Shard, EyeL/R | same crystal | frost orb | TODO |
| frozen crown | Ring, Shard, Crown | same crystal | (reuses cold snap ring) | TODO |

### Verdant: Bloom Queen
| Attack | Kind / emitter | Current art | Needs | Status |
| --- | --- | --- | --- | --- |
| stinger thorns | Aimed, Bolt, Stinger | green-white seed spike | thorn | TODO |
| vine lash | lash hazard | `verdant_attack_lash.png`, pale outline | clean vine, NO white outline | TODO (priority 3) |
| spore seeds | Seeds, Shard, Petals | green burst | spore cloud / sap glob | TODO |
| trunk toss | roll hazard | `verdant_attack_log.png` | check pale rim | TODO |
| overgrowth / thorn storm | Seeds/Fan, Bolt/Shard | same pair | thorn + sap | TODO |
| acid cannons | beam (kit, used by pods) | lime kit | keep | kept |

### Ember: Cinder Drake
| Attack | Kind / emitter | Current art | Needs | Status |
| --- | --- | --- | --- | --- |
| fire breath | Fan, Bolt, Jaw | orange fireball | mouth flame jet | TODO |
| cinder spit | Aimed, Shard, Furnace | red-orange shard | molten brass slug | TODO |
| magma bombs | Bomb, Shard, Furnace | same shard | ember bomb | TODO |
| brow laser | beam, painted kit, Brow emitter | plain amber beam | long FLAMETHROWER from the jaw (re-map emitter/pose), no rim | TODO (priority 4) |
| eruption columns | strike hazard | themed tint | lava column art | TODO |
| slag storm | Fan/Aimed | same pair | slag | TODO |

### Tide: Iron Kraken
| Attack | Kind / emitter | Current art | Needs | Status |
| --- | --- | --- | --- | --- |
| port / starboard cluster | Fan, Bolt/Shard | teal orb / green spear | pressure-jet water bolt, brass harpoon | TODO |
| tidal wall | Curtain, Shard | teal spear | bubble wall | TODO |
| beak jet | beam, generic | flat teal bar (no kit) | water jet kit | TODO |
| surf wave | wave hazard | themed | painted wave | TODO |
| riptide / maelstrom | Aimed/Fan | same pair | bubble mine | TODO |

### Storm: Tempest Engine
| Attack | Kind / emitter | Current art | Needs | Status |
| --- | --- | --- | --- | --- |
| core arc fan | Fan, Bolt, Core | white lightning ball | arc bolt | TODO |
| crown fan / storm rain | Fan, Rain, Shard, Rods | white-blue spear | hail | TODO |
| coil beams | beam, generic | flat grey bar (no kit) | ELECTRIC beam kit (priority 2) | TODO |
| lightning lattice / cage storm | Lattice, Bolt, Core | same bolt | coil lightning chain | TODO |
| thunderhead | Rain + Aimed + Fan | same pair | mix of the above | TODO |

### Desert: Dune-Eater
| Attack | Kind / emitter | Current art | Needs | Status |
| --- | --- | --- | --- | --- |
| grit fan | Fan, Shard, Maw | sand-brown rock shard | sand spit | TODO |
| dune fan | Fan, Bolt, Boom | same rock | glass shard | TODO |
| buried mines | Mines, Shard, Boom | same | buried glass mine | TODO |
| lens beam | beam, generic | flat bar, no kit | sunlight lens laser kit (priority 1) | BLOCKED: Codex usage limit |
| closing jaws | Pincer, Bolt, Maw | same rock | sand spit | TODO |
| quicksand / maw | Fan/Aimed | same | sand/glass mix | TODO |

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
