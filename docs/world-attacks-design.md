# World attacks: the themed attack for every enemy, elite, boss and mine

Branch `feature/attacks-design`. Reads with `world-attacks-audit.md` (what exists), `world-attacks-art.md` (art list),
`world-attacks-codex-prompts.md`, `world-attacks-implementation-plan.md`.

User requirement (verbatim): "the attack patterns for each world should match the enemies there, for example,
frost world there will be ice attacks and blasts etc, for fire world fire attacks, flame throwers, fire lasers etc,
for water world, surf wave attacks, water guns, thunder etc... for space, laser neon attacks etc. does that make
sense, not all enemies should be doing same attacks as first world; if you need animations have codex make it, else
do it yourself; and for forest world (he wrote 'frost' by mistake: Verdant) have vine attacks, leaf attacks, tree
trunks etc. ... and update skill to remember that for next worlds we create".

Answer: yes, it makes sense, and it is mostly a *skin + motion + FX + sound* job on top of attack logic that already
exists. Five genuinely new hazard shapes are needed (cone/column jet, sweeping wave band, expanding blast ring, lane strike,
lashing arc). Everything else re-uses `Shot`, `Lob`, `Laser`, `Lunge`, `Ring`, the elites' `Sling` / `Orb` fuse / `Slab`
glide, the boss `Aimed` / `Fan` / `Lob` / `Beam`.

## 0. Principles

### 0.1 The pink-cue contract (readability stays one language)

Hostile = a **pink-leaning "this hurts" cue**, in every world; the world supplies **shape, motion, trail, impact and sound**.
The rule from `HostileShotPalette` (hue 312-326; pickups own cyan 178, green 82, violet 259, amber 37; red 345-15 is the
player's) is not loosened. Every themed hostile thing (shot, jet, band, ring, strike, lash) must satisfy:

| # | Rule | How it is checked |
| --- | --- | --- |
| PC1 **Keyline** | a pink outline on the whole silhouette: `ShotOutline.For(sprite, size, bold)` for sprites (bold two-ring keyline with `BoldKey` when `BackdropCatalog.CurrentIsBright`); for strips (jets, bands, rings, strikes) the art carries a 2 px pink-white edge stroke (hue 312-326) | `AtomClarityTest` hostile-edge cue; `ReadabilitySweep` edge-ring 3:1 |
| PC2 **Core** | the innermost / hottest part is pink-white (`HostileShotPalette.Core`), >= 30% of the opaque area is in the pink family or white; the core flickers hard at `FlickerHz` 5 (`FlickerHot`) | new `ShotSkinTest` pixel audit |
| PC3 **Material pixels** | the world's material (ice, leaf, flame, water) covers at most half of the saturated area; within 25 deg of a pickup hue the saturation stays <= .65 (so a pale frost-blue, never shield-cyan; a deep leaf-green, never repair-green; a hot orange 20-30, never amber 37); never the red band | `ShotSkinTest` hue histogram; `AtomClarityTest` hue gap + cues |
| PC4 **Shape** | hostile shapes are pointed, elongated or angular (spears, thorns, flames, bars); a round body is only allowed with a pink spiked/scalloped rim (the pearl, the bubble mine), never a smooth disc: atoms are round | `AtomClarityTest` silhouette IoU cue |
| PC5 **Motion is not an atom's** | atoms drift and breathe smoothly; hostile things flicker, tumble or lash in stepped motion (core `FlickerHot`, 8-12 fps frame swaps) | design review + preview gif |

A themed *painted* body (the boss-shot model: painted material + pink rim) is allowed where the art is bold enough to
pass `MinBossCues`; roster and elite shots (`MinShotCues`) must also pass the hue gap on the *measured* pixels, so their
bodies are the pink-leaning ramps below, with the material only as a secondary ramp.

### 0.2 The fairness contract (instant-hit hazards need more warning than projectiles)

A projectile has travel time; a cone, column, band or strike arrives at once. So the new hazards obey, in addition to the
standing rules (tell never under `EnemyBrain.TellFloorSeconds` .45 s, aim locked at the tell, fire only >= `MinFireAbove` 1.6 u above and
>= `MinFireDistance` 1.8 u from the pilot):

| # | Rule | Number |
| --- | --- | --- |
| FR1 | instant-hit tell (jet, strike, wave, blast) | >= .7 s total; the first .3 s is the pose, the footprint shows >= .4 s before it goes live |
| FR2 | footprint preview | dotted pink-white outline of the exact hit shape at 50% alpha (code-drawn from the same geometry as the hitbox) |
| FR3 | live time / speed | jet <= .6 s live; strike column <= .25 s live; wave front >= .8 s from spawn to the pilot's row (<= 3 u/s relative to the board); blast ring <= 2.8 u/s |
| FR4 | safe corridor | at every moment of every attack at least one corridor >= 1.4 u wide (the Eventide curtain's own gap is 2 x `lobSpacing` .75 = 1.5 u) that the pilot can reach in the time left; asserted by a headless sweep (`AttackFairnessTest`) |
| FR5 | damage | unchanged: a heart per hit, the same i-frames; a shielded hit is absorbed (`EliteShip.ShieldRam` erases hitboxes); a blink erases a hostile only when the hull lands on it |
| FR6 | budget | each themed attack replaces one existing attack at equal or lower cooldown/volleys, and a **dodge bot** (reaction delay .25 s, the ship's normal speed, aim locked at the tell like the game) must be hit no more often than by the attack it replaces x 1.15, over 2000 seeded rolls per attack (`AttackBudgetTest`; the old attacks' hit rates are pinned as constants). Swept area is printed for information, not asserted (a ring or band sweeps far more area than a fan and is still fairer because its gap is guaranteed) |
| FR7 | threat weight | `EnemyThreat` counts: jet 1.5, strike 1, wave 2, blast 2, lash 1.5 live "shots" against `MaxEnemyShots` 12 |
| FR8 | pause / perf | timers advance only through `Step(dt)`, pooled, no per-frame allocation (sprites resolved at launch) |
| FR9 | friendly fire | every new hazard hurts other hazards through `FriendlyFire.HostileHit` exactly as a laser does, never its own shooter, at most once per live pulse |

New hazards register with `HostileShots` like `BossBeam` / `RailMineLaser` already do (`HostileShots.Register(BossBeam)`): a live jet
or strike burns light/heavy shots crossing it and is not stopped by one; bands and rings do the same but are *not* shot down
by the player's weapons (like lasers); pools stay fixed mass.

### 0.3 Sound identity

No hostile attack makes a sound today (audit, finding 3). Rule: **material, not beep.** Every cue is a short synthesised
noise-and-body sound (a generator under `Pause/Assets/Audio/AttackSrc~`, built like `EnemyDeathSrc~`; `verify_wavs.py`)
with no sine/square tone as the main layer. Four cues per hostile family: `charge` (telegraph), `fire`, `travel` (loop, optional),
`impact`. Per world:

| World | Material voice | charge | fire | impact |
| --- | --- | --- | --- | --- |
| Space | filtered-noise energy: hiss-sweeps, a dry thump, a rail "crack" | rising noise-hiss sweep | short bright zap-hiss; rail slug: crack + whoosh | soft electrical fizz |
| Frost | glass and cold air | low wind swell + creaking ice | glassy "tchk" spear release, sleet hiss | crystal shatter (high tinkle + puff) |
| Verdant | wood, leaf, sap | wood creak, leaf rustle swell | whip crack (vine), dry leaf flutter, log "thunk-whoom" | wet thud, splintering, spore "pfff" |
| Ember | gas flame, magma | gas hiss swell + ember crackle | roar (filtered noise burst), magma "plop", heat whoosh | sizzle burst, slag splat |
| Tide | water under pressure, thunder | pressure hiss, deep gurgle | jet "pssshh", wave swell and crash, thunder crack + rumble | splash, bubble "blub", muffled boom |

Boss versions are the same cues, longer and lower.

### 0.4 Sizes and the new vocabulary

* **S** = skin / art / FX only (no new motion code). **M** = a new projectile behaviour inside the existing pool
  (`EliteShot`). **L** = a new hazard primitive (new component, hitbox, preview, tests).
* **Skin** (`ShotSkin`, new): per (world, `EliteShots.Kind`) record: sprite pair (2 frames), draw scale, trail emitter, spin/flutter, impact set, sound ids.
  Every elite and roster shot picks its skin from `def.world` automatically, so the *whole* existing roster/elite set is
  re-themed by art alone. Space's skins are the current procedural sprites.
* **New attack primitives** (all new `EnemyAttack` / `BossAttackKind` values): **Jet** (cone/column), **Wave** (band with a gap),
  **Blast** (expanding ring with a gap), **Strike** (telegraphed lane column), **Lash** (swept arc).
* **New projectile behaviours** inside `EliteShot`: **Streak** (very fast slug with a long trail), **Shatter** (splits into chips at a fuse
  or on contact with a rail), **Flutter** (sinusoidal sideways flutter + spin), **Slash** (thin crescent crossing the lane on its row),
  **Roll** (a lobbed heavy log: lands, rolls down the board, bounces off rails), **Burst** (a lobbed pod that opens into a lingering cloud).

## 1. Space (laser / neon / rail gun / ion)

Space is the baseline: neon pink lines on indigo already *are* the theme. The change is to make its attacks read as
**energy weapons that differ from each other** (pulse lance, rail slug, ion arc, scan line) and to keep all of it procedural.

| Enemy | Attack name | Reuses / new | Telegraph / fairness | Damage / hitbox | Counter | Pink cue + VFX | Sound | Size |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Rail Mine | **Neon laser** (reference) | `Laser`, unchanged | aim line .7 s, beam .4 s | thickness .28 | off the aim line | existing magenta-pinked boss laser cells | zap-hiss | - (baseline) |
| Bastion | **Twin pulse lances** | NEW `Jet` style Lance: two short column pulses (1.8 u long, .2 wide, .35 s live) straight down from the prongs, replaces twin bolts; 3 volleys | eye flare .8 s, two dotted neon sight lines the last .4 s | lances sweep ~.25 u^2 in all; the old bolt pair swept ~1.9 u^2 over its flight; FR6 by the bot test | leave the column | pink-white core, cyan-free; procedural bars with a hot-pink edge, afterimage fade | hiss-sweep + thump | M (first `Jet`) |
| Needle | **Ion dash** | `Lunge` unchanged + cosmetic afterimage trail | thrusters flare .45 s | body only | step off its line | 3 fading magenta ghost copies | air-slip hiss | S (code) |
| Steel Claw | **Claw snap arc** | `Lunge` + cosmetic ion arc between the claws on the pinch | pinch .55 s | body only | sidestep | a zig-zag line (no hitbox) | crackle burst | S (code) |
| Twin Claw | **Split bolts + arc bridge** | `Shot` Bolt x2 unchanged; cosmetic ion arc linking the two bolts in flight (so the safe gap between them is *drawn*) | flare .6 s | each bolt r .054 | stand in the gap | thin arc, no hitbox | zap-hiss x2 | S (code) |
| Warden | **Rail-gun slug** | NEW `Streak` behaviour on `Shell`: aimed slug at 6.0 u/s with a 1.4 u afterimage trail, pierce 2 | the cannon charges **1.1 s** (was .9) and a **hairline sight line** runs to the locked point for the last .5 s (a faster slug is paid for with a longer tell and a drawn line) | r .108 unchanged, 4 volleys -> 3 | move after the charge starts; the line shows where it goes | white-hot slug with a pink core, trail fades pink | rail crack + whoosh | M |
| Steel Hound | **Ion wake** | chaser, no attack; cosmetic wake | | | | pink spark trail | engine hiss | S (code) |
| Bile Mite | **Ion spit** | `Shot` Shard x1 re-skinned: pellet with comet tail | .55 s | r .051 | the gap in the line | small pink-white pellet | soft pip (noise, not a tone) | S (code) |
| Rocks | none | | | | | | | - |

Elites (all already neon; additions are trails only): Eventide Bastion `ward_curtain` keeps its bolts but each bolt leaves a thin
pink ion wake (S); Orbit Reaver `crescent_volley` unchanged; **Rift Lancer** `rift_rail` becomes the true rail gun: its 5 bolts
become `Streak` slugs along the already-shown sight line (S after `Streak`); Singularity Hauler `gravity_sling` draws an ion tether
between claws and shot (S).

**Void Archon** (boss, 5 attacks, rail-gun + neon): chin cannon = 3 aimed **rail slugs** (Streak skin, painted cell kept); core burst = **plasma shards**
(unchanged); pod lasers = **pod lasers** (unchanged); NEW signature (phase 1+): **ion arcs** (`Jet` Lance pulses at the pilot's x +-1 from both pods, .8 s tell);
NEW phase 3: **scan line** (a neon `Wave` band sweeping down the lane from pod to pod with a 1.6 u gap aimed at the pilot at the tell; 1.0 s gap marker; traverse 3 u/s).

Space totals: new code = `Jet` Lance (shared), `Streak`, `Wave` (shared), cosmetic arc; **no Codex art**.

## 2. Frost (ice shards, icicle drops, cold blasts, snow and hail, frozen mines)

Material: pale ice-white / lilac, rime, crystal, sleet, mist. Pink cue: a **hot-pink core inside a pale crystal body**
(a crystal lit from within by "danger"); never shield-cyan: bodies are white-lilac (hue 205-225, sat <= .35), the sat >.5 pixels are the pink.

| Enemy | Attack name | Reuses / new | Telegraph / fairness | Damage / hitbox | Counter | Pink cue + VFX | Sound | Size |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Geode Mine | **Frost ray** (frozen mine) | `Laser`, a Frost beam skin: crystalline cold beam, frost crystals bloom along the line and linger .5 s harmless | aim line .7 s; crystals start growing on the mine at the charge | thickness .28 | off the aim line | pink-white core streak inside pale ice | wind swell then glassy hiss | S (art) |
| Glacier Golem | **Cold blast** | NEW `Blast` ring: an expanding frost ring from the maw, 18 bars, speed 2.6 u/s to 3.4 u radius, a 70-deg crack (gap) aimed at the pilot's x at the tell (gap chord at the pilot's range always >= 1.4 u); replaces the 3-shard fan | maw cracks .9 s, a pulsing frost glyph ring at the muzzle, the gap edges marked | bar r .09, 3 -> 2 volleys | step into the crack | pink-white bars with pale rime, ice-mist ring | low wind swell, crystal ring | L (first `Blast`) |
| Flake | **Lance dash** | `Lunge` + frozen vapour trail | swirls .55 s | body | step off | pale trail | whoosh | S |
| Icicle | **Icicle spear** | `Shot` Bolt x1 aimed: skin Icicle (long crystal spear, pink-white core) that **shatters** into 3 chips on a rail/edge/pop (Shatter, cosmetic chips) | blades pinch .55 s | r .054 as today | sidestep after the pinch | spear + sparkle trail | glass "tchk" + tinkle | M (skin + Shatter) |
| Frost Kite | **Splinter pair** | `Shot` Bolt x2 spread 34 with **Shatter**: each spear splits once, at 1.2 s, into two small chips at +-12 deg (4 total, chips r .04); volleys 3 -> 2 (FR6) | flare .55 s | | between the pair, and again after the split | | | M |
| Hailstorm | **Hail cloud** | `Shot` Shard x5 spread 24, **Hail skin** (faceted pellet, pink core); NEW code-only **snow cloud** that puffs over the muzzle during the tell and sheds falling flakes; shots leave the cloud | big flare 1.0 s, cloud darkens, flakes fall | r .064, 2.0 u/s | pick a gap | grey-white cloud, pink-core pellets | sleet clatter | M (cloud procedural) |
| Frost Lancer | **Spike dash** | chaser dash + ice-spike trail | stops to aim | body | step off | | | S |
| Cryo Jelly / rocks | none; Cryo Jelly's tendril lash is cosmetic | | | | | | | - |

Stretch (OPEN question to the user): rocks never attack; optionally **Frozen Chunk drops an icicle** (`Strike` Icicle, 1 s glyph) when the
pilot passes under it. Default: no.

Elites (shot art swaps only, behaviour untouched): Rimebreaker `ice_ram` shards = Icicle chips; Whiteout Sentinel `armour_shatter` = Hail; Cryo Siren `frost_bloom`
orb keeps its fuse ring but bursts into **Hail** chips; Floe Harrower `floe_cast` already fires painted slabs (keep), its lance = Icicle; Glacier Tender drones' bolts = Icicle. All S.

**Hoarfrost Leviathan** (5): icicle spray (Fan, painted crystals, Shatter chips on rails: S); glare beams (Beam, Frost beam skin with crystals: S);
blowhole hail (Lob rain, **Hail** skin + snow cloud over the crown: S/M); NEW signature (phase 1+): **icicle drop** (`Strike` Icicle: 3 lane glyphs, 1.0 s, then a falling spear column, .25 s live, one lane left open, spaced >= 1.4 u);
NEW phase 3: **cold blast** (`Blast` x2 from the crown, 1.1 s apart, gaps offset).

Frost totals: new = `Blast` (L), `Strike` (L, shared), `Shatter` (M), Hail cloud (code), skins x5, Frost beam skin, impact + strike art.

## 3. Verdant (vine whips, leaf volleys, tree trunks, spore clouds, seed pods)

Material: wet bark, sap, deep-green leaf, thorn, spore. Pink cue: **a pink thorn tip / pink-lit seam** on every vine, leaf and trunk
(bodies in deep olive and bark brown, hue 90-125 only below value .55; saturated lime is the repair atom, so the lime of the Bile
colours is *not* used for hostile bodies); the Verdant boss's lime beam is the one existing violation and is fixed here (section 3.2).

| Enemy | Attack name | Reuses / new | Telegraph / fairness | Damage / hitbox | Counter | Pink cue + VFX | Sound | Size |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Burr Mine | **Thorn vine lash** (vine across the lane) | `Laser`, Verdant beam skin: a thorned vine grows rail to rail along the aim line and whips (1 step wobble); thorn tips pink | aim line .7 s (a thin dotted vine sprouting) | thickness .28 | off the line | pink thorn tips on dark bark, sap drips | wood creak -> whip crack | S (art) |
| Bloom Maw | **Seed pod** | `Lob`, Glob skin Pod (**Burst**): the pod lands on the marked spot, splits and leaves a **spore cloud** pool (same 2.5 s, same hit, fixed mass); a ring marks the spot | petals snap .9 s, landing ring | r .12 (cloud) | leave the spot | pink-lit seam on the pod, mist cloud with pink core | wet thud + "pfff" | M |
| Gnat | none (scout) | | | | | | | - |
| Wasp | **Stinger dive** | `Lunge` + venom flash | wings blur .5 s | body | sidestep late | pink stinger tip flash | buzz-whoosh | S |
| Mantis | **Leaf-blade slash** | NEW `Slash` projectile on `Cross`: a thin crescent (1.1 u wide, .12 thick) crosses the lane on the Mantis's row at 4.5 u/s from its muzzle; replaces the sideways body lunge (body now hovers) | arms raise .6 s + a faint row line | r .06 along the crescent | be above or below its row | pink-edged leaf blade, leaf-chip trail | blade whoosh | M |
| Hornet Queen | **Thorn fan** | `Shot` Bolt x3 spread 12, Thorn skin (a dart with a pink tip, bark body) | abdomen glow .8 s | r .048 | one clear step | | dry "tk" | S |
| Dragonsting | none; wing glints | | | | | | | - |
| Snap Sprout | **Leaf volley** | `Shot` Shard x2 spread 20, `Armed(.4)`, skin Leaf (**Flutter**: spinning, sideways sine 0.12 u at 2 Hz, so the pair weaves; 1.8 u/s) | jaws snap .6 s | r .05 (hitbox on the leaf's core only) | the gap, the flutter is regular | pink core, dark green blade | leaf rustle + flutter | M |
| Rocks | none; Vine Rock's whip stays cosmetic | | | | | | | - |

Elites: Resin Warden `resin_mortar` becomes **seed-pod mortar** (Pod skin, spore clouds, S after Burst). NEW elite proposal, one
more Verdant elite to carry the user's tree trunks: **Timber Hauler** (hauler brain; NEW elite attack id `log_roll`): it lobs 2 trunks onto marked spots (1.1 s glyphs); each lands, rolls
down the board at 1.4 u/s and bounces off the rails once, `Heavy` mass, r .28 along its length, pierces nothing, 5 s life;
tell .8 s; counter: the rolling line is drawn by the landing glyph's arrow. Needs one Codex elite strip (art-briefs section 7) plus the Log skin; L-ish (new elite attack + `Roll`).

**The Bloom Queen** (5): stinger thorns (Aimed, Thorn skin, painted cell kept); spore bloom (Fan from petals, the spore balls **leave a short spore puff** where they die: S);
acid cannons (Beam: the Verdant boss's *lime* beam art is replaced by a thorn-ichor beam, pink edge, dark bark body: S, fixes the pink rule);
NEW signature (phase 1+): **vine lash** (`Lash`: one petal whip sweeps an arc across the lane (90 deg over .7 s) from a petal tip, pink thorn tip leading, 1.0 s tell with a dotted arc preview);
NEW phase 3: **trunk toss** (`Roll`: two lobs, each a trunk that lands and rolls with one rail bounce, 1.2 s glyph).

Verdant totals: new = `Lash` (L), `Roll` (M), `Burst` (M), `Slash` (M), `Flutter` (M); skins x5 (+ Crescent + Log); the elite.

## 4. Ember (flamethrowers, fire lasers, slag lobs, ember showers, eruption columns)

Material: magma, slag, flame, ash, brass. Pink cue: **white-hot core -> hot-pink fringe** at every flame edge (hue 312-326: reads as heat
past orange), orange only in the middle band (hue 22-30, sat <= .8: five to 15 degrees off amber 37 and 15+ off the red band; the
`AtomClarityTest` decides the exact pick), plum-black smoke.

| Enemy | Attack name | Reuses / new | Telegraph / fairness | Damage / hitbox | Counter | Pink cue + VFX | Sound | Size |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Crucible Mine | **Fire laser** | `Laser`, Ember beam skin: white-hot core, orange body, `EliteFx.HeatShimmer` along the line, magma drips from the muzzle | aim line .7 s | thickness .28 | off the line | pink-white core + pink fringe | gas roar-hiss | S (art + shimmer code) |
| Magma Skull | **Slag lob** | `Shot` Slag x2 spread 56 (heavy blobs, sink, linger) with the Slag skin (molten blob, splat decal on sink); keeps numbers | jaw drops 1.0 s | r .134 | don't follow it down | pink-fringed molten blob, drips | magma plop | S (art) |
| Cinder | **Afterburner dash** | `Lunge` + fire trail | vents flare .45 s | body | never tracks | | whoosh | S |
| Scorch | **Ember shower** | `Shot` Bolt x1 straight x3-4 volleys, Ember skin (fast spark pellet with a short fire tail, 3.8 u/s); optional cosmetic mortar tube pop | vents white .5 s | r .054 | keep moving | | spit-crackle | S (art) |
| Brand | **Flamethrower** | NEW `Jet` style Flame: a cone 1.8 u long, 12 deg half-angle (.76 u wide at the tip), live .6 s, aimed at the locked pilot point and **swept 10 deg** while it strafes; replaces the aimed bolt | fins flare **.7 s** (was .5), dotted cone outline the last .4 s | ~1.1 u^2 per volley; cooldown 1.8 -> 2.6, volleys 3 -> 2 (FR6 by the bot test) | leave its line | white-pink flame core, orange body, pink fringe, heat shimmer | gas whoosh-roar | L (shares `Jet` with Space) |
| Pyre | **Nova** | `Ring` of 8, Fireball skin (large fire orb with a trail), unchanged numbers | core swells 1.1 s | r .054 | back off | | roar swell | S (art) |
| Cinder Fang / Ember Imp | none; fire trail / ember flicker are cosmetic | | | | | | | - |

Elites: Sunstoke (needles = Ember pellets), Coalrunner (`broadside`: bolts = Ember; OPTIONAL **flame broadside**, two short `Jet` Flame bursts sideways), Kilnback `slag_drop` = Slag skin + splat,
Brass Vulture / Ash Wraith shards = Cinder shard skin, Cauterizer `siege_cannon` = magma **Fireball shell** with a white tail (its sight line stays). All S.

**Cinder Drake** (5): fire breath (Fan, painted fire spikes kept, each leaves ember sparks: S); furnace slugs (Aimed, painted magma shards kept, splat on rails: S);
brow laser (Beam, Ember beam skin with heat shimmer: S); NEW signature (phase 1+): **flame sweep** (`Jet` Flame cone from the jaw sweeping +-30 deg over .6 s, slow, 1.0 s tell);
NEW phase 3: **eruption columns** (`Strike` Eruption: 4 lane glyphs, 1.1 s, then magma geysers .25 s live, one gap >= 1.4 u, columns >= 1.8 u apart).

Ember totals: new = shares `Jet`, `Strike`; skins x5 (the Fireball is the boss shot atlas's fire art reused at roster size); Ember beam skin; impact, jet, strike art.

## 5. Cross-world summary: attack per world and role

| Role | Space | Frost | Verdant | Ember | Tide |
| --- | --- | --- | --- | --- | --- |
| Rail mine | neon laser | frost ray | thorn vine lash | fire laser | pressure jet |
| Big (holds a column) | twin pulse lances | cold blast ring | seed pod -> spore cloud | slag lob | pearl fan |
| Fighter 1 (dash) | ion dash | lance dash | (none) | afterburner dash | wake dash |
| Fighter 2 | claw snap arc | icicle spear | stinger dive | ember shower | water gun spits |
| Fighter 3 | split bolts + arc | splinter pair | leaf-blade slash | flamethrower | thunder strike |
| Fighter 4 (heavy) | rail-gun slug | hail cloud | thorn fan | nova ring | surf wave |
| Alien | ion spit | (none) | leaf volley | (none) | (none) |
| Elites | ion tethers, rail slugs | ice chips, hail | spore mortar, log roll | flame, magma shells | nets, depth charges, whirlpool sling |
| Boss signature | ion arcs, scan line | icicle drop, cold blast | vine lash, trunk toss | flame sweep, eruption columns | thunder strike, surf wave |
| Hostile shot body | neon slug | ice spear / hail chip | thorn / leaf / trunk | ember pellet / fireball | spout drop / pearl / depth charge |

## 6. Tide (surf waves, water guns, thunder, bubble mines, whirlpools, pearls)

Designed for the roster in `world-5-6-briefs.md` (12 units), built when Tide's roster lands (add-world phase 12b). Material: water, brine, barnacled brass, pearl,
lightning. Pink cue: **pink-white pressure core in a teal-white spray**, bodies in pale sea-foam/teal (hue 165-190, sat <= .4: the shield atom is 178, so the water is
mostly white, the pink is the only saturated colour), a pink bolt core in thunder.

| Enemy | Attack name | Reuses / new | Telegraph / fairness | Damage / hitbox | Counter | Pink cue + VFX | Sound | Size |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Limpet Mine | **Pressure jet** (bubble mine line) | `Laser`, Tide beam skin: a white-water jet, spray beads and a splash at the rail | aim line .7 s, a few bubbles rise from the mine at the charge | thickness .28 | off the line | pink-white core through white water | water jet "pssshh" | S (art) |
| Nautilus Bulwark | **Pearl fan** | `Shot` Shard x3 fan, Pearl skin: a scalloped clam cup with a pink pearl core (PC4: not a disc), trailing bubbles; 2.0 u/s | the shell opens 1.0 s | r .064 | between the pearls | | water "bloop" + shell clack | M (skin) |
| Remora | **Suction dash** | `Lunge` + foam wake | .55 s | body | the dash never tracks | | | S |
| Needlefish | **Water gun** | `Shot` Bolt x1 straight, up to 3 volleys, Spout skin: a fast water drop (teardrop, pink-white core) with a short spray tail, 3.8 u/s | beak flares .5 s | r .054 | keep moving | splash on the rails | jet spit | S (art) |
| Lantern Angler | **Thunder lure** | NEW `Strike` Thunder (shared with Ember): the lure flares .8 s, then a lane glyph on the pilot's x for .9 s, then a lightning column .25 s live; the bolt drops a thin harmless **arc** along the row from rail to rail for .15 s | **1.7 s total** (>= the old .8 s tell + a visible glyph) | column .36 wide, replaces one aimed bolt; 2 volleys | step off the glyph column | pink-white bolt in blue-white glow | thunder crack + rumble | L (shared `Strike`) |
| Hammerhead | **Surf wave** | NEW `Wave` (shared with Space's scan line): a foam-crested band .4 thick across the lane with a 1.6 u gap aimed at the pilot's x at the tell, falling at 2.6 u/s relative to the board; replaces the ring of 8; volleys 3 -> 2 | slams .9 s, ripple markers at the gap edges, **1.1 s** to the first contact | band .4 thick across the lane minus the gap, live ~1.2 s | go to the gap | pink foam crest | swell + crash | L |
| Wire Eel | **Static trail** | chaser, cosmetic electric trail | | | | | | S |
| Glow Jelly | cosmetic tendril sting | | | | | | | - |
| Rocks | none | | | | | | | - |

Elites (existing brains and ids from the brief; Tide shots are skins): Riptide Lancer `lance_dash` with a foam wake and Spout needles (S); **Trawler Maw** `slag_drop`
casts sinking **nets** (Glob pool skin: a mesh pool, fixed mass, swallows shots; S); Abyss Lamp `blink_shards` with a lure beam (Pearl shards; S);
**Brine Siege** `siege_cannon` lobs a **depth charge** (Orb skin: a bubble holding a horned mine; on the fuse it bursts into a bubble ring of pearls; reuses `Fuse()`; S);
Manta `claw_dive` with a wing-wave spray (S); **Pearl Bastion** `ward_curtain` of slow pearls with one gap (Pearl skin; S).
**Whirlpool** (optional): any Tide elite or the boss may `Sling` its Pearl shots through a ringed well drawn as a whirlpool (reuses `Sling`, the Singularity Hauler's curve; no ship pull, so the hook's "ship current" system is *not* required).

**Iron Kraken** (5): beak spit = Fan of **water cannon** spouts (Spout skin, bounce once: S); ink barrage = Lob rain of **ink blots** (Glob pool skin: dark ink pool, pink rim: S);
tentacle lasers = two **thunder arcs** from the tentacle tips (Beam, a jagged lightning skin: S); NEW signature (phase 1+): **thunder strike** (`Strike`, 3 lanes, 1.0 s);
NEW phase 3: **surf wave** (`Wave`, two bands 1.4 s apart with gaps on opposite sides).

## 7. Boss summary (5 attacks each, 2-3 per phase)

`BossDef.attacks` grows from 3 to 5; each `BossAttack` gets `minPhase` (1..3). Unlock: phase 1 = attacks[0] + signature 1 (2 attacks),
phase 2 = attacks[0..1] + signature 1 (3), phase 3 = all five rotating three at a time (the shorter final-phase cooldown stays).
`BossCatalog.UnlockedAttacks` generalises to filter by `minPhase`; `BossEncounter` already rotates what is unlocked.

| Boss | Phase 1 | Phase 2 | Phase 3 (adds) |
| --- | --- | --- | --- |
| Void Archon | rail slugs, ion arcs | + plasma shards | + pod lasers, scan line |
| Hoarfrost Leviathan | icicle spray, icicle drop | + glare beams | + blowhole hail, cold blast |
| Bloom Queen | stinger thorns, vine lash | + spore bloom | + acid cannons, trunk toss |
| Cinder Drake | fire breath, flame sweep | + furnace slugs | + brow laser, eruption columns |
| Iron Kraken (Tide) | beak spit, thunder strike | + ink barrage | + tentacle thunder arcs, surf wave |

Boss beams and the new bands/rings/strikes follow the same safe-corridor test (FR4) against `BossConfig` hit sizes.

## 8. Counts

| Thing | Count |
| --- | --- |
| New attack primitives (L) | 5: Jet, Wave, Blast, Strike, Lash |
| New projectile behaviours (M) | 6: Streak, Shatter, Flutter, Slash, Roll, Burst |
| Shot skins | 22 (Space 1 new: slug; Frost 5; Verdant 5 + Crescent; Ember 5; Tide 5) |
| Mine beam skins | 4 (Frost, Verdant, Ember, Tide); Space is the reference |
| Boss new attacks | 10 (2 per boss) |
| Elite attack changes | 1 new id (`log_roll`) + 1 new Verdant elite; all 15 existing elite defs get new shot art through the skin lookup, no behaviour change |
| Hostile sounds | 5 worlds x 4 cues = 20 base cues (+ variants) |
| Passive on purpose | rocks (14), chasers (4, they hunt), Gnat and three aliens; every other enemy gets a themed attack |
