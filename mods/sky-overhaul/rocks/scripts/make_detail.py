"""Builds src/textures/rock_detail.dds, the rock detail map Sky Overhaul embeds, from Poly Haven's
CC0 `rock_face` scan: an A8R8G8B8 DDS whose every mip level is a plain average of the one above.

Needs numpy and pillow; downloads the 2K maps into rocks/work/ the first time.
"""

import pathlib
import struct
import urllib.request

import numpy as np
from PIL import Image

ASSET = "rock_face"
SOURCE_SIZE = 2048
SIZE = 512
# Variations wider than this share of the tile are the retail texture's business, not the detail's.
GRAIN_WIDTH = 1 / 16

ROOT = pathlib.Path(__file__).resolve().parents[2]
WORK = ROOT / "rocks" / "work" / ASSET
OUTPUT = ROOT / "src" / "textures" / "rock_detail.dds"


def fetch(kind):
    path = WORK / f"{ASSET}_{kind}_2k.png"
    if not path.exists():
        WORK.mkdir(parents=True, exist_ok=True)
        url = f"https://dl.polyhaven.org/file/ph-assets/Textures/png/2k/{ASSET}/{path.name}"
        print(f"downloading {url}")
        urllib.request.urlretrieve(url, path)
    with Image.open(path) as opened:
        # Poly Haven's single-channel maps are 16-bit, which a conversion to RGB clips to white.
        if opened.mode.startswith("I"):
            grey = np.asarray(opened, dtype=np.float64) / 65535.0
            image = np.dstack([grey] * 3)
        else:
            image = np.asarray(opened.convert("RGB"), dtype=np.float64) / 255.0
    if image.shape[:2] != (SOURCE_SIZE, SOURCE_SIZE):
        raise ValueError(f"{path.name} is {image.shape[:2]}, not {SOURCE_SIZE} square")
    return image


def linear(srgb):
    return np.where(srgb <= 0.04045, srgb / 12.92, ((srgb + 0.055) / 1.055) ** 2.4)


# A Gaussian blur that wraps at the edges, so a tiling map stays tiling.
def blur(channel, sigma):
    frequency = np.fft.fftfreq(channel.shape[0])
    kernel = np.exp(-2 * (np.pi * sigma) ** 2 * (frequency[:, None] ** 2 + frequency[None, :] ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(channel) * kernel))


def halve(image):
    return 0.25 * (image[0::2, 0::2] + image[1::2, 0::2] + image[0::2, 1::2] + image[1::2, 1::2])


def reduce_to(image, size):
    while image.shape[0] > size:
        image = halve(image)
    return image


def detail():
    diffuse = linear(fetch("diff"))
    luminance = diffuse @ np.array([0.2126, 0.7152, 0.0722])
    sigma = GRAIN_WIDTH * SOURCE_SIZE
    grain = luminance / np.maximum(blur(luminance, sigma), 1e-4)

    occlusion = fetch("ao")[..., 0]
    cavity = occlusion / occlusion.mean()

    normal = fetch("nor_dx") * 2 - 1
    relief = normal[..., :2] / np.linalg.norm(normal, axis=-1, keepdims=True)

    texels = np.dstack([relief * 0.5 + 0.5, cavity * 0.5, grain * 0.5])
    return reduce_to(np.clip(texels, 0, 1), SIZE)


def dds(levels):
    flags = 0x1 | 0x2 | 0x4 | 0x8 | 0x1000 | 0x20000
    pixel_format = struct.pack("<8I", 32, 0x41, 0, 32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000)
    header = struct.pack("<7I", 124, flags, SIZE, SIZE, SIZE * 4, 0, len(levels))
    header += b"\0" * 44 + pixel_format + struct.pack("<5I", 0x1000 | 0x400000 | 0x8, 0, 0, 0, 0)
    body = b""
    for level in levels:
        rgba = np.round(level * 255).astype(np.uint8)
        body += rgba[..., [2, 1, 0, 3]].tobytes()
    return b"DDS " + header + body


def main():
    level = detail()
    levels = [level]
    while level.shape[0] > 1:
        level = halve(level)
        levels.append(level)
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_bytes(dds(levels))
    top = levels[0]
    print(f"wrote {OUTPUT}: {SIZE}x{SIZE}, {len(levels)} levels; "
          f"cavity mean {top[..., 2].mean() * 2:.3f}, grain mean {top[..., 3].mean() * 2:.3f}")


if __name__ == "__main__":
    main()
