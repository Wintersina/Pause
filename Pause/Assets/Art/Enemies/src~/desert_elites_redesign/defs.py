#!/usr/bin/env python3
"""Update the six Desert elite defs for the redesigned art: text (lore / role / name), engine colour (= the flames drawn into the strip),
and for the Scorpion the new nose-up drawing (noseDeg 90, no procedural exhaust: the flames are in the strip) and cell size 384.
Hardpoints come from points.py. Keys, brains, attacks, numbers, cells map are untouched."""
import json
from pathlib import Path
DEF = Path(__file__).resolve().parents[3] / "Resources/Elites/Defs"
UPD = {
    "dune_stalker": dict(engineColor="#BFE3FF", role="burrowing interceptor",
        lore="A cobalt lance-fighter that hunts out of a worm-burrow hole at the edge of the dunes. It sights you, rears, and drives its brass drill-lance down the lane in one straight dash, grit needles spitting from its flanks. Step out of the sand wake, not along it."),
    "caravan_hauler": dict(engineColor="#FFD060", role="quicksand canister hauler",
        lore="A heavy-lift freighter that still hauls its last caravan of sand-pit canisters, now on lift fans instead of tracks. It rises out of its bay and lets the canisters drop one at a time; each lands as a pool of quicksand that swallows shots. Watch the pool drawn on the ground before it lands."),
    "glass_cannon": dict(engineColor="#FFE9A0", role="sun-lens siege cannon",
        lore="A siege gun on a flying deck, its fused-glass sun lens focusing a lane of light before the shell leaves the barrel. A ringed lane preview shows where it will land. Leave the lane, it does not follow."),
    "lens_matriarch": dict(engineColor="#9FF5DA", role="mirage blink skirmisher",
        lore="A slim heliograph of six mirror panels that signals across the dunes. It blinks beside you and fans out real glass shards among hollow decoy glints. The decoys are empty: watch which ones are solid."),
    "mirror_bastion": dict(engineColor="#9AD8FF", role="glass ward bastion",
        lore="A shielded warship ringed in overlapping glass-mirror plates. It rises out of a ruin dock and hangs a curtain of slow glass shards in front of itself with one gap in it. Find the gap and wait."),
    "scorpion_walker": dict(engineColor="#FFC060", role="venom claw striker", displayName="Scorpion Skiff", noseDeg=90, exhaustScale=0,
        lore="A black-armoured gunship that lifts off a ruin dock with its stinger tail raised over its back. It dives claws first, then lashes the sting across the lane: the glowing white-gold tip is the part that hurts. Slip behind the lash."),
}
for k, u in UPD.items():
    p = DEF / f"desert_elite_{k}.json"
    d = json.load(open(p))
    d.update(u)
    d["cellPixels"] = 384
    d["exhaustShip"] = d.get("exhaustShip", 3)
    d["exhaustScale"] = 0
    json.dump(d, open(p, "w"), indent=2)
    open(p, "a").write("\n")
    print(k, d["displayName"], d["noseDeg"], d["cellWorldSize"], d["hullRadius"])
