# Image-generation source prompts

Built-in image generation created two transparent candidates for each ship. All candidate paintings and the five selected full-resolution masters are saved in this directory; full-resolution hue-cleaned versions are in the output folder as `_concept.png` hero images. The selected candidates were Skyhook Harrier B, Refinery Tender A, Rod Dancer B, Anvil Cloud A, and Static Bastion B.

Shared source prompt: one isolated top-down elite ship, nose upward and rear thrusters downward, transparent canvas, centered. Dense painterly pixel art with crisp blocky pixels, rich tonal variation, hard near-black one-pixel outline, short metal ramps, rugged industrial weathering. Bruised slate, graphite gunmetal, pale hail-grey pitting, muted teal-grey. Cold upper-left rim, tiny storm-white `#F2F6FF` core with subtle pale-yellow `#FFF3A0` edge and sparse cold white-blue arcs. No background, cloud, platform, UI, text, or other ships. No red, pink, violet, green, lime, orange, amber, brown-brass, or saturated cyan fields.

| Ship | Candidate A subject | Candidate B subject |
| --- | --- | --- |
| Skyhook Harrier | Sleek interceptor, long hooked grappling-lance boom, swept slate wings, exposed cable winch, two rear thrusters. | Swept-wing interceptor, barbed hook at the upper nose, exposed winch cable, needle cockpit, compact aft engines. |
| Refinery Tender | Squat wide tank-barrel hull, central canister cradle, external pipe racks, downward drop hatch, twin hauler engines. | Bulky rectangular tanker with rounded nose-to-tail pressure barrel, outer racks, cradled canisters, blunt front. |
| Rod Dancer | Very slim skirmisher, two long separated forward lightning rods, central cockpit, tiny delta stabilizers, split thrusters. | Needle skirmisher, twin shoulder rods like a tuning fork, narrow hull and aft fins, pinprick arc source. |
| Anvil Cloud | Flat-topped anvil front, massive central upward shell cannon, deep flared stern, layered plate armor, large twin vents. | Squat anvil artillery barge, flat crossbeam and rear stem, forward bore, riveted plating. |
| Static Bastion | Wide shield tank, four shoulder capacitor spires, wraparound segmented armor, small storm-white core, four thrusters. | Broad fortress hull, four exposed side spires, overlapping shield plates, central eye, heavy engine bank. |

The selected paintings are scaled only with nearest-neighbour sampling. `build_storm_elites.py` makes localized hue corrections for prohibited color families while preserving thousands of source tones per cell, applies a sparse cold rim, and composes each registered animation state.
