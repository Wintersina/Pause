# Painted source record

Generated with the image-generation tool on 2026-10-10. The Space boss sheet,
shot strip, pod-laser before/after captures, and the Void Archon card were used
as visual references. The source paintings were generated separately, then
reduced to 64 px native cells and cleaned by `build.py`; the finished sheets
are exact x2 nearest-neighbour exports.

- `beam_candidate_a.png`: isolated vertical white-hot pink plasma beam with
  twisting strands, magenta edge arcs and no hardware. Rejected because its
  painted silhouette was too broad for the 100 px beam budget.
- `beam_candidate_b_selected.png`: narrower vertical plasma beam, rolling
  strands and sparks. Selected for the body loop.
- `plasma_bursts_selected.png`: six isolated charged orb, ignition, muzzle,
  impact and spark motifs on transparent ground. Selected for the FX sheet.
- `reticle_selected.png`: painted dotted sight motif, four magenta reticle
  corners and a center cross. Selected for the tell sheet.

Shared generation constraints: transparent background; rich clustered pixel
painting; white-pink `#FFF4FB`, `#FFE0F8`, `#FF8AE6`, `#FF4FD8`, magenta
`#A8208A`, violet-magenta `#5B1A7A`; no hardware, scenery, text, red, cyan,
green, orange, or glow box. The selected sources were quantized to a stepped
plasma ramp, with the broad generated halos removed.
