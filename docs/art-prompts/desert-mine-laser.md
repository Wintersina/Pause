# Desert mine laser

Generated with the built-in image generation tool, then refined with two image edits. Runtime asset: `Pause/Assets/Art/Resources/Attacks/Desert/desert_attack_minebeam.png`.

Prompt specification: transparent square production sprite atlas for a top-down pixel-art shooter; six columns. Top half: six vertical sun-glass sand-lance beam frames, root at bottom, ivory core, pale gold / sandstone edges, angular glass pulses and tiny turquoise accents. Lower half: three six-cell rows. First: four charge diamonds, two dotted aim-line tiles. Second: four muzzle flashes, first two rail-impact bursts. Third: final fading impact, two faint heat-haze wisps, three empty cells. No text, grid, background, red, pink or purple. Crisp 16-bit pixel clusters.

Refinement prompts: remove background noise and broad glow, retain frame registration and true alpha; make the beam core continuous and brighter; replace reddish copper with yellow-gold / olive-brown (HSV hue 42–55), preserve turquoise flecks and the same cell layout.

The generated source is retained at its native resolution. Unity imports a 256×256 point-filtered texture (256 KiB RGBA32; ASTC 4×4 on mobile). Runtime sprites normalize cell dimensions and trim transparent side margins from the beam and sight so their visible width follows the unchanged hitbox. Charge, sight, six-frame beam loop, muzzle, impact and haze use the existing mine clocks, geometry and hitbox.

## Asset cleanup

The four Desert backdrop `v1..v4/mid.png` files were byte-identical to their corresponding `sky.png`, including fully opaque alpha. The `mid` layer now uses that same sky texture, with its own previous scroll rate, grading and pinned-ground behavior. The Codex thumbnail follows the catalogue's texture mapping. Removed the redundant PNGs and metas after checking serialized GUID references.

Removed PNG source bytes: **14,154,965** (about 13.5 MiB). This removes four duplicated packaged textures; final download-size savings depend on platform compression and are not the PNG byte total. Source drafts and live boss-laser kits are retained.
