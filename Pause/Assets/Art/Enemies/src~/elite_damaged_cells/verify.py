import sys
sys.path.insert(0, '/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/dmg')
from common import *
rows = []
for w, n, key in keys():
    d = defn(key); c = d['cells']; fl = c['flight'][0]; dm = c['damaged']
    im, h, cs = strip(respath(w, key))
    f = np.asarray(cs[fl]).astype(int); g = np.asarray(cs[dm]).astype(int)
    mf, mg = mask(cs[fl]), mask(cs[dm])
    iou = (mf & mg).sum() / max(1, (mf | mg).sum())
    diff = (np.abs(f[..., :3] - g[..., :3]).max(-1) > 35) & mf & mg
    ch = ((mf ^ mg) | diff).sum() / max(1, mf.sum())
    bb = cs[dm].getbbox(); fb = cs[fl].getbbox()
    print(f'{key:34s} cell{h} fl{fl} dm{dm} iou{iou:.3f} changed{ch*100:.1f}% flbb{fb} dmbb{bb} cellWorld{d.get("cellWorldSize")} exh{d.get("exhaustScale")} glow{d.get("glowScale")}')
    row = Image.new('RGBA', (h * 3, h), (30, 24, 60, 255))
    row.alpha_composite(cs[fl], (0, 0)); row.alpha_composite(cs[dm], (h, 0))
    dd = np.zeros_like(f, dtype=np.uint8); dd[..., :3] = f[..., :3] // 3; dd[..., 3] = np.where(mf, 255, 0)
    chm = (mf ^ mg) | diff; dd[chm] = [255, 0, 0, 255]
    row.alpha_composite(Image.fromarray(dd, 'RGBA'), (2 * h, 0))
    rows.append(row.resize((h * 3 // 2, h // 2), Image.BOX))
for i in range(0, len(rows), 7):
    part = rows[i:i + 7]
    S = Image.new('RGBA', (part[0].width, part[0].height * len(part)))
    for j, r in enumerate(part): S.paste(r, (0, j * r.height))
    S.save(SCR + f'verify_{i//7}.png')
