"""Writes codex/skinref_state{0,1,2}.png from the FINAL composed + overlaid hull sheet (so skin refs carry the
composed damage). Run after build_restyle + patch + damage_overlay, before the skin jobs."""
import importlib.util, os, sys
from PIL import Image
D = os.path.dirname(os.path.abspath(__file__))
T = os.path.join(D, '..', '..', 'src~')
sys.path.insert(0, T)
spec = importlib.util.spec_from_file_location('br', os.path.join(T, 'build_restyle.py'))
br = importlib.util.module_from_spec(spec); spec.loader.exec_module(br)
sheet = Image.open(os.path.join(D, '..', 'Ligher.png')).convert('RGBA')
br.write_skin_refs(sheet, os.path.join(D, 'codex'))
print('skinrefs written')
