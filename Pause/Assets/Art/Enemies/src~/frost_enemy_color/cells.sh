#!/bin/bash
cd "$(dirname "$0")"
export EXTRA="Keep the SAME solid painted materials and colours as image 2 exactly (no frosting over, no recolour drift). Copy image 1's pose, charge-up / attack burst / hit-damage differences and per-frame small motion exactly, in image 2's palette."
./gen_cells.sh frost_fighter_3 "ivory-lavender frost sail, graphite-black frame, oxidised copper trim, aurora-green core and vent flame" 4 5 6
./gen_cells.sh frost_fighter_4 "storm-slate hull, indigo-violet plates, nickel trim, electric violet-white cannon core and exhaust" 4 5 6
./gen_cells.sh frost_rock_chunk "glacier blue-white crust, basalt blue-black plates, verdigris green-bronze straps, cyan-white crystal core" 4 5 6
