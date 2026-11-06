# The printed sheet on paper that has aged a little, and the ballpoint written over it.
import numpy as np
from scipy import ndimage

# What the print fades toward, and how far.
YELLOWED = np.array([0.965, 0.945, 0.885], np.float32)
FADE = 0.2
DESATURATE = 0.1


def noise(rng, shape, scale):
    # Smooth noise with features about `scale` pixels across, unit variance.
    h, w = shape
    small = rng.standard_normal((h // scale + 4, w // scale + 4)).astype(np.float32)
    big = ndimage.zoom(ndimage.gaussian_filter(small, 1), scale, order=3)[:h, :w] if scale > 1 else small[:h, :w]
    return (big - big.mean()) / big.std()


def finish(base, ink, seed=7):
    rng = np.random.default_rng(seed)
    shape = base.shape[:2]
    img = base[..., :3].astype(np.float32) / 255

    grey = img @ np.array([0.299, 0.587, 0.114], np.float32)
    img += (grey[..., None] - img) * DESATURATE
    img += (YELLOWED - img) * FADE

    # Uneven tone, and yellowing only where it is darker, so the paper never turns grey or blue.
    blotches = noise(rng, shape, 400) * 0.6 + noise(rng, shape, 60) * 0.4
    grain = ndimage.gaussian_filter(rng.standard_normal(shape).astype(np.float32), 0.7)
    grain /= grain.std()
    img *= (1 - np.abs(blotches) * 0.012 + grain * 0.016)[..., None]
    img *= 1 - np.clip(blotches, 0, None)[..., None] * np.array([0.0, 0.012, 0.045], np.float32)

    # Ballpoint sits in the paper as a multiply, and skips a little where the grain stands up.
    cover = ink[..., 3].astype(np.float32) / 255 * np.clip(0.9 - grain * 0.08, 0, 1)
    colour = ink[..., :3].astype(np.float32) / 255
    img *= 1 - cover[..., None] * (1 - colour)
    return (np.clip(img, 0, 1) * 255).astype(np.uint8)
