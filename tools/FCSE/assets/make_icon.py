"""Builds assets/fcse.ico - FCSE.exe's application icon - from assets/logo.png.

    python make_icon.py logo.png fcse.ico

Run by hand, not by the build. Unlike the .mgb layouts (built every build by JackAll, which this
project already depends on for the format), an .ico needs an image library to resample, and making
Pillow a prerequisite for compiling a mod loader is a worse trade than committing a 70 KB artifact.
The .ico is therefore checked in next to the .png it came from, and this script is what regenerates
it when the logo changes.

Three things here are not arbitrary:

* SIZES is the classic Windows triple: 16 (title bar, small icons, tray), 32 (taskbar, alt-tab) and
  48 (medium icons). Every other size the shell wants it synthesises by resampling the nearest of
  these at display time - the DPI variants (20/24/40) off 16 and 32, and everything from Explorer's
  large view up off 48. That is the deliberate trade here: ~15 KB in the exe instead of ~70 KB, paid
  for by a soft icon in the views that ask for 96px and above. Adding a size back is one entry in
  this list; the two policy constants below already cover whatever it turns out to be.

* Entries below 96 are 32bpp bottom-up DIBs with a real 1bpp AND mask, larger ones PNG-compressed
  (a PNG entry needs Vista+, no constraint for sizes only a modern shell requests, and 96/128 as
  DIBs would cost 100 KB between them). The mask is written even though a 32bpp icon composites
  from its alpha channel: it is what the shell falls back to when it has to reduce the icon to a
  depth without alpha.

* All three get an unsharp pass. logo.png is 688px of gear teeth around a three-armed spiral; at a
  10x reduction the spiral's arms fall under one pixel and LANCZOS averages them into a grey blob.
  Sharpening the alpha channel afterwards pulls them back apart. It is still tight at 16 - that is
  the artwork, not the resampler.
"""
import struct
import sys
from io import BytesIO

from PIL import Image, ImageFilter

SIZES = [16, 32, 48]
PNG_AT_OR_ABOVE = 96
SHARPEN_AT_OR_BELOW = 64


def resample(src: Image.Image, size: int) -> Image.Image:
    frame = src.resize((size, size), Image.LANCZOS)
    if size <= SHARPEN_AT_OR_BELOW:
        # Alpha only: the logo is black everywhere and carries its shape entirely in the alpha
        # channel, so sharpening the colour channels would have nothing to work on.
        frame.putalpha(frame.getchannel("A").filter(
            ImageFilter.UnsharpMask(radius=1, percent=150, threshold=0)))
    return frame


def and_mask(img: Image.Image) -> bytes:
    """1bpp mask, bottom-up, rows padded to 4 bytes. A set bit means transparent."""
    w, h = img.size
    alpha = img.transpose(Image.FLIP_TOP_BOTTOM).getchannel("A").tobytes()
    stride = ((w + 31) // 32) * 4
    out = bytearray()
    for y in range(h):
        row = bytearray(stride)
        for x in range(w):
            if alpha[y * w + x] == 0:
                row[x >> 3] |= 0x80 >> (x & 7)
        out += row
    return bytes(out)


def as_dib(img: Image.Image) -> bytes:
    w, h = img.size
    # BITMAPINFOHEADER, then the colour bits, then the mask. biHeight counts both halves, so it is
    # doubled; biSizeImage may be 0 for an uncompressed bitmap.
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    bits = img.transpose(Image.FLIP_TOP_BOTTOM).tobytes("raw", "BGRA")
    return header + bits + and_mask(img)


def as_png(img: Image.Image) -> bytes:
    buf = BytesIO()
    img.save(buf, format="PNG", optimize=True)
    return buf.getvalue()


def main(src_path: str, dst_path: str) -> None:
    src = Image.open(src_path).convert("RGBA")
    if src.width != src.height:
        raise SystemExit(f"{src_path} must be square, got {src.width}x{src.height}")
    if src.width < max(SIZES):
        raise SystemExit(f"{src_path} is {src.width}px, needs at least {max(SIZES)}px")

    frames = []
    for size in SIZES:
        frame = resample(src, size)
        frames.append((size, as_png(frame) if size >= PNG_AT_OR_ABOVE else as_dib(frame)))

    # ICONDIR, then one ICONDIRENTRY per frame, then the frames. A 256px entry writes its dimension
    # as 0 - the field is one byte and 256 does not fit.
    parts = [struct.pack("<HHH", 0, 1, len(frames))]
    offset = 6 + 16 * len(frames)
    for size, blob in frames:
        parts.append(struct.pack("<BBBBHHII", size & 0xFF, size & 0xFF, 0, 0, 1, 32,
                                 len(blob), offset))
        offset += len(blob)
    parts.extend(blob for _, blob in frames)

    with open(dst_path, "wb") as fp:
        for part in parts:
            fp.write(part)
    print(f"wrote {dst_path}: {len(frames)} entries, {offset} bytes, {SIZES}")


if __name__ == "__main__":
    main(*sys.argv[1:3])
