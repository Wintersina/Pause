# Boss death and hurt sounds

Status 2026-10-11: authored for all five bosses (Space, Frost, Verdant, Ember, Tide).

`EnemyDeathAudio.PlayBossDeath(artKey, forceScream)` runs from `BossActor.BeginOutro(explode: true)` (real boss kill)
and from the Codex boss triple-tap (`CodexPanel.PlayDetailDeath`). `EnemyDeathAudio.PlayBossHurt(artKey)` runs from
`BossActor.SetHearts` whenever a blow costs a heart (never the last one). Both play authored clips from
`Pause/Assets/Audio/Resources/Audio/EnemyDeath/` or stay silent (no warning, no beep, no fallback).

| Boss | Death cue | Scream layer | Hurt blow (0.55-0.7 s, A50 -17 dB) |
|---|---|---|---|
| Void Archon (Space) | `boss_space_0/1.wav` | none | `boss_space_hurt_0/1.wav` hull strike, torn plates |
| Hoarfrost Leviathan (Frost) | `boss_frost_0/1.wav` | `boss_frost_scream_0/1.wav` | `boss_frost_hurt_0/1.wav` ice-steel crack |
| Bloom Queen (Verdant) | `boss_verdant_0/1.wav` | `boss_verdant_scream_0/1.wav` | `boss_verdant_hurt_0/1.wav` petal-armour rupture |
| Cinder Drake (Ember) | `boss_ember_0/1.wav` | none | `boss_ember_hurt_0/1.wav` scale fracture |
| Iron Kraken (Tide) | `boss_tide_0/1.wav` | none | `boss_tide_hurt_0/1.wav` pressure-hull dent |

Sources: `Audio/EnemyDeathSrc~/build_boss_gate_sounds.py` (deaths, screams, gates) and `build_boss_hurt_sounds.py` (hurts; notes
`sound_notes_boss_hurt.md`, audition `audition_boss_hurt.wav`). All noise / impact based, no tones. Only Frost and Verdant request the scream layer
(`BossScreams`). The scream-borrow map (`EnemyDeathAudio.ScreamBorrow`) stays empty until the user approves it.
