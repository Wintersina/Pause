"""Pose brief for each Tide / Storm regular ship (rocks and mines are out of scope).

tellA / tellB / hit are plain-language pose changes handed to Codex together with the ship's
current idle cell. Order of priority = most-seen ships first.
"""
SHIPS = [
    # name, cell, tellA, tellB, hit
    ("tide_fighter_1", 192,
     "ANTICIPATION: the paired side fins are cocked sharply forward and tucked in, the oval back disc is dilating open with a bright mint glow inside, the twin engine jets flare longer.",
     "CHARGED: the side fins snap swept far back and out into wide blades, the back disc is fully open and blazing white-mint, the jets flare long and bright.",
     "RECOIL: the whole body is knocked back and yawed about 12 degrees, one fin crumpled and bent, plates jolted apart, jets sputtering short."),
    ("tide_fighter_2", 192,
     "ANTICIPATION: the paired fins fold flat against the hull and the dorsal blade lowers, the needle beak cannon slides forward with a mint glow building at its tip.",
     "CHARGED: the fins flare wide open and back, the dorsal blade rises, the beak cannon is fully extended with a bright white-mint charge at the tip.",
     "RECOIL: the narrow hull is knocked back and slewed about 12 degrees, one fin bent, the beak cannon jolted sideways."),
    ("tide_fighter_3", 192,
     "ANTICIPATION: the hinged lower jaw swings open halfway showing the teeth, the curved lure stalk is cocked back and bent, the lure bulb dimmed and gathering light.",
     "CHARGED: the jaw gapes wide open, the lure stalk snaps forward and straight, the lure bulb flares very bright white-mint, pectoral fins flared.",
     "RECOIL: the head is knocked back and tilted about 12 degrees, the jaw snapped crooked half shut, the lure stalk whipped sideways and bent."),
    ("tide_fighter_4", 192,
     "ANTICIPATION: the broad hammer head swivels so its two tip cannons angle inward and glow, the gill plates part, the tail fin curls under.",
     "CHARGED: the hammer head is swung square and wide, both tip cannons flare with bright white-mint muzzle glow, gill plates fully open, tail fin fanned.",
     "RECOIL: the whole hammerhead is knocked back and rotated about 12 degrees, one tip cannon bent, plates jolted."),
    ("tide_chaser", 192,
     "ANTICIPATION: the segmented eel body coils into a tight deep S curve, head reared back and up, side thruster fins tucked in, mint eye glowing.",
     "CHARGED: the body whips nearly straight and stretched, side thruster fins flared wide, head thrust forward with a bright white-mint eye and thruster glow.",
     "RECOIL: the body is kinked into a sharp reverse bend as if struck, head jerked sideways, segments jolted apart, fins crooked."),
    ("tide_alien", 192,
     "ANTICIPATION: the bell contracts, narrower and taller, the tentacles draw up and curl in tight, the pods along them begin to glow mint.",
     "CHARGED: the bell flares wide and flat, the tentacles splay out in a wide fan, every pod blazing bright white-mint.",
     "RECOIL: the bell is dented and tilted about 15 degrees, tentacles whipped to one side and tangled, pods dark and jolted."),
    ("tide_big", 256,
     "ANTICIPATION: the spiral shell plates lift and part slightly, the lower tentacle pipes retract inward, the maw opens a little with mint light behind it.",
     "CHARGED: the shell plates flare open, the lower tentacle pipes thrust out and downward and glow, the maw gapes wide with bright white-mint light.",
     "RECOIL: the whole hull is knocked back and tilted about 10 degrees, shell plates jolted loose and askew, pipes crooked."),
    ("storm_fighter_1", 192,
     "ANTICIPATION: the manta wings sweep up and back in a cocked V, the central spear lance retracts and glows at its tip, the cyan seams brighten.",
     "CHARGED: the wings flare wide and forward, the spear lance extended long with a bright white-blue charge, all cyan seams blazing.",
     "RECOIL: the hull is knocked back and slewed about 12 degrees, one wing crumpled, the lance bent, seams flickering."),
    ("storm_fighter_2", 192,
     "ANTICIPATION: the sweeping wings are raised and cocked far up and back, the three jets flare long, the lower pods glow orange.",
     "CHARGED: the wings snap down and spread wide, the jets burn at full length, the central core blazes.",
     "RECOIL: the hull is knocked back and slewed about 12 degrees, one wing folded, jets sputtering, plates jolted."),
    ("storm_fighter_3", 192,
     "ANTICIPATION: the hazard-striped side panels hinge outward, the yellow gun cluster in the middle extends and glows, the two top coil pods begin to arc.",
     "CHARGED: the side panels swing fully open, the gun cluster protrudes blazing, both top coil pods spit bright blue arcs, engines flaring.",
     "RECOIL: the gunship is knocked back and tilted about 10 degrees, one panel bent open, pods askew."),
    ("storm_fighter_4", 192,
     "ANTICIPATION: the two crowning tesla coils tilt inward and blaze brighter, blue arcs begin to crawl between them, the wing plates pull in.",
     "CHARGED: the coils flare outward and are wreathed in long bright blue lightning, the wing plates flared wide, the core blazing.",
     "RECOIL: the whole ship is knocked back and tilted about 10 degrees, one coil knocked crooked, plates jolted, arcs dying."),
    ("storm_chaser", 192,
     "ANTICIPATION: the four curved blades fold in and retract toward the hub, the hub core glows brighter, the arms coil tight.",
     "CHARGED: the four blades extend and swing wide to full reach, edges glowing blue-orange hot, the hub core blazing white-blue.",
     "RECOIL: the hub is knocked and rotated about 20 degrees off axis, one blade bent, one arm jolted."),
    ("storm_alien", 192,
     "ANTICIPATION: the lattice cage plates draw tight, the violet core brightens and swells, the bottom spike lengthens.",
     "CHARGED: the cage plates spread open, the core blazing white-violet with short arcs jumping across, all spikes extended.",
     "RECOIL: the cage is dented and tilted about 15 degrees, one plate knocked loose, the core flickering dim."),
    ("storm_big", 256,
     "ANTICIPATION: the tail flukes and side fins tilt up and cock back, the belly vents begin to glow, the pale plates part slightly.",
     "CHARGED: the tail flukes and fins flare out wide, the belly vents blaze bright white-blue, plates opened and glowing.",
     "RECOIL: the whale hull is knocked back and tilted about 10 degrees, one fin bent, plates jolted loose."),
]

STYLE = ("Keep EXACTLY the same ship design, same hull shapes, same palette and the same hard-edged pixel-art rendering "
         "(crisp pixels, dense tone ramps, rivets, no blur, no outline, no pale rim, no glow halo around the sprite). "
         "The body centre stays at the exact same position and size in every pose; only the named moving parts change. "
         "No pink, magenta or red. No water, no background, no shadow, no text.")


def prompt(name, cell, a, b, h, in_path, out_path):
    return (
        f"The attached image {in_path.name} is ONE game enemy sprite (top-down, facing down, transparent background). "
        "Use your image generation tool to paint a new image: a horizontal sheet of THREE poses of this SAME ship, "
        "side by side, three equal square cells (sheet is 3x wide, 1x high), the ship centred in each cell at the same "
        "scale as the attached image with generous empty margin, genuinely transparent background. "
        f"Pose 1 (left) {a} Pose 2 (middle) {b} Pose 3 (right) {h} "
        "Each pose must be a clearly different silhouette from the attached idle. "
        + STYLE +
        f" Save the final PNG (RGBA, transparent) to {out_path} (copy it from the generated images folder if needed) "
        "and reply with just the path.")
