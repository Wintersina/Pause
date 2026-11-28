"""Per-elite hand spec for the Paragon-standard elite redraw (build.py).

All coordinates are in the APPROVED drawing's own 128 px space AFTER the vertical flip (the PixelLab drawings face down toward the
player; every elite strip, def and the game draw the elite nose UP: noseDeg 90, engines at the bottom), x right, y down.

  engines   [(x, half width)]  one per engine bell; the mouth is found under it (the drawn flame's root, else the lowest hull pixel)
  flames    True: the drawing has flames drawn under those bells; they are cut off (the strips draw their own, animated)
  muzzles   {def muzzle name: (x, y)}  where that muzzle's shot leaves the new drawing (same names / order as the def)
  cut       optional [(x0, y0, x1, y1)] boxes (exclusive x1/y1) cut off the drawing (stray drips / spark lines under the hull)
  root      optional drawing row where every drawn flame starts (when the flame detector cannot tell flame from hull)
  edge      optional multiplier on the old frame-0 edge (cellWorldSize divided by it: same world size)
  nozzles   optional indices into engines: the def's nozzles (the rest are drawn flames only, e.g. engines high on the hull)
  L         optional flame length (engine_fx units, ~2 px each)
  glow      optional (hue lo, hue hi, s min, v min) for the lights mask when the drawing's glow is not in the world cue band
"""

SPEC = {
    # ---- Desert ----------------------------------------------------------------------------------------------------------
    "desert_elite_caravan_hauler": dict(engines=[(47, 3), (64, 4), (81, 3)], flames=False,
                                        muzzles={"port": (22, 52), "star": (106, 52), "canister": (64, 74)}),
    "desert_elite_dune_stalker": dict(engines=[(44, 4), (85, 4)], flames=True,
                                      muzzles={"hook": (64, 20)}),
    "desert_elite_glass_cannon": dict(engines=[(44, 4), (84, 4)], flames=False,
                                      muzzles={"cannon": (64, 6)}),
    "desert_elite_lens_matriarch": dict(engines=[(44, 6), (82, 6)], flames=True,
                                        muzzles={"fan": (64, 6), "portrod": (30, 30), "starrod": (98, 30)}),
    "desert_elite_mirror_bastion": dict(engines=[(47, 8), (81, 8)], flames=False,
                                        muzzles={"core": (64, 10), "portcap": (40, 12), "starcap": (88, 12)}),
    "desert_elite_scorpion_walker": dict(engines=[(48, 5), (79, 5)], flames=True, muzzles={"claw": (64, 10)}),
    # ---- Ember -----------------------------------------------------------------------------------------------------------
    "ember_elite_ash_wraith": dict(engines=[(44, 5), (85, 5)], flames=True, root=105, cut=[(75, 105, 76, 128)], muzzles={"beak": (64, 30)}),
    "ember_elite_brass_vulture": dict(engines=[(44, 7), (84, 7)], flames=False, muzzles={"claw": (64, 26)}),
    "ember_elite_cauterizer": dict(engines=[(42, 8), (86, 8)], flames=False, muzzles={"cannon": (64, 22)}),
    "ember_elite_coalrunner": dict(edge=1.16, L=30, nozzles=[1], engines=[(26, 4), (64, 4), (102, 4)], flames=False,
                                   muzzles={"left": (14, 34), "right": (114, 34)}),
    "ember_elite_kilnback": dict(L=33, nozzles=[1, 2], engines=[(20, 3), (38, 6), (91, 6), (108, 3)], flames=True,
                                 muzzles={"l": (40, 70), "r": (88, 70)}),
    "ember_elite_sunstoke": dict(engines=[(44, 5), (84, 5)], flames=True, muzzles={"lance": (64, 6)}),
    # ---- Frost -----------------------------------------------------------------------------------------------------------
    "frost_elite_cryo_siren": dict(engines=[(61, 2), (67, 2)], flames=False, muzzles={"outlet": (64, 60)}),
    "frost_elite_floe_harrower": dict(engines=[(58, 3), (70, 3)], flames=False,
                                      muzzles={"chuteL": (52, 100), "chuteR": (76, 100), "keel": (64, 110)}),
    "frost_elite_glacier_tender": dict(engines=[(46, 6), (82, 6)], flames=False,
                                       muzzles={"podA": (50, 96), "podB": (78, 96), "podC": (46, 112), "podD": (82, 112)}),
    "frost_elite_rimebreaker": dict(L=22, engines=[(44, 7), (84, 7)], flames=False, muzzles={"prow": (64, 6)}),
    "frost_elite_whiteout_sentinel": dict(engines=[(48, 5), (82, 5)], flames=True, root=111, cut=[(36, 111, 39, 128)],
                                          muzzles={"eye": (64, 72), "sideL": (20, 92), "sideR": (108, 92)}),
    # ---- Space -----------------------------------------------------------------------------------------------------------
    "space_elite_eventide_bastion": dict(engines=[(42, 4), (86, 4)], flames=False,
                                         muzzles={"lpod": (20, 40), "rpod": (108, 40)}),
    "space_elite_orbit_reaver": dict(engines=[(34, 2), (46, 5), (82, 5), (94, 2)], flames=True,
                                     muzzles={"lclaw": (40, 8), "rclaw": (88, 8)}),
    "space_elite_rift_lancer": dict(engines=[(43, 7), (83, 7)], flames=True, muzzles={"prong": (64, 36)}),
    "space_elite_singularity_hauler": dict(engines=[(46, 5), (64, 5), (82, 5)], flames=False,
                                           muzzles={"lclaw": (38, 22), "rclaw": (90, 22)}),
    # ---- Storm -----------------------------------------------------------------------------------------------------------
    "storm_elite_anvil_cloud": dict(engines=[(48, 5), (80, 5)], flames=False, muzzles={"cannon": (64, 20)}),
    "storm_elite_skyhook_harrier": dict(engines=[(54, 4), (74, 4)], flames=False, muzzles={"hook": (64, 22)}),
    "storm_elite_rod_dancer": dict(engines=[(48, 4), (62, 5), (80, 4)], flames=False,
                                   muzzles={"fan": (64, 8), "portrod": (53, 16), "starrod": (75, 16)}),
    "storm_elite_refinery_tender": dict(L=27, engines=[(53, 4), (74, 4)], flames=True,
                                        muzzles={"port": (14, 40), "star": (114, 40), "canister": (64, 70)}),
    "storm_elite_static_bastion": dict(engines=[(52, 4), (64, 4), (75, 4)], flames=False,
                                       muzzles={"core": (64, 26), "portcap": (40, 6), "starcap": (88, 6)}),
    # ---- Tide ------------------------------------------------------------------------------------------------------------
    "tide_elite_bathyscaphe": dict(engines=[(36, 5), (92, 5)], flames=False,
                                   muzzles={"portclaw": (14, 44), "starclaw": (114, 44), "rack": (64, 100)}),
    "tide_elite_pearl_warden": dict(engines=[(40, 6), (86, 6)], flames=False,
                                    muzzles={"shellp": (42, 62), "shells": (86, 62)}),
    "tide_elite_thunderfin": dict(engines=[(48, 5), (76, 5)], flames=True,
                                  muzzles={"rod": (64, 6), "coilp": (46, 62), "coils": (78, 62)}),
    "tide_elite_trawler": dict(engines=[(34, 5), (92, 5)], flames=True,
                               muzzles={"port": (14, 60), "star": (114, 60), "winch": (64, 84)}),
    "tide_elite_riptide": dict(engines=[(49, 4), (77, 4)], flames=True, muzzles={"lance": (64, 10)}),
    # ---- Verdant ---------------------------------------------------------------------------------------------------------
    "verdant_elite_leafblade": dict(engines=[(47, 4), (78, 4)], flames=True, root=107,
                                    muzzles={"nose": (64, 4), "lblade": (24, 70), "rblade": (104, 70)}),
    "verdant_elite_sporebloom": dict(engines=[(51, 5), (75, 5)], flames=False,
                                     muzzles={"lvent": (26, 36), "rvent": (102, 36), "pod": (64, 32)}),
    "verdant_elite_thornlash": dict(engines=[(48, 5), (76, 5)], flames=True,
                                    muzzles={"lroot": (38, 40), "rroot": (90, 40)}),
    "verdant_elite_timber_hauler": dict(engines=[(39, 4), (50, 4), (76, 4), (86, 4)], flames=True,
                                        muzzles={"lgrab": (40, 12), "rgrab": (88, 12)}),
    "verdant_elite_resin_warden": dict(engines=[(45, 5), (83, 5)], flames=True, muzzles={"lpod": (28, 68), "rpod": (100, 68)}),
}
