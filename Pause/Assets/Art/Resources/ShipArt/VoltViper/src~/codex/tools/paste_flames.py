"""Rewrite only VoltViper's flame strips (its <Key>_flame.png), without rebuilding the sheet.
    python3 paste_flames.py   (cwd anywhere)"""
import importlib.util
import os

SHIPART = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))))
spec = importlib.util.spec_from_file_location("br", os.path.join(SHIPART, "src~", "build_restyle.py"))
br = importlib.util.module_from_spec(spec)
spec.loader.exec_module(br)
codex = os.path.join(SHIPART, "VoltViper", "src~", "codex")
br.write_flames("VoltViper", br.load_cfg("VoltViper"), codex)
print("flames written")
