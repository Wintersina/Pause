# Longer scream borrowing proposal

These are proposed substitutions at lower volume, with no new WAVs or gameplay edits. Each recipient has a unique donor, so enemies that appear together never share one. Pitch is playback speed relative to the donor clip; the revised donor lengths still leave roughly 0.64–0.81 s of cry after pitching up.

| Small key | Donor key | Pitch | Reason |
|---|---|---:|---|
| `space_fighter_1` | `ember_fighter_1` | 1.25× | Small scout pilot; startled high cry. |
| `space_fighter_2` | `verdant_fighter_2` | 1.20× | Claw pilot; tense nasal cry. |
| `space_fighter_3` | `ember_fighter_3` | 1.16× | Twin crew craft; strained breathy cry. |
| `space_alien` | `verdant_alien` | 1.32× | Living Bile Mite; wet creature voice. |
| `frost_alien` | `ember_alien` | 1.38× | Living Cryo Jelly; reedy creature voice, with the low volume masking its fiery texture. |

Use `BorrowVolume` at 0.7 or lower relative to the normal scream volume, then check the 10–14 dB voice/death gap on device. The Space recipients already own shorter scream clips, and `EnemyDeathAudio.ScreamBorrow` currently only applies to keys **without** their own scream clips; a later implementation must choose to replace those Space voices. Frost currently has no authored death WAV, so its borrow also requires an authored death cue before the current playback path can use it. This file is only a proposal.

The Frost fighters (`frost_fighter_1` through `_4`) are described in the roster as crystal **drones**, so they are excluded by the living-occupant rule. The Frost chaser is also a machine. Space Warden is an officer craft, but it is the heavy fighter rather than a small enemy. Mines and rocks are excluded.
