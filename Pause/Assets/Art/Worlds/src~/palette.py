"""Every colour the world backgrounds use, in one place.

Follows docs/art-style.md (80s TV-anime cels, Akira palette). The core
names and hex values mirror docs/art-samples/src/akira.py; the per-world
sky ramps and structure tones are the guide's table in section 1.3.

Backdrop rules this file is tuned for (section 4, enforced by
WorldBackdropTest):
  * backdrop forms stay at HSV value <= 35% and saturation <= 60%; only
    point lights (windows, lamps, dashes, sparks) go brighter
  * no RED (the player owns it) and no MAGENTA (enemy lights only)
  * Ember stays at or below ~30% value; its lava is sodium, never red
  * background ink is BG_INK, thinner than sprite ink

A restyle is: edit this file, run ./render.sh in each world's src~ folder.
"""

# ---- core palette (mirrors docs/art-samples/src/akira.py) ----------------
NIGHT_0 = "#070A16"
NIGHT_1 = "#0E1424"
INDIGO_0 = "#1A1F45"
INDIGO_1 = "#2A2E6B"
DUSK = "#3A2A5C"
SODIUM = "#F2862B"
AMBER = "#FFB43C"
SODIUM_SH = "#A9481A"
TEAL = "#1FB5B9"
CYAN = "#6EF2EE"
TEAL_SH = "#0F5E6A"
BONE = "#F4EAD4"
INK = "#140C14"

# ---- background ink (guide section 2.4: background structures) -----------
BG_INK = "#0A0C1C"
INK_W = 2.4            # outline width at 512-px tile scale (~1.9 u on a 128-u canvas)
GRAIN = 0.05           # backdrop grain opacity (<= 6%)

SPACE = dict(
    sky=[NIGHT_0, NIGHT_1, INDIGO_0, "#2A1E48", NIGHT_0],
    lane=NIGHT_1,
    vignette=NIGHT_0,
    nebula=[INDIGO_0, "#2A1E48", "#14304A", DUSK],
    stars=[BONE, CYAN, AMBER],
    # Planets are drawn in neutral greys and tinted per spawn
    # (BackdropDirectors.SpaceDirector.PlanetTints), landing near INDIGO_1.
    planet_base="#6a6a6a", planet_band="#545454", planet_shadow="#2a2a2e", planet_hi="#a8a8a8",
    planet_spot="#7c7c7c", planet_rim=CYAN,
    ring="#5e5e5e", ring_shadow="#3a3a3e", ring_hi="#9a9a9a",
    station_hull=INDIGO_0, station_shadow="#11142c", station_hi="#2c3366",
    station_window=SODIUM, station_light=CYAN, station_beacon=AMBER,
    comet_core=BONE, comet_trail=CYAN,
    galaxy_core=BONE, galaxy_arm=DUSK,
)

FROST = dict(
    sky=["#04080F", "#0A1A2A", "#123248", "#0A1A2A", "#04080F"],
    lane="#0A1A2A",
    vignette="#04080F",
    sky_glow=["#0F3040", "#12283E"],
    stars=[BONE, CYAN],
    tower="#0F2134", tower_dark="#0a1828", tower_kick="#1f4a66", window=AMBER,
    far=dict(lit="#11273a", dark="#0b1a2a", cap="#1d4058", cap_dark="#163248"),
    near=dict(lit="#16324A", dark="#0F2134", cap="#2a5672", cap_dark="#1e4058"),
    big=dict(lit="#16324A", dark="#0F2134", cap="#33627e", cap_dark="#23485f"),
    rim="#9FE8F0",
    snowfield="#0c1c2c", snowfield_hi="#12283a",
    ice="#0b2236", ice_shadow="#08192a", ice_plate="#10304a",
    water_dash=CYAN,
    pine="#060e18",
    aurora_core=CYAN, aurora_body=TEAL, aurora_fringe=DUSK,
    geyser="#9FE8F0", geyser_shadow=TEAL_SH,
    # altitude: aerial haze colour, clouds between the ship and the ground
    air="#1d3a52", cloud="#3e5c74", cloud_shadow="#24384c", band="#2a4a62",
    ice_hi="#4f7f9c", lake="#0c3a4a",
)

VERDANT = dict(
    sky=["#05100E", "#0B1F1C", "#143430", "#0B1F1C", "#05100E"],
    lane="#0B1F1C",
    vignette="#05100E",
    canopy=["#0c221e", "#0f2824", "#11302a"], canopy_hi="#1E4A3C",
    blossom=[SODIUM, TEAL],
    mist="#143430",
    far=dict(lit="#173a30", dark="#0f2824", cap="#1E4A3C", cap_dark="#173a30"),
    near=dict(lit="#1E4A3C", dark="#12302a", cap="#2C5A40", cap_dark="#214a36"),
    rim="#7FAF6A",
    bank="#10241e", water="#0a1c22", water_shadow="#07141a", water_dash=TEAL,
    pad="#1E4A3C", stone="#1a2a2a",
    tree="#173a30", tree_dark="#0a1a16", tree_hi="#2C5A40",
    cliff="#1a2e2a", cliff_dark="#0f1e1c", fall="#0e4a54", fall_hi=CYAN, fall_shadow="#0a343c",
    stone_lit="#1E4A3C", stone_dark="#10261f", glyph_a=TEAL, glyph_b=SODIUM,
    obelisk="#1a2e2e", obelisk_dark="#0e1c1c", obelisk_light=SODIUM,
    air="#18322c", cloud="#3a564c", cloud_shadow="#22362f", band="#2a4840",
    plateau="#1E4A3C", plateau_dark="#143428", rock="#24302e", rock_dark="#141c1c", rock_hi="#3a4a46",
)

EMBER = dict(
    sky=["#120608", "#24090E", "#3E1016", "#24090E", "#120608"],
    lane="#24090E",
    vignette="#120608",
    smoke=["#2a1016", "#1e0c14", "#2e1418"],
    crack=SODIUM_SH, crack_hot=SODIUM,
    ash=["#3a2a30", "#2a2028"], spark=SODIUM,
    far=dict(lit="#2A1416", dark="#1a0c0e", cap="#3a1a1c", cap_dark="#2a1214"),
    near=dict(lit="#341a1a", dark="#1e0e10", cap="#4a1e1c", cap_dark="#341416"),
    cone=dict(lit="#3a1c1c", dark="#200e10", cap="#5A1A1A", cap_dark="#3a1214"),
    rim=SODIUM_SH,
    bank="#160a0c", lava="#4a1a0c", lava_shadow="#341208", lava_glow="#4e1c0a",
    crust="#1a0c0e", seam=SODIUM_SH, lava_dash=AMBER,
    rock="#140a0c", rock_hi="#2e1a1c",
    eruption_hot=SODIUM, eruption_hi=AMBER, eruption_core=BONE,
    smoke_cel=DUSK, smoke_cel_dark=INDIGO_0,
    air="#3e1418", cloud="#3a2a40", cloud_shadow="#22182a", band="#40161c",
    cone_lit="#4a2a2a", cone_dark="#1e1016", cone_hi="#6a3a34", apron="#2a161a",
)
