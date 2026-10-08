# Skin hearts: colours make the ship tougher

Buying hull colours (skins) for a ship already improves its weapon
(`ShipWeaponUpgrades`: weapon level = that ship's non-stock colours owned,
0..4). They now also raise the ship's hearts (lives). One tuning block:
`Pause/Assets/Scripts/Ship/SkinHearts.cs`.

## Rule

Max hearts for a run = hull base (`ShipLives.Base`, by price tier)
+ colour hearts (per ship, same tier as the weapon)
+ all-skins bonus (every ship, once every skin of every ship is owned).

| colours owned for that ship | 0 | 1 | 2 | 3 | 4 |
|---|---|---|---|---|---|
| extra hearts (`ByColoursOwned`) | 0 | +1 | +1 | +2 | +2 |

All-skins bonus (`AllSkinsBonus`): +2 on every ship once all 60 non-stock
skins (15 ships x 4) are owned. This is the "final skin for all ships is
unlocked" condition.

| hull (base) | stock | 1-2 colours | 3-4 colours | + all skins |
|---|---|---|---|---|
| Neon Comet (2) | 2 | 3 | 4 | 6 |
| Volt Viper, Solar Fang, Crimson Halo, Lightning, Ligher, Paranoid, Ninja, Saboteur, UFO, Dove (3) | 3 | 4 | 5 | 7 |
| Turtle, Ion Lancer, Jade Phantom (4) | 4 | 5 | 6 | 8 |
| Gold Warden (5) | 5 | 6 | 7 | 9 |

`ShipLives.Most` (9) is the most hearts any ship can have, and the heart
orbit is sized for it.

## Details

- The first colour's +1 replaces the old starter-only rule ("Neon Comet: 3
  once it wears a colour"). That rule now applies to every ship.
- Which skin is equipped doesn't matter. Only ownership counts, as with the
  weapon.
- Nothing extra is saved. The bonus is worked out from the owned-skin keys
  (`skinOwned<id>_<n>`), so old saves, cloud restore and restored purchases
  get it automatically. Developer mode owns every skin, so it gets the full
  bonus.
- Every run starts at full hearts at the new max. Heal atoms and Mending undo
  hits, so they can never go over the max.
- The tutorial uses the bare hull (`ShipLives.TutorialMax` = base), so its
  lessons stay the same.
- Dock: the heart badge shows the ship's hearts. While you preview an
  unbought colour that adds hearts, it reads e.g. `3+1` with the gain in
  gold. The line under the weapon row shows `START SPD n` on the left and,
  on the right, `HEARTS 4: 3 +1 COLOURS` (or `... +2 SET`). While
  previewing, the right side reads `BUY: +1 HEART`. After a purchase the
  popup says `ACQUIRED  +1 HEART` when the purchase added a heart. The
  purchase that completes the set shows a one-time toast: `EVERY SKIN
  OWNED` / `ALL SKINS: +2 HEARTS`.
- Codex: each ship's lore lists its hull hearts, the most its colours can
  reach and the most with every skin, plus the colour-heart and all-skins
  lines.

## Changing the curve

Edit `ByColoursOwned` (one entry per colour count 0..4, never decreasing)
and `AllSkinsBonus` in `SkinHearts.cs`. `ShipLives.Most`, the dock text and
the codex all update from them. `SkinHeartsTest` pins the current numbers
(the per-tier rows and the 9-heart maximum), so update it to match.
