# Pause — User Art and World Brief

This is the authoritative working brief consolidated from the art-direction conversation. It records requests, decisions, approvals, and safety rules so work can continue one asset at a time without drifting from the agreed direction.

## Non-negotiable art direction

- Recreate assets from scratch rather than applying superficial touch-ups to the old vector/SVG art.
- Use dense, readable pixel-art sprites in a rugged cyberpunk + steampunk industrial direction: dark inked outlines, gunmetal/charcoal armor, brass/copper mechanical details, rivets, pipes, cables, scorched/weathered surfaces, and bright world-specific energy cores.
- Keep the confident, polished detail level of the approved rail mine, boss, Bile Mite, Frost alien, and Space fighter examples.
- Keep enemy silhouettes visibly varied. Each role and world needs a distinct silhouette and material story, not recolors of the same body.
- Preserve only the gameplay contracts during a redraw: filename/key, transparency, canvas dimensions, frame count/grid, animation state order, pivots, and hit/collision behavior.
- Never alter player fighters while rebuilding enemy fighters.

## Review and replacement process

1. Rebuild one asset or one coherent family at a time.
2. Stage the fresh raster candidate first.
3. Open every completed section in Preview for review.
4. Install it into the live game only after approval; preserve the existing Unity `.meta` importer/GUID.
5. Validate the exact dimensions, alpha, frame contract, and relevant Unity tests.
6. Delete superseded old vector/source art only after every matching live replacement is installed and verified. Do not delete it early.

## Enemy roster to rebuild

Rebuild all enemies, across Space, Frost, Verdant, and Ember, in the agreed pixel direction:

- **Rocks / obstacle hazards**
  - Space: `space_rock_crater`, `space_rock_cluster`, `space_rock_dark`
  - Frost: `frost_rock_chunk`, `frost_rock_rime`, `frost_rock_shard`
  - Verdant: `verdant_rock_spore`, `verdant_rock_vine`, `verdant_rock_pod`, `verdant_rock_knot`
  - Ember: `ember_rock_magma`, `ember_rock_islet`, `ember_rock_cinder`, `ember_rock_obsidian`
- **Enemy fighters, tiers 1–4**
  - `space_fighter_1` … `_4`, `frost_fighter_1` … `_4`, `verdant_fighter_1` … `_4`, `ember_fighter_1` … `_4`
- **Heavies**
  - `space_big`, `frost_big`, `verdant_big`, `ember_big` — includes Bastion-style heavy role.
- **Chasers**
  - `space_chaser`, `frost_chaser`, `verdant_chaser`, `ember_chaser` — includes Steel Hound and named world equivalents such as Flake/Icicle/Wasp where applicable.
- **Aliens**
  - `space_alien`, `frost_alien`, `verdant_alien`, `ember_alien` — includes Bile Mite and world equivalents.
- **Rail mines / rail hazards**
  - Four themed states/rows. Retain the currently approved rail/mine quality and rail-clamped behavior. Only redraw if a review identifies a real gap.
- **Hazard effects**
  - Rebuild destruction/explosion effects and projectile hazards to match the same visual direction.

## Current enemy approvals and status

- **Approved:** all sixteen enemy fighter strips. Space Fighter 1 is already live; the other approved strips must be installed safely in their matching live slots.
- **Approved and installed:** Space Bile Mite / `space_alien.png`, new 7-frame raster strip, 1344 × 192 with alpha.
- **Approved and installed:** Frost alien / `frost_alien.png`, new 7-frame raster strip, 1344 × 192 with alpha.
- **Generated/staged work requires review before install:** remaining enemy candidates and rock strips. Nothing is assumed approved merely because it exists in staging.
- **Unfinished:** Verdant and Ember aliens; chasers; heavies; missing Ember rocks; hazards/effects; the remaining staged enemy installation pass.

## Bosses

- Boss concepts must use the same dense, rugged cyberpunk/steampunk pixel-art vibe, but fit gameplay scale; the early oversized concepts are not the live scale target.
- Create complete animation sprite sheets, not isolated concepts.
- The approved four boss body atlases are now integrated live:
  - `Resources/Bosses/Space.png`
  - `Resources/Bosses/Frost.png`
  - `Resources/Bosses/Verdant.png`
  - `Resources/Bosses/Ember.png`
- Body atlas contract: 1920 × 1536, 5 × 4 grid, 20 non-empty 384px cells. Existing Unity metadata was retained and Boss encounter validation passed.
- Still rebuild/align boss shots, cards, and remaining animation support before deleting any old boss SVG source frames (for example `Pause/Assets/Art/Bosses/Frost/src~/frost_07_tell1_a.svg`).

## Worlds, background, and rails

- Fully refactor each world’s visual presentation in the same high-detail pixel direction.
- Start with Space and use the approved space-world concept as the visual benchmark: live nebula/space depth, industrial rail framing, large planet, debris, and cyberpunk magenta/cyan lighting.
- Backgrounds must be live rather than static: parallax layers, animated background effects, and visibly spinning planets.
- Rail system is approved and should remain industrial, readable, and integrated with the worlds.
- Rail mines must mount to and travel down actual rails; their behavior must align with the rail’s position and scrolling rather than using a fixed screen position.
- Space backdrop was installed as the approved live Space background set. Frost world assets are staged for review; Verdant and Ember worlds remain queued.

## Engineering / workflow constraints

- Another contributor may merge score-refactor work; preserve unrelated changes and be ready to integrate around them.
- Track work in `docs/art-production-queue.md` one item at a time.
- Do not claim a subagent is producing files unless it has actually started successfully and produced a tangible artifact.
- If the subagent dispatcher errors, continue directly in this session and keep the production queue accurate.
