import json
m=json.load(open('measure.json')); t=json.load(open('tiers.json')); lo=json.load(open('lockon.json')); lg=json.load(open('lang.json'))
WN=['space','frost','verdant','ember','tide','storm','desert']
PL=m['player']
P1080,P720=1080/7.44,720/7.44
o=[]
w=o.append
w('''# Ship scale, lock-on and design-language analysis

Branch `feat/ship-scale-lockon`. Written before any gameplay edit (phase 1); the implementation notes at the end are updated as the work lands.
Generated numbers come from `docs/tools/ship_audit/` (measure.py, sheet.py, lockon.py, lang.py): re-run them after any art or size change.

Contact sheets (every ship, current vs proposed, with the flown hull beside it): `docs/ship-scale-analysis/sheet_elites.png`, `docs/ship-scale-analysis/sheet_enemies.png`.

## 0. How big things are on a phone

* The gameplay camera is width-fitted: the view is **7.44 u wide** on every phone (`CameraFit.GameplayHalfWidth` 3.72), 13.2 u (16:9) to 18.2 u tall.
* So 1 u = **145 px** on a 1080-wide phone (the 1080x2420 reference) and **97 px** on a 720-wide small phone (720x1280 / 720x1600). 540-wide budget phones: 73 px.
* The flown hull is `ReferenceHullSize` 0.58 u x `ShipScale.Main` 1.35 = **0.78 u across = 114 px (1080) / 76 px (720)**.
* Art: enemy / fodder flipbooks are 192 px cells shown 0.66-1.3 u wide, i.e. **0.33-0.55 screen px per art px at 720** (the drawings are downscaled 2-3x; a 1 px outline or a 3 px eye is below one screen pixel). Elite strips are 192 px cells shown 1.45-2.07 u: 0.76-1.04 screen px per art px at 720 (about 1:1). Bosses: 384 px cells at 3.3 u (about 1.25 screen px per art px at 1080).
* Detail rule of thumb used here: a feature (eye, weapon port, cockpit, engine glow) is readable at **>= 3 screen px across**, and a 1 px outline survives only at >= 0.75 screen px per art px.

## 1. Lock-on

### 1.1 Why it feels like "locking in"

The framework gives every elite **perfect, zero-latency knowledge of the pilot** and aims with it at the last possible instant:

1. `EliteShip.Perceive`: `seen = pilot position, this frame`. Only a pause-teleport ever makes it lose you. There is no reaction delay.
2. Brains steer to `Goal(seen)` every frame: the gunship copies your height and x (plus a lane offset), the interceptor sits `followDistance` behind your exact x, the skirmisher / striker / reaver orbit your exact centre, the lancer holds a flank of your exact x. The only softening is the speed cap.
3. Many attacks read `ship.Seen` in `BeginAction`, i.e. **after** the tell is over: `blink_shards` (fires at your position at the moment it fires, after teleporting), `resin_mortar`, `ward_curtain` / `pearl_curtain` (the row is centred exactly on you), `gravity_sling`, `floe_cast`, `frost_bloom` orb, `armour_shatter` plated shards. A tell that tracks you all the way to the shot is not a warning: whatever you do during the tell is read and answered. Only attacks that lock at the **start** of the tell and draw where they will land (all Verdant / Tide telegraphs, `claw_dive`, `crescent_volley`, `ice_ram`) are fair, and those are the model.
4. Nose: facing turns at `turnRate x 1.4` (up to 420 deg/s) towards you for the interceptor, skirmisher and lancer brains: the nose snaps onto you, which reads as a hard lock.
5. Tells are short where it matters: Ash Wraith 0.45 s, Brass Vulture 0.5 s, Coalrunner 0.5 s, Sunstoke / Cryo Siren / Whiteout Sentinel 0.6 s. With aim live to the shot the warning is 0.

There is no velocity lead anywhere (nothing predicts you), so the problem is exactness and latency, not prediction.

### 1.2 Per-elite measurement (before / after the proposal)

Columns: `follow` = how the brain uses the pilot position; `aim` = when the shot's aim is fixed; `tracked` = seconds of tell during which the aim still follows you; `warning` = seconds between the aim being fixed and the shot / dash starting (what the player can react to); severity 0-5 (higher = locks in harder).

| Elite | World | Brain / attack | follow (now) | aim now | tell s (now -> new) | tracked s (now -> new) | warning s (now -> new) | sev |
|---|---|---|---|---|---|---|---|---|''')
for r in sorted(lo,key=lambda r:(-r['sev'],r['world'])):
    w('| %s | %s | %s / %s | %s | %s | %.2f -> %.2f | %.2f -> %.2f | %.2f -> %.2f | %d |'%(r['name'],r['world'],r['brain'],r['attack'],r['tight'],{'T':'tracks then locks','L':'LIVE until the shot','F':'locked at tell start (drawn)','N':'not aimed'}[r['cls']],r['tell'],r['tell2'],r['trk0'],r['trk2'],r['warn0'],r['warn2'],r['sev']))
w('''
"tracked" for `L` attacks was the whole tell (and, for `blink_shards`, also the blink and 35% of the action).

### 1.3 Proposal (generic, data-driven, personality kept per elite)

Three new knobs, all in `EliteDef` with defaults, so no elite is rewritten:

1. **`followLag` (s)** - the pilot position the *brain* steers to is `seen` run through an exponential low-pass with this time constant (`EliteShip.Follow`, fed to `Brain.Goal`; snapped on spawn and teleports). An elite no longer mirrors every twitch: it converges on you in ~3 x `followLag`. Values by personality: twitchy skirmishers 0.25, stalkers and strikers 0.30-0.35, the gunship 0.45 (the worst mirror), thorn lane 0.40, slow haulers / bastions / sieges / tugs 0.25 (they already crawl, so it is a no-op there), station elites (warden) 0.20.
2. **`aimCommit` (share of the tell, default 0.5)** - the aim point the attacks use (`EliteShip.AimSeen`) is live for the first `aimCommit` of the tell, then frozen. Every `ship.Seen` read in an attack's action moves to `AimSeen`: `blink_shards`, `resin_mortar`, `ward_curtain` / `pearl_curtain`, `gravity_sling`, `floe_cast`, `frost_bloom` orb, `armour_shatter` plated shards. Lance / rift rail / armour charge already lock at 0.5-0.6; they are unchanged. After the commit the player has `tell x (1 - aimCommit)` to move.
3. **Minimum tell**: aimed attacks (`T` and `L` above) get an effective tell of at least **0.6 s**, so the post-commit warning is >= 0.3 s (human reaction + a short sidestep); Ash Wraith (0.45) and Brass Vulture (0.5) go to 0.65 in data.
4. **`faceTurnMax` (deg/s, default 300)** caps how fast the hull turns to face you, whatever `turnRate` x 1.4 says (the skirmishers, striker, interceptor and lancer are 364-420): the nose now swings round instead of snapping.

What each personality keeps: Ash Wraith / Lens Matriarch / Rod Dancer stay fast, twitchy blinkers - they blink and shoot where you *were* half a tell ago, so the trick is to move after the glow; Sunstoke / Dune Stalker / Skyhook Harrier / Riptide are still relentless stalkers with a committed, baitable ram, just slower to re-centre on a juking player; Coalrunner keeps its lane beside you but trails your height and column by ~0.45 s instead of welding to them; Warden / bastion curtains and mortars keep their gap geometry, but the row is where you were at the commit, not where you are at the shot.

Tests (gate lane): `EliteLockOnTest` asserts for every def: `followLag` in [0.15, 0.6]; effective tell x (1 - aimCommit) >= 0.3 for aimed attacks; the nose never turns faster than `faceTurnMax` (stepped); after the commit, moving the pilot 2 u does not move any `AimSeen`-driven first shot/spot by more than the pilot's drift before the commit (aim is frozen); the brain's goal lags a stepped pilot by about `followLag`.

## 2. Size audit
''')
w('### 2.1 Tier system')
w('''All sizes are the drawn silhouette's longest side, in world units, from the idle cells (not the cell). "x player" is relative to the flown hull (0.78 u).

| Tier | What | Today (drawn, u) | Today x player | Proposed | Proposed x player | px @1080 / @720 (proposed) |
|---|---|---|---|---|---|---|
| T0 fodder | rocks, mites / aliens | 0.52-0.74 | 0.7-0.9 | **x1.20** -> 0.62-0.89 | 0.8-1.1 | 90-129 / 60-86 |
| T1 fighter | fighters, chasers | 0.75-0.90 | 0.95-1.15 | **x1.20** -> 0.90-1.08 | 1.15-1.4 | 131-157 / 87-104 |
| T2 heavy | the world "Big" (Bastion, Magma Skull, ...) | 1.10-1.22 | 1.4-1.6 | **x1.20** -> 1.32-1.46 | 1.7-1.9 | 192-212 / 128-141 |
| T3 elite | all elites | 0.95-1.69 | 1.2-2.2 | **floor 1.50 u** (1.65 u for armoured / slow classes) | >= 1.9 / 2.1 | >= 218 / 145 (240 / 160 heavy) |
| T4 boss | the seven bosses | 2.85-3.21 | 3.6-4.1 | unchanged | | 413-466 / 275-311 |
| rail mines | | 0.65 u | 0.83 | unchanged (rail-mounted; collider pinned) | | 94 / 63 |

Rules: (a) tiers strictly increase: fodder < fighter < heavy (1.35 x fighter) < elite floor (1.07 x heavy, 1.2 x for the armoured 1.65 u class) < boss (about 2 x elite); (b) elite floor = 1.9 x the flown hull (`ShipScale.Main` x `ReferenceHullSize`), armoured classes (hauler, bastion, siege, tug, herder, tender, ironclad, logger, abyss, netter, pearler brains) 2.1 x, so the player's own ship never reads as larger than the elite it is fighting; (c) elites that are already above the floor (Ember: Ash Wraith 1.51, Vulture 1.56, Cauterizer 1.69, Sunstoke 1.47 -> 1.5) are left alone, which keeps the originals the user already approved; (d) hit radius scales with the drawing (x0.95 of the size factor, 5% leniency), never sprite-only.

### 2.2 Elites (36)

`k` = proposed scale of `cellWorldSize` and `hullRadius` (art, muzzles, nozzles, hearts, FX all follow because they are in art px / cell fractions). Detail columns: the three largest bright accent features found in the drawing (px across on screen at 720), before -> after; a value under 3 is flagged `!`.
''')
w('| Elite | World | Long side now (u) | px 1080 / 720 | x player | k | Long side new (u) | px 1080 / 720 new | cellWorldSize | hullRadius | accent features @720 now -> after | pale outline | priority |')
w('|---|---|---|---|---|---|---|---|---|---|---|---|---|')
rows=[]
for key,e in m['elites'].items():
    k=t['elites'][key]['k']
    d=json.load(open('/Users/sina/Developer/Pause-shipscale/Pause/Assets/Art/Resources/Elites/Defs/%s.json'%key))
    feats=e['top_diam_small']
    now=' / '.join(('%.1f'%x)+('!' if x<3 else '') for x in feats)
    new=' / '.join(('%.1f'%(x*k))+('!' if x*k<3 else '') for x in feats)
    pr=(k-1)*3+sum(1 for x in feats if x<3)*.5
    rows.append((pr,key,e,k,d,now,new))
for pr,key,e,k,d,now,new in sorted(rows,key=lambda x:-x[0]):
    hr=d['hullRadius']
    w('| %s | %s | %.2f | %d / %d | %.2f | x%.2f | %.2f | %d / %d | %.2f -> %.2f | %.2f -> %.2f | %s -> %s | %s | %s |'%(e['name'],e['world'],e['long_u'],e['long_big'],e['long_small'],e['long_u']/PL['long_u'],k,e['long_u']*k,e['long_big']*k,e['long_small']*k,d['cellWorldSize'],round(d['cellWorldSize']*k,2),hr,round(hr*k*.95,3) if k>1 else hr,now,new,'yes' if e['pale'] else 'no','HIGH' if pr>=1.8 else 'med' if pr>=.8 else 'low'))
w('''
Reading it: the 6 Desert, 5 Storm, 5 Tide, 5 Verdant, 5 Frost and 3 Space elites are only 1.2-1.5 x the player's ship (0.95-1.2 u, 92-111 px at 720) - the Tide set (Bathyscaphe 0.97, Pearl Warden 0.95, Trawler 0.98, Thunderfin 0.98) and Frost Floe Harrower / Rimebreaker (1.01-1.03) are the smallest and need x1.5-1.7. Their art draws only 63-67% of its cell (the cell is 1.5 u, the ship 0.95-1.0 u), which is why they look small next to Ember's 1.45-1.7 u at the same `cellWorldSize`: **the cell size is not the ship size**. The Ember originals (the ones that read well) are 82% fill. Storm / Desert / Tide / Verdant elites also have most of their accents under 3 screen px at 720 (Storm 2.4-3.3, Tide 1.7-2.6, Verdant Thornlash 1.5-1.7).

### 2.3 Enemies, fodder and heavies (75) at x1.2

Same method; `px` = longest side on screen. The enemy flipbooks are the least readable things in the game: 0.33-0.45 screen px per art px at 720 (fighters 0.45, rocks 0.33, aliens 0.34-0.49), so a 4 px eye is 1.5 px and a 1 px pale outline cannot render. x1.2 only buys 20% (to 0.40-0.54): it fixes the size tier but **not** the thin-detail problem, which needs the Codex redraw list in 2.5.
''')
w('| Ship | World | Role | Long side (u) | px 1080 / 720 | x player | art->screen px @720 | x1.2 -> u | accent features @720 now -> x1.2 | pale outline |')
w('|---|---|---|---|---|---|---|---|---|---|')
for key,e in m['enemies'].items():
    feats=e['top_diam_small']
    now=' / '.join(('%.1f'%x)+('!' if x<3 else '') for x in feats)
    new=' / '.join(('%.1f'%(x*1.2))+('!' if x*1.2<3 else '') for x in feats)
    w('| %s | %s | %s | %.2f | %d / %d | %.2f | %.2f | %.2f | %s -> %s | %s |'%(e['name'],WN[e['world']],{'R':'rock','F':'fighter','C':'chaser','A':'alien','B':'heavy'}[e['role']],e['long_u'],e['long_big'],e['long_small'],e['long_u']/PL['long_u'],e['sc_small'],e['long_u']*1.2,now,new,'yes' if e['pale'] else 'no'))
w('''
Smallest on screen (0.52-0.61 u, 49-60 px at 720): all rocks - Tide Brain Coral 0.52, Kelp Islet / Staghorn / Urchin 0.54, Ember Magma / Cinder 0.54, Storm Cloud Islet 0.54, Desert Glass Spire 0.56, Verdant Bramble Knot 0.55 - and the Verdant / Space aliens (0.56 / 0.59). These are smaller than the player's own hull (0.78 u).

### 2.4 Mines, hazards, boss, shots

* **Mines**: 0.65 u (`MineWidth`), 94 / 63 px: smaller than the player; legible only through their glow. Left unchanged here (they ride the rail inner edge, the collider is pinned at 0.621 u by `RailMineArtTest`, rail clearance and the laser mount x follow the rail inset); listed as a follow-up with its own redraw.
* **Rocks** also carry `HazardSize` tiers (small / typical / large around the nominal); the tiers keep the same mean area and scale with the role size, so x1.2 on the role size scales the whole rock distribution.
* **Elite hazards** (Frost slabs 0.78 u, Verdant trunks, Tide pearls 0.36, Frost orbs 0.36): already deliberate, drawn and sized as mechanics, not ships. Elite shots are 0.16-0.5 u (23-72 px at 1080); anything under 0.2 u is 29 / 19 px and sits at the edge: Coalrunner / Cryo bolts 0.2, the lowest are 0.16. Left alone (shot size is a fairness number), but the minimum shot size is asserted at 0.16 u.
* **Bosses**: 2.85-3.21 u drawn (3.3 u cell, ~4x the player, 413-466 px at 1080). Fine; unchanged.

### 2.5 Priorities and the redraw list

1. **Elites below the floor** (Tide x4, Frost Floe Harrower / Rimebreaker, Storm x5, Desert x6, Verdant Timber Hauler / Thornlash / Sporebloom): scale, data only.
2. **Enemies**: scale x1.2 (code constants), then redraw for detail: ships whose key features (eyes, ports) are under ~3 px even after x1.2 - Ember fighters 1 / 3 (eye 1.4 / 0.9 px), Storm Cyclone Drone / fighters 2-4, Tide Wire Eel / Remora / Hammerhead, Desert fighter 4, Storm / Tide / Desert aliens and rocks, Verdant Hornet / Gnat features, Tide rocks, Frost Frost Lancer. The brief for Codex: bold the key eye / weapon / cockpit to >= 5 art px at 192 px cells and give a 2 px pale (not 1 px) outline for ships drawn at 0.33-0.5 screen px per art px, or render these on a 256 px cell.
3. **Elites with sub-3 px features at 720 even after scaling**: Verdant Thornlash, Tide Riptide / Thunderfin / Trawler, Desert Dune Stalker / Lens Matriarch / Scorpion Walker, Storm Refinery Tender / Static Bastion / Skyhook Harrier: Codex redraws at the new size (staged, not wired).

## 3. Design language
''')
# world stats
import collections
by=collections.defaultdict(list)
for k,v in lg.items(): by[(k.split(':')[0],v['world'])].append(v)
import numpy as np
w('Measured on the idle drawings (mean over the world): luminance (0-1), saturation, share of near-white highlights (>0.75), share of near-black (<0.18), top-minus-bottom luminance (light from above if > 0), pale outline detected (a bright 1 px boundary against a darker interior).\n')
w('| Group | World | lum | sat | highlights | near-black | top-bottom | pale outline |')
w('|---|---|---|---|---|---|---|---|')
for (kind,wd),l in sorted(by.items()):
    f=lambda key: np.mean([x[key] for x in l])
    w('| %s | %s | %.2f | %.2f | %.2f | %.2f | %+.3f | %d/%d |'%(kind,wd,f('lum'),f('sat'),f('bright'),f('dark'),f('top_minus_bottom'),sum(1 for x in l if x['pale']),len(l)))
w('''
### 3.1 What is consistent

* Light from the top (+0.01 to +0.09 top-bottom everywhere; only Desert Mirage Wisp, Ember Brand and Storm Cloud Islet are bottom-lit).
* No pink outside Space: 0 of 35+40 non-Space ships carry magenta/pink accents (Space: 37-40%, by design).
* Each world has its own material palette and enemies match their elites' world (bronze / ice-blue / green-gold / teal / slate / sand).

### 3.2 Where it breaks

1. **Outline**: the thin pale outline (`enemy-visibility-add-border`) is present on Storm enemies (11/11), Ember (10/11), Tide (8/11), Verdant (7/11) but on **no Frost or Space enemy and no Frost, Space, Storm or Tide elite**; Desert has it on 2/11 enemies and 3/6 elites. So the same world reads two different ways: a Storm fighter is outlined, the Storm elite next to it is not (and is the darkest, lowest-contrast set: mean lum 0.25, 3% highlights).
2. **Value structure**: elites are darker and have fewer highlights than the same world's enemies in Ember, Storm, Tide, Verdant and Desert (Frost and Space are level; Ember 0.23 / 1% vs 0.26 / 6%; Storm 0.25 / 3% vs 0.31 / 14%; Tide 0.30 / 6% vs 0.39 / 19%; Verdant 0.26 / 3% vs 0.50 / 22%). A bigger, more dangerous ship should not be the dimmer one.
3. **Detail density**: elites are drawn at roughly 1:1 and carry 20-110 accent specks (Frost Floe Harrower: 112, Frost Whiteout 99, Desert Mirror Bastion 27), the enemies at 0.33-0.55 px/px. Small-specks-plus-no-outline is what makes Frost and Storm ships dissolve into the backdrop.
4. **Eye / weapon motif**: Ember and Space ships read by one hot core (orange or magenta); Verdant by a green eye; Tide and Storm have no consistent single focal glow (Storm Anvil Cloud / Refinery Tender: no accent at all). Rule below.
5. **Neon accent usage**: Space has magenta + cyan engines; every other world should accent with its own cue colour (`WorldCue`): the Ember / Desert / Verdant accents are good; Storm and Tide elites under-use theirs (saturation 0.31 for Storm elites vs 0.38 for Storm enemies).
6. **Dark outlines**: Verdant Resin Warden is hard-outlined in near-black, unlike the pale outline rule used by the rest.

### 3.3 The design language (rules)

1. **Size tiers** (section 2.1). A ship's tier, not its world, decides its on-screen size.
2. **Silhouette first**: one dominant shape per ship with a recognisable nose; role is told by silhouette (wings = fast, slab = hauler, claws = striker); one asymmetry at most.
3. **Outline**: every enemy, elite and mine has a **pale (value > 0.7, world-tinted) outline, 1 art px for elites (1:1 drawn), 2 art px for ships shown below 0.6 screen px per art px**. No dark outlines, no outline-free ships.
4. **Value structure**: three values - near-black underside, mid body, bright rim and accents; highlights on >= 4% of the pixels; light from the top-left; elites at least as bright as their world's enemies.
5. **Focal point**: exactly one hot accent in the world's cue colour (cockpit / eye / weapon core), >= 6 art px across for elites (>= 3 screen px at 720 after scaling) and >= 5 art px for fodder; weapons are drawn lit when charged (the tell cell), never smaller than that.
6. **Detail density**: no detail the size of one art pixel inside the body; panel lines in the body hue, not in white specks; 20-40 accent specks maximum for an elite.
7. **Neon**: engines / weapon cores are the neon, in the world's cue colour (no pink outside Space, hearts and shots stay magenta / violet / cyan per `EliteTest`); the player's red is never used.
8. **Player ships are not touched**.

### 3.4 Deviations (ships to redraw or touch up, staged for vetting)

* No pale outline on an elite: Frost x5 (Cryo Siren, Floe Harrower, Glacier Tender, Rimebreaker, Whiteout Sentinel), Space x4, Storm x5, Tide x5, Desert Glass Cannon / Lens Matriarch / Mirror Bastion, Ember Brass Vulture / Cauterizer / Kilnback / Sunstoke, Verdant Resin Warden (which is hard-outlined in near-black instead).
* No pale outline on an enemy: all ten Frost and Space ones and Desert x9 of 11, Tide x3 of 11, Verdant x4 of 11 (the exact list is the `pale outline` column of 2.3).
* Darkest and lowest-contrast elites (the first to redraw): Storm Anvil Cloud, Refinery Tender, Static Bastion, Rod Dancer; Space Singularity Hauler / Eventide Bastion; Ember Kilnback / Cauterizer.
* Accent-starved: Storm elites (accents 2.4-3.3 px even at x1.5), Tide Thunderfin / Trawler / Riptide, Verdant Thornlash.

The mechanical fix for the outline (a 1 px, world-tinted pale outline script run over the elite strips: `enemy-visibility-add-border`) can be done without a redraw and is part of the staged art branch; the focal-point / value changes need Codex.

## 4. Knock-on effects of the scale

| Area | What changes |
|---|---|
| Elite defs | `cellWorldSize` x k and `hullRadius` x 0.95k (data); muzzles and nozzles are in art px and follow; hearts orbit is a share of `hullRadius`; shots keep their size |
| `EliteSystem` / evasion / `ClampGoal` | all read `hullRadius`: rail clearance and the avoid radius grow with it (checked by `EliteEvasionTest`, `EliteSurvivalProbe`) |
| Landing sites | parked sprite is drawn `scale .3` of the new size: the parked ship grows with it; `landscape pads` are authored per landmark and the site scale is unchanged |
| Lift-off / dock flare / heat shimmer | use `cellWorldSize`: follow |
| Codex card / burst / title screen traffic elite | scale with `cellWorldSize` (`EliteScale`); the codex card must still fit (`CodexTest`) |
| Death strips, debris | cell-sized: follow |
| Enemy roster | `EnemyRoster.FrameWorldSize` / `ColliderSize` / `TargetWidth` / `TargetCollider` (x1.2 for Rock, Fighter, Chaser, Alien, Big), roster pins in `EnemyRosterTest`, `HazardSizeTest` (target = TargetWidth x tier), `SpawnSpaceTest` (bodies are >= TargetWidth) |
| Enemy muzzle points | `EnemyBehaviour.muzzle` (u below the centre) x 1.2 so shots still leave the cannon |
| Spawn lanes / formation widths | `SpawnLane.HalfExtents` / `SpawnSpace.BodyHalf` read the collider and `TargetWidth`: rows get 1-2 slots fewer; `EnemyDensityTest` re-tuned if the probe moves; `SpawnLane.ShipGap` (the player's gap) unchanged: difficulty decision, flagged |
| Rail clearance | `BossRails` inner edge unchanged; fighters 1.0 u of a 6.4 u lane still leave > 5 u; elites 1.65 u: `ClampGoal` uses hullRadius |
| Shield burst | scales with the hit body (`TargetExplosion`); unchanged |
| Tests that assert sizes | `EnemyRosterTest` (+/-15% of the new target, heavies 1.2-1.5 u), `HazardSizeTest`, `SpawnSpaceTest`, `EliteTest` (hearts vs player, muzzles on art), `ScreenFitTest` (elites still fit), new `ShipTierTest` (tier floors) |

## 5. What this branch does

Implementation status and verification are appended below as the work lands.
''')
open('/Users/sina/Developer/Pause-shipscale/docs/ship-scale-and-lockon-analysis.md','w').write('\n'.join(o))
print(len('\n'.join(o)))
