#!/usr/bin/env python3
"""Copy the built Ember strips (Resources/Elites/Ember) next to Codex's folder (Enemies/Elite/Ember), the source EliteArtSync copies from,
so a re-import cannot bring the old 192 px strips back."""
import shutil
from pathlib import Path
R = Path(__file__).resolve().parents[3]
for k in "sunstoke coalrunner brass_vulture kilnback ash_wraith cauterizer".split():
    n = f"ember_elite_{k}.png"
    shutil.copy(R / "Resources/Elites/Ember" / n, R / "Enemies/Elite/Ember" / n)
    print("copied", n)
