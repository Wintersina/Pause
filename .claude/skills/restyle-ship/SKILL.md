---
name: restyle-ship
description: Restyle ONE existing player ship (hull flipbook, damage rows, silhouette column, every skin, exhaust flames, nozzle/emitter points, baked hitbox + shield silhouettes) from the old flat-cartoon vector look into Paragon's hi-res neon pixel-art look, art side only. Worked example NeonComet (the first restyled ship). Use when the user says "restyle <ship>", "redo the ship art in the Paragon style", or when auditing that a restyled ship missed a piece. Not for adding a new ship (add-ship), enemies (add-elite-ship) or weapon FX.
---

# Restyle a player ship in the Paragon style

Goal: ship `<Key>` (e.g. `VoltViper`) keeps its name, roster id, price, stats, attack roster and colour
identity, but its ART (hull sheet, damage rows, silhouette column, skins, flames) is repainted by Codex
in Paragon's hi-res neon pixel-art style: densely painted plated hull, rim lights, glowing canopy/engines,
saturated cores, real damage (blown panels, cracks, scorch). Worked example: NeonComet. Style reference:
`Pause/Assets/Art/Resources/ShipArt/Hulls/Paragon.png` (+ `src~/paragon/`). Everything lives under
`Pause/Assets/Art/Resources/ShipArt/Hulls/`; call it `HULLS` below. `src~` hides from Unity.

Sheet contract (never change): `<Key>.png` = 2304 x 768, 9 columns x 3 rows of 256 px cells. Row 0 intact,
row 1 damaged, row 2 wreck. Columns: 0 key pose, 1 bob up, 2 bob up lights off, 3 glint 1, 4 glint 2 (lights
off), 5 bob down, 6 bank left, 7 bank right, 8 hit flash = the flat bone-white SILHOUETTE column. Rows 1-2
share row 0's alpha EXACTLY, alpha is binary (0/255, no faint rim), no outline halo.

## 0. Environment rules that bit us

- The agent worktree is a plain **git worktree** (`.git` file) of a jj repo whose `jj workspace root` is the
  MAIN checkout. NEVER run `jj` in it (it snapshots / acts on the main checkout, which holds other sessions'
  uncommitted edits). Use plain `git status / add <paths> / commit / push` inside the worktree, add ONLY your
  files by path, commit messages carry NO Claude attribution lines (user rule).
- The sandbox rejects "complex" Bash (heredocs, `&&` chains with `$VAR`, `cd` elsewhere). Write scripts with
  the Write tool and run them as one plain command with literal absolute paths. Long jobs: Bash
  `run_in_background`, then wait for the completion notification (never `sleep`-poll).
- Never run Unity tests locally (CI only). Local Unity = only the bake in step 7, one at a time.
- Never touch the main checkout. Revert anything Unity rewrites (see step 7).

## 1. Prepare (per ship)

1. `mkdir -p HULLS/src~/<key lower>/codex/{briefs,tools}`.
2. Gather the OLD facts into a `SHIPS["<Key>"]` entry in `src~/build_restyle.py` (copy the NeonComet entry):
   - `hull_w`: width in px of the old rest drawing in its 256 cell (open old `<Key>.png`, bbox of cell 0 alpha).
   - `world_w`: old rect width / old ppu, from `Scripts/Ship/ShipHullArt.cs` row (`new HullRect(x, y, w, h, ppu, ...)`).
     The script keeps `world_w`, so the sprite keeps its world size (ppu = new rect width / world_w).
   - `nozzles` / `noz_scale` (count + scale: `Twin` = .72, single big = 1.5, see `Scripts/Ship/ShipNozzles.cs`),
     or `noz_cells=[(x_px, y_px, scale)...]` (rest-cell pixel coords of each nozzle lip) when auto-detect (lowest
     row's separate column groups) does not fit, e.g. one centre nozzle or nozzles not on the lowest row.
   - `emitters`: paste the old row from `Scripts/Ship/ShipDamageTable.cs` into `parse_emitters("E(1, Sparks, ...)")`.
     They are nudged onto paint automatically; since the briefs keep the damage in the same places they mostly stay.
   - `skins`: `[(name, fallback_mode)]` for the 4 non-stock skins from `ShipSkins.cs` (recolour modes in
     `recolour()`: night / sodium / bone / stock; add a mode per new colour, it is only the fallback + silhouette guard).
   - `flame`: atlas slots from `ShipExhaustStyle.cs` strips (`x` of Plume and PlumeBoost, `y`, `frames`) and the
     five band colours from that ship's `Row(...)` (outer, mid, core, dark, accent) + tick list.
3. Add the key to `RESTYLED` in `src~/ships.py` (stops the old vector pipeline `build.py` / `build_skins.py`
   from regenerating the sheet/skins/C# rows). Already done for NeonComet.
4. Reference images (1024 px cells on flat `#00FF00`): upscale the OLD cell 0 of each damage row 4x nearest
   (`ref_old_state{0,1,2}.png`), plus `paragon_ship_alone.png` (= `src~/paragon/ship_alone.png`) and
   `paragon_damaged.png` (= `src~/paragon/codex/tools/j1b_ref_state1.png`) for style. See `src~/neoncomet/codex/`.

## 2. Codex rules (all jobs)

Run through `scripts/codex-art.sh <workdir=worktree root> <brief.md> <last-message-file> <ref images...>`
(a mkdir lock so only ONE codex runs at a time, model `gpt-5.6-luna`, `--skip-git-repo-check -s workspace-write`,
`-c model_reasoning_effort="high"`, no `--full-auto`; refs are attached with `-i`). Run it in the background
and wait for the notification; a job takes 10-40 min (skins longest). Briefs: copy `src~/neoncomet/codex/briefs/`
(j1, j1b, j2, skin_*, mk_skin_briefs.py) and replace the ship description/palette/damage placement.
Every brief must: say "use IMAGE GENERATION, then nearest-neighbour clean-up, never filter/warp a reference",
flat `#00FF00` background, no pink/magenta (pink is Space-only), no outline/halo around the ship (only Paragon's
natural dark edge shading), no flames on the hull, keep the old silhouette family and colour identity, and finally
demand a `tools/check_*.py` that prints a threshold table which must pass ("fix and redraw until it passes, print
the table"). Codex's final message is NOT evidence: re-measure the files yourself (step 6).

Image-cap handling: before queueing, probe with a tiny image call (brief: "make a 256x256 red square on green").
On `429 usage_limit_reached` read `resets_at` (epoch seconds) from the error, sleep (background until-loop) until
that epoch + ~5 min and retry; the image cap is separate from the text cap. Never run two Codex jobs at once.

## 3. Jobs, in order (what worked for NeonComet)

- **J1 key poses** (`briefs/j1.md`): refs = paragon_ship_alone, paragon_damaged, ref_old_state0/1/2. Output
  `key_state0/1/2.png`, each ONE 1024x1024 image: intact, damaged (blown panel with ribs + sparking wires,
  scorch, hairline cracks, nozzle scorch), wreck (torn tip, burning pod, cracked canopy, embers). Ask for the
  same framing as the old ref (width ~95% of the cell) and that states 1/2 are derived from state 0 so the
  outline is identical. Prompt text: "slim crimson delta-wing ... painted like Paragon: panel plating detail,
  rim lights up-left, glowing neon canopy, lit nozzle rims; NO thick cartoon outline". Do NOT ask Codex for the
  bob or the hit flash: the script makes bob (exact 4 px translation) and the flash. First attempt passed
  (silhouette delta 0.000%, colour delta 30%).
- **J1b pose redo** (`briefs/j1b.md`): refs = the three approved keys. Output `hull_redo_state{0,1,2}.png`, each
  a 3x2 grid of 1024 cells: 0 lights off (wing slash lights, tip lights, star mark dark), 1 glint 1 (left half of
  canopy), 2 glint 2 (right half, fainter, lights off), 3 bank left (PAINTED perspective, left half ~20% narrower),
  4 bank right (not a flipped copy; damage stays on the same physical panels), 5 empty. The brief states the
  thresholds Codex must print: cell0 >= 0.4% pixels differ from key, glint1 >= 0.8%, glint2 differs from glint1,
  banks: residual after best integer translation >= 5% and silhouette diff >= 3%. (Paragon's first pass cheated:
  glint = copy, banks = scripted squash; hence the explicit numbers.) NeonComet passed first time.
- **J2 flames** (`briefs/j2.md`): refs = paragon_ship_alone (its jet), the new key_state0, an upscaled crop of the
  old flame strip (`ref_old_flame.png`: atlas crop `[x, H-y-192, x+frames*50, H-y]` of
  `Art/Resources/ShipArt/Exhaust/exhaust_atlas.png`, H=1472; atlas y counts from the BOTTOM). Output
  `flame_cruise.png` + `flame_boost.png`, N cells (N = old `frames`, 6 or 7, stride 50 so N*50 must not reach
  the next strip) 1:4 on green, head at top. ONLY the five band colours of the ship's exhaust Row (+ green key),
  hard-stepped, no outline. The script snaps every pixel to the nearest band so the skin recolour shader
  (`ExhaustRemap`) still finds the five bands exactly: THIS is how flames get tinted per skin, nothing is
  repainted per skin for flames.
- **Skins**: see step 5.

## 4. Assemble

`python3 src~/build_restyle.py <Key> [--no-skins] [--no-flame] [--no-codex] [--no-redo]` (cwd anywhere; it imports
`paragon/build_paragon.py` for key/fit/warp helpers). It: keys the green (despill), fits every Codex cell with the
SAME scale + anchor (the intact key's bbox width -> `hull_w`, centred on the cell), builds bob/flash itself, takes
the redo poses, forces rows 1-2 onto row 0's alpha exactly, writes `<Key>.png`, writes
`codex/skinref_state{0,1,2}.png` (3x3 refs of the final flipbook for the skin briefs), builds skins
(`Skins/<Key>_<Name>.bytes`), pastes flames into the atlas, measures rect/ppu/nozzles/emitters into
`src~/<key lower>/data.json`. Then `python3 src~/patch_restyle.py <Key>` rewrites that ship's rows IN PLACE in
`ShipHullArt.cs` (rect + ppu, colours kept), `ShipNozzles.cs`, `ShipDamageTable.cs` (emitters). Exhaust Row
colours/ticks, skin colours, hearts, attacks are untouched. Order: J1 -> J1b -> build (`--no-skins --no-flame`) ->
J2 -> skins -> final build -> patch -> bake.

## 5. Skins (every ship has 4 non-stock skins; each is repainted by Codex, never just hue-shifted)

How Paragon's were made (`src~/paragon/codex/briefs/skin_Aurum.md`, `build_paragon.py: codex_skin_sheet`) and
how NeonComet's are made (same): per skin one Codex job with refs `skinref_state{0,1,2}.png` (the FINAL
approved flipbook, three 3x3 sheets, 512 px cells) -> outputs `skin_<Name>_state{0,1,2}.png` (3x3 grid,
1024 px cells, 3072x3072). The brief says: the drawing must stay identical cell for cell (silhouette, panel
lines, damage, cracks, glints, bank perspective, light dashes) and ONLY colours change; names the armour
target as base/shadow/highlight hex (the `ShipSkins.cs` row colours: hue, hueShadow, hueHighlight); canopy,
engine glow, ink edge, glints and all damage effects stay; hit-flash stays bone-white; and demands
`tools/check_skin_<Name>.py` with: silhouette diff vs ref cell < 1.5%, canopy/blue region diff < 6%, fraction
changed > 12% (a special livery skin: > 3%). Generate the four briefs with `briefs/mk_skin_briefs.py` (edit the
`skins` dict: one entry per skin with its colour sentence; Special skins = racing / chevrons / diagonal /
split livery pattern in the `accent*` colours from the ShipSkins row, described in words and positions). Run the
four jobs one after another (`scripts/codex-art.sh` serialises; a chain script is fine). `build_restyle.py`
then keys + downsamples each cell, imposes the stock alpha exactly, fills any silhouette pixel Codex left green
from the palette-shift fallback (it prints the count; a few hundred is fine, thousands = redo), and writes
`.bytes` PNGs (copy a neighbour's `.bytes.meta` ONLY for a brand new skin; existing ones keep theirs).
All three damage states of every skin come from the Codex sheets, so damage looks right per skin.
Check any skin colour rows that break the art rules (no pink outside Space: e.g. Seraph #C8285E, Jester
#B83CC0 are pink-ish: keep the table colours but paint them clearly red/violet, flag it to the user).

## 6. Verify (do all of it; Codex's report is not evidence)

1. View frames: `Read` the sheet on a dark background (composite `<Key>.png` over `#0e1424`, save a PNG in the
   scratchpad) and every skin's row 0; zoom crops of glint cells and banks (nearest upscale). Look for:
   misaligned cells (hull jumping between columns), clipped tips, green fringe, pink, damage that does not
   progress row 0 -> 1 -> 2.
2. Numbers: for each of the 27 cells, bbox centre/size drift vs cell 0 (idle cells within ~2 px except the 4 px
   bob), alpha strictly {0,255}, rows 1-2 alpha == row 0 alpha, % pixels differing row0->row1 and row1->row2
   (must grow), glint/lights cells differ from key (> 0.4%), banks differ after best translation (> 5%),
   `PIL.Image.open(f).load()` succeeds, sheet size 2304x768, each skin sheet alpha == stock alpha.
3. Emitters: `build_restyle.py` asserts every emitter sits on paint (margin 2 px) in all poses it is drawn in;
   nozzles are printed; open a crop around them.
4. Never run Unity tests locally; CI runs them after the push. A local Unity run is only the bake (step 7).

## 7. Bake hitbox + shield silhouettes (Unity, one at a time)

`ShipHitbox` / the shield contour read `Resources/Shield/hull_silhouettes.bytes` + `hull_hitboxes.bytes`, baked
from every stock rest drawing. After the sheet is final (and again after any later ship lands):

```
cp -Rc <idle worktree>/Pause/Library Pause/Library     # APFS clone, only once per worktree (e.g. ../rec-ships)
UNITY_BATCH_IGNORE_FOREIGN=1 UNITY_BATCH_MAX_SWAP_MB=8000 scripts/unity-batch.sh \
  -projectPath "$PWD/Pause" -executeMethod ShieldSilhouetteBaker.Bake -batchmode -quit
```
(~30 s; if it hangs or shows fake compiler errors kill the stale `Unity.Licensing.Client` helper and rerun.)
Unity also rewrites ~100 unrelated `.meta` files and drops untracked `.meta` for other files: revert them
(`git ls-files -m | grep '\.meta$' > list` then `git checkout --pathspec-from-file=list`, delete untracked
`*.cs.meta` it created) and commit only `hull_silhouettes.bytes` + `hull_hitboxes.bytes`. When several agents
restyle ships in parallel, the two `.bytes` conflict: whoever merges last re-runs the bake on the merged tree.

## 8. Commit

One commit per ship, only your files, by path: `Hulls/<Key>.png`, `Hulls/src~/<key lower>/` (codex outputs,
briefs, data.json; keep working PNGs under ~2 MB each, quantize big Codex sheets if needed), `Skins/<Key>_*.bytes`,
`Exhaust/exhaust_atlas.png`, `Shield/hull_*.bytes`, the three patched `.cs` files, `src~/ships.py`/`build_restyle.py`
if you extended them. Message: `art: restyle <Key> in the Paragon style (hull, damage, skins, flames)`.
Dock / codex / hearts / weapons read the sheet and tables above and need no change. Push the branch
(`git push origin <branch>`), never merge to master; the final PR to Wintersina/Pause is opened by the coordinator.

## Tests (CI only) and what a restyle must wire

Add the ship's id to `PaintedShips.restyled` (`Scripts/Ship/PaintedShips.cs`). The vector-art tests
(`ShipArtTest` flat fills, `ShipSkinsTest` flat-fill / friendly-red pixel checks, `ShipDamageTest`'s 2.5x
damage ratio -> "clearly worse" 1.3x rule) skip painted ids; sheet contract checks (alpha identical across
states and to stock for every skin, damage progression) still run. `ExhaustStyleTest` requires the BOOST
strip > 1.08x the cruise strip in brightness and longer: `build_restyle.heat()` grows the hotter bands one ring
outward in the boost frames (NeonComet ratio 1.22); check it with the sum of alpha*(r+g+b) over both strips.
Failures seen on NeonComet's first PR run: [SD] x5, [SA] x1, [SK] x6 (all the exemptions above) and [EX] x1 (boost).

## Pitfalls

- A faint alpha rim fails the art test: `downscale_rgba` thresholds alpha at 128 -> binary. Keep it.
- Banks: Codex's painted banks differ ~15% in silhouette; fine. Emitters are nudged against the ACTUAL sheet alpha.
- Rect width changes the sprite bounds; ppu = rect width / world_w keeps the old world size (so hull_w must match
  the old drawing width, otherwise the ship visibly shrinks or grows in game).
- Flame strips: more frames than the old strip overwrites the next strip in the atlas. Keep the old frame count.
- Do not regenerate a restyled ship with `build.py` / `build_skins.py` (blocked by `RESTYLED`).
- Skin count: every ship has exactly 4 non-stock skins (PerShip = 5), so 4 Codex skin jobs per ship.
