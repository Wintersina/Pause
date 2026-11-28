# Storm flow strip (soft painted pipeline): ONE PixelLab painting (raw/a_0.png, generate-image-v2 512, no style image) ->
# luminance -> alpha, downsampled to its true resolution (256: drops the dither), smooth upscale, then laid out as a
# seamless 512 x 2048 toroidal tile (sin^2 cross-fades across and down). Four variants = four arrangements of the one painting.
import numpy as np, sys
from PIL import Image
from scipy import ndimage
W, H = 512, 2048
PEAK = .42          # strongest vapour alpha (the layer tint adds x .85): subtle
def load():
    a = np.asarray(Image.open('raw/a_0.png').convert('RGB')).astype(float) / 255
    small = np.asarray(Image.fromarray((a * 255).astype(np.uint8)).resize((256, 256), Image.LANCZOS)).astype(float) / 255
    small = np.stack([ndimage.gaussian_filter(small[..., c], .7, mode='reflect') for c in range(3)], -1)
    big = np.asarray(Image.fromarray((np.clip(small, 0, 1) * 255).astype(np.uint8)).resize((512, 512), Image.BICUBIC)).astype(float) / 255
    big = np.stack([ndimage.gaussian_filter(big[..., c], 1.2, mode='reflect') for c in range(3)], -1)
    return np.clip(big, 0, 1)
def premul(rgb):
    L = rgb @ [.2126, .7152, .0722]
    t = np.clip((L - .17) / .7, 0, 1); al = t * t * (3 - 2 * t)
    return np.concatenate([rgb * al[..., None], al[..., None]], -1)
def wrapx(p):
    w = np.sin(np.pi * np.arange(p.shape[1]) / p.shape[1]) ** 2
    return p * w[None, :, None] + np.roll(p, p.shape[1] // 2, 1) * (1 - w)[None, :, None]
def wrapy(p):
    w = np.sin(np.pi * np.arange(p.shape[0]) / p.shape[0]) ** 2
    return p * w[:, None, None] + np.roll(p, p.shape[0] // 2, 0) * (1 - w)[:, None, None]
def build(seed):
    rng = np.random.default_rng(seed)
    P = wrapx(premul(load()))
    tiles = []
    for i in range(4):
        q = P if (i + seed) % 2 == 0 else P[::-1, ::-1]          # 180 deg turn keeps the wind's diagonal
        q = np.roll(q, int(rng.integers(0, 512)), 1)
        tiles.append(q)
    # stack with 96 px feathered overlaps (cross-fade), then make the whole column toroidal
    ov = 96; step = 512 - ov
    col = np.zeros((step * 4 + ov, 512, 4))
    wsum = np.zeros((step * 4 + ov, 1, 1))
    for i, q in enumerate(tiles):
        ramp = np.ones(512); ramp[:ov] = np.linspace(0, 1, ov); ramp[-ov:] = np.linspace(1, 0, ov)
        col[i * step:i * step + 512] += q * ramp[:, None, None]; wsum[i * step:i * step + 512] += ramp[:, None, None]
    col = col / np.maximum(wsum, 1e-6)
    col = np.asarray(col[:H] if col.shape[0] >= H else np.concatenate([col, col[:H - col.shape[0]]], 0))
    col = np.stack([np.asarray(Image.fromarray(col[..., c].astype(np.float32), 'F').resize((W, H), Image.BICUBIC)) for c in range(4)], -1)
    t = wrapy(wrapx(np.clip(col, 0, 1)))
    a = np.clip(t[..., 3], 0, 1)
    rgb = np.where(a[..., None] > 1e-4, t[..., :3] / np.maximum(a[..., None], 1e-4), 0)
    # storm vapour palette: keep the painting's blue-white / lavender, pull any warm or pink hue to blue
    rgb = np.clip(rgb, 0, 1); rgb[..., 0] = np.minimum(rgb[..., 0], rgb[..., 2] * .97)
    a = a / max(a.max(), 1e-3) * PEAK
    a = np.where(a < 2 / 255, 0, a)
    out = np.concatenate([rgb, a[..., None]], -1)
    return Image.fromarray((out * 255 + .5).astype(np.uint8), 'RGBA')
if __name__ == '__main__':
    for v in range(1, 5):
        build(v * 7 + 1).save(f'v{v}_flow.png'); print('v', v)
