# World attack colours: no pink outside Space (user, 2026-10-11)

Rule: only Space keeps the hot pink cue. Every other world's hostile things (shots, beams, hazards, telegraphs, impacts, warning banner,
painted atlases) use that world's own cue colour. Single source of truth in code: `WorldCue` (`Gameplay/Enemies/WorldCue.cs`); the
rail-mine constants (`MineCue`, `recolor_nopink.py`) are the same numbers.

| World | cue | hue | body sat | notes |
| --- | --- | --- | --- | --- |
| Space | hot pink (unchanged) | 318 | .70 | `HostileShotPalette` 312-326 |
| Frost | icy white-cyan | 195 | .45 | cyan elite defs kept |
| Verdant | green-white (jade) | 135 | .60 | clear of repair-green 82 |
| Ember | amber / orange-white | 32 | .85 | clear of the player's red band (< 28) |
| Tide | aqua-white | 188 | .45 | |
| Storm | white-violet | 258 | .38 | pale; elite defs kept |
| Desert | sun-white / gold | 45 | .33 | S <= .33, never star-dust amber; elite defs kept |

## Inventory (before this pass)

Pink = hue 280..352, chroma >= 9/255, not dark (saturated magenta AND pale pinkish-white / lavender-white). "Painted" = % of opaque pixels.

| Asset / runtime piece | Frost | Verdant | Ember | Tide | Storm | Desert |
| --- | --- | --- | --- | --- | --- | --- |
| Roster / elite shots (runtime tint, `HostileShotPalette`) | pink | pink | pink | pink | pink | pink |
| Elite shot colour in defs (`shotColor/shotCore`) | cyan ok | pink core | pink/violet | pink | ok | ok |
| Hazard art: ring / strike / jet / wave / lash / motion bodies (procedural) | pink | pink | pink | pink | pink | pink |
| Telegraph previews, strike band, log / lash tint | pink | pink | pink | pink | pink | pink |
| Boss shot rim (`BossProjectiles.glowTint`) | pink | pink | pink | pink | pink | pink |
| Boss shots atlas | 47% pink | 8% | 46% | ok | ok | ok |
| Boss laser kit (`Attacks/<W>/_attack_laser*`) | 50% | 47% | 27-37% | none | none | none |
| Boss atlas / damage / death / card | 2-3% (+pale) | damage 0.7% | 14% body + tells | ok | ok | ok |
| Boss attack fx sheet row | ok | ok | 24% | ok | ok | ok |
| Verdant attack shots / lash / log | | 26% / 8.5% / 6.7% | | | | |
| Rail mine beams (pale specks 255,224,248 missed by the first pass) | pale | pale | pale | pale | pale | n/a |
| Boss WARNING banner (shared painted, pink stripes) | pink | pink | pink | pink | pink | pink |
| Elite rail spark (`EliteFx.RailSpark`) | pink | pink | pink | pink | pink | pink |
| Ember boss hit flash | | | magenta | | | |
| Enemy / elite bodies | clean | clean | clean | clean | clean | clean |

All of the above are now the world's cue colour (or free of pink). Not changed on purpose (listed as follow-ups): the Verdant lit rail-mine
atlas bodies (`rail_mines_neon_verdant_lit.png`, enemy art, pink thorn tips), elite heart colours (`heartColor` violet / magenta in Ember, Verdant),
Codex / title-screen mock shots that read the def colours, sounds.

## Tools

* `Art/Attacks/WorldCues~/recolor_world_cue.py` recolours every painted attack file listed in its `JOBS` table from `pink_original/` (originals kept; idempotent).
* `Art/Attacks/MineBeams~/recolor_nopink.py` (rail-mine atlases; same mask since this pass) and `verify_minebeam.py` (now fails on any pink pixel).
* `.claude/skills/add-world/scripts/verify_attack_art.py --world <non-space world>` holds the atlas to zero pink pixels.
* Guard: `NoPinkOutsideSpaceTest` (cue table, every painted file, every procedural sprite, runtime tints).
