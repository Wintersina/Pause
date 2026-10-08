# Frost backdrop v3, run C painted key sources

All five candidates were made with the built-in image generation tool using a
transparent background. Each is a source contact sheet, not a finished atlas.
`build_c.py` crops the painted poses, preserves point sampling, matches each
loop to a fixed Frost palette, and authors the in-between frames. The ground
reference was the committed `mid.png`, `landmarks.png`, `weather.png`, and the
Frost direction in `docs/art-style.md`.

| Painted source | Prompt direction | Used for |
|---|---|---|
| `smoke_keys.png` | 4×2 sheet: four thick grey-violet refinery stack plumes drifting right above a tiny ember-lit emitter; four pale thin refinery steam-smoke plumes curling left. Down-looking industrial frozen ocean, hand-placed pixel clusters, real alpha, no scenery or space. | `smoke_a`, `smoke_b` |
| `steam_keys.png` | 4×2 sheet: vent puff grows and dissipates; icy cryo geyser grows, peaks, falls, with fractured crystals and cyan-white vapour. Transparent isolated key poses, sparse pixel glow. | `steam_vent`, `geyser` |
| `fire_lights_keys.png` | 4×2 sheet: tiny orange-magenta rig flare flames with sparks and an emitter; four faint cold-cyan overhead searchlight cones from a small lamp. Transparent painterly pixel art, industrial Frost context. | `flare`, `searchlight` |
| `beacons_keys.png` | 4×4 sheet: magenta beacon states, cyan beacon states, double-flash white strobe states, and small warm/cyan compound lamp and window clusters. One tiny isolated emitter per panel. | `beacon_magenta`, `beacon_cyan`, `strobe_white`, `window_lights` |
| `aurora_keys.png` | 4×2 sheet: eight wide, very faint teal-blue/violet aurora curtain poses with short vertical pixel streaks. Viewed beneath the cloud ceiling over a planet, no stars or space, true alpha. | `aurora` |

Selection and cleanup: all five source sheets were retained after visual review.
The geyser's painted peak touched its source-panel border, so its top was
tapered into pixel wisps. The thin refinery smoke had a few particles from the
adjacent painting, removed by keeping the connected plume and nearby flecks.
The source searchlight cone is warped into eight projected azimuth poses; its
emitter stays at `(128,236)` in each cell. Beacon and window lamps have small
hand-placed stepped cores over their painted fixtures.

Regenerate with `python3 src~/build_c.py`, then check with
`python3 src~/verify_c.py` from this directory's parent.
