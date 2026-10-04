# Frost pixel-art migration manifest

`Frost_pixel_atlas_candidate.png` is the approved staged **body** atlas.  It
is deliberately outside `Resources`, so it cannot be loaded by the live boss
until this migration is explicitly promoted.

## Atlas contract

The candidate is exactly 1920 x 1536 px: a 5 x 4 grid of 384 px cells.  Its
row-major cells map one-for-one to `BossArt.Body(Frost, index)`:

| Index | Legacy source name | Runtime role |
| ---: | --- | --- |
| 0-3 | frost_00_idle_0 through frost_03_idle_3 | idle loop |
| 4 | frost_04_hit | hit flash |
| 5-6 | frost_05_tell0_a, frost_06_tell0_b | shard tell |
| 7-8 | frost_07_tell1_a, frost_08_tell1_b | bolt tell |
| 9 | frost_09_fire | firing pose |
| 10-11 | frost_10_tell2_a, frost_11_tell2_b | lane tell |
| 12-16 | frost_12_death_0 through frost_16_death_4 | death sequence |
| 17-18 | frost_17_retreat_0, frost_18_retreat_1 | retreat/arrival |
| 19 | frost_19_portrait | Codex portrait |

## Promotion gate

Do not replace `Assets/Art/Resources/Bosses/Frost.png` or remove the legacy
`Frost/src~/*.svg` files yet.  Promotion must include and validate all three
runtime inputs:

1. this 20-cell body atlas;
2. a new 8-cell `Frost_shots.png` (bolt 0-1, shard 0-1, telegraph, beam 0-1,
   charge); and
3. a new `Frost_card.png`.

After import, verify `BossArt.AllSprites(BossCatalog.ForWorld(1))` produces
20 body sprites, 8 shot sprites, and the card without nulls.  Only then may
the old SVG sources and their legacy generator path be deleted.
