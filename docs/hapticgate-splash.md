# HapticGate splash (first scene, `spashS7`)

The title card plays the approved HapticGate gate-opening film (brand job
`anim_v2`, `~/Developer/HapticGate-brand/anim_v2/`): the closed steel H-gate in
charcoal smoke is forced open, smoke, steam and warm haze spill out, and the red
"HapticGate" wordmark flies out of the door and lands at the bottom. A card shake
and the user's gate-opening sound ride on top; three taps smash it.

## Behaviour

Code: `Scripts/UI/splashScene.cs` (scene component, layout, input, scene change),
`GateFilm.cs` (the film: frame table, progressive loading, one SpriteRenderer),
`GateSim.cs` (pure simulation: clock, shake, taps; fake-clock friendly),
`GateView.cs` (tap / smash effects over the film: particle pools, flash),
`GateArt.cs` (the effect sprite sheets), `SplashInput.cs` (press reading). All
time is **unscaled** (`Time.unscaledDeltaTime`, clamped to 0.1 s per frame), so a
leftover `timeScale = 0` cannot stall the card.

**Intro** (`GateSim.IntroSeconds` = film 3.33 s + 0.67 s hold = **4.0 s**):

| card time | film frame | what shows |
|---|---|---|
| 0 - 1.13 s | 0 (still) | closed gate in smoke; the card rattles harder and harder (shake), the opening clip's sweep builds (starts at 0.03 s) |
| 1.133 s | 34 | first split, lined up with the clip's big hit (1.11 s into the file + 0.03 s); card kick |
| 1.13 - 1.90 s | 34 - 57 | leaves forced apart (pause at 43-44, second shove at 45 = second kick), smoke and steam |
| 2.20 s | 66 | the wordmark appears tiny, deep in the gap |
| 2.57 s | 77 | it rushes out of the door, growing, heading down |
| 3.30 s | 99 | landed (bounce + neon flicker done); the "A" / "Game" words fade in over 0.2 s |
| 3.33 - 4.0 s | 100 (held) | final frame; at 4.0 s the card leaves to `startS4` (the clip fades over 0.8 s) |

The "camera shake" moves the whole card (film, mark and words) rather than the
camera, so the black backdrop always covers the screen.

**Tapping** (the film keeps playing - it is never frozen, so a single tap and
walking away still ends in the normal finish):

| press | effect |
|-------|--------|
| 1 | sharp kick, sparks + chips + glow off a leaf, small flash |
| 2 | bigger kick, more sparks, steam leaks from the leaves |
| 3 | SMASH: white flash, 40 chunks + 64 sparks + steam burst, hard shake, the film jumps to its final frame (gate open, wordmark landed); next scene 0.45 s later |

* Presses are discrete: only the frame a mouse button / finger goes down counts.
  Hold-repeat does nothing; several fingers (and the mouse that touch emulates) in
  one frame are one press.
* Presses queue (up to the three needed) and are applied at least 0.12 s apart, so
  even three taps in one frame show crack 1, crack 2, smash in order. None is lost.
* Presses in the first 0.15 s are dropped (stray tap from the previous screen).
  Presses after the smash are ignored. The scene is loaded exactly once.
* If the intro runs out before the taps complete, the card just proceeds normally.
* Back / Escape leaves at once, as before (a `BackNavigator` layer; only `BackNavigatorRunner` reads Escape). Any other key counts as a press.

## The film's frames

`Assets/Resources/HapticGate/Film/gate_film_NNN.jpg`, NNN = film frame at 30 fps,
one file per **kept** frame (`GateFilm.Kept`, 60 files, ~300 KB each, 18 MB):

* source: `anim_v2/frames_portrait/frame_0000..0100.png` (1080x1920; 101..179 equal 100),
* cropped to rows 416..1520 (1080x1104: every lit pixel of the whole film is inside),
  scaled 0.75 to **810x828**, JPEG q95 4:4:4 (no chroma subsampling: the red neon
  edges stay sharp),
* frames 1..33 equal frame 0 (not stored); in 58..73 (only the tiny wordmark deep in
  the gap moves, mean change 0.5/255) every second frame is stored; everything else
  (the opening and the wordmark's flight) at the full 30 fps.
* rebuild: `python3 Assets/Art/HapticGate~/build_gate_film.py [frames_dir]` (Pillow).

Importer (`Editor/Importers/GateFilmImporter.cs`): Default texture, no mipmaps,
Non-Power-of-2 None, clamp, bilinear, not readable, max 1024, desktop CompressedHQ;
Android / iOS **ASTC 6x6** from `AndroidTextureDiet` (soft painted splash art; at
~1.33x magnification on a 1080 px phone 6x6 is indistinguishable from 4x4 and half
the bytes). About 300 KB of GPU memory per frame, ~18 MB if all were resident.

Loading (`GateFilm`): `Begin()` loads frame 0 synchronously (one ~300 KB texture, in
`splashScene.Awake`), then the final frame (a smash jumps to it), then the rest
stream in with `Resources.LoadAsync` in film order, 3 in flight, up to 24 kept frames
ahead of the playhead, during the 1.1 s the closed gate stands still. A frame that is
not in yet never blocks: the newest loaded frame at or before it stays up. Frames
behind the playhead are released (`Resources.UnloadAsset`) as it passes them, the
final one stays. In edit mode (tests, previews) all frames load synchronously.

`GateFilm` is self-contained (film-local units, 1 unit = film width, `Aspect` tall):
`new GateFilm(parent, sortingOrder)`, `Begin()`, `Show(seconds)` every frame,
`ShowEnd()`, `Release()`; statics `FrameAt`, `KeyFor`, `OpenAt`, `LeafCentreX` and the
geometry constants (`GateCentreY`, `GateHeight`, `LeafWidth`, `LeafClosedX/OpenX`,
`WordmarkCentreY`, `WordmarkWidth`).

## Layout

The film is as wide as the view (`FilmFraction` 1: its edges are soft smoke on
black), but never so wide that its lit content (`FilmContentShare` 0.88 of its width)
leaves the safe area, and never taller than 0.9 of the safe area; it is centred in
the safe area. `Layout.filmWidth / filmCenter / gateCenter / logoCenter`. The mark
sprite (`HapticGate_title_red`) sits exactly where the film's wordmark lands, at its
width; it is hidden while the film plays and is the fallback if the film's art is
missing (the edit-mode layout tests and the screen-fit rig measure it there).

## Effect art slots

PNGs in `Assets/Resources/HapticGate/`, loaded by exact slot name in `GateArt.cs`.
If a slot is missing, `GateArt.Load` returns null and the card plays the film
without tap effects.

| Resources path | spec |
|---|---|
| `HapticGate/gate_debris` | 1024x512, 8x4 cells of 128 px (drawn at 1.8x: the shard fills about half a cell) |
| `HapticGate/gate_steam` | 1024x1024, 4x4 cells of 256 px: columns are growth stages (stepped with the puff's age), rows are variants |

Both: Sprite, Point, no mips, uncompressed. The old procedural door
(`industrial_gate_v4` 6144x4096 and `gate_cracks_1..3`) is retired (deleted,
`UnusedAssetGuardTest` keeps it out).

## Sounds

There is no authored cue set for this card, so the code only fires named events,
`splashScene.SoundCue` (static `Action<string>`). Sounds needed (go to Codex /
the user; no beeps or coin sounds):

* `gate_rattle` - once at the start: heavy steel latch rattling under strain.
* `gate_step` - 7 times, one per forced jolt: metal shriek and thud, steam hiss.
* `gate_crack` - tap 1 and tap 2: sharp metal crack / groan.
* `gate_smash` - tap 3: heavy impact, shattering metal, steam blast.

## Tests and previews

* Suite: `SplashViolentGateTest` (`-executeMethod SplashViolentGateTest.Run`), in
  `AllTests`: film frame table and files (every kept frame present, < 1 MiB, 810x828,
  import settings, ASTC 6x6), sim, film playback on the card clock, taps / smash,
  input path, screen fit on every `FitDevice`, zero allocations per frame.
  `SplashLayoutTest` (layout on 18 screens, film drawn at its layout, words timing),
  `GateAudioTest` (the clip's hit lines up with the film's split), ScreenFit's `splash` screen.
* Full-frame captures at 1080x2400, 1080x2520 and 1600x2560 (0.2 / 1 / 2 / 2.6 / 3 / 3.6 s, taps 1..3):
  `-executeMethod GateShotPreview.Render -shotDir <dir> -shotPrefix <name>`.
* Filmstrips: `-executeMethod HapticGatePreview.Render -previewDir <dir>` writes
  `natural.png`, `tap1.png`, `tap2.png`, `tap3.png`.

## Opening clip

* The user's `gate_open.wav` (Audio/Resources/Audio/GateOpen, 5.6 s stereo 48 kHz, Vorbis q0.65; byte-identical to
  the brand film's soundtrack `HapticGate-brand/audio/neon_latch_opening.wav`) replaces the natural-run rattle and
  the step cues (`GateAudio.OpenReplaces`). Its first big hit is 1.11 s into the file; `splashScene.OpenClipStart`
  = 0.03 s (the film's own audio delay) puts it on the film's first split (1.133 s). Tap cracks and the smash are
  unchanged; a smash fades the clip out in 0.1 s (Back too), a natural finish (4.0 s) over 0.8 s; the card never
  waits for it, and if the three taps are queued before 0.03 s it never starts. Mute (`SoundSettings`) is honoured.
* The Credits head their roll with the animated gate-opening logo (`GateOpenPlayer` + `GateOpenFrames`, frames
  `Art/Resources/HapticGate/GateOpen/gate_open_00..68.png`, 512 px RGBA, 15 fps, 4.6 s, then holds the last
  frame). The frames are a transparent re-render of `HapticGate-brand/final` (same `build_final.py` timing and
  particles, every 2nd frame up to the final hold). The closed-gate lockup (`hapticgate_lockup.png`,
  `HapticGateMark.Lockup`) is only the fallback when those frames are missing.
