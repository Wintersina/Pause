# Next release backlog (after 1.1.0)

Written down on 2026-11-08, while 1.1.0 (version code 10013) was waiting to be
sent for review. Version code 10014 is taken by the new Top Score leaderboard
(Season 2) rebuild of 1.1.0, so these go into the following release unless
we decide otherwise. Nothing here is designed yet: discuss each item before
building it.

## Bugs

1. **Gold Warden drones take damage from enemies.** Its drones (sentries) can
   be hurt and killed by enemy objects and enemy projectiles. They should only
   be hurt by what is meant to hurt them (to confirm: maybe nothing).
2. **Bloom Maw's attack passes through the player.** The attack still goes
   through the ship without hurting it. Check its hitbox and damage path.
3. **Ship stuck before "GO".** If the player moves their finger before the
   "GO" prompt, the ship stays stuck in place. It should keep following the
   finger the whole time.

## Shop: boosters

4. **Track boosters better in the Shop.** Show clearly which boosters are
   owned and how much is left (time left for timed ones, count left for
   Hazard Clear).
5. **Pause a booster.** If a timed booster still has time left, the player
   can switch it off (and on again) so the remaining time is saved.
6. **Shorter defaults.** Dust x2 and Homing Atoms last 5 minutes per purchase
   by default (Dust x2 is 10 minutes today). Buying more stacks the time.

## In-game HUD

7. **Active power icons.** In a run, show very small icons on the right-hand
   side for each active power, with a small timer under timed ones and a
   small counter under count-based ones (e.g. Hazard Clear).
8. Use the same icons as the Shop (`Pause/Assets/Art/Resources/ShopIcons/`)
   and remove the in-game icon we have today.
