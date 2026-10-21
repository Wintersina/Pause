# Elite scripts: roles, timelines, tempo

Every elite is one authored fight: Entrance, Phase 1 (full hearts, a fixed loop), Phase 2 (after the first lost hearts, a visibly
different loop), Last heart (desperation), Exit (warning tell, then the escape). The script lives in the def json
(`Pause/Assets/Art/Resources/Elites/Defs/<key>.json`, block `"script"`); the machinery is `EliteScript.cs`.

## How a script reads

```json
"script": {
  "tempo": 1.35, "exitTell": 1.4, "lastExit": "none",
  "entrance": { "kind": "slam", "seconds": 1.6, "amp": 1.4 },
  "phase1": { "speed": 1.0, "gap": 1.0, "loop": [ {"wait": 2.2, "move": "L", "attack": "wide"}, {"wait": 1.5, "attack": "-", "move": "R"} ] },
  "phase2": { ... }, "last": { ... },
  "variants": [ { "name": "wide", "set": [ {"key": "shotCount", "value": 5}, {"key": "shotSpread", "value": 24} ] } ]
}
```

* `wait` seconds before a beat (x tempo x phase gap / AttackRate 1.2). `move` is a token the brain reads (`OnScriptMove`),
  `attack` is `""` (plain), `"-"` (movement beat, no attack) or a variant name.
* A variant overrides def numbers (`EliteTune.Keys`) for that one attack only.
* Phase 2 starts at "all but the last heart" (an armoured 5-heart elite: at 3 hearts); the last heart is the desperation phase.
* No coin flips: edges, lanes, perches and sides come from `Script.Cycle/Alternate` (a counter), aim wobble from `Script.Offset`.
* A lost heart is a flinch (knock-back + shudder). `hurtHide` elites may make ONE scripted exit-and-return at the last heart
  (`lastExit`: left / right / top, `none` to stay).
* Roamers turn round at the rail (a U-turn arc inside the view); they never leave the view except for the one scripted exit.
* Engage (`engageSeconds`) ends with the exit tell (`exitTell`, pulsing hull + no new attacks), then the escape boost.

Entrance kinds: `arc` (curved sweep in), `overshoot` (rushes past and settles), `slam` (rises, then drops onto the spot),
`swirl` (decaying spiral), `dash` (straight, hard brake). Each ends with a spark burst.

Tempo / engage defaults (PR A): armoured heavies tempo 1.35 / 55 s; light quick jabbers 0.8 / 42 s; snipers 1.2 / 48 s; others 1.1 / 48 s.

## Roles per world

Role vocabulary: **anchor** (tank that holds ground), **sniper** (long-range single aimed threats), **sweeper** (moves across and
covers the board), **denier** (area denial: walls, pools, fields), **summoner** (adds things that live on), **rammer** (committed charges),
**diver** (swoops through and out).

### Space

| Elite | Role | Entrance | Phase 1 loop (fixed) | Phase 2 | Last heart | Exit | Tempo / engage |
|---|---|---|---|---|---|---|---|
| Rift Lancer | sniper | dash from the gate side | perch right flank: snipe, snipe, mine run; cross to left flank (token `R`/`L`), repeat | double-rail snipes, perches swap every shot | triple rail from the top centre, then its one scripted exit by the top | tell, boost out the top | 1.2 / 48 |
| Orbit Reaver | sweeper | swirl | racetrack loops: pass right to left on the upper lane, U-turn at the rail, return on the lower lane; one volley per pass | faster loops (x1.25), volley is 5 shards | figure-eight with a volley at each crossing, one scripted exit left | tell, boost | 0.8 / 42 |
| Eventide Bastion | anchor / denier | slam | slides at a crawl; curtain A (gap left), curtain B (gap right), shield mine beat | two staggered rows | rows plus mines every beat | tell, boost | 1.35 / 55 |
| Singularity Hauler | summoner / rammer | overshoot | tow-sling lane left, well, tow-sling lane right, well | slings in pairs | double well + sling | tell, boost | 1.35 / 55 |

### Frost

| Elite | Role | Entrance | Phase 1 | Phase 2 | Last heart | Exit | Tempo / engage |
|---|---|---|---|---|---|---|---|
| Rimebreaker | rammer | slam from the top | one ice-crack run per cycle on lanes 1, 3, 2 (fixed order), then a U-turn up the rail and a rest at the top | runs in pairs | triple run, lanes left to right | tell, boost | 0.8 / 42 |
| Floe Harrower | sweeper / denier | arc | slides the top rail away from the pilot: floe_cast row, single skid slabs between | slab rows with the gap moving a hand each cast | rows back to back | tell, boost | 1.1 / 48 |
| Cryo Siren | sniper | swirl | three fixed perches left, right, centre; one bloom per perch; a short VISIBLE hop (blink trail) between them | two blooms | beam every cast | tell, boost | 1.2 / 48 |
| Glacier Tender | summoner | overshoot | hangs near the top; drones out (2), fog vent, drones refill | three drones | all drones released at once | tell, boost | 1.35 / 55 |
| Whiteout Sentinel | anchor | slam | patrol legs across the top; plates soak, spray shards at each end | plates stripped: icicle barrage lanes | barrage back to back | tell, boost | 1.35 / 55 |

### Verdant

| Elite | Role | Entrance | Phase 1 | Phase 2 | Last heart | Exit | Tempo / engage |
|---|---|---|---|---|---|---|---|
| Timber Hauler | denier | slam | lobs trunks onto fixed row (left, centre, right); rolls to the nearer rail | two trunk rows | trunks plus a second wave | tell, boost | 1.35 / 55 |
| Thornlash | rammer (melee) | arc | latches beside the left rail, whip, latches right rail, whip, swing across the top | double whip | whip from both rails | tell, boost | 0.8 / 42 |
| Sporebloom | summoner / denier | swirl | floats, spore burst left, right; sinks and rises at fixed canopy slots | more pods | all pods | tell, boost | 1.1 / 48 |
| Leafblade | diver | dash | circle, dive down a drawn lane (lanes cycle left, right, centre), crescent along the row | two crescents | dive pair | tell, boost | 0.8 / 42 |
| Resin Warden | sniper / mortar | arc | four canopy perches in fixed order, mortar row at each | wider rows | double mortar | tell, boost | 1.1 / 48 |

### Ember

All six stay on screen (none roams). Sunstoke and the Coalrunner work the two side rails, the Vulture circles high inside the view, the
Wraith and the Cauterizer hold the top, the Kilnback is planted there. Entrances and exits are in the def json (`script.entrance`, `lastExit`).

| Elite | Role | Entrance | Phase 1 | Phase 2 | Last heart | Exit | Tempo / engage |
|---|---|---|---|---|---|---|---|
| Sunstoke | rammer | dash (1.2 s) | works the rails: lance pass across on lane 0, a U-turn arc up/down the far rail to lane 1, return pass, then lane 2 (tokens `lane:N`, rails alternate; heat needles back along the lane) | pairs of passes back to back, the second a swift one (lanes 1,2 then 0,1) | triple swift pass (lanes 0,1,2) then a wide fan | tell, one scripted exit left at the last heart (back in from the right), boost | 0.8 / 42 |
| Coalrunner | sweeper (stop and fire) | overshoot (1.4 s) | races rail to rail along the top third (`R`, `L` tokens, the band steps down on a 3-cycle), STOPS dead at each rail and fires the crossing bolt volley standing (tight, wide, tight, wide) | coal clump on every stop | two clumps and a wider volley, faster legs; one scripted exit right | tell, boost | 0.8 / 42 |
| Brass Vulture | diver | swirl (1.6 s) | circles high INSIDE the view (flat ellipse, `wide` / `tight` tokens); the ring cycles through three slots (on the pilot, a hand either side) dive after dive, then pulls up to the circle | extra offset dive: pairs, the second a fast one (more scrap) | double fast dive; one scripted exit by the top | tell, boost | 0.8 / 42 |
| Ash Wraith | sniper / blinker | arc (1.5 s) | blinks between three high perches in the script's order 2,1,0,1 (`perch:N`); an ember cloud where it stood, a shard fan from where it lands | a second cloud where it lands too; order 0,2,1; five shards | clouds at both ends, seven shards, faster; one scripted exit by the top | tell, boost | 0.8 / 42 |
| Kilnback | anchor | slam (1.7 s) | planted at the top centre; slag lanes, then lava lane, alternating (no sliding pendulum) | heaves to the left then the right station (`L`, `R`); five blobs and two lava strips | slag (7 blobs) and lava (2 strips) back to back, quick; back at the centre | tell, boost (no scripted exit: armoured) | 1.35 / 55 |
| Cauterizer | artillery | overshoot (2.0 s, wide) | parks high and TRACKS the pilot's column (`track`); sight line + piercing shell down it, holds (`hold`) for a firebomb beat, shell, bombs | shells come as two lanes with a gap (the pilot's column between them); bombs, three-spot bombs | shell and three bombs at once, then a double shell | tell, boost (no scripted exit: armoured) | 1.35 / 55 |

Mechanics notes (Ember): the Kilnback's and the Cauterizer's casts are chosen by the variants through `hazardArmour` (a slab-only number that
no Ember attack otherwise reads): Kilnback 0 dump / 1 spill, Cauterizer 0 shell / 1 bombs / 2 double shell / 3 shell and bombs. The Coalrunner's
coal clumps are `hazardCount` (0 none, 1, 2); the Wraith's clouds a cast are `hazardCount` (1 where it stood, 2 and where it lands); the
Kilnback's strips are `hazardCount` too (1 or 2).

### Tide

| Elite | Role | Entrance | Phase 1 | Phase 2 | Last heart | Exit | Tempo / engage |
|---|---|---|---|---|---|---|---|
| Riptide | sweeper (wave rider) | arc | sine wave across the top third, descending one lane per pass, foam wake, rolling U-turn at the rail | two wakes | wake at both rails | tell, boost | 0.8 / 42 |
| Bathyscaphe | summoner / denier | slam | surfaces at fixed column 1, 3, 2: mine chain down to the pilot's column, hold; slides low along the zone rather than leaving | longer chains | chain plus a second column | tell, boost | 1.35 / 55 |
| Trawler | denier | dash | works the left flank: net lines, turn at the top, works the right flank | double nets | nets from both rails | tell, boost | 1.1 / 48 |
| Thunderfin | sniper / denier | swirl | slow ellipse; thunder columns sweep in the direction it flies, direction flips each cast | faster ellipse | both directions at once | tell, boost | 1.1 / 48 |
| Pearl Warden | anchor | slam | three stations in fixed order; pearl spiral with the gap toward the pilot | wider spirals | double spiral | tell, boost | 1.35 / 55 |

### Storm

| Elite | Role | Entrance | Phase 1 | Phase 2 | Last heart | Exit | Tempo / engage |
|---|---|---|---|---|---|---|---|
| Anvil Cloud | denier (stationary) | slam | holds three fixed stations left, centre, right; diamond of static fields at each | fifth field at the centre | fields everywhere | tell, boost | 1.1 / 48 |
| Refinery Tender | summoner | overshoot | slow airship: a leg across the top laying flare staircase, wide U-turn at the rail one tier lower, next leg | tier lowers faster | flares both legs | tell, boost | 1.35 / 55 |
| Rod Dancer | sweeper / striker | swirl | figure eight; rod columns strike outside in | inside out | both | tell, boost | 0.8 / 42 |
| Skyhook Harrier | diver | dash | V-shaped swoop: top corner down to mid centre and up to the opposite corner, cables raked during the dip | deeper dip | double rake | tell, boost | 0.8 / 42 |
| Static Bastion | anchor / wall | slam | crawls the top; hail wall with the gap sliding a hand per wave | three waves | wall that closes in | tell, boost | 1.35 / 55 |

### Desert

| Elite | Role | Entrance | Phase 1 | Phase 2 | Last heart | Exit | Tempo / engage |
|---|---|---|---|---|---|---|---|
| Dune Stalker | diver / ambusher | slam (erupts) | low rippling band mid-board, worm trail of four bursts aimed along the pilot's row, rises to the top and dives again | double trail | trails from both sides | tell, boost | 1.1 / 48 |
| Caravan Hauler | anchor | overshoot | plods legs with a rest at each end, throws only while resting: quicksand row | second row | rows plus a leg shake | tell, boost | 1.35 / 55 |
| Lens Matriarch | sweeper | arc | zig-zag, halts mid-pass: glass fan | double fan | fan every halt | tell, boost | 1.1 / 48 |
| Glass Cannon | sniper | dash | three fixed columns left, centre, right; beam fan from each | fan widens | beams from all three | tell, boost | 1.2 / 48 |
| Scorpion Skiff | rammer / flanker | swirl | holds a flank, scuttles across after every throw; boulder from the flank | two boulders | boulders both flanks | tell, boost | 1.1 / 48 |
| Mirror Bastion | anchor / wall | slam | slides UP AND DOWN one side rail (not along the top); dust wall sweeps from the opposite rail; rails swap each phase | double wave | wall plus mirror wall | tell, boost | 1.35 / 55 |

## Clone fixes

Strafe-across-the-top-and-drop: **Riptide** wave-rider (sine, descending lanes, rolling U-turn, slow); **Coalrunner** fast straight
strafe that only fires when stopped at a rail; **Dune Stalker** low rippling mid-board band that erupts upward; **Skyhook Harrier**
V-shaped swoop with a deep dip; **Refinery Tender** slow tiered airship that gets lower every leg.

Pendulum-and-cast: **Anvil Cloud** fixed three-station caster that does not slide; **Static Bastion** crawl plus closing hail wall;
**Mirror Bastion** vertical slide along one rail; **Kilnback** planted lane and lava anchor.

## Decisions to confirm

* Roamers keep to the view: the Ember Sunstoke, Brass Vulture, the Tide Bathyscaphe and the Dune Stalker were "off-screen ambushers"
  by identity. They now stay on screen (U-turn at the rail, low band, dive from the top of the view); only the one scripted last-heart
  exit leaves the view. Confirm this reading.
* The Frost Cryo Siren hop and the Verdant Sporebloom sink were off-screen vanishes; they are now short visible blinks and canopy
  dips. Confirm.
* `hurtHide` now means "makes the one scripted exit at the last heart". Elites that should not leave at all get `lastExit: "none"`.
* Rimebreaker no longer exits through the bottom: it runs down the lane, then arcs back up the rail inside the view.
