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
| **Blue atom** | Shield, boost and brief invincibility |
| **Stars** | Star Dust — the currency |

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

| Ship | Power | Does |
|---|---|---|
| **Rookie** | `LANCE` | Piercing beam straight ahead |
| **Proteus** | `SWARM MISSILES` | Homes on the nearest few targets |
| **Amadeus** | `SHOCKWAVE` | Clears everything close by |
| **Darkwing** | `PHASE CLOAK` | Brief invulnerability |
| **Cygnus** | `TRACTOR FIELD` | Pulls pickups toward you |
| **Vesper** | `TIME DILATION` | Slows the world down |
| **XR7** | `OVERCHARGE` | Restores extra pauses |

Ships take visible damage — every hull has three states, and you watch yours
come apart as the run goes badly.

Star Dust is deliberately hard to earn, and payout scales with speed: flying
fast is worth more than crawling. Prices run 150 → 4000.

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
