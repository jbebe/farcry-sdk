#!/usr/bin/env python3
"""sbao_tool - unpack/repack Far Cry 2 music & dialogue .sbao files.

The Ogg-backed .sbao variant used for long audio (music, dialogue) is simply:

    [40-byte header][verbatim Ogg Vorbis bitstream]

The header is byte-identical across files except a 16-byte asset GUID at 0x08
(not a content checksum - it is NOT recomputed here and does not need to be).
Field @0x04 is a little-endian u32 giving the offset to the Ogg payload (always
40 in the retail files, but read rather than assumed). See
research/sbao_format.md for the full derivation.

So "convert to sbao and back" needs no audio-codec work at all:
  unpack  = carve the Ogg out
  repack  = concatenate the original header with a new Ogg

The only real constraint is that Far Cry 2 plays music at 48000 Hz: a
replacement Ogg must be 48 kHz (stereo) or it will play too fast/slow. `repack`
takes any format ffmpeg can read (mp3/wav/flac/m4a/...) and transcodes it to
48 kHz stereo Ogg Vorbis for you, which is the actual fix behind the community
guide's "-8.120% speed" workaround (44100/48000 = 0.91875). ffmpeg is required
for `repack`; point at it with --ffmpeg or have it on PATH.

Usage:
    python sbao_tool.py info   <file.sbao>
    python sbao_tool.py unpack <in.sbao> [out.ogg]        # also writes out.hdrsbao
    python sbao_tool.py repack <template.sbao> <in.audio> <out.sbao>
                               [--ffmpeg PATH] [--quality N]

    --ffmpeg PATH   Path to ffmpeg(.exe), or a directory containing it. Defaults
                    to 'ffmpeg' resolved on PATH.
    --quality N     libvorbis quality for transcoding, -1..10 (default 6, ~192kbps).
                    Ignored when the input is already a 48 kHz stereo Ogg.
"""

import os
import shutil
import struct
import subprocess
import sys
import tempfile

SBAO_MAGIC = bytes([0x02, 0x1F, 0x00, 0x10])  # constant type marker at 0x00
HEADER_OFFSET_FIELD = 0x04                     # u32 LE: offset to the Ogg payload
EXPECTED_RATE = 48000                          # FC2 music playback rate
EXPECTED_CHANNELS = 2


def _find_ogg_offset(data):
    """Payload offset per the header field, validated against the 'OggS' magic;
    falls back to the first 'OggS' occurrence if the field looks wrong."""
    if len(data) < 8:
        raise ValueError("file too small to be an .sbao")
    off = struct.unpack_from("<I", data, HEADER_OFFSET_FIELD)[0]
    if 0 < off < len(data) - 4 and data[off:off + 4] == b"OggS":
        return off
    idx = data.find(b"OggS")
    if idx < 0:
        raise ValueError("no Ogg bitstream found - not an Ogg-backed .sbao "
                         "(short SFX .sbao use Ubi ADPCM codecs, unsupported here)")
    return idx


def _read_vorbis_id(ogg):
    """Return (sample_rate, channels) from the Vorbis identification header of an
    Ogg stream, or (None, None) if it can't be parsed."""
    # First Ogg page: 'OggS'(4) ver(1) type(1) granule(8) serial(4) seq(4)
    # crc(4) nsegs(1) segtable(nsegs); then the Vorbis ID packet:
    # 0x01 'vorbis'(6) version(4) channels(1) sample_rate(4 LE) ...
    if len(ogg) < 28 or ogg[:4] != b"OggS":
        return None, None
    nsegs = ogg[26]
    packet = 27 + nsegs
    if ogg[packet:packet + 7] != b"\x01vorbis":
        return None, None
    channels = ogg[packet + 11]
    sample_rate = struct.unpack_from("<I", ogg, packet + 12)[0]
    return sample_rate, channels


def _load(path):
    with open(path, "rb") as f:
        return f.read()


def _resolve_ffmpeg(explicit):
    """Return a usable ffmpeg command. `explicit` may be the exe path, a directory
    containing it, or None (in which case PATH is searched). Exits if not found."""
    if explicit:
        if os.path.isdir(explicit):
            found = (shutil.which("ffmpeg", path=explicit)
                     or shutil.which("ffmpeg.exe", path=explicit))
            if not found:
                sys.exit(f"error: no ffmpeg(.exe) found in directory {explicit}")
            return found
        if os.path.isfile(explicit):
            return explicit
        # treat as a command name to resolve on PATH
        found = shutil.which(explicit)
        if found:
            return found
        sys.exit(f"error: --ffmpeg '{explicit}' is not a file, directory, or "
                 f"a command on PATH.")
    found = shutil.which("ffmpeg") or shutil.which("ffmpeg.exe")
    if not found:
        sys.exit("error: ffmpeg is required for repack. Pass --ffmpeg <path> or "
                 "put ffmpeg on PATH.")
    return found


def _transcode_to_ogg(ffmpeg, src, dst, quality):
    """Transcode any ffmpeg-readable audio to 48 kHz stereo Ogg Vorbis."""
    cmd = [ffmpeg, "-y", "-hide_banner", "-loglevel", "error",
           "-i", src,
           "-vn", "-map_metadata", "-1",
           "-c:a", "libvorbis", "-ar", str(EXPECTED_RATE), "-ac",
           str(EXPECTED_CHANNELS), "-q:a", str(quality),
           dst]
    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0:
        err = (result.stderr or "").strip()
        sys.exit(f"error: ffmpeg failed transcoding {src}:\n{err}")
    if not os.path.isfile(dst) or os.path.getsize(dst) == 0:
        sys.exit(f"error: ffmpeg produced no output for {src}")


def cmd_info(path):
    data = _load(path)
    off = _find_ogg_offset(data)
    guid = data[0x08:0x18].hex()
    rate, ch = _read_vorbis_id(data[off:])
    print(f"file          : {path}")
    print(f"size          : {len(data)} bytes")
    print(f"magic @0x00   : {data[0:4].hex()} "
          f"({'ok' if data[0:4] == SBAO_MAGIC else 'UNEXPECTED'})")
    print(f"payload offset: {off} (header field @0x04)")
    print(f"asset GUID    : {guid}")
    print(f"ogg payload   : {len(data) - off} bytes")
    print(f"vorbis        : {rate} Hz, {ch} ch"
          + ("" if (rate, ch) == (EXPECTED_RATE, EXPECTED_CHANNELS)
             else "   <- differs from FC2's 48000 Hz / 2 ch"))


def cmd_unpack(in_path, out_path=None):
    data = _load(in_path)
    off = _find_ogg_offset(data)
    if out_path is None:
        out_path = in_path.rsplit(".", 1)[0] + ".ogg"
    hdr_path = out_path.rsplit(".", 1)[0] + ".hdrsbao"
    with open(out_path, "wb") as f:
        f.write(data[off:])
    with open(hdr_path, "wb") as f:
        f.write(data[:off])
    rate, ch = _read_vorbis_id(data[off:])
    print(f"wrote {out_path} ({len(data) - off} bytes, {rate} Hz {ch} ch)")
    print(f"wrote {hdr_path} ({off} bytes) - the original header, reuse it to repack")


def cmd_repack(template_path, in_path, out_path, opts):
    template = _load(template_path)
    off = _find_ogg_offset(template)   # validates template is a real Ogg-backed sbao
    header = template[:off]

    # ffmpeg is required for repack (input may be any format). Resolve it up front
    # so we fail fast with a clear message rather than after reading files.
    ffmpeg = _resolve_ffmpeg(opts["ffmpeg"])

    data = _load(in_path)
    rate, ch = _read_vorbis_id(data) if data[:4] == b"OggS" else (None, None)

    if (rate, ch) == (EXPECTED_RATE, EXPECTED_CHANNELS):
        ogg = data
        print(f"input is already a {rate} Hz stereo Ogg - using as-is (no re-encode).")
    else:
        why = ("input is not Ogg Vorbis" if rate is None
               else f"input is {rate} Hz / {ch} ch")
        print(f"{why}; transcoding to {EXPECTED_RATE} Hz stereo Ogg Vorbis via "
              f"ffmpeg (q={opts['quality']})...")
        with tempfile.TemporaryDirectory() as td:
            tmp = os.path.join(td, "converted.ogg")
            _transcode_to_ogg(ffmpeg, in_path, tmp, opts["quality"])
            ogg = _load(tmp)
        r2, c2 = _read_vorbis_id(ogg)
        if (r2, c2) != (EXPECTED_RATE, EXPECTED_CHANNELS):
            sys.exit(f"error: transcode produced {r2} Hz / {c2} ch, expected "
                     f"{EXPECTED_RATE}/{EXPECTED_CHANNELS} - is this ffmpeg built "
                     f"with libvorbis?")

    with open(out_path, "wb") as f:
        f.write(header)
        f.write(ogg)
    print(f"wrote {out_path} ({len(header)} B header + {len(ogg)} B ogg = "
          f"{len(header) + len(ogg)} B)")
    print("drop it into Data_Win32\\Loose\\ at the file's archive-relative path "
          "to override it via ModPatcher.")


def _extract_opts(args):
    """Pull recognized --flags out of a positional arg list. Returns
    (positionals, opts)."""
    opts = {"ffmpeg": None, "quality": "6"}
    positional = []
    i = 0
    while i < len(args):
        a = args[i]
        key = None
        if a in ("--ffmpeg", "--quality"):
            if i + 1 >= len(args):
                sys.exit(f"error: {a} needs a value")
            key, value, step = a[2:], args[i + 1], 2
        elif a.startswith("--ffmpeg="):
            key, value, step = "ffmpeg", a.split("=", 1)[1], 1
        elif a.startswith("--quality="):
            key, value, step = "quality", a.split("=", 1)[1], 1
        if key:
            opts[key] = value
            i += step
        else:
            positional.append(a)
            i += 1
    return positional, opts


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1
    cmd = argv[1]
    pos, opts = _extract_opts(argv[2:])
    try:
        if cmd == "info" and len(pos) == 1:
            cmd_info(pos[0])
        elif cmd == "unpack" and len(pos) in (1, 2):
            cmd_unpack(pos[0], pos[1] if len(pos) == 2 else None)
        elif cmd == "repack" and len(pos) == 3:
            cmd_repack(pos[0], pos[1], pos[2], opts)
        else:
            print(__doc__)
            return 1
    except (ValueError, OSError) as e:
        sys.exit(f"error: {e}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
