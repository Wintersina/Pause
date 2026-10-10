# Boss death sounds (hook only, no clips authored yet)

`EnemyDeathAudio.PlayBossDeath(artKey, forceScream)` runs from `BossActor.BeginOutro(explode: true)` (real boss kill)
and from the Codex boss triple-tap (`CodexPanel.PlayDetailDeath`). It plays authored clips if present, else stays silent
(no warning, no beep, no fallback).

Drop WAVs into `Pause/Assets/Resources/Audio/EnemyDeath/` (same folder and import settings as the other death cues):

| Boss | Death cue (variants `_0`, `_1`, ...) | Scream layer (living bosses) |
|---|---|---|
| Void Archon (Space) | `boss_space_0.wav` | none |
| Hoarfrost Leviathan (Frost) | `boss_frost_0.wav` | `boss_frost_scream_0.wav` |
| Bloom Queen (Verdant) | `boss_verdant_0.wav` | `boss_verdant_scream_0.wav` |
| Cinder Drake (Ember) | `boss_ember_0.wav` | optional `boss_ember_scream_0.wav` (only plays if present) |
| Iron Kraken (Tide) | `boss_tide_0.wav` | optional `boss_tide_scream_0.wav` |

Character: a heavy, long explosion (about 1.5 s, low boom, debris tail) with a huge creature or machine cry on top.
Space: capital-ship hull breach, metal groan and reactor collapse. Frost: ice and steel shatter. Leviathan and Bloom Queen
also get a scream layer (forced on in the Codex tap, `ScreamChance` in the fight). The cue uses `EnemyDeathAudio.BossVolume`.
Only Frost and Verdant request the scream layer (`BossScreams`); a scream clip for another boss is used only if code
passes forceScream for it.
