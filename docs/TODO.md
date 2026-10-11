# TODO

Open items waiting on the team. Code that depends on an item carries a matching
`TODO(<tag>)` comment.

## Boss music `TODO(boss-music)`

Boss music: user will supply a custom track per world boss. Drop files at
Resources/WorldMusic/Boss_<World>.(wav|ogg); WorldMusic picks them up automatically.

- Folder: `Pause/Assets/Audio/Resources/WorldMusic/`
- Names: `Boss_Space`, `Boss_Frost`, `Boss_Verdant`, `Boss_Ember` (`.wav` or `.ogg`)
- Code: `WorldMusic.BeginBoss` / `WorldMusic.EndBoss` in
  `Pause/Assets/Scripts/Worlds/WorldMusic.cs`. The track crossfades in when
  the boss intro starts, and the world's own track comes back when the
  encounter ends. Until a file exists, the world's current track keeps
  playing at a slightly raised pitch (`BossFallbackPitch`).

- Codex-only scream boost: when the Codex triple-taps a ship (`EnemyDeathAudio` forceScream), the scream plays at cue volume x `CodexScreamBoost` (2.6, clamped to 1, no BorrowVolume cut, 0-20 ms delay) over a death cue ducked by `CodexDeathDuck` (-6 dB); in-game deaths are unchanged.
