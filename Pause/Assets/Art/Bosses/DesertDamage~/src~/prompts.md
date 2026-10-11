# Desert boss damage source paintings

The built-in image generation tool edited `pristine_cell.png` for three body
candidates. The selected paintings are `stage2_candidate.png`,
`stage3_candidate.png`, and `stage4_candidate.png`. Stage 1 uses localized
painted areas from stage 2 over the approved pristine body; later stages use
their generated mechanical paintings. `build_damage.py` fits each source to the
approved alpha bounds with nearest-neighbour resampling and cleans palette
fringes. The generated sources remain here for review.

## Body prompts

- Stage 2: “Edit the provided transparent 384x384 pixel-art game boss sprite
  into a moderately battle-damaged version of EXACTLY the same Harvester
  bucket-wheel excavator, same top-down view, placement, scale, and silhouette.
  Crack and remove several left shoulder armor plates to reveal densely painted
  pipes and dark skeleton; shatter upper parabolic lens dish with 2-3 missing
  petals, cut one third of bone-colored bucket teeth off lower wheel, snap the
  tip of the right drill vent stack. Place SMALL white-hot #FFF1C8 machinery
  light only in exposed breaches. Preserve the original copper, bone, dark
  indigo palette, fine gritty pixel detail and transparent background. Create
  real holes and jagged torn plate edges, no decal overlays, no smoke, no
  lightning, no text. Absolutely no red, magenta, orange or amber glow, no
  violet, cyan, green.”
- Stage 3: “Edit the provided transparent 384x384 pixel-art game boss sprite
  into a DRAMATICALLY wrecked stage 3 version of EXACTLY the same Harvester
  bucket-wheel excavator. Preserve exact top-down view, size, original footprint
  and sprite registration. The silhouette remains recognizable: wheel at bottom,
  shattered lens frame on top, tracked flanks, right drill boom. Tear a huge
  jagged breach into the central hull showing dark structural ribs, exposed
  pipes and a white-hot #FFF1C8 core; upper dish petals broken away leaving
  skeletal radial frame; right drill boom bent and dangling on cables; lower
  maw wheel half stripped with missing bucket teeth and twisted remaining
  teeth; missing armor plates exposing the skeleton. Fine intricate rugged pixel
  art with 4-6 tonal steps on metal, matte rust copper, bone ivory, indigo
  shadow; transparent background. No smoke or electricity. No red, orange glow,
  amber glow, magenta, cyan, green, violet, text.”
- Stage 4: “Edit the provided transparent 384x384 pixel-art game boss sprite
  into a near-destroyed stage 4 version of EXACTLY the same Harvester
  bucket-wheel excavator. SAME registered footprint, top down camera, size and
  anchor, transparent. It must remain recognizable as the original excavator
  while more devastated than a typical last-heart frame: upper lens is broken
  into a few skeletal petals and radial struts; both shoulders have deep gaping
  jagged armor losses, exposed dense pipes and dark rib structure; the central
  white-hot #FFF1C8 core gutters through breaches; the drill boom is mangled
  and hangs by cables within its original area; the lower bucket-wheel is
  twisted and almost half stripped. Make torn plates, actual holes, broken
  mechanical silhouettes, rough chipped edges, rich pixel-art material tones
  and tiny pale rim lights. Preserve matte copper, desaturated bone, dusty
  indigo. No smoke, no electrical arcs, no text. No red, orange/amber glow,
  magenta, cyan, green, violet.”

## Effect and death prompts

- `smoke_candidate.png`: transparent overlay of heavy opaque charcoal-indigo
  dust from both shoulders and broken drill, pale bone-grey steam and sun-white
  underlight, keeping the central machinery clear.
- `arcs_candidate.png`: transparent overlay of large irregular sun-white arcs
  and endpoint bursts across the same boss. Its bright endpoint painting was
  used at small scale; the routed arc paths are finished in `build_damage.py`.
- `death_split_candidate.png`: the same boss detonating, with a pale sun-white
  core explosion, jagged matte copper plates, bucket teeth, dark ribs, and bone
  shards in a dusty top-down burst.
- `death_fireball_candidate.png`: an enormous nearly circular white-hot pale
  bone-gold core fireball with silhouetted copper and ivory industrial debris
  and charcoal-indigo outer smoke, no intact machine.
- `death_ring_candidate.png`: an expanding pale bone-grey dust and charcoal
  smoke shockwave ring, sparse hot fragments and matte industrial wreckage,
  transparent central hole.

All prompts prohibited red, orange or amber emission, magenta, violet, cyan,
green, UI, text, and backgrounds. The source outputs had occasional chromatic
fringes; `clean_palette` in the build script removes them from the game sheets.
