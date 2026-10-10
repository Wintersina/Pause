# Review notes

- These are staged art files only. Nothing under `Art/Resources` or gameplay
  code was changed, so Unity does not load them yet.
- The original mine atlas divides 1254 pixels into four 313.5-pixel columns.
  This one-row export uses alternating 313/314-pixel cell widths and is meant
  for atlas assembly or explicit rects, rather than equal-width strip slicing.
- The painted rupture sources were composed from references, then recelled
  with nearest-neighbour sampling. Some micro-detail will be lost at phone size.
- `audit.py` checks measurable rules. Its debris-area test is only a proxy for
  the visual no-hull rule; inspect `preview.png` for that call, especially the
  broad coral and nautilus sprays.
- `preview.gif` represents four examples at 0.08/0.11/0.20 s. Unity playback
  and rail clamp contact have not been tested because these files are staged.
