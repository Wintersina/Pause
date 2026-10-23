# Player ships: one folder per ship

Goal: every player ship lives under its own name so a new or restyled ship touches only its own folder and
two ship PRs never conflict. Before this change every ship PR edited the same files: one baked
`hull_silhouettes.bytes` / `hull_hitboxes.bytes` for all ships, one `exhaust_atlas.png`, one `restyled = {...}`
line in `PaintedShips.cs`, adjacent rows of five shared C# tables, and a growing `SHIPS` dict / `RESTYLED` set in
the art scripts.

## Layout

```
Pause/Assets/Art/Resources/ShipArt/
  <Key>/                     NeonComet, VoltViper, ... Paragon (the art key from ShipId.cs)
    <Key>.png                hull sheet, 2304 x 768 (9 columns x 3 damage rows of 256 px cells)
    <Key>_<Skin>.bytes       skin sheets (PNG bytes), 4 per ship
    <Key>_flame.png          flame strips (layout in the data file)
    <Key>_shield.bytes       baked shield silhouette   (SIL1 container, one ship)
    <Key>_hitbox.bytes       baked hull + shield hit polygons (HBX1 container, one ship)
    <Key>_data.json          the ship's data file
    src~/                    Codex paintings, briefs, per-ship tools, restyle.json (build config), data.json
  src~/                      shared pipeline tools (shiplib.py, build_restyle.py, patch_restyle.py, ...)
  Guns/, DamageFx.png        shared FX atlases (not per ship)
```
Every file has its `.meta`; the folder has a folder `.meta`.

`<Key>_data.json` (read by `ShipData`) holds what used to be shared C# rows:

| field | was | read by |
|---|---|---|
| `painted` | `PaintedShips.restyled` | `PaintedShips.Is` |
| `rect` (x, y, w, h, ppu), `hue` | `ShipHullArt.rects` | `ShipHullArt.RectFor` |
| `nozzles` | `ShipNozzles.table` | `ShipNozzles` |
| `damageNote`, `emitters` | `ShipDamageTable.table` | `ShipDamageTable` |
| `skins` | `ShipSkins.table` | `ShipSkins` |
| `exhaust` (kind, 5 colours, ticks, strips) | `ShipExhaustStyle.table` + `strips` + the shared atlas | `ShipExhaustStyle` |

## Rules

- Adding or restyling a ship = adding/replacing files in `ShipArt/<Key>/`. No shared file is edited: no C# table,
  no atlas, no bake file, no registry list in the art scripts. `painted` is the registry: `PaintedShips.Is` and
  `ships.py RESTYLED` (via `shiplib.painted_keys()`) are derived from the data files.
- Loaders find everything from the art key: `ShipData.HullPath/SkinPath/FlamePath/ShieldPath/HitboxPath/DataPath`.
  `ShipId.keys` (roster id -> art key, the saved ids) is the one remaining list; it only changes when the roster
  gets a new slot, never for art work.
- `ShieldSilhouetteBaker` / `ShipHitboxBaker` iterate the ships and write one file per ship, only when its bytes
  changed, so a rebake after one ship's art touches that ship's two files.
- Tools are addressed by ship name only (`python3 ShipArt/src~/build_restyle.py VoltViper`); `shiplib.py` knows where
  every file of a ship is.

## Bank poses

`ShipArt/src~/bank_pose.py <Key>` rebuilds columns 6 (BankLeft) and 7 (BankRight) of the stock sheet and every skin sheet from each row's own idle cell, applying `ShipDamageTable.Posed` as a nearest-neighbour warp (near side k .80, far side .95, 1.5 unit shift toward the bank, vertical offset, near side shaded 12 %), so damage panels and skins stay consistent; skins take the stock bank alpha. It then verifies left/right extent ratios, binary alpha, skin/stock alpha equality and width change. It is idempotent and touches no other column; per-ship overrides (`axis`, `shade`) go in `<Key>/src~/bank_pose.json`. The shield and hitbox bakes read only the idle silhouette, so no re-bake is needed. Spinning ships (Ninja, UFO) never bank and are not covered.

## Design choices (and the alternative)

- Per-ship `src~/` under the ship folder instead of the old `Hulls/src~/<ship>/`: the Codex paintings, briefs and
  config travel with the ship's art (one folder to move, review, delete), and `src~` is hidden from Unity at any
  depth. Alternative: keep sources under one shared `Hulls/src~/`; rejected because it keeps a shared directory
  every ship PR writes into and splits a ship over two trees.
- JSON data file loaded with `JsonUtility` (parsed once per ship, on first use) instead of C# partial classes or
  ScriptableObjects: `Resources.Load` is how every ship asset is found, it needs no editor step, a script can
  merge into it (`shiplib.update_data`), and its diffs are per ship.
- Flame strips: one sheet per ship (`<Key>_flame.png`, strips stacked with a 2 px clear gap) instead of the shared
  atlas. The skin remap shader (`ExhaustRemap`) works on the sprite's texels, so it is unchanged; every frame is
  pixel-identical to the old atlas.
- Bake files keep the `SIL1` / `HBX1` formats with `count = 1`, so parsers are unchanged.

## Moving a branch that predates this layout

See the table "Moving an older branch onto the per-ship layout" in `.claude/skills/restyle-ship/SKILL.md`. In short,
after merging master: `git mv` your `Hulls/<Key>.png`, `Skins/<Key>_*.bytes`, `Hulls/src~/<key>/` into
`ShipArt/<Key>/` (metas along), take master's version of every shared table/atlas/bake file, convert your
`SHIPS["<Key>"]` entry to `<Key>/src~/restyle.json`, re-run `build_restyle.py <Key>` and `patch_restyle.py <Key>`
(rewrites `<Key>_flame.png` and merges your measurements into `<Key>_data.json` with `"painted": true`), and re-run
the Unity bake for your two bake files.
