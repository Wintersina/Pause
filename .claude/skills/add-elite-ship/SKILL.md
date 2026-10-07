---
name: add-elite-ship
description: Add or wire a new elite enemy ship (from Codex's art strip) into Pause's elite system with its own personality and custom attack pattern. Use when Codex delivers a new elite strip (`<world>_elite_<name>.png`, or a bare `<name>.png` with a manifest -- Frost / Verdant / Space elites, or more Ember ones), when an elite needs its points re-measured after an art change, when optional parked / lift-off / death strips arrive, or when a world needs elite landing sites.
---

# Add an elite ship

Pause's elites are data-driven: an elite is **one JSON def + its art strip**.
The framework (Unity project root `Pause/`, code in
`Pause/Assets/Scripts/Gameplay/Elites/`) does the rest:

| Piece | File | What it does |
| --- | --- | --- |
| `EliteDef` / `EliteCatalog` | `EliteDef.cs` | the def (JSON in `Assets/Art/Resources/Elites/Defs/<key>.json`), loaded by world |
| `EliteCells` | `EliteDef.cs` | the def's `cells` map: which strip cell is flight / parked / lift-off / banks / tell / action / hit / damaged |
| `EliteArt` / `EliteFxArt` | `EliteArt.cs` | slices `Resources/Elites/<World>/<key>.png`; optional `_parked/_liftoff/_death` strips; procedural placeholder FX sprites |
| `EliteShip` | `EliteShip.cs` | life cycle Parked → LiftOff → Join → Follow → Attack → (hit) → Dead; steering, dodging, crashes, damage hooks, muzzles |
| `EliteBrain` (+8) | `EliteBrains.cs` | personalities: `interceptor`, `gunship`, `striker`, `hauler`, `skirmisher`, `siege`, `breaker` (Rimebreaker), `warden` (Resin Warden) |
| `EliteAttack` (+8) | `EliteAttacks.cs` | attacks: `lance_dash`, `broadside`, `claw_dive`, `slag_drop`, `blink_shards`, `siege_cannon`, `ice_ram`, `resin_mortar` |
| `EliteShots` | `EliteShots.cs` | pooled shots (`bolt`, `shard`, `slag`, `shell`, `glob` = lobbed then pools), `shotBounces` off rails, friendly fire |
| `EliteHearts` | `EliteHearts.cs` | the 2 hearts (shared `HeartOrbit` with the player's `ShipLivesIndicator`) |
| `EliteFx`, `EliteDeath`, `EliteRewards` | `EliteFx.cs` | dust / shimmer / sparks / debris; pluggable death; 50 score + 15 dust + "ELITE DOWN" |
| `EliteDirector` | `EliteDirector.cs` | spawning rules (max 3, groups 1-3, not first 20 s / boss / tutorial / final choice) |
| landing sites | `Scripts/Worlds/Backdrop/LandingSites.cs`, `BackdropDirector.LandingSites` | where parked elites sit, per world backdrop |
| art copy | `Assets/Editor/EliteArtSync.cs` | copies Codex's final strips into Resources (bare `<name>.png` -> `<world>_elite_<name>.png`), fixes alpha on import |
| tests | `Assets/Editor/Tests/EliteTest.cs` | walks **every** def automatically |
| previews | `Assets/Editor/Tools/ElitePreview.cs` + `scripts/make_preview_gif.py` | life-cycle GIF + contact sheet per elite |

Unity: `/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity`.
Always pass an **absolute** `-projectPath` (the inner `Pause/` folder), run with
the working directory `Pause/`, and give every run its own `-logFile`.

## Constraints (read first)

- **Never edit Codex's art** in `Assets/Art/Enemies/Elite/**` (or `Staging/`).
  Don't commit the `.meta` files Unity generates there. The game uses the
  copies `EliteArtSync` makes under `Assets/Art/Resources/Elites/<World>/`
  (those copies + their metas *are* committed).
- Ignore `*_candidate.png` / `*_concept.png` (Codex's working files; the sync skips them).
- **Enemies never use the player's red.** Hearts and shots: magenta / violet /
  cyan (`heartColor`, `shotColor`, `shotCore`). EliteTest checks the hue gap.
- Art direction: neon pixel art on a dark indigo base; placeholder FX are
  drawn in code (`EliteFxArt`) in hard steps, never smooth gradients.
- Don't touch the PAUSE logo or the HapticGate splash. Never commit in the
  main checkout `/Users/sina/Developer/Pause` (Codex works there).
- New elites need **no code** unless their attack is genuinely new. Prefer
  composing an existing brain + attack with new numbers.

## Procedure

### 1. Inspect the strip

Final strip: `<world>_elite_<name>.png` (or a bare `<name>.png` plus a
`manifest.md` in the world folder), square cells in a row. Two layouts
exist so far -- **read the manifest / README** to know which:

| Layout | Cells | Used by | `cells` in the def |
| --- | --- | --- | --- |
| Ember (default) | idle0..3, tell, action, hit | the six Ember elites | omit `cells` |
| flight | landed, grounded idle, lift-off, hover, bank left, bank right, damaged | Frost/rimebreaker, Verdant/resin_warden | `"cells": {"flight": [3], "parked": 0, "parkedIdle": 1, "liftoff": 2, "bankLeft": 4, "bankRight": 5, "tell": -1, "action": -1, "hit": -1, "damaged": 6}` |

Any other layout: just write its `cells` map (see `EliteCells` in
`EliteDef.cs`); -1 = no such drawing. What the framework does with it:
`flight` loops (one cell = held); `parked` sits on the pad and
`parkedIdle` blinks with the engine lights in the launch tell; `liftoff`
shows for the first 60% of the lift-off (no procedural heat shimmer);
`bankLeft/Right` replace the flight cell while sliding sideways
(|vx| > 0.35 x speed, out below 0.2); `damaged` replaces flight + banks
for good after the first lost heart; the debris is cut from `damaged`
(else `hit`, else flight). **No tell / action / hit cells** -> procedural:
a stepped charge glow at the muzzles + squash + lean in the tell
(`ChargeGlowOn`, `Squash`), a muzzle flash + recoil on every shot
(`OnFired`, from `EliteAttack.Fire`), the current frame flashing on a hit.
Flames drawn into the flight cells: set `exhaustScale: 0` and a small
`glowScale` (~0.08) so the procedural plume / light dot don't double them.
The codex loops `cells.CodexLoop` (flight, or flight-left-flight-right)
and plays `CodexTell` (tell + action, else the launch cells).

Check size and look at it zoomed:

```bash
python3 - <<'EOF'
from PIL import Image
p="Pause/Assets/Art/Enemies/Elite/<World>/<world>_elite_<name>.png"
im=Image.open(p).convert("RGBA"); print(im.size)          # W = 7 * H
h=im.size[1]
for c in range(im.size[0]//h): print(c, im.crop((c*h,0,c*h+h,h)).getbbox())
im.resize((im.size[0]*2,h*2),Image.NEAREST).save("/tmp/zoom.png")
EOF
```

The zoom is easier to read on a dark backdrop with a 16 px grid (alpha
composite onto (30,24,60) and draw lines every 16 px) -- you can read
seed coordinates straight off it. Note stray pixels near the cell edges
(e.g. `resin_warden.png` cell 3 has a dark sliver at x~163): report them to
Codex, never edit the art.

Then Read the zoomed PNG. Decide: which way the **nose** points in the art
(degrees, 0 right, 90 up), whether it is a hover/upright design (gunships,
haulers: `turnsToFace: false`) or a pointy craft that should rotate to face
its heading, where it **fires from** (cannon mouths, lance tip, claw, beak),
which frame shows the muzzle flash (usually 5 = action; some strips flash in
the tell, 4), and where its **engine nozzles** are (idle0).

### 2. Sync the art into Resources

```bash
cd Pause && "$UNITY" -batchmode -quit -projectPath "$PWD" \
  -executeMethod EliteArtSync.SyncAllAndExit -logFile /tmp/elite-sync.log
```

(Also happens automatically on import.) A bare `Frost/rimebreaker.png` is
copied as `Resources/Elites/Frost/frost_elite_rimebreaker.png`, so the
def's `key` is `frost_elite_rimebreaker`. Working files ending
`_candidate / _concept / _wip / _draft / _old` are skipped. Optional strips, picked up with no
code the moment they exist: `<key>_parked.png` (on the pad, engines off),
`<key>_liftoff.png` (played over the lift-off), `<key>_death.png` (played
once, then the debris) -- any number of square cells.

### 3. Measure muzzles and nozzles

```bash
python3 .claude/skills/add-elite-ship/scripts/measure_elite_points.py \
  Pause/Assets/Art/Resources/Elites/<World>/<key>.png \
  --radius 8 [--muzzle-frame 4] [--layout flight] \
  --muzzle cannon:171,22 [--muzzle left:36,95:180 ...] \
  --nozzle l:32,127 [--nozzle r:90,152 ...] \
  --preview /tmp/<key>-points.png \
  --write Pause/Assets/Art/Resources/Elites/Defs/<key>.json   # after creating it (step 4)
```

Seeds are rough cell-pixel clicks (x right, y down); `:DIR` (art-space
degrees) only when the shot/plume does not go along/against the nose (side
cannons: 180 / 0; downward thrusters / slag chutes: 270). The script snaps
each to the hot pixels of the muzzle flash / engine glow and onto solid art,
prints `hullRadiusPx` (use ~0.85 x it x `cellWorldSize / cellPixels` for
`hullRadius`), a `noseDeg` guess (verify by eye!) and per-frame bboxes
(art touching a cell edge = ask Codex to inset it). **Read the preview** and
re-seed anything off. For the flight layout pass `--layout flight`: muzzles,
nozzles and body numbers are all taken on the hover cell 3 (the frame it
fires on); seed nozzles on the engine bells with a small `--radius` (~6),
the drawn flames are hotter than the bell. EliteTest checks muzzles on the
action/tell cell (else the flight cell) and nozzles on `cells.flight[0]`.

### 4. Write the EliteDef

Copy the closest existing def in `Pause/Assets/Art/Resources/Elites/Defs/`
and change it. Every field is documented in `EliteDef.cs`. Required:
`key` (= the strip name), `displayName`, `world` (`space`/`frost`/`verdant`/`ember`),
`role` (one line), `codexId` (`elite_<world>_<name>`), `lore` (max 3 sentences,
the codex voice: second person, playful, gives a hint), `brain`, `attack`,
`cellWorldSize` (1.45-1.7: a cell's world size), `hullRadius`, `noseDeg`,
`turnsToFace`, colours, `muzzles`, `nozzles`. Keep `hearts: 2`, `heartSize`
~0.18 (the player's are 0.22) and pick `heartOrbit` so `hullRadius x heartOrbit
+ heartSize x 1.05` stays under ~0.52 u -- a tighter orbit than the player's
(EliteTest compares them).

Personality numbers (shared base): `speed`, `accel`, `turnRate`,
`followDistance`, `aggression` (0-1), `tellSeconds`, `attackGap`,
`avoidance` (0-1 dodge skill), `lookAhead` (s), `perception` (u/s to re-find
the pilot after a teleport). Brain-specific: `laneOffset` (gunship),
`circleRadius` (striker), `keepDistance` (skirmisher), `topMargin` (siege),
`followDistance` (interceptor / hauler / breaker), `laneOffset` + `topMargin`
(warden). `armored: true` = smashes rocks without losing a heart (haulers);
an attack with `Ploughs` (ice_ram) does so only while it acts. Shot extras:
`shotBounces` (glances off rails), `lobSeconds` / `lobSpacing` / `lobAhead` /
`poolSeconds` (glob). Keep `lore` to 3 sentences.

### 5. Pick a brain, design its attack

Match the role in Codex's README (`Art/Enemies/Elite/<World>/README.md`):

| Role | Brain | Attack idea |
| --- | --- | --- |
| interceptor / lancer | `interceptor` (stalks from behind) | `lance_dash`: tell, then a committed ram (bait-able) |
| gunship / turret | `gunship` (holds a lane beside) | `broadside`: volleys out of side cannons |
| striker / raptor | `striker` (circles) | `claw_dive`: dive through the locked spot, fling shards |
| hauler / tank | `hauler` (blocks the lane ahead) | `slag_drop`: sinking blobs down the lane |
| skirmisher / ghost | `skirmisher` (keeps distance, blinks) | `blink_shards`: blink then fan shards |
| siege / artillery | `siege` (holds the top) | `siege_cannon`: sight line, piercing shell down the lane |
| icebreaker / ram | `breaker` (prowls ahead, sweeping across the lane) | `ice_ram`: locks its lane, ploughs down it through rocks, rail-bouncing shards |
| mortar / area denial | `warden` (station high on the far side, crosses after attacking) | `resin_mortar`: ringed spots ahead of the pilot, lobbed globs land as pools riding the board |

Each elite must feel **its own**: vary `shotKind`, `shotCount`, `shotSpread`,
`shotSpeed`, `shotInterval`, `dashSpeed`, `actionSeconds`, `tellSeconds`,
colours. Only if no combination fits, add a new `EliteAttack` subclass in
`EliteAttacks.cs` (register its id in `EliteAttacks.Ids` + `Create`) or a new
`EliteBrain` in `EliteBrains.cs` (`EliteBrains.Ids` + `Create`). Rules for new code:
fire only through `Fire(muzzle, deg, speed)` (shots must leave a measured
muzzle), drive movement only via `ship.Drive` / `ship.Steer`, keep committed
moves committed (that is what makes baiting work), no allocation per step, no
`Time.*` (everything is stepped with the world's `dt`). Add a signature check
for it in `EliteTest.cs` (see the existing ones; `Breaker()` / `Warden()`
show the pattern for an attack with its own shot behaviour). Things that
ride the board (spots, pools) must move with `EliteSystem.Scroll`, and a
lob must lead by `Scroll x flight time` or it lands behind the pilot.
Hooks for new attacks: `Ploughs` / `OnPlough` (rock crashes while acting),
`ship.OnFired` is automatic via `Fire`, `EliteShot.Lob(to, seconds)`.

### 6. Landing sites for the world

Parked elites sit on the world backdrop's terrain. A world without
`LandingSites` gets **no** elites even with defs. Override in that world's
director (`Scripts/Worlds/Backdrop/BackdropDirectors.cs`), like
`EmberDirector.LandingSites`:

```csharp
public override void LandingSites(List<LandingSite> into)
{
    foreach (var p in <landmarkPool>.items)
    {
        if (!p.active || p.sr.sprite == null) continue;
        if (p.y < -HalfH * .15f || p.y > HalfH - p.size * .3f) continue;   // still high in the view
        Bounds b = p.sr.sprite.bounds;
        into.Add(new LandingSite { anchor = p.root,
            local = new Vector3(b.center.x + fx * b.size.x, b.center.y + fy * b.size.y, 0f),
            scale = .3f, order = p.sr.sortingOrder + 1, id = <unique per pad> });
    }
}
```

Or use `PlanetDirector.LandmarkPads(pool, into, piece => pads, scale, idBase)`
(handles the "upper part of the view" band, mirrored drawings and unique
ids), as `FrostDirector` / `VerdantDirector` do. Pick pads on flat-ish parts
of a landmark (shoulders, platforms, decks): crop the landmark from its
atlas (`Art/Backgrounds/Resources/Worlds/<World>/Backdrop/<fx|anim>.json`; Unity rects, y
from the bottom: PIL box `(x, H-y-h, x+w, H-y)`), draw a 10% grid, read
the fractions off it (from the centre, y up). Done: Ember volcano
shoulders, Frost glacier snout apron + lateral ridges + massif saddle,
Verdant ruin summit + terrace ledges + waterfall canopy. Space: a backdrop
wreck or station if one exists (else ask Codex for one). Note: as of
2026-10-04 `Ember/Backdrop/anim.json` looks out of date with its redrawn
`anim.png` (rects match the png only top-origin) -- check the crops before
trusting fractions.

### 7. Codex entry

Automatic: `CodexCatalogue` lists every `EliteCatalog` def in Enemies (after
the roster's enemies; the panel files it under its own world's section)
with `displayName`, `lore`, the card drawing `cells.flight[0]`, the
`CodexLoop` and the `CodexTell` beat. The locked card must be a **readable solid
silhouette**: EliteTest renders every elite's locked card. If it fails ("N
opaque px"), the art is too thin or too translucent -- `EliteArtSync` already
snaps alpha >= 240 to solid; otherwise ask Codex for a chunkier silhouette.

### 8. Tests

`EliteTest` iterates **all** defs (art loads, cell map fits the strip,
brain/attack ids, hearts and shots not red, muzzles/nozzles on the art, the
original untouched, life cycle with the right tell/action drawing or the
procedural glow / flash, shots from muzzles, codex silhouette). Update the
counts it pins (`six Ember elites`, Frost / Verdant one each, `no elites in
Space yet`, every elite its own brain/attack) for the new world. `CellMaps()`
checks the Ember defaults and walks a flight-layout ship through parked ->
lift-off -> flight -> banks -> damaged; `WorldSites()` spawns from a real
Frost / Verdant backdrop. Add a signature check for any new brain / attack
(and an allocation run if it adds shot behaviour).

```bash
cd Pause && "$UNITY" -batchmode -quit -projectPath "$PWD" -executeMethod EliteTest.Run -logFile /tmp/elite-test.log
grep -E "\[ELT\] (FAIL|failures)" /tmp/elite-test.log
```

### 9. Preview

```bash
cd Pause && ELITE_PREVIEW_DIR=/tmp/elites ELITE_PREVIEW_ONLY=<key>[,<key2>] "$UNITY" -batchmode -quit \
  -projectPath "$PWD" -executeMethod ElitePreview.Run -logFile /tmp/elite-preview.log
python3 ../.claude/skills/add-elite-ship/scripts/make_preview_gif.py /tmp/elites [--prefix elite2-] [--only <key>]
```

The contact sheet samples every 6th frame; to inspect an attack, paste
frames ~100-150 of `elite-<key>/` side by side at half size (the attack
sits there in the default script).

Read `elite-<key>-sheet.png`: parked small and hazy on the pad, engine lights
before launch, lift-off growing into the play layer, the join, its brain,
its tell + action, the heart crumbling, the crash and debris.

### 10. Verify everything

```bash
cd Pause && "$UNITY" -batchmode -quit -projectPath "$PWD" -executeMethod AllTests.RunAll -logFile /tmp/elite-all.log
grep -E "\[ALL\]" /tmp/elite-all.log | grep -v PASS
"$UNITY" -batchmode -quit -projectPath "$PWD" -executeMethod BuildScript.BuildMac -logFile /tmp/elite-mac.log
"$UNITY" -batchmode -quit -projectPath "$PWD" -executeMethod BuildScript.BuildAndroidDev -logFile /tmp/elite-android.log
grep "\[BUILD\] result=" /tmp/elite-mac.log /tmp/elite-android.log   # both Succeeded
```

(Android: retry up to 3x with a wait if a Gradle daemon was stopped.) Commit
the def JSON, the Resources copies + their metas, any new code/tests; not
Codex's originals or their metas.

## Design rules the framework already enforces (don't break them)

- At most 3 elites alive; groups of 1-3; none in the first 20 s of a world,
  during a boss (or its last 12 s run-up), the final choice or the tutorial.
- Parked/lifting elites: no collider, not a target, behind gameplay; the
  collider turns on only when fully in the play layer; the join point is
  >= 2.6 u from the pilot and attacks wait a 2 s escape window.
- Elites never retreat or time out; they die by crashing (rocks, enemies,
  mines, elites, rails) or to the pilot. They do NOT teleport with the pilot.
- Every damage source costs one heart (shielded ram: both). Any death pays
  `ScoreRules.EliteDown` + `EliteDownDust` with "ELITE DOWN" (lures too).
  Friendly-fire kills of other hazards pay nothing.
- Everything steps on the world's clock (frozen at timeScale 0) with zero
  per-frame allocation (EliteTest measures it).
