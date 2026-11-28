"""Tide soft painted weather (user-approved 2026-11-28) into Resources/Worlds/Tide/Backdrop3, under the OLD sprite names:
  weather.png  cloud_bank_00..03 <- wx_bank 0..3;  cloud_wisp_00..03 <- wx_squall 0..3 (rain squalls drift as wisps);  mist_00..02 <- wx_mist 0..2;
               gust_00..02 <- wx_spray 0..2 (spray gusts);  pall_00 and rain_00 stay as they were. 256 px cells, box-filtered from the 512 paintings.
  wx_hazard.png  row 0 (the whirlpool / waterspout loop, 6 cells of 512) <- wx_spout (8 frames sampled to 6);  row 1 (the current surge / storm front, 6 cells)
               <- wx_front (6 seamless frames, scaled to the 512 cell, centred);  row 2 (the warnings) untouched.
Run once on the committed sheets (the old cells are replaced in place; idempotent for the same inputs)."""
import os, json, numpy as np
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__))
D = os.path.normpath(os.path.join(HERE, '../../../Backgrounds/Resources/Worlds/Tide/Backdrop3'))
S = f'{HERE}/src/out2'
def premul_resize(im, w, h, f=Image.BOX):
    a = np.asarray(im.convert('RGBA')).astype(np.float32) / 255; a[..., :3] *= a[..., 3:4]
    ch = [np.asarray(Image.fromarray(a[..., i], 'F').resize((w, h), f)) for i in range(4)]; o = np.stack(ch, -1); al = o[..., 3:4]
    o[..., :3] = np.where(al > 1e-4, o[..., :3] / np.maximum(al, 1e-4), 0); return Image.fromarray((np.clip(o, 0, 1) * 255 + .5).astype(np.uint8), 'RGBA')
def wx(name, k): return Image.open(f'{S}/bd_tide_wx_{name}/r1_{k}.png').convert('RGBA')
GAIN_BANK, GAIN = .6, .85
def weather():
    j = json.load(open(f'{D}/weather.json')); sheet = Image.open(f'{D}/weather.png').convert('RGBA'); H = sheet.height
    plan = {f'cloud_bank_0{i}': ('bank', i) for i in range(4)}; plan.update({f'cloud_wisp_0{i}': ('squall', i) for i in range(4)})
    plan.update({f'mist_0{i}': ('mist', i) for i in range(3)}); plan.update({f'gust_0{i}': ('spray', i) for i in range(3)})
    for s in j['sprites']:
        if s['n'] not in plan: continue
        name, k = plan[s['n']]; c = premul_resize(wx(name, k), s['w'], s['h'])
        # CI (2026-11-28): the painted banks read 0.87 value against the planetfall deck's 0.44 and the atlas 0.906 as drawn (<= 0.8): the cloud is toned down
        # (the ceiling's lift 1.6 and alpha thickening multiply it), the banks hardest; alpha untouched
        g = GAIN_BANK if name == 'bank' else GAIN
        a = np.asarray(c).astype(np.float32); a[..., :3] *= g; c = Image.fromarray(np.clip(a + .5, 0, 255).astype(np.uint8), 'RGBA')
        box = (s['x'], H - s['y'] - s['h']); sheet.paste(Image.new('RGBA', (s['w'], s['h']), (0, 0, 0, 0)), box); sheet.paste(c, box)
    sheet.save(f'{D}/weather.png', optimize=True); print('weather.png', len(plan), 'cells replaced')
def hazard():
    sheet = Image.open(f'{D}/wx_hazard.png').convert('RGBA'); P = 512
    sp = wx('spout', 0); n = sp.width // sp.height; fr = [sp.crop((i * sp.height, 0, (i + 1) * sp.height, sp.height)) for i in range(n)]
    for c, idx in enumerate([round(i * n / 6) % n for i in range(6)]):
        cell = premul_resize(fr[idx], P, P, Image.BILINEAR); sheet.paste(Image.new('RGBA', (P, P), (0, 0, 0, 0)), (c * P, 0)); sheet.paste(cell, (c * P, 0))
    fl = wx('front', 0); w6 = fl.width // 6
    for c in range(6):
        f = fl.crop((c * w6, 0, (c + 1) * w6, fl.height)); h = max(8, int(round(fl.height * P / w6)))
        cell = Image.new('RGBA', (P, P), (0, 0, 0, 0)); cell.paste(premul_resize(f, P, h, Image.BILINEAR), (0, (P - h) // 2))
        sheet.paste(Image.new('RGBA', (P, P), (0, 0, 0, 0)), (c * P, P)); sheet.paste(cell, (c * P, P))
    sheet.save(f'{D}/wx_hazard.png', optimize=True); print('wx_hazard.png rows 0-1 replaced')
if __name__ == '__main__': weather(); hazard()
