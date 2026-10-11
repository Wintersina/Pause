# Tide sounds: reuse map and what is authored

Rules (user, 2026-10-09): never a beep or a coin; noise / impact / material only; the world's backdrop, ambience and music are the
user's, none are added here.

## Reuse: deaths (`EnemyDeathAudio.DeathAlias`)

A Tide key with no clips of its own plays a donor key's clips at a pitch (playback speed relative to the donor), and the authored
`tide_wet_0..2` layer plays under it (`WetVolume` .6 of the cue). A key that owns clips never aliases. `boss_tide_0/1` already exist.

| Tide key | Donor | Pitch | Why |
| --- | --- | --- | --- |
| tide_rock_brain | frost_rock_chunk | .85 | heavy crunch, slower |
| tide_rock_staghorn | frost_rock_shard | 1.00 | brittle blade shatter |
| tide_rock_urchin | ember_rock_obsidian | 1.12 | glassy spikes snapping |
| tide_rock_islet | ember_rock_islet | .92 | the floating slab again, softer |
| tide_mine | frost_mine | .90 | limpet mine |
| tide_big | verdant_big | .85 | big organic hull |
| tide_fighter_1 / 2 / 3 / 4 | frost_fighter_1 (1.10) / frost_fighter_2 (1.00) / frost_fighter_3 (.90) / frost_fighter_4 (.80) | | Remora, Needlefish, Lantern Angler, Hammerhead (heavy, low) |
| tide_chaser | space_chaser | .95 | Wire Eel sparks |
| tide_alien | ember_alien | 1.15 | Glow Jelly; its cry is `ScreamBorrow` ember_alien at 1.30 |
| tide_elite_riptide | ember_elite_sunstoke | 1.10 | needle lancer |
| tide_elite_bathyscaphe | ember_elite_cauterizer | .80 | siege sphere |
| tide_elite_trawler | ember_elite_kilnback | .85 | hauler |
| tide_elite_thunderfin | space_elite_rift_lancer | 1.05 | rail / lightning |
| tide_elite_pearl_warden | space_elite_eventide_bastion | .90 | shield platform |

## Authored (water, the one thing nobody else says): `Audio/EnemyDeathSrc~/build_tide_sounds.py`

| Clip (3 variants each) | Where | Fired by |
| --- | --- | --- |
| `EnemyDeath/tide_wet_N` | under every aliased Tide death | `EnemyDeathAudio.PlayAuthored` |
| `Attack/Tide/jet_N` | pressure jet / water gun | `AttackJet` goes live (`AttackAudio`) |
| `Attack/Tide/wave_N` | surf wave: a swell, then the crash | `AttackWave` goes live |
| `Attack/Tide/strike_N` | thunder: crack, sizzle, long rumble | `AttackStrike` goes live (Thunderfin, Lantern Angler, boss) |
| `Attack/Tide/lash_N` | tentacle lash: a wet snap | `AttackLash` goes live (Tide) |
| `Attack/Tide/burst_N` | depth charge bursts into pearls | `EliteShot.Burst` (Bathyscaphe) |
| `Attack/Tide/splash_N` | a net lands as a pool | `EliteShot.Land` (Trawler) |

`AttackAudio` (new) plays `Resources/Audio/Attack/<World>/<cue>_N`; a (world, cue) without clips is silent, so no other world's
attacks change. Checks: `TideSoundTest` (clips resolve, other worlds silent, an autocorrelation scan finds no tone over 80 ms),
the generator's own table (peak / RMS, centroid, HF share, tonal run < 80 ms, no clipping, variant correlation < .6).
No listening monitor was available: audition `EnemyDeathSrc~/audition_tide.wav` and check on a phone speaker.
