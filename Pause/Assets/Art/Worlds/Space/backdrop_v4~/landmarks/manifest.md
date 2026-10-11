# Space landmarks atlas

Generated with the built-in image generation tool, then sliced, resized with nearest-neighbour, and packed on transparent canvases. The live Space atlas and sky files were not changed.

## Delivered

- `wrecks.png` / `wrecks.json`: 2048 × 2048 RGBA, eight 512 × 1024 cells in a 4 × 2 grid, `wreck_00`–`wreck_07`.
- `cities.png` / `cities.json`: 2048 × 2048 RGBA, four 1024 × 1024 cells in a 2 × 2 grid, `city_00`–`city_03`.
- `preview.png`: both atlases on `#0a0820`, plus a phone composition of `city_00` and `wreck_05` over `sky_01.png` scaled to cover.

Both JSON files use `pixelScale: 2`, bottom-origin Unity `y` coordinates, and tight sprite rectangles padded by 2 transparent pixels.

## Measurements

P90 measures HSV value on pixels with alpha > 32. Margins are clear transparent pixels from the cell edges, ordered left / right / top / bottom. Bright magenta means hue 312–326°, saturation > 0.5, and value > 0.55; largest patch is the largest four-connected component.

| Sprite | P90 value | Clear margins (px) | Bright magenta pixels | Largest patch (px) | Pixels above V 0.60 |
|---|---:|---|---:|---:|---:|
| `wreck_00` | 0.380 | 115 / 96 / 44 / 52 | 0 | 0 | 984 |
| `wreck_01` | 0.349 | 97 / 96 / 56 / 32 | 0 | 0 | 627 |
| `wreck_02` | 0.408 | 97 / 104 / 53 / 32 | 0 | 0 | 1073 |
| `wreck_03` | 0.404 | 97 / 96 / 44 / 44 | 0 | 0 | 1845 |
| `wreck_04` | 0.349 | 96 / 96 / 41 / 52 | 0 | 0 | 294 |
| `wreck_05` | 0.365 | 96 / 96 / 48 / 52 | 11 | 7 | 658 |
| `wreck_06` | 0.353 | 96 / 96 / 51 / 57 | 0 | 0 | 381 |
| `wreck_07` | 0.380 | 96 / 97 / 38 / 86 | 0 | 0 | 1186 |
| `city_00` | 0.294 | 62 / 62 / 187 / 187 | 460 | 12 | 840 |
| `city_01` | 0.275 | 62 / 62 / 186 / 186 | 0 | 0 | 1341 |
| `city_02` | 0.235 | 71 / 72 / 62 / 62 | 128 | 20 | 594 |
| `city_03` | 0.239 | 77 / 78 / 62 / 62 | 140 | 36 | 874 |

## Geometry checks

- Atlas dimensions: both 2048 × 2048 pixels, RGBA.
- Sprite rectangles: all within image bounds, no overlaps.
- Minimum fully transparent cell margin: 32 px for wrecks, 62 px for cities; required minimum is 14 px.
- P90 limits: every wreck ≤ 0.50 and every city ≤ 0.58.
- Tiny bright lamps remain in the generated art; the total bright-pixel count includes isolated metal highlights and city glints.
