# Star dust economy

Star dust is a float (`StarDustLedger`, saved as `PlayerCurrecny`). Shop prices (ships 300-12,000, Volt Viper 300 and Lightning 500 being the entry ships, skins 350-1,700 per colour and 850-4,300 per Special by dock row, `ShipSkins.RowPrices`)
and saved balances are unchanged.

## The dial

`ScoreRules.DustRewardScale = 0.60` (one place, `Core/Scoring/ScoreRules.cs`). Every dust REWARD goes through
`ScoreRules.RewardDust(base)`; the star and blue-atom PICKUPS do not. There is no per-award rounding (dust is a
float, so a 0.12 kill crumb pays 0.072, never 0 and never a lost remainder); only the end-of-run score bonus is
tidied to 0.01 after scaling.

| Source | Base (master) | Now | Path |
|---|---|---|---|
| Small star pickup | 0.5 | 0.5 (unchanged) | `collisionDetection` -> `score.AwardStarDust` |
| Large star pickup | 1 | 1 (unchanged) | same |
| Blue (shield) atom pickup | 2 | 2 (unchanged) | same; a collectible the pilot flies into |
| Flight trickle | 1/12 per unit distance | x0.60 (0.05/unit) | `ScoreRules.FlightDust` |
| Any kill (weapons, ultimate, powers, ram, teleport kill, death combo chain) | 0.12 | 0.072 | `AwardDestroyedTarget` -> `score.AwardRewardDust` |
| Elite down | 15 | 9 | `EliteRewards.Pay` -> `score.AwardRewardDust` |
| End-of-run score bonus | min(1.5, 0.02 sqrt(score)) | x0.60, cap 0.90 | `ScoreRules.ScoreDustBonus` |
| Achievements (claimed in the Codex) | 5 or 25 each, 640 total | same | `AchievementStore.Claim` -> `StarDustLedger.Grant` |
| Codex unlock (claimed in the Codex) | 5 per entry | same | `Codex.CollectReward` -> `StarDustLedger.Grant` |
| Bosses, worlds, loops, daily, leaderboards | no dust | - | - |

See `docs/codex-and-achievement-rewards.md` for the claimable rewards.

Tutorial practice dust uses the same paths (so the Tutorial Complete card shows the reduced reward numbers);
it never touches the saved balance. Developer mode earns no bonus, as before.

## Effect

`DustEconomyTest` simulates seeded runs: pickups identical to master, rewards 0.60x. Rewards are only a part of a
typical run (the stars are most of it), so the combined income drops by less than 40% (see the test log lines
`[DUSTECON]`). Prices are untouched, so ships and skins take proportionally more runs to afford.

## Max-speed streak (2x score)

Holding the speed cap for 15 s without losing a heart doubles the score gained from then on
(`ScoreMultiplier`, see `docs/speed-and-loops.md`). Star dust is not multiplied: the dust rewards and the
flight trickle are unchanged. The end-of-run score bonus reads `RunScore.Total`, so a doubled score raises
it, but it is `min(1.5, 0.02 sqrt(score))` x `DustRewardScale` and caps at 5,625 points (0.90 dust), so
doubling a 3,000-point run moves it from 0.66 to 0.78 dust at most. No retune needed.

## Boosters (bought with star dust)

Three repeatable purchases, `Boosters` (`Scripts/Boosters/`). No store product, no platform code: iOS is the same code.

| | Price | What it does |
|---|---|---|
| Dust x2 | `Boosters.DustBoosterPrice` = 600 | Doubles pickup and reward dust for 10 minutes of play (`DustBoosterSeconds` = 600). Buying again adds 10 minutes (stored time caps at an hour). |
| Hazard clear | `Boosters.HazardClearPrice` = 200 per charge | One charge, held (max 9), fires AUTOMATICALLY when a hit would cost a heart: the heart is kept. |
| Homing atoms | `Boosters.HomingPrice` = 1000 | For 10 minutes of play (`HomingSeconds` = 600) every atom that comes on screen flies at the ship; dodge it or teleport and it misses. Buying again adds 10 minutes (stored time caps at an hour). |

**What the multiplier touches:** star and large-star pickups, the blue atom, the flight trickle, kill crumbs and elite rewards (`score.AwardStarDust`, `score.calcScore`).
**What it never touches:** claims (achievements, Codex), purchases (packs), the end-of-run score bonus, the tutorial and practice runs. Score is never affected, and the leaderboard's "run star dust" board reads `Boosters.RankedDust` (earned minus what the booster added).
**The clock** counts only unfrozen frames of a real run (`score.payDust`, called from `StepRunning`): not in a menu, not while the world is frozen with the finger up, not during a scripted entrance, a dead player, or a practice run. It is saved in milliseconds, so a restart keeps it.
**Stacking:** `Boosters.DustMultiplier(boosterActive, adDoubled)` is additive (1 + 1 + 1), capped at 3x. Never 4x.

### Where the dust price comes from

`BoostersTest` simulates 10 minutes (5 levels of 120 s, 60 fps, speed 0.10 to 0.33 per level, 12 seeded runs), the same model as `DustEconomyTest`:

| | Dust |
|---|---|
| Typical 10 minutes of play (flight + pickups + kills + elites, no score bonus) | about 275 |
| What x2 adds (equal to the base) | about 275 = 46% of 600 |
| Flawless run (adaptive difficulty's best case, x1.35) | about 370 = 62% of 600 |
| Rule: earn back under 70% | 420 at a price of 600 |

So even a flawless player never earns back the price (it is a net cost; the value is the ad-free 10 minutes and a faster ramp). `BoostersTest` fails if a retune of the economy moves the extra dust over 70% of the price.
**How far to trust it:** the sim uses seeded pickup, kill and elite counts that match the `DustEconomyTest` model, not logged play. The star counts per level (45-80 small, 8-18 large) and the 120 s level length are assumptions, and pickups are most of the income. A player who collects far more than the model (a magnet or Gold Magnet run, a very good player) can pass 70%; the 1.35x flawless case is the adaptive-difficulty estimate, itself "not measured in play". Re-check with real averages once the Play Console or leaderboard stats give dust per run.

**Hazard clear, 200:** one typical 4-level run earns about 220 (same sim), so a charge costs about one run's earnings. It saves a heart (or the run, on a fatal hit), but extending a run by a level only pays about 55 dust, so it never pays for itself. Max 9 held so a hoard cannot sit on screen.

### Homing atoms rules

`AtomHoming` (`Scripts/Gameplay/Pickups/`), driven by the atom mover (`moveItemEnmInStrightLine.Step`), so it runs only on the frames the world moves. While `Boosters.HomingRunning` (time left, a real run, not the tutorial or a practice run), an atom that is `AtomHoming.LockBelowTop` inside the top of the view leaves its `AtomWander` drift and locks onto the ship ONCE. It flies like a slow missile: `Speed` 6 u/s (never under 1.6x its own fall), turn rate `TurnRate` 2.2 rad/s (a ~2.7 u turn circle), straight down to start. Reaching the hull collects it the ordinary way (`collisionDetection`), so it pays, scores and counts as if flown into.
It MISSES (and goes back to its wander from where it is, never locking again) when the ship is more than `MissAngle` (100 deg) off its heading (a late side-step: a ~2 u dodge in the last ~1.5-2 u does it, an early one is followed), on any pause-jump (`movePlayer` calls `AtomHoming.OnTeleport`, which breaks every lock), or after `MaxSeconds` (4 s) without a catch.
Only atoms home (what the mover hands to `AtomWander`): star dust is never pulled, so it does not overlap DUST x2 or the Magnetic Atom, though a homed Magnetic Atom still pulls dust as usual. Its time does not switch ads off (`AnyActive` reads the multiplier only). The clock is the multiplier's: `score.payDust` -> `Boosters.Tick`, unfrozen real-run frames only, saved in milliseconds. A run with homing time spent routes to the Power-Ups board (`LeaderboardRunTag.PowerHoming`, label HOMING).
No payback rule is pinned for it: atoms pay score and powers, not dust, so the 70% dust rule above does not apply. Re-check if atoms ever start paying dust.

### Hazard clear rules

Clears every rock, mine and ordinary ship in view, and every hostile projectile in flight (`HostileShots.ClearIn`). It leaves bosses, elites (and their drones and shot hitboxes), backdrop rocks, weather, pickups and timed area attacks (jets, waves, blast rings, strikes, lashes). Bosses and elites take no damage from it.
It is not a kill: it never calls `AwardDestroyedTarget`, so there is no run score, kill chain, combo, achievement, Codex entry, secret-meter charge or dust. A run that used it still counts for score and leaderboards (see the tracker's open questions). It is automatic (the game is one-finger, so there is no button): `collisionDetection` is the one place a heart is lost, and a hit that would remove one (the fatal hit included) spends a held charge instead, keeps the heart, runs the clear from the ship and starts the usual 2 s post-hit i-frames, so the same hazard cannot hit again. Hits that cost no heart (shield, Hard Shell, Cloak, i-frames) spend nothing. A boss, elite or timed area attack that hits is absorbed but not removed. Never in the tutorial or practice runs, when dead, or in scripted entrances (then the hit lands). There is no cooldown: the i-frames are the guard. Developer mode grants nothing: it fires, and spends, only real held charges (buy them in the Shop). The run shows a small non-interactive readout of the charges held under the Replay/Home pair. The save helps a player survive a run, which matters for the leaderboard question in the tracker.

### Cloud save

Six monotonic counters (`ProgressSnapshot.boostDustBought`, `boostDustUsedMs`, `boostClearBought`, `boostClearUsed`, `boostHomingBought`, `boostHomingUsedMs`), merged per counter by the LARGER value (`ProgressMerge`). Held = bought - used. A max can't duplicate a paid item (a sum would) or bring back a used one; a booster bought only on the other device is paid for from the newer balance (`UnpaidBoosters`), as ships and skins are. A purchase writes the new balance and the counter together and saves at once, so a restart never leaves "paid, nothing received".
