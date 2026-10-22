"""Adds damage_zones to restyle.json from ellipses measured on the approved key poses (1024 px key coords:
cx, cy, rx, ry). Key -> cell: cell = 128 + (k - 512) / 4 (the key bbox centre is (512, 512), scale 0.25)."""
import json, os
D = os.path.dirname(os.path.abspath(__file__))
S1 = [(385, 430, 80, 125), (650, 540, 48, 72), (262, 775, 72, 72), (765, 770, 72, 72), (518, 935, 62, 45)]
S2 = [(515, 385, 80, 145), (510, 150, 68, 82), (400, 470, 95, 135), (640, 560, 85, 115),
      (270, 775, 90, 90), (770, 775, 90, 90), (515, 925, 90, 58), (430, 830, 55, 65)]
conv = lambda z: [round(128 + (z[0] - 512) / 4, 1), round(128 + (z[1] - 512) / 4, 1), round(z[2] / 4, 1), round(z[3] / 4, 1)]
cfg = json.load(open(os.path.join(D, 'restyle.json')))
cfg['damage_zones'] = {'s1': [conv(z) for z in S1], 's2': [conv(z) for z in S2]}
json.dump(cfg, open(os.path.join(D, 'restyle.json'), 'w'), indent=1)
