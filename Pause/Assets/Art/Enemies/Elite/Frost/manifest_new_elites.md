# Frost World 2 — four new elite ship concepts

These are art-only flight strips for Frost's atmosphere-level run over a frozen ocean and industrial coast. All craft are viewed directly from above, point up, and use transparent 192 × 192 cells. The painted concept masters and the deterministic nearest-neighbour pixel conversion live in `src~/`; each `_concept.png` is the full-resolution generated hero painting.

## Shared cell contract

Each `frost_elite_*.png` is **1344 × 192 RGBA**, seven cells in this exact order:

| Cell | State | Art change |
| --- | --- | --- |
| 0 | Landed, engines off | Dark nozzles and unpowered coolant. |
| 1 | Grounded idle | Restrained cyan coolant and nozzle light. |
| 2 | Lift-off ignition | White-hot nozzle centers and long hard-edged cyan jets. |
| 3 | Straight hover | Same hull and anchor, short steady jets. |
| 4 | Bank left | Hover hull rolled 8° left with nearest-neighbour sampling. |
| 5 | Bank right | Hover hull rolled 8° right with nearest-neighbour sampling. |
| 6 | Second-heart damage | Complete flight-capable hull, cracked ice, sparking right thruster. |

All locations below are approximate **cell-local pixels in cell 3**, x from left, y from top. They are guides for the later wiring pass, which should re-measure the final images. The rear engine coordinates are nozzle centers; projectile origins are suggested muzzle or payload release points.

## Floe Harrower — `frost_elite_floe_harrower`

- **Launch site:** ice-shelf barge hangar.
- **Silhouette:** broad open barge, two splayed ski-runner pontoons, central ice-cutter fan, row of four rear ice-slab drop chutes. Patient, deliberate herder.
- **Palette:** weathered blue-grey rime steel, blue-white chipped pontoon armor, cyan cutter and coolant, small brass clamps. Ink outline and narrow cyan rim.
- **Engines:** paired rear nozzles at **(83,143)** and **(109,143)**.
- **Weapon / payload origins:** ice-slab chutes around **(78,117), (90,117), (102,117), (114,117)**; cutter/fan center around **(96,72)**. Suggested attack is drifting slabs that funnel the pilot.
- **Hover opaque bbox:** **118 × 110** px. The open pontoon gaps distinguish it from the other three ships.

## Cryo Siren — `frost_elite_cryo_siren`

- **Launch site:** coastal relay pad ring.
- **Silhouette:** thin spine and long pointed nose, large circular coolant-dish crown, visible trailing hoses. Fast, fragile kiter.
- **Palette:** deep rime steel and chipped ice, luminous cyan segmented dish, thin brass dish bearings.
- **Engines:** two small rear vent/nozzles at **(90,155)** and **(102,155)**.
- **Weapon origins:** dish center around **(96,79)**, forward cryo orb outlet around **(96,62)**. Suggested attack is a cryo orb that bursts into a shard ring.
- **Hover opaque bbox:** **62 × 146** px. The dish remains the bright focal point.

## Glacier Tender — `frost_elite_glacier_tender`

- **Launch site:** offshore rig crawler bay.
- **Silhouette:** fat, squared tug with large side cable winches, a forward beacon mast and **four individually visible clamped drone pods** on the rear deck. Slow support craft.
- **Palette:** cold blue-grey steel, chipped ice side plating, cyan mast/pod lights and conduits, sparse brass winch fittings.
- **Engines:** two heavy rear nozzles at **(77,158)** and **(115,158)**.
- **Weapon / drone origins:** four pod centers around **(84,108), (108,108), (84,128), (108,128)**; beacon at **(96,29)**; side tether winches around **(56,78)** and **(136,78)**. Suggested attack launches ice drones, with a shield while any drone lives.
- **Hover opaque bbox:** **98 × 142** px.

## Whiteout Sentinel — `frost_elite_whiteout_sentinel`

- **Launch site:** armored coastal silo hatch.
- **Silhouette:** broad solid shield wedge with **three overlapping front ice plates** and one narrow cyan eye slit. Stubborn tank rather than a winged interceptor.
- **Palette:** dark rime-steel rear chassis, pale fractured glacier armor, cyan eye and coolant seams, tiny brass retainers.
- **Engines:** paired rear nozzles at **(84,151)** and **(108,151)**.
- **Weapon origins:** eye slit around **(96,72)**; side emitters around **(53,119)** and **(139,119)**. Suggested attack strips layered ice plates into shard volleys.
- **Hover opaque bbox:** **120 × 128** px. In cell 6 the left-front armor section is torn off to show darker steel understructure; the other plates are cracked. Cells 7 and 8 were omitted because the requested flight strip has exactly seven cells.

## Preview and production notes

- `contact_sheet.png` stacks Rimebreaker then these four strips at 2× nearest-neighbour scale for direct comparison.
- `preview.gif` shows the four new ships together, cycling **3 → 4 → 3 → 5**.
- Two generated candidate paintings per ship are saved in `src~/`. The selected Floe Harrower and Cryo Siren are the first candidates; the selected Glacier Tender and Whiteout Sentinel are the darker alternate candidates. `src~/build_new_elites.py` derives the final cells from each selected painted hull, maps materials into short Frost color ramps, adds a one-pixel dark outline and stepped cyan glow, then draws pose-specific nozzles and damage. No blur is used.
- The strips are review art and are not yet wired into enemy behavior or Unity metadata.

## Programmatic strip audit

All four strips are RGBA and exactly 1344 × 192. Minimum clear margin across all cells is **31 px** (Floe Harrower), **19 px** (Cryo Siren), **16 px** (Glacier Tender), and **23 px** (Whiteout Sentinel). Cell 3 to bank-cell opaque centroid displacement is at most **0.7 px**, **0.2 px**, **1.0 px**, and **1.6 px** respectively. Bank opaque bbox dimensions remain close to the straight hover: Floe **120 × 113**, Siren **63 × 146**, Tender **102 × 143–144**, Sentinel **121 × 129**. The four hover bbox sizes above show the distinct wide/open, narrow/tall, boxy/tall and broad/wedge proportions.
