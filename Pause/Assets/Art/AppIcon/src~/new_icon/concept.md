# Chasm breakout

GoldWarden Regent rises through the game's existing red PAUSE title in a narrow asteroid corridor. The hull, exhaust, and menu wordmark are composited from their original assets; the title pixels and colors are unchanged. A few suspended chips and the blue rim lit rocks give the launch a frozen moment without adding another red focal point.

Five generated backgrounds were compared with the same ship and logo. The impact tunnel was too busy at 48 px; the orbital rail fought the ship's horizontal silhouette; the time vortex made a second focal point; the eclipse pulled attention upward. The chasm kept the ship outline clear, and its center was refined to leave a darker launch lane. The four rejected compositions appear in `preview.png`.

`build_icon.py` recreates the deliverables from the included `concept_*.png` backgrounds and the repository's title, Regent hull, and exhaust atlas. It uses nearest-neighbour resizing and hard stepped glow only. Android's foreground is laid out separately for its 264 px safe circle, so its wordmark is necessarily smaller than the full icon. The source artwork is still visually legible at 96 px; at 48 px the ship silhouette and red sign are the primary cues.

Run `python3 build_icon.py && python3 verify_icon.py` from this directory. No Unity settings or existing assets were changed.
