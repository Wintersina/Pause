<div align="center">

# ⏸ PAUSE

**A one-touch arcade runner where letting go is the only move you have.**

Hold to fly. Lift your finger to freeze the universe.

`Unity 6` · `C#` · `iOS · Android · Desktop`

</div>

---

## The hook

Most games ask what you'll do next. Pause asks what you'll *stop* doing.

Your ship never steers on its own terms — it runs, and the galaxy runs at it.
The only verb you own is **absence**. Lift your finger and everything holds:
asteroids hang mid-tumble, alien columns freeze mid-sweep, the starfield stops
breathing. Put it back down and the universe resumes exactly where it paused.

But pauses are finite. You start a run with five. Spend them badly and you're
flying a bullet-hell on rails with no brakes left.

That's the whole game. One input, used sparingly, under rising pressure.

---

## How it plays

| | |
|---|---|
| **Hold** | Fly. Speed climbs the whole time you're moving. |
| **Lift** | Freeze everything. Costs one pause. |
| **Lift + tap elsewhere** | Teleport to that spot. Your escape hatch. |
| **Red atom** | +2 pauses |
| **Blue atom** | Shield, boost, brief invincibility, `+2` dust |
| **Stars** | Star Dust — `0.5` small, `1.0` large. See [Economy](#economy). |

Run out of pauses and the game stops waiting for you: it plays on without the
freeze, and you fly it raw until you die.

---

## Difficulty

Speed rises continuously while you fly, and the spawner escalates with it —
not by reskinning the same wave, but by changing what's actually in play.

| Phase | Speed | What's out there |
|---|---|---|
| **Warm-up** | `< 0.20` | Rails and big enemies. Room to breathe. |
| **Debris** | `< 0.30` | Small enemies and mid asteroids join in. |
| **Asteroid field** | `< 0.40` | Small asteroids, and the first **aliens**. |
| **Swarm** | `< 0.50` | Big asteroids. Intervals tighten hard. |
| **Chaos** | `≥ 0.50` | Everything, at the shortest intervals in the game. |

Phases are serialized data, not hardcoded branches — retune the whole
difficulty curve from the Inspector without touching code.

---

## The hangar

Seven ships. Each has one power that recharges roughly once a minute and fires
on its own — the game is one-touch, so there's no spare input to bind.

| Ship | Cost | Power | Does |
|---|---:|---|---|
| **Rookie** | — | `LANCE` | Piercing beam straight ahead |
| **Proteus** | `150` | `SWARM MISSILES` | Homes on the nearest few targets |
| **Amadeus** | `400` | `SHOCKWAVE` | Clears everything close by |
| **Darkwing** | `900` | `PHASE CLOAK` | Brief invulnerability |
| **Cygnus** | `1,600` | `TRACTOR FIELD` | Pulls pickups toward you |
| **Vesper** | `2,600` | `TIME DILATION` | Slows the world down |
| **XR7** | `4,000` | `OVERCHARGE` | Restores extra pauses |

Ships take visible damage — every hull has three states, and you watch yours
come apart as the run goes badly.

---

## The shop

Reached from the main menu. Every ship is a button on the grid; tapping one
opens a confirm panel that doubles as both the buy dialog and the select
dialog depending on whether you already own it.

- **Not owned** — shows the price and asks to buy. Declines silently if you're short.
- **Owned** — offers to make it your active ship.
- Purchases and selection persist in `PlayerPrefs` (`boughtship{n}`, `spawnShip`,
  `PlayerCurrecny`).

The active ship determines which power you fly with, so the shop is the only
place the game's build variety lives.

---

## Economy

**Star dust comes almost entirely from pickups.** The passive trickle is under
1% of income — worth `0.30` in your first minute against roughly `17–40` from
collecting. If you want to change how fast players earn, change the pickups.

| Source | Value | Spawns |
|---|---:|---|
| Small star | `0.5` | clusters of 4–9, every 5–7s |
| Large star | `1.0` | clusters of 3–5, every 10–14s |
| Blue atom | `2.0` | every 6–9s (also grants shield + boost) |
| Passive trickle | `0 → 0.05/s` | continuous, scales with speed |

Collecting *everything* would pay about `67/min`, which no one does — you're
dodging at the same time. Realistic rates and what they buy:

| Ship | Cost | @17/min | @27/min | @40/min |
|---|---:|---:|---:|---:|
| Proteus | `150` | 9 min | 6 min | 4 min |
| Amadeus | `400` | 24 min | 15 min | 10 min |
| Darkwing | `900` | 53 min | 33 min | 22 min |
| Cygnus | `1,600` | 95 min | 59 min | 40 min |
| Vesper | `2,600` | 154 min | 96 min | 64 min |
| XR7 | `4,000` | 237 min | 148 min | 99 min |

Owning the full roster costs `9,650` — between **4 and 9.5 hours** of active
flight depending on how cleanly you collect. Times are cumulative flight time,
not wall clock; a run is typically one to three minutes.

Dust carries across runs. It's banked to `PlayerPrefs` on death and reloaded at
the start of the next run, so a bad run never costs you your savings. Tutorial
earnings are tracked separately and never bank, so practice runs can't be farmed.

### Tuning the economy

Everything above is Inspector-exposed — no code changes needed.

| Knob | Where |
|---|---|
| Star and atom payouts | `collisionDetection` on the player ship |
| Passive trickle rate | `score.dustPerSecondAtTopSpeed` |
| Ship prices | `shopingShips.shipCost[]` |
| Pickup spawn rates | `spawnGoodStuff` |

---

## Planets

A run is a journey outward. Every 8 minutes of active flight a **portal** opens
and drifts down the screen; fly into it and you arrive somewhere new.

| # | Planet | Ramp | Top speed | Feel |
|---|---|---:|---:|---|
| 0 | **Space** | `0.0020` | `0.60` | Where every run begins. The original starfield. |
| 1 | **Frost** | `0.0024` | `0.66` | Ice sheets and blue crevasses. Cold and still. |
| 2 | **Verdant** | `0.0028` | `0.72` | Jungle canopy, rivers, bioluminescence. |
| 3 | **Ember** | `0.0032` | `0.80` | Black basalt cut by veins of lava. |

**Crossing a portal resets your speed to zero, but your pauses and star dust
carry over.** Each planet is a fresh difficulty ramp, not a fresh start — and
because every planet ramps faster and tops out higher than the last, the run
gets harder even though it always begins slow.

Miss a portal and you're not stranded: another opens after a quarter of the
usual wait.

By default every run starts at Space, so the planets are a route you fly rather
than a menu you pick from. The furthest planet reached is recorded either way —
flip `startAtHighestUnlocked` on `WorldManager` to treat them as unlocked
shortcuts instead.

A world is a **re-theme of `gameS1`**, not a separate scene: the backdrop and
side walls swap textures, the music crossfades, and the difficulty curve
changes, while every other system keeps running. Adding a planet is an art drop.

```
Resources/Worlds/<Name>/backdrop.png     1024 x 4096, seamless vertical tile
Resources/Worlds/<Name>/wallLeft.png       64 x 448,  seamless vertical tile
Resources/Worlds/<Name>/wallRight.png      64 x 448,  mirror of wallLeft
Resources/WorldMusic/<Name>.wav          44.1kHz stereo, ~40s seamless loop
```

Missing art or music falls back to whatever is already playing rather than
cutting to black or silence, so a half-finished planet stays playable.

> In a development build, press **P** to open a portal immediately instead of
> waiting eight minutes. The shortcut is compiled out of release builds.

---

## Running it

Requires **Unity 6000.3.23f1**. iOS builds need Xcode; Android needs the
Android module.

```bash
# Unity Hub -> Add -> select the Pause/ folder
# (the inner folder is the project root, not the repo root)
```

Command-line builds:

```bash
UNITY=/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity

$UNITY -batchmode -quit -projectPath Pause -executeMethod BuildScript.BuildMac
$UNITY -batchmode -quit -projectPath Pause -executeMethod BuildScript.BuildAndroid
$UNITY -batchmode -quit -projectPath Pause -executeMethod BuildScript.BuildIOS
```

Output lands in `Pause/Builds/`. The Mac player opens fullscreen on its own
Space; for a windowed run:

```bash
Pause/Builds/Mac/Pause.app/Contents/MacOS/Pause \
  -screen-fullscreen 0 -screen-width 540 -screen-height 960
```

---

## Layout

```
Pause/Assets/
  Art/          sprites, materials, animations, effects
  Audio/        music and sound effects
  Editor/       editor-only tooling and headless tests
  Resources/    anything loaded by path at runtime
    Prefabs/Ships/Sprites/<Name>/   ship damage sheets
    Prefabs/Enemies/Kenney/         enemy and meteor art
    Worlds/<Name>/                  planet backdrops and walls
    WorldMusic/<Name>.wav           planet music
  Scenes/       every .unity scene
  Scripts/
    Core/       input, scene lookup, score, social, shared helpers
    Gameplay/   movement, spawning, collisions, ship powers
    UI/         HUD, menus, icons, ads
    Audio/      music control
    Ship/       player ship behaviour
    Shop/       hangar and purchasing
    Menu/       main menu
    Tutorial/   tutorial flow and skip
    Credits/    credits roll
    Worlds/     planet themes, portals, progression
```

No path under `Assets/` contains a space, bracket or `#`, so globbing and
shell tooling work without quoting. Only paths under `Resources/` are
referenced by string from code — everything else is resolved by GUID and can
be moved freely.

---

## Scenes

```
spashS7  ->  startS4  ->  gameS1
                 |
                 +-->  tutorialS5   (first run only, skippable)
                 +-->  shopS6
                 +-->  leaderboardS3
                 +-->  creditsS7
```

---

## Architecture notes

Handful of things worth knowing before you dig in:

**`TouchInput`** — every input check goes through here. It falls back to the
mouse when there's no touchscreen, which is the only reason the game is
playable in the editor or on desktop at all.

**`SocialBridge`** — wraps Unity's built-in Social API. On iOS that's Game
Center with no plugin. Achievements and leaderboards are not platform-gated.

**`AdMob`** — currently a stub. The 2016 SDK was removed during the Unity 6
upgrade; the call surface is intact, so restoring ads means filling in one file.

**`ShipPowerController`** — attaches itself at runtime by finding `movePlayer`,
so adding a ship needs no prefab surgery.

**The economy lives in `collisionDetection`**, not in `score`. `score.calcScore`
looks like the money code but is a sub-1% trickle; the pickup handlers are where
the currency actually moves.

**Serialized arrays** — several scripts hold `public` arrays sized from
`shopingShips.shipTotal`. Scenes cache them at the *old* size, so they're
regrown in `Start()`. Keep that in mind when growing the roster again.

---

## History

Built in 2016, near the end of my senior year, as a showcase of what I'd
learned. Originally Unity 5.5.1 and Android-only.

Brought up to Unity 6 and made cross-platform since: the input layer, the
social layer, the difficulty curve and the economy were all either
Android-specific or framerate-dependent, and are neither now.

Pre-alpha `.apk` on the release tag hasn't been rebuilt since 2016. On a modern
phone it will look rough.

---

## Credits

Code and original art by **Sina Serati**.

Enemy and meteor sprites from
[Kenney](https://kenney.nl) — *Space Shooter (Remastered)*, CC0.

Planet backdrops and world music are generated procedurally — the generators
live outside the project, and the committed assets are plain PNG and WAV files
like everything else.
