# Boss telegraphs: only the major attacks, light and dotted

User direction: the on-beat preview was intrusive on busy sections. Now (1) only MAJOR attacks get a preview at all, and
(2) what is drawn is light, dotted and thinner / fainter toward a drop. Timing, gaps and fairness are untouched.

## Which attacks are major (one flag per attack)

`BossAttack.telegraph`: `-1` by kind (default), `1` always, `0` never; `BossAttack.IsMajor`. The by-kind rule is
`BossTelegraphStyle.MajorByKind`.

| kind | attacks today | major? | why |
| --- | --- | --- | --- |
| Beam | pod lasers (Void Archon), glare beams (Hoarfrost), brow laser (Cinder Drake), beak jet (Iron Kraken), coil beams (Tempest), lens beam (Dune-Eater) | yes | big sustained laser: hard to read, own dotted aim line + lock reticle |
| Lash | vine lash (Bloom Queen) | yes | the vine attack: a whip sweeping the ship's place, own dotted arc |
| Roll | trunk toss (Bloom Queen) | yes | heavy area denial on marked spots (own landing preview) |
| Jet / Wave / Blast / Strike | surf wave (Iron Kraken); eruption columns (Cinder Drake) | yes | area denial with no projectile to read; the preview is the only warning |
| Aimed / Fan / Lob | (none left in the live tables) | no | shots leaving the muzzles are readable |
| Pattern | every bullet-hell attack: curtains, rings, fans, rain, lattices, spirals, mines, bombs, breath / sweep waves, e.g. stinger thorns, ion arcs, fire breath, magma bombs, storm rain | no | ordinary bullet waves: no lane / line / ring overlay |

The boss's tell POSE and muzzle charge stay on the beat for all attacks (boss animation). To give one Pattern attack an
overlay (say Drake's fire breath), set `telegraph = 1` on it: the dotted lane overlay below then draws for it.

## What is drawn

Every major attack's preview is dotted and light, scaled by the song's intensity (calm -> drop); the actual laser beam,
vine and hazard art are unchanged.

- Hazards (Jet / Wave / Blast / Strike / Roll / Lash outlines, `AttackPreview`): when the hazard is armed by a boss
  (`BossTelegraphStyle.OwnerIntensity`) its dotted outline gets `PreviewAlpha` x alpha, `PreviewDot` x dot size and
  `PreviewSpacing` x spacing (fewer, smaller, fainter dots). Enemies and elites are unchanged. A boss strike no longer draws
  its filled lane band under the outline.
- Vine lash: the ghost whip and the dash arc ease with `PreviewAlpha`.
- Laser aim line: the painted dashed sight (and reticle) eases to `SightAlpha` toward a drop; a boss with no painted tell
  sheet (Tide, Storm, Desert) now gets a thin dotted line (`BossTelegraphStyle.Dash`) instead of a solid bar.
- The dotted lane / aim / ring overlay (`BossPatternMarkers`, opt-in via the flag; no attack uses it today): a lane is a
  brighter dotted PAIR on the gap's two edges. One pooled tiled sprite per line, 16 marks x 2 renderers built at fight
  start, nothing allocated while playing. Drawn only for its last `VisibleSeconds(intensity)` before the beat, total capped
  at `MaxTotalWeight` (alpha x dotted length). World cue colour lightened `PaleMix` toward white (no pink outside Space).
- Muzzle charge / flash glow keeps its art; alpha and size ease down at a drop (`MuzzleGlow*`).

## Tuning (all in `Scripts/Bosses/Patterns/BossTelegraphStyle.cs`, calm -> drop)

| constant | calm | drop |
| --- | --- | --- |
| `EdgeAlpha*` (lane edges) | .60 | .22 |
| `AimAlpha*` | .32 | .12 |
| `RingAlpha*` | .45 | .18 |
| `Width*` (world units) | .07 | .04 |
| `VisibleSeconds*` | 1.6 | .55 |
| `SightAlpha*` (laser aim line) | 1 | .6 |
| `MuzzleGlow*` (alpha) | .85 | .45 |
| `MuzzleSizeBusy` | 1 | .8 |
| `PreviewAlpha*` (hazard dots, factor) | .85 | .7 |
| `PreviewDot*` (dot size factor) | .9 | .8 |
| `PreviewSpacing*` (dot spacing factor) | 1.1 | 1.4 |

Dots: `DashPixels` 2, `GapPixels` 3 at `PixelsPerUnit` 12.5 (dot .16 u, period .4 u). Intensity is the music director's
`Intensity` (0 calm .. 1 drop); scripted non-musical patterns use `NonMusicIntensity`.

Test: `BossTelegraphTest` (in `AllTests`). Previews: `BossTelegraphPreview.Run` (old vs dotted overlay), `BossAttackPreview` (`BOSSATK_INTENSITY=0|1` pins calm / drop);
captures in `docs/boss-telegraph-captures/`.
