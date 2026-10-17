# World 7 "Desert" (index 6): Phase 0 brief

Branch `feature/desert-world` (worktree `.claude/worktrees/desert-world`). Playbook: `.claude/skills/add-world/SKILL.md`, models: Tide
(`docs/`, merged) and Storm (`docs/storm-world-plan.md` on `feature/storm-world`). World order: Space 0, Frost 1, Verdant 2, Ember 3, Tide 4, Storm 5,
**Desert 6**, then the loop returns to Space (no planned world 8). Release switch `DesertEnabled` stays **false**: Play v1.0 ships 4 worlds, nothing here
reaches master, nothing is flipped. This document is the direction brief (Phase 0); wiring (Phases 9+) comes later.

Working name **Desert** (alternatives for the user: Dune, Sirocco, Mirage). User brief: "get started ASAP with the dunes and giant worms for the background".

## HARD RULE for the wiring phases (user requirement): feature flag OFF, assets IN the bundle

Tide, Storm and Desert are all OFF behind feature flags: `WorldManager.DesertEnabled` defaults **false** and is chained after `StormEnabled` exactly like
`TideEnabled` / `StormEnabled` are chained in `WorldManager.cs` on master (Desert is only reachable when Storm is enabled and Desert's own flag is on;
`LastLiveWorld`, `HasNext`, `PortalDestination` and the final lift-off all read the chain). The production loop stays **Space -> Frost -> Verdant ->
Ember -> Space** until the flags flip. BUT the code and assets are real and ship in the bundle: when a phase wires an asset it is **installed under a
`Resources/` folder** (planetfall under `Backgrounds/Resources/Worlds/Desert/Planetfall/`, backdrop under `.../Desert/Backdrop3/`, worm atlas beside it,
enemies/bosses/elites/rails/audio in their usual Resources folders), never left in `~` staging folders after wiring. `~` folders are only the Codex
staging area before wiring. Unfinished-world tests use `LiveWorldCount`/`LastLiveWorld`; `DesertLoopSoakTest` walks the chain with the flags off and on.
Nothing is flipped by this work.

## 1. Setting (what the ship flies over at atmosphere level)

A dead, sun-bleached industrial world. The planet was strip-mined and left to the wind: pale bone-gold dune seas with long wind ripples, glass and salt
flats where the old reactors fused the ground, buried machine ruins (half-sunk refinery hulls, ribcage gantries, drill heads), skeletons of solar-farm
arrays, a dry canyon with rust-copper walls, and caravans of rusted crawlers crawling between wells. Very dry, very quiet, very large; and under it all,
**giant sand worms**.

Time of day / four variants (`variantNight` = v4):

| Set | Look | `flow` (sparse scrolling strip) |
| --- | --- | --- |
| v1 Day dunes | open bone-gold dune sea, crescent barchans, long wind ripples with indigo shadow on the lee side | wind-blown sand streaks and ripple lines |
| v2 Canyon and ruins | dry canyon floor between rust-copper cliff walls, buried refinery hulls, drill heads, ribcage gantries, scattered rubble | drifting dust ribbons along the canyon |
| v3 Salt flat with solar skeletons | pale cracked salt/glass pan, rows of toppled solar-array skeletons and dead mirror fields, fused-glass scars, heat shimmer rows | thin glints and crack lines (dry polygons) |
| v4 **Night side** | the same dune sea under a cold moon-glow: dusty-indigo sand, cold bone-white crests, a few dim work lights on ruins | pale drifting sand veils |

No space imagery (the moon-glow is a lighting colour only; no moon disc, no stars). The cloud ceiling is a thin high dust/sand haze (see the planet).

## 2. Palette and COLLISION CHECK (the decision the user must see)

Rule from the playbook: a world accent at a pickup hue over large areas hurts pickup readability. The Desert's signature surface (sand) sits next to the star-dust
amber (37), so the fix is **hue distance AND saturation distance**: star dust is a fully saturated gold-amber (S ~ .9-1.0, V 1.0); Desert sand is a pale,
desaturated bone-gold (S .17-.33), so the two never read as the same thing even where the hue is near.

Computed swatches (HSV, hue in degrees):

| Role | Hex | Hue | S | V | Note |
| --- | --- | --- | --- | --- | --- |
| Bone highlight (crest lights, salt) | #E6DCC0 | 44 | .17 | .90 | accents only |
| Sand light | #D9CBA0 | 45 | .26 | .85 | accents / day crest tops |
| Sand mid (the dune body, in tiles darkened to V .35-.50) | #BFAE80 | 44 | .33 | .75 | **S cap .33 (p95 of sand pixels)** |
| Sand dark / ochre | #8F8160 | 42 | .33 | .56 | |
| Dusty indigo shadow | #2E3150 .. #454A78 | 234-236 | .42-.43 | .31-.47 | shadow tint only, never a lit feature |
| Oxidised copper (metal, never emissive) | #9A5E3A | 23 | .62 | .60 | cliffs, plates, <= 12% of a tile |
| Verdigris patina (trace) | #6FA08A | 153 | .31 | .63 | <= 3% of a tile, grey-green not teal |
| Sun-white (enemy/boss light, heat cores) | #FFF1C8 | 45 | .22 | 1.00 | the "light" colour |
| Glass / salt shimmer | #9FC9C0 | 167 | .21 | .79 | desaturated, <= 4% of a tile |

Numeric checks (all must hold; `verify_backdrop_tiles.py --forbid-hue` is run with the lists below):

| Neighbour | Their hue | Desert's nearest | Result |
| --- | --- | --- | --- |
| Pickup star dust amber | 37, S ~ .95 | sand 42-45 (5-8 deg), but **S <= .33 vs >= .85**: 0.5+ saturation gap; Desert fill never above S .33, only 1-2 px bright accents may exceed it | OK by saturation; any sand pixel with S > .45 inside hue 30-48 is a FAIL in the verifier |
| Pickup cyan 178 | 178 | glass shimmer 167 at S .21, <= 4% | OK (desaturated, trace) |
| Pickup green repair 82 | 82 | verdigris 153 (71 deg) | OK |
| Pickup violet capacitor 259 | 259 | indigo shadow 234-236 (23-25 deg), shadow-only | OK; no Desert pixel in hue 250-300 |
| Pause atom red ~7 / player red 345..15 | | copper 23 (8 deg above the band edge at 15; S .62) | **tight**: copper capped at hue >= 22 and S <= .62, V <= .6; verifier red-band count must be 0 on every set; copper is dull-metal only |
| Hostile pink 312-326 | | none | OK; the pink cue stays on every hostile thing |
| Space indigo/magenta | ~240-250 / ~330 | indigo shadow 235 | shadow-only, <= 25% of a tile, never saturated >.45 |
| Frost white-cyan | 180-200 | bone highlights (hue 44) | OK |
| Verdant lime | 75-90 | verdigris 153 | OK |
| Ember orange/black | 20-40, emissive | copper 23, S .62, matte and dull; **Desert never glows orange/amber**: heat is sun-white/pale | OK; the rule is "no emissive hue 15-40" |
| Tide teal-green + brass | 165-175 / mint 158 | verdigris 153 (S .31), glass 167 | **tight**: both are desaturated traces (<= 3-4%); Tide's mint is S > .5 emissive | OK, trace-only |
| Storm slate / electric white-violet | 212-230 (S <= .30), lightning 55-62 | indigo shadow 235 (S .42); sun-white 45 (S .22) | 5-23 deg hue gap; Desert is warm-bright vs Storm cold-dark, and Desert indigo only in shadows. Sun-white is capped at hue 40-48 so it never drifts into Storm's 55-62 yellow-white |

Forbidden-hue lists for the verifiers: red band (345..15, S>.5); pink 312-326 (tiny lamps only); 250-300 violet; and saturated 30-48 (S > .45) except
<= 0.3% of opaque pixels (sparks). Brightness: p90 of HSV value over opaque pixels .40-.50 (day sets), .32-.42 (night). Because bone-gold is naturally
bright, the tiles are painted **dark-base**: sand body V .30-.50, indigo shadows V .12-.28, bright bone only as thin crest highlights (Ember's mid is the
target: dark base, lively accents). The player reads against this: red ship, bone-gold sand, pink shots.

Enemy light (`EnemyPalette.Theme.light`): sun-white **#FFF1C8**. Boss flash: **#F4E3B0** (hue 44, S .27); boss heart colour: pale bone **#FFF6DC**; warning
accent: pale gold-white (S <= .35). All >= 22 degrees from the player's red.

## 3. The hook: GIANT SAND WORMS in the backdrop

An art-and-ambient system (no gameplay effect in the first pass; a themed boss/elite attack idea for later).

- **What the player sees.** A long **surface-ridge trail** travels across the dunes: a rolling hump with sand cascading off its flanks and a dust wake behind
  it (the worm is just under the sand). Now and then the worm **erupts**: the head arcs up and out in a spray of sand (a huge ringed, plated, ribbed body of
  oxidised-copper segment rings with bone-pale mouth plates and a ringed maw), reaches the top of the arc, and **dives back in** with a second sand burst, the
  ridge trail resuming. Several sizes: small (ship-sized, frequent), medium, giant (rare, slow, takes a whole screen).
- **Layering rules (non-negotiable).** Worms are pinned to the sand ground class (`GroundClass` of the mid tile, `PinnedTo mid`, same scroll rate: they ride
  the ground, they are not floating). They always draw **behind** enemies, bullets, pickups, and the player (readability sweep must see no new LOW/WEAK);
  alpha-capped (body <= 85%, spray <= 70%), never brighter than the sand's own highlights (no bright fills), no pink, no red, outline in dark indigo. They
  never cover the lane contrast guard (enemy bodies >= 2.5:1 against the rendered lane).
- **Art plan (dedicated Codex job).** `worm.png` atlas + manifest (see J4 below): surface-ridge loop (bottom-centre anchored, seamless), emergence frames,
  arc/body frames at several scales, dive frames, sand-spray puffs (up and down), cascade/dust wake loops. Scale family 1x / 1.6x / 2.6x generated from separate
  drawings, not up-scaled copies.
- **Code (later, Phase 11).** A `DesertWormDirector`-style ambient spawner that places a worm on the mid tile with long calm gaps (one at a time, a giant
  at most every ~60 s and never in the first 12 s, never at a boss), reads `points.json` for the emergence anchor, and plays its sequence on the ground layer.
  Kill switches: `WorldBackdrop` quality tiers (low tier = ridge only). Texture budget share: the worm atlas lives inside the 7 MB world budget.
- **Later gameplay re-use (not in this pass).** Elite site kind "worm burrow"; boss attack "the Burrower": a telegraphed ridge races down a lane then erupts
  (a lane `Strike`, pink-cored spray + pink keyline, >= .7 s tell).

## 4. Planet (approach, planetfall, lift-off)

Seen from orbit it must read as a **desert world at a glance** and have a silhouette distinct from Frost's ring city, Verdant's vine ring, Ember's brass radiator
ring, Tide's rig ring and Storm's skyhook ring: **a half-sunken dune planet with a single enormous crescent "dune sea" scar, a dark salt-glass basin as an
eye, and a broken orbital ring segment (a partial ring: one arc missing, debris trailing, like a snapped bracelet)** plus two thin, tilted **dust plume
trails** swept off the limb like a comet tail (wind-lifted sand). Sand-gold day side (pale, desaturated), indigo terminator, night-side city lights as sparse
amber-white dots (tiny, 1-2 px), thin dusty rim light (pale gold-white, not the amber hue).

Cloud decks = **dust/sand haze**: layered thin sand-storm sheets (bone-gold on dusty brown-grey, the dark deck dusty indigo-brown), tileable both axes; the
backdrop ceiling will continue them. Limb: dune-sea horizon under a dust haze, buried ruins as silhouettes. Entry plasma: wide white-hot shroud with pale gold
edge flames (a wide fierce shroud, not a ribbon). Breakthrough: a pale sand-spray burst with dust rings. Streaks: fine pale sand-grain speed lines.

## 5. Roster of 12 (names, silhouettes, behaviours, themed attacks)

Every enemy keeps a pink-leaning core and/or keyline (pink-cue rule); the material is sand, dust, heat, glass, mirage. Behaviours use existing primitives
(`docs/enemy-behaviours.md`); about 40% shoot, six attack with the body. Counter = what the player reads.

| Key | Name | Silhouette | Movement and themed attack (tell, counter) |
| --- | --- | --- | --- |
| `desert_rock_dune` | Dune Boulder | wind-carved sandstone ball with grooves | slow tumble, `Drift`; wide and slow |
| `desert_rock_spire` | Glass Spire | fused-glass crystal blade, sheared | `Glide` one slanted slice; reads early; shatters in glass shards |
| `desert_rock_husk` | Fulgurite Husk | rough twisted sand-glass tube with a dark hollow | `Sink` + spin; arrives sooner than it looks |
| `desert_rock_slab` | Dune Slab (floating) | a floating chunk of crust with a crescent crest and dangling roots | upright `Sway` + `Bob`; wide and slow |
| `desert_mine` | Mirage Mine | rusted brass lens-sphere on a clamp, dormant dull | rail mine: `Laser` x2, **heat-lance** (white-gold beam with pink keyline), core lights at windup |
| `desert_big` | Sandbarge | armoured rusted crawler hulk with treads, a bucket-wheel, a cabin | holds a column, rolls its bucket-wheel (tell), then a fan of 3 **sand-slug** shots that burst into grit; stand between them |
| `desert_fighter_1` | Sand Skimmer | tiny wedge dune-skiff | diagonal `Drift` then a straight `Lunge` with a dust wake; the dash never tracks |
| `desert_fighter_2` | Glint Hawk | angular scavenger glider with mirror-plate wings | `Track` to line up, then 3 quick **glass-shard** bolts straight down; keep moving |
| `desert_fighter_3` | Lens Gunner | tripod gunship with a big mirror lens | `Brake` hover; lens flares white .9 s, then one aimed **heat-lance** (thin hot bolt, pink core); move after the flare |
| `desert_fighter_4` | Dust Devil Tank | heavy armoured hulk wrapped in a dust cone | `Brake` + slow `Track`; releases a spinning **dust-devil** ring of 8 grit bolts with a guaranteed crack (`Blast`); back off |
| `desert_chaser` | Scarab Drone | beetle drone with copper elytra | hunts in a new style (new `ChaserStyle`, "Burrow": dives, vanishes under a sand puff, resurfaces beside the pilot, then lunges); or shares a style and relaxes `EnemyBehaviourTest` |
| `desert_alien` | Mirage Wisp | shimmering heat-haze sprite with a hard bright core | `Pulse` + `Sway`; **mirage flicker**: it spawns 2 decoy afterimages that are harmless and visibly translucent (decision still OPEN, see section 11) |

Screams (living occupants/creatures only): Sand Skimmer pilot, Glint Hawk pilot, Lens Gunner gunner, Scarab Drone creature (chitter), Mirage Wisp (reedy).
No screams for the Sandbarge, Dust Devil Tank, mine, rocks. Explosion material: **Dust/Grit** (a new `TargetExplosion.Kind` row: sand burst with glass
glints), or reuse `Rock` for the first pass (OPEN, section 11).

## 6. Elites (6; launch sites in the dunes)

| Key | Name | Role (brain) | Themed attack | Site kind (`LandingKind` prefix `Desert`) |
| --- | --- | --- | --- | --- |
| `desert_elite_dune_stalker` | Dune Stalker | interceptor | `lance_dash` through a sand wake, grit needles | sand hatch (worm-burrow hole) |
| `desert_elite_caravan_hauler` | Caravan Hauler | hauler | drops sinking sand-pit canisters (`slag_drop`, a Glob skin: a quicksand pool that swallows shots) | crawler bay |
| `desert_elite_lens_matriarch` | Lens Matriarch | kiter (skirmisher) | blink + mirage fan: 3 real shards among 3 decoy glints (decoys visibly hollow) | glass pad |
| `desert_elite_glass_cannon` | Glass Cannon | artillery (siege) | lane-telegraphed **sun-lens beam** shell (a sustained heat-lance rail) | buried silo |
| `desert_elite_scorpion_walker` | Scorpion Walker | striker | tail **venom lash** (lash with pink-tipped sting) + `claw_dive` | ruin dock |
| `desert_elite_mirror_bastion` | Mirror Bastion | shield platform (bastion) | `ward_curtain` of slow glass shards with one gap | ruin dock |

## 7. Boss: DUNE-EATER, "SKY-DREDGER" (flying redesign, Oct 16; replaces the Harvester excavator concept)

Everything in this game floats, so the Desert boss is a flying creature, not a machine riding on the ground: a front-on **brass-and-glass sand leviathan,
half manta, half armoured sandworm gunship** that dredges the sky the way it once dredged the dunes. No treads, wheels, legs or ground. Parts, inside the
576 px cell of the 35-cell Kraken-layout atlas: a great **maw** at the lower centre (two hinged lower mandibles, ivory teeth, a sun-gold and teal-white
throat), a big faceted glass **lens** turret in a brass gimbal on the crown, a long articulated **crane boom** on the right flank holding a spiked mine (a winch
drum and counterweight balance it on the left), two huge lateen **sail-wings** in lapis, saffron, teal and cream panels, four **turbine** pods with glowing
teal-white fans, lacquered hull plates in verdigris teal, saffron, lapis and terracotta with brass trim. Three named firing parts (`measure_emitters.py`):
**Maw** (grit fan, pincer, sandstorm wave walls from the throat), **Lens** (the beam, the aimed stream, sandstorm wall), **Boom** (mines and the burrower
strike from the mine's claw). Attacks (unlock in thirds, timing unchanged): (1) *grit fan*, tell0 maw opens, `Fan` of 5 sand-slugs; (2) *dune lob*, tell1 crane
lowers, `Lob` on 4 chosen columns with lane telegraphs; (3) *lens beam*, tell2 lens flares, `Beam` .8 s hold; signature phase 1+: *burrower strike* (`Strike`);
phase 3: *sandstorm wall* (`Wave` band with a gap). Flash #F4E3B0, heart #FFF6DC, never red, cue colour sun-white. Damage: four painted stages (torn sails,
cracked lens, broken teeth, pods ripped off, the hull blown open); death: reactor overload, white-gold flash, sand shockwave ring. Smoke = dust and steam.
Art source: `Art/Bosses/Desert~/src~/` (hull_key.png = the approved master, prompts in prompts.md; build.py / build_damage.py / install.py as for the Cinder Drake).

## 8. Enemy sound materials (authored by Codex; no beeps, no coins, no ambience)

Dry grit hiss, glass tink-then-crack (low-passed, never sine), brass clank, dry wind gust, rust-screech under load, hydraulic gasp, bucket-wheel rumble,
sand slump (filtered noise). No sustained pure tones; sonar/ping style never. Death cues: rocks = sandstone crumble / glass crack; Skimmer = sheet-metal
tear + grit; Hawk = brittle glass crash; Gunner = lens shatter + servo; Dust Devil Tank = rolling rumble + sand slump; Scarab = chitin crunch; Wisp = papery
heat-pop. Screams as listed. Backdrop/ambient/music (including worm sounds) are the user's.

## 9. Difficulty tier

`speedRampPerSecond` ~ **.00425**, `enemyRampScale` ~ **1.80** (Space 1.00, Frost 1.10, Verdant 1.20, Ember 1.35, Tide 1.50, Storm 1.65); pilot load 5.0 (low
speed) / 4.0 (high). Validate with `DifficultyRetuneTest`, `WorldPaceTest`, `SpeedCapTest`. Music `""` (the user supplies it).

## 10. Themed attacks (Phase 12b table; pink cue on every row)

Material: sand, grit, dust, heat, glass, mirage, worm. Pink cue: pink-white heat cores/keylines inside sand-pale bodies (sand never saturated; the pink
carries the "hurts" signal); pointed shapes only (grit slugs get spiked pink rims, no round pearls).

| Unit | Attack | Primitive | Tell and counter |
| --- | --- | --- | --- |
| Mirage Mine | **heat-lance** rail beam (white-gold with pink keyline, heat shimmer) | `Laser` + Desert beam skin | core lights, off the aim line |
| Sandbarge | **sand-slug fan** of 3, bursts to grit | `Shot` Shard, Slug skin | bucket-wheel spin-up 1.0 s |
| Sand Skimmer | dash with a dust wake | `Lunge` + trail | straight, untracked |
| Glint Hawk | **glass-shard** triple | `Shot` Bolt, Shard skin | wings tilt |
| Lens Gunner | **heat-lance** aimed bolt | `Shot` + lens flare | lens flares .9 s |
| Dust Devil Tank | **dust-devil** ring of 8 bolts (guaranteed crack) | `Blast` | cone spins up 1 s |
| Scarab Drone | burrow-lunge from a sand puff | `Chaser` Burrow | puff marks where it surfaces |
| Mirage Wisp | **mirage flicker**: translucent decoys of its own shot (OPEN readability decision) | `Shot` + decoy | decoys hollow, no pink core |
| Dune Stalker | wake dash + grit needles | `lance_dash` | |
| Caravan Hauler | quicksand pit canisters (a pool that swallows shots) | `slag_drop` Glob skin | pool drawn .4 s ahead |
| Lens Matriarch | mirage fan (3 real among decoys) | `blink_shards` | |
| Glass Cannon | **sun-lens beam** shell | `siege_cannon` | ringed lane preview |
| Scorpion Walker | venom lash | `claw_dive` + `Lash` | tail rears |
| Mirror Bastion | glass-shard `ward_curtain` with one gap | `ward_curtain` | |
| Harvester (boss) | grit fan, dune lob, lens beam, burrower strike (lane), sandstorm wall | `Fan`/`Lob`/`Beam`/`Strike`/`Wave` | |

Attack art (later, J12): shots, fx, beam, strike (burrower), wave (sandstorm), jet (not needed), lash (venom), ring (dust devil) = 7 atlases.

## 11. OPEN questions for the user

1. Name: Desert, Dune, Sirocco or Mirage?
2. Boss: Harvester (feral bucket-wheel excavator with a worm-maw) or a worm-themed boss ("Sand Leviathan")? Default Harvester.
3. Mirage Wisp decoys: allow harmless translucent decoys (readability risk) or drop the mechanic (default: only decoys of glints, never of shots)?
4. Explosion material: new Dust/Grit row (Codex) or reuse Rock (default: reuse for the first pass).
5. Chaser style: new "Burrow" style or shared?
6. Worm frequency/scale: ambient only (default) or also a gameplay worm hazard later?

## 12. Codex job order (this run) and prompts

Common preamble and conventions as `docs/ultimates-codex-prompts.md` (image generation mandatory then pixel-clean, x2 nearest, 1 px dark outline, no space
imagery, atmosphere level), sizes and checks per `.claude/skills/add-world/art-briefs.md`. Prompts live in `docs/desert-codex-prompts/`; launch with
`~/.local/bin/codex exec -C <wt> -s workspace-write -c model_reasoning_effort='"high"' -i <abs ref> -o <last.txt> - < prompt.md > out.log 2>&1`
(max 2 Codex jobs machine-wide). Output `Pause/Assets/Art/Worlds/Desert/<job>~/` with `src~/`.

| Job | Worktree / branch | Output | Verify |
| --- | --- | --- | --- |
| J1 planet + planetfall (7 files) | `codex-desert-planet` / `art/desert-planet` | `Worlds/Desert/descent~/` | `verify_planetfall_art.py <dir> desert` |
| J2 backdrop run A (4 sets x sky/far/mid/flow 512x1024) | `codex-desert-backdrop-a` / `art/desert-backdrop-a` | `Worlds/Desert/backdrop_v3~/v1..v4/` | `verify_backdrop_tiles.py` + forbidden hues |
| J3 backdrop run B (landmarks, pipes, features, weather, sites) | `codex-desert-backdrop-b` / `art/desert-backdrop-b` | same folder | atlas part, margin 14 |
| J4 worm atlas | `codex-desert-worm` / `art/desert-worm` | `Worlds/Desert/worm~/worm.png` + manifest | custom: cell sizes, loop seam, anchor |
| later | enemies, deaths, rails, boss, elites, sounds, attack art, backdrop C loops | | |
