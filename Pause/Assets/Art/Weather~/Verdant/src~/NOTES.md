# Verdant weather art sources

Painted source art was made with the built-in image generation tool on transparent canvases. The selected spore gust is `gust_selected.png`; the selected vine updraft is `updraft_selected.png`. The second candidates are kept alongside them for review. `ambient_source.png` supplies all sixteen ambient particle variants.

## Prompt set

- **Spore gust:** A horizontal, loose rolling weather band of jade and lime-white pollen cloud, pale-gold spores, a few tumbling leaves and seed-fluff; porous middle, denser rim, crisp hand-placed pixel art, upper-left light, transparent background.
- **Vine updraft:** An open vertical spiral of rising warm air carrying leaves, spores and seed-fluff, with thin curling green-light vines at its edges; bottom-centre foot, top fade, transparent middle, crisp pixel art.
- **Ambient:** Four isolated variants each of tiny pollen glints, tumbling green leaves, white seed-fluff puffs and lime spores with pale rims, arranged as separated source groups on transparency.
- All prompts forbade backgrounds, UI, text, red, pink, orange, beams, hot cores, glows and soft blur, and used the Verdant palette in the task brief.

`assemble.py` crops the painted sources, applies nearest-neighbour scaling, palette and alpha-step cleaning, builds six looping poses and two warning frames per hazard, and lays out the final sheets. The final assets use `draft3`.

Verification: `python3 scripts/verify_weather_art.py Pause/Assets/Art/Weather~/Verdant --world Verdant --fronts A --sheet Pause/Assets/Art/Weather~/Verdant/preview.png` passed every check. Body mean alpha is 13.5% for the gust and 6.4% for the updraft. The greatest front edge alpha difference is 4.7/255; detected red and pink shares are both zero. The updraft's pose change is intentionally restrained and is the weakest motion cue.
