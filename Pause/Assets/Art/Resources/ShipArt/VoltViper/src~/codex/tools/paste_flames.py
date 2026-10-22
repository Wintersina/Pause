"""Paste only VoltViper's flame strips into the (master's) exhaust atlas, without rebuilding the sheet.
    python3 paste_flames.py   (cwd anywhere)"""
import importlib.util
import os

HULLS = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))))
spec = importlib.util.spec_from_file_location("br", os.path.join(HULLS, "src~", "build_restyle.py"))
br = importlib.util.module_from_spec(spec)
spec.loader.exec_module(br)
codex = os.path.join(HULLS, "src~", "voltviper", "codex")
br.write_flames(br.SHIPS["VoltViper"], codex)
print("flames pasted")
