"""Builds the thunder banks in layer/mods/soundbinary from public-domain field recordings: clips cut
out of the storms, shaped for their distance, levelled, and packed three banks of four, one per
distance (00f90100 near, 00f90200 a few kilometres off, 00f90300 far).

    python tools/build_thunder.py <recordings folder> <work folder>

The recordings are Wikimedia Commons files, all public domain, saved under the names below:
  summer-storm-01..04.ogg  "Thunderstorm after hot summer day 17 minutes 01..04 of 04.ogg" (stephan)
  rain-thunder-and-other-noises.ogg, thunder-and-rain-on-a-v.ogg, rain-and-thunder-1.ogg
                           "Rain thunder and other noises.ogg", "Thunder and rain on a v.ogg",
                           "Rain and thunder (1).ogg" (ezwa)
Needs ffmpeg on PATH, numpy and scipy, and jackall-cli built in the main tree.
"""
import subprocess
import sys
import wave
from pathlib import Path

import numpy as np
from scipy import signal

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parents[2] / "scripts"))
from modstatus import MAIN_TREE  # noqa: E402

CLI = MAIN_TREE / "tools/JackAll/src/JackAll.Cli/bin/Release/net10.0/jackall-cli.exe"
LAYER = HERE.parent / "layer/mods/soundbinary"
RATE = 44100

# Per distance: the bank's event id, a low-pass for what the air takes off a far strike, and the
# loudest 50 ms each clip is levelled to.
CLASSES = {"close": (0x00F90100, None, -6.0), "roll": (0x00F90200, 3000.0, -10.0),
           "far": (0x00F90300, 600.0, -14.0)}
# Distance, recording, from and to in seconds.
CLIPS = [
    ("close", "summer-storm-03.ogg", 192.0, 203.0),
    ("close", "summer-storm-02.ogg", 21.5, 28.0),
    ("close", "rain-thunder-and-other-noises.ogg", 9.6, 19.5),
    ("close", "summer-storm-02.ogg", 224.2, 236.2),
    ("roll", "summer-storm-01.ogg", 159.2, 171.2),
    ("roll", "summer-storm-02.ogg", 137.6, 146.4),
    ("roll", "summer-storm-03.ogg", 112.6, 124.4),
    ("roll", "thunder-and-rain-on-a-v.ogg", 4.0, 16.0),
    ("far", "summer-storm-04.ogg", 216.4, 222.0),
    ("far", "summer-storm-04.ogg", 228.6, 240.6),
    ("far", "rain-and-thunder-1.ogg", 2.5, 14.5),
    ("far", "summer-storm-01.ogg", 240.5, 250.0),
]
FADE_IN = 0.3
FADE_OUT = 2.0
CEILING = -1.0


def load(source, name, start, end):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-ss", str(start), "-t", str(end - start), "-i",
                          str(source / name), "-ac", "2", "-ar", str(RATE), "-f", "f32le", "-"],
                         check=True, capture_output=True).stdout
    return np.frombuffer(raw, dtype=np.float32).reshape(-1, 2).copy()


def loudest(x, window=0.05):
    mono = x.mean(axis=1)
    hop = int(window * RATE)
    frames = len(mono) // hop
    rms = np.sqrt(np.mean(mono[:frames * hop].reshape(frames, hop) ** 2, axis=1))
    return 20 * np.log10(rms.max() + 1e-9)


def shape(x, lowpass):
    """Rumble below hearing off, the far end's highs off, and curved fades in and out."""
    x = signal.sosfiltfilt(signal.butter(2, 25.0, btype="high", fs=RATE, output="sos"), x, axis=0)
    if lowpass:
        x = signal.sosfiltfilt(signal.butter(2, lowpass, btype="low", fs=RATE, output="sos"), x, axis=0)
    n = len(x)
    fade_in = int(FADE_IN * RATE)
    fade_out = int(FADE_OUT * RATE)
    x[:fade_in] *= (np.sin(np.linspace(0, np.pi / 2, fade_in)) ** 2)[:, None]
    x[n - fade_out:] *= (np.cos(np.linspace(0, np.pi / 2, fade_out)) ** 2)[:, None]
    return x


def write(path, x):
    with wave.open(str(path), "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(np.clip(x * 32767, -32768, 32767).astype("<i2").tobytes())


def main():
    source, work = Path(sys.argv[1]), Path(sys.argv[2])
    work.mkdir(parents=True, exist_ok=True)
    clips = {kind: [] for kind in CLASSES}
    for kind, name, start, end in CLIPS:
        _, lowpass, target = CLASSES[kind]
        x = shape(load(source, name, start, end), lowpass)
        peak = 20 * np.log10(np.abs(x).max() + 1e-9)
        gain = min(target - loudest(x), CEILING - peak)
        x *= 10 ** (gain / 20)
        stem = work / f"thunder_{kind}_{len(clips[kind]) + 1}"
        write(stem.with_suffix(".wav"), x)
        # A fixed stream serial and no encoder tag, so the same clips build the same banks.
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(stem.with_suffix(".wav")), "-c:a",
                        "libvorbis", "-q:a", "5", "-serial_offset", "1", "-fflags", "+bitexact",
                        "-flags:a", "+bitexact", str(stem.with_suffix(".ogg"))], check=True)
        clips[kind].append(stem.with_suffix(".ogg"))
        print(f"{stem.name}: {name} {start}-{end} s, {gain:+.1f} dB")
    LAYER.mkdir(parents=True, exist_ok=True)
    for kind, (event, _, _) in CLASSES.items():
        bank = f"{event:08x}"
        subprocess.run([str(CLI), "spk", "new", f"0x{bank}", *map(str, clips[kind]), "-o",
                        str(work / bank)], check=True, stdout=subprocess.DEVNULL)
        subprocess.run([str(CLI), "spk", "encode", str(work / bank / f"{bank}.xml"), "-o",
                        str(LAYER / f"{bank}.spk")], check=True)


if __name__ == "__main__":
    main()
