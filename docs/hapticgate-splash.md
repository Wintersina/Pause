# HapticGate splash (first scene, `spashS7`)

The title card: a steel two-leaf gate that is forced open, with the HapticGate
mark behind it. It is violent on purpose, and three taps smash it.

## Behaviour

Code: `Scripts/UI/splashScene.cs` (scene component, layout, input, scene change),
`GateSim.cs` (pure simulation, fake-clock friendly), `GateView.cs` (renderers and
particle pools), `GateArt.cs` (art slots and procedural stand-ins),
`SplashInput.cs` (press reading). All time is **unscaled** (`Time.unscaledDeltaTime`,
clamped to 0.1 s per frame), so a leftover `timeScale = 0` cannot stall the card.

**Intro** (`holdSeconds`, 2.3 s in the scene):

1. 0 - 0.7 s: both leaves rattle on the latch. Amplitude rises from sub-pixel to
   a hard rattle (hash jitter on x, y and rotation, re-rolled about 50 Hz, plus a
   faster sine). Steam jets from the leaf grilles with pressure building.
2. 0.7 - 1.9 s: the leaves are forced apart in seven jerky steps (snap, recoil,
   hold, rattle, snap). Each step is an *impact*: card shake kick, flash, glow at
   the seam, sparks, debris chunks and a steam gust. The last step overshoots and
   shudders to a stop. The logo is revealed behind.
3. Then the card leaves to `startS4`.

The "camera shake" moves the whole card (mark, door and the A / Game words) rather
than the camera, so the black backdrop always covers the screen.

**Tapping** (the intro keeps playing - it is never frozen, so a single tap and
walking away still ends in the normal finish):

| press | effect |
|-------|--------|
| 1 | crack overlay 1, sharp shake, a few sparks and chips |
| 2 | crack overlay 2, leaves bulge, steam leaks from the cracks, bigger shake |
| 3 | crack overlay 3 for a beat, then the SMASH: big debris burst, flash, hard shake, leaves vanish; next scene loads 0.45 s later |

* Presses are discrete: only the frame a mouse button / finger goes down counts.
  Hold-repeat does nothing; several fingers (and the mouse that touch emulates) in
  one frame are one press.
* Presses queue (up to the three needed) and are applied at least 0.12 s apart, so
  even three taps in one frame show crack 1, crack 2, smash in order. None is lost.
* Presses in the first 0.15 s are dropped (stray tap from the previous screen).
  Presses after the smash are ignored. The scene is loaded exactly once.
* If the intro runs out before the taps complete, the card just proceeds normally.
* Back / Escape leaves at once, as before (a `BackNavigator` layer; only `BackNavigatorRunner` reads Escape). Any other key counts as a press.

## Art slots

All installed (Codex, branch `art/gate-steampunk`, commit 0313a15c; source and
`verify.py` stay in `art/gate-steampunk` under `Art/HapticGate~/`). PNGs in
`Assets/Resources/HapticGate/`, loaded by exact slot name in `GateArt.cs`. If any
slot is missing, `GateArt.Load` returns null and the card shows just the mark.

| Resources path | spec |
|---|---|
| `HapticGate/industrial_gate_v4` | 6144x4096 two-leaf door (HD, exact 4x of the old 1536x1024 v3, same layout); crop contract (fractions, resolution-independent) left x 0.055, right x 0.51, w 0.44, y 0.05..0.95 |
| `HapticGate/gate_cracks_1` `_2` `_3` | 6144x4096 RGBA cumulative crack overlays, cut with the same leaf rects |
| `HapticGate/gate_debris` | 1024x512, 8x4 cells of 128 px (drawn at 1.8x: the shard fills about half a cell) |
| `HapticGate/gate_steam` | 1024x1024, 4x4 cells of 256 px: columns are growth stages (stepped with the puff's age), rows are variants |

Importer settings:

* Door and cracks (HD, painted art, not pixel art): texture type Default (GateArt makes its own
  sprites), **Trilinear** filter (`GateArt.Load` sets it), **mipmaps on** (a leaf is shown at
  roughly a third of its texture size on phones; without mips it shimmers during the shake),
  **Non-Power-of-2: To Nearest**, max size 2048. The 6144x4096 source therefore imports as
  **2048x1024** (about 900 texels across a leaf for ~330 px on a 1080 phone, ~600 px on a
  tablet). Android and iOS: ASTC 6x6 (set by `AndroidTextureDiet`, which skips its
  "NPOT means no mips" rule when NPOT scaling is on).
* The To-Nearest import squashes the sheet vertically by 0.75, so `GateArt.YStretch(sprite)`
  (= texture w / (1.5 x texture h)) is applied to the leaf transform's y scale in
  `GateView.NewLeaf` / `SetLeaf`; overlays inherit it. Anything that swaps the door for a
  different-shaped import must keep that in mind (a true 3:2 texture gives 1).
* `gate_debris`, `gate_steam`: unchanged (Sprite, Point, no mips, uncompressed).

GPU memory, measured with `Profiler.GetRuntimeMemorySizeLong` on the imported door (ASTC 6x6,
12 mips): 2.50 MB per HD texture, 10.0 MB for the four (the old four 1536x1024 sheets were
about 2.8 MB). Raising `maxTextureSize` to 4096 gives 4096x2048, about 10 MB per texture
(40 MB total) for more detail on very large tablets; the crop fractions need no change.

To swap art later, replace the PNGs (same names, same layout; any 3:2 size works since the crop is fractional) and keep the meta
settings. If the crop contract changes, edit the constants at the top of
`GateArt.cs`; vent and crack anchor points in `GateView.cs` (`LeafPoint(...)`) are
leaf-relative (u across, v up).

Art note: `industrial_gate_v4.png` is an alpha cut-out (leaf alpha bounds are the old (85,37,1451,947) x4; transparent outside and in the central gap), so no backdrop box shows against the black card.

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
  `AllTests`. Fake clock via `splashScene.Step(dt)` / `GateSim.Advance(dt)`;
  `splashScene.LoadScene` and `InputSource` are replaceable hooks. The legacy
  `Input` class cannot be injected in a batch editor, so the real reader
  (`SplashInput.ReadLegacy`) is only checked to be idle and the scene's `Update` is
  driven through the same hook the real reader plugs into.
* Existing: `SplashLayoutTest` (layout), ScreenFit's `splash` screen.
* Full-frame captures at 1080x2520 and 1600x2560: `-executeMethod GateShotPreview.Render -shotDir <dir> -shotPrefix <name>`.
* Filmstrips: `-executeMethod HapticGatePreview.Render -previewDir <dir>` writes
  `natural.png`, `tap1.png`, `tap2.png`, `tap3.png`.

## Brand mark (final frame) and opening clip

* `HapticGateMark` loads `Resources/HapticGate/hapticgate_mark.png` (closed gate + smoke, 512x448)
  and `hapticgate_lockup.png` (gate + smoke + wordmark, 640x640): the approved closed-gate logo from
  `HapticGate-brand/closed/`. Replace those two files to change the logo; nothing else knows about them.
  `splashScene` shows the mark above the wordmark, fading in as the door passes `MarkRevealFrom` (half open)
  and solid when it is open or smashed (`MarkReveal`); Credits shows the lockup above "Made by HapticGate"
  (`CreditsMove.EnsureLockup`).
* The user's `gate_open.wav` (Audio/Resources/Audio/GateOpen, 5.6 s stereo 48 kHz, Vorbis q0.65, 141 KB
  imported) replaces the natural-run rattle and the seven step shrieks (`GateAudio.OpenReplaces`). Its first
  big hit is 1.11 s into the file (the broadband sweep ends and the latch lets go; found with astats/spectrogram);
  `splashScene.OpenClipStart` = 0.11 s lines that with the door's biggest forced step at 1.22 s. The loudest
  peak (5.0-5.1 s, the closing swell) cannot line up with a 2.3 s card. Tap cracks and the smash are unchanged;
  a smash fades the clip out in 0.1 s (Back too), a natural finish over 0.8 s; the card never waits for it, and
  if the three taps are queued before 0.11 s it never starts. Mute (`SoundSettings`) is honoured. Tests: GateAudioTest.
