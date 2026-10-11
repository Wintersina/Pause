# Storm sounds: reuse map and what is authored

Rules (user, 2026-10-09): never a beep or a coin; noise / impact / material only; the world's backdrop, ambience and music are the
user's, none are added here. The user's instruction for Storm: REUSE existing sounds wherever they fit, author only where nothing does.

## Reuse: deaths (`EnemyDeathAudio.DeathAlias`)

A Storm key with no clips of its own plays a donor key's clips at a pitch (playback speed relative to the donor), and the authored
`storm_crack_0..2` layer plays under it (`StormVolume` .55 of the cue; `LayerOf(key)` picks `storm_crack_` for `storm_*` / `boss_storm`,
`tide_wet_` for Tide). A key that owns clips never aliases.

| Storm key | Donor | Pitch | Why |
| --- | --- | --- | --- |
| storm_rock_hail | frost_rock_chunk | 1.20 | hailstone: hard-grain clatter, smaller and faster |
| storm_rock_ore | frost_rock_shard | 1.08 | crystal blade snap |
| storm_rock_slag | ember_rock_obsidian | .82 | dense iron thud |
| storm_rock_islet | ember_rock_islet | .85 | platform break, a duller crumble |
| storm_mine | space_mine | 1.00 | rod mine: concussive pop (the crack layer is its crackle tail) |
| storm_big | verdant_big | .75 | Cloud Whale: a huge slow body |
| storm_fighter_1 | space_fighter_1 | 1.15 | Stormray Pup, light plate crack |
| storm_fighter_2 | space_fighter_2 | 1.00 | Stormray, wing snap and gun pop |
| storm_fighter_3 | ember_fighter_3 | .90 | gunship, cannon drum boom |
| storm_fighter_4 | space_fighter_4 | .85 | Tempest Anvil, coil discharge and heavy collapse |
| storm_chaser | verdant_chaser | .90 | Cyclone Drone, rotor shreds (insect wing buzz, slower) |
| storm_alien | space_alien | 1.10 | Static Mote, ball lightning cage bursts |
| storm_elite_skyhook_harrier | space_elite_orbit_reaver | 1.05 | interceptor |
| storm_elite_refinery_tender | space_elite_singularity_hauler | .80 | floating refinery hauler |
| storm_elite_rod_dancer | space_elite_rift_lancer | 1.15 | rail / arc skirmisher |
| storm_elite_anvil_cloud | ember_elite_cauterizer | .75 | siege cloud |
| storm_elite_static_bastion | space_elite_eventide_bastion | .82 | ward bastion |
| boss_storm | boss_space | .88 | the floating turbine engine: a capital hull breaking (2 variants, like every boss; `BossKey("Storm")`) |

Boss damage has no separate sound cue in the code (only the death cue, `PlayBossDeath`), so nothing else is needed for it.

## Reuse: screams (`ScreamBorrow`)

Only units with a living occupant borrow (no cry for the mote, rotor drone, whale, mine or rocks):

| Key | Donor scream | Pitch |
| --- | --- | --- |
| storm_fighter_1 / 2 / 3 / 4 (pilots) | ember_fighter_1 / verdant_fighter_2 / ember_fighter_3 / space_fighter_4 | 1.35 / 1.10 / .95 / .90 |
| storm_elite_skyhook_harrier | ember_elite_coalrunner | 1.10 |
| storm_elite_refinery_tender | space_elite_singularity_hauler | .85 |
| storm_elite_rod_dancer | ember_elite_ash_wraith | 1.10 |
| storm_elite_anvil_cloud | ember_elite_brass_vulture | .85 |
| storm_elite_static_bastion | space_elite_eventide_bastion | .90 |

## Reuse: attacks (`AttackAudio`)

`AttackAudio.WorldFolders` gains "Storm" (index 5). `AttackHazard` now plays with its own unclamped world index (`audioWorld`), because
`ShotSkins.Worlds` is still 5 and clamps Storm's hazards to Tide's skins. `AttackAudio.Fallback(5, "strike") = 4`: a Storm lightning
strike plays Tide's thunder (`Attack/Tide/strike_N`: crack, sizzle, long rumble), no new clip. Cue labels (`AttackAudio.CueOf`) the
Storm attacks should use for their hazard's `Label` (or `EliteShots` can call `AttackAudio.Play(5, cue)` directly):

| Label | Cue | Clip |
| --- | --- | --- |
| "strike" | strike | Tide's thunder (fallback) |
| "arc", "arc bolt", "chain lightning", "ion bolt" | arc | authored `Attack/Storm/arc_N` |
| "gust", "gust wave" | gust | authored `Attack/Storm/gust_N` |
| "hail", "hail volley" | hail | authored `Attack/Storm/hail_N` |

Nothing fires them until the roster / elite / boss agents' attacks go live; a (world, cue) without clips is silent, so no other
world changes.

## Authored (`Audio/EnemyDeathSrc~/build_storm_sounds.py`, numpy + wave, deterministic seeds, 3 variants each)

| Clip | Where | What it is |
| --- | --- | --- |
| `EnemyDeath/storm_crack_N` | under every aliased Storm death | thunder crack, low rumble, sizzle and a far rolling echo (.75 s, quiet layer) |
| `Attack/Storm/arc_N` | arc / ion bolt hazard goes live | dry crackle (1.5-5 ms band-limited noise grains) over a thump and short rumble |
| `Attack/Storm/gust_N` | gust wave goes live | wind swell and rush past, two noise bands, no whistle |
| `Attack/Storm/hail_N` | hail volley | hard pellet clatter over a low drumming bed |

Checks: the generator's own table (peak / RMS, centroid, HF share, attack, tonal run < 80 ms, no clipping, variant correlation < .6, all
passed) in `sound_notes_storm.md`, `sheet_storm.png`, `audition_storm.wav` (variant 0 of crack, arc, gust, hail); `StormSoundTest`
(every Storm key resolves a donor and clips, screams, fallback, no leak into other worlds, an autocorrelation scan finds no tone over
80 ms in any authored clip). The reused death cues keep their already-equalised loudness; the donors' loudness is the approved set's,
only re-pitched .75-1.2x. No listening monitor was available: audition the file and the donor pitches on a phone speaker.
Most likely to need ear-tuning: `StormVolume` (.55), how the .75 / .82 pitched-down cues sound, and the gust.
