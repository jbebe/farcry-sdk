"""Build the layer's breath banks from the game's own stamina breathing: each of its twelve breaths is
an exhale and then an inhale, cut apart here, and the halves become banks the plugin plays as the
breath is held and let go - the weaker breaths while stamina is high, the stronger ones while it is
low. Two more hold the heartbeat, heard while the breath is in and faintly while the heart races.

    python data\\breath.py <the worlds archive's soundbinary folder>

The folder is the extracted worlds archive's soundbinary\\, which holds 00455c76.spk (the breathing),
00455cd1.spk (the gasp out of water, whose event the new ones copy) and 004c661e.spk (a beat of the
heart as the player goes down). The banks are written to
layer\\mods\\soundbinary\\.
"""
import array
import os
import shutil
import subprocess
import sys
import tempfile
import wave

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.join(HERE, "..", "..", "..")
CLI = os.path.join(REPO, "tools", "JackAll", "src", "JackAll.Cli", "bin", "Release", "net10.0",
                   "jackall-cli.exe")
LAYER = os.path.join(HERE, "..", "layer", "mods", "soundbinary")

BREATHING = "00455c76.spk"
LIKE = "00455cd1.spk"
# Each breath's audio and the millisecond its exhale ends and its inhale begins. The game picks the
# first six at low stamina and the last six at medium, by its stamina switch.
STRONG = (("0x00455c57", 634), ("0x00455c58", 575), ("0x00455c59", 475),
          ("0x00455c5e", 529), ("0x00455c5f", 609), ("0x00455c60", 601))
WEAK = (("0x00455c65", 581), ("0x00455c66", 527), ("0x00455c67", 540),
        ("0x00455c6c", 593), ("0x00455c6d", 467), ("0x00455c6e", 388))
# The breaths whose exhale is too harsh to hear again and again; their inhales are kept.
HARSH_EXHALES = ("0x00455c67", "0x00455c57", "0x00455c58")
# The banks, by what they hold and its gain in dB, which src\breath.cpp plays by id: the breath held,
# at the level it was recorded at. The game's own breathing plays 32 dB lower, through its two
# switches, which was very quiet for a held breath.
BANKS = {"0x00fc0b00": ("inhale", WEAK, "0"), "0x00fc0b20": ("inhale", STRONG, "0"),
         "0x00fc0b40": ("exhale", WEAK, "0"), "0x00fc0b60": ("exhale", STRONG, "0")}
# The heart beating: one of the beats the game plays as the player goes down wounded, as banks with
# their gains in dB - a little quieter than the game plays it while the breath is held, and faint
# down the sights while the heart races.
WOUNDED_BEAT = ("004c661e.spk", "0x004c660d")
HEARTBEATS = {"0x00fc0b80": "-12", "0x00fc0c20": "-14"}
# Milliseconds each half fades in and out at the cut, so it does not click.
FADE_MS = 5


def run(*args):
    subprocess.run([CLI, *args], check=True, stdout=subprocess.DEVNULL)


def faded(frames, rate):
    """16-bit mono frames with a short fade at both ends."""
    samples = array.array("h", frames)
    fade = min(rate * FADE_MS // 1000, len(samples) // 2)
    for i in range(fade):
        samples[i] = samples[i] * i // fade
        samples[-1 - i] = samples[-1 - i] * i // fade
    return samples.tobytes()


def cut(work, audio, at_ms):
    """Splits the breath's .wav into its exhale and inhale, beside it."""
    with wave.open(os.path.join(work, audio[2:] + ".wav")) as source:
        rate = source.getframerate()
        frames = source.readframes(source.getnframes())
    at = rate * at_ms // 1000 * 2
    halves = {}
    for half, part in (("exhale", frames[:at]), ("inhale", frames[at:])):
        path = os.path.join(work, "%s_%s.wav" % (audio[2:], half))
        with wave.open(path, "wb") as out:
            out.setnchannels(1)
            out.setsampwidth(2)
            out.setframerate(rate)
            out.writeframes(faded(part, rate))
        halves[half] = path
    return halves


def main(soundbinary):
    work = tempfile.mkdtemp()
    try:
        # spk extract writes beside the bank, so it reads a copy.
        for bank in (BREATHING, LIKE, WOUNDED_BEAT[0]):
            shutil.copy(os.path.join(soundbinary, bank), work)
        bank = os.path.join(work, BREATHING)
        halves = {}
        for audio, at_ms in STRONG + WEAK:
            run("spk", "extract", bank, audio)
            halves[audio] = cut(work, audio, at_ms)
        os.makedirs(LAYER, exist_ok=True)
        for event, (half, breaths, gain) in BANKS.items():
            folder = os.path.join(work, event[2:])
            clips = [halves[audio][half] for audio, _ in breaths
                     if half == "inhale" or audio not in HARSH_EXHALES]
            run("spk", "new", event, *clips, "--unpositioned", "--gain", gain,
                "--like", os.path.join(work, LIKE), "-o", folder)
            out = os.path.join(LAYER, event[2:] + ".spk")
            run("spk", "encode", os.path.join(folder, event[2:] + ".xml"), "-o", out)
            print(os.path.relpath(out, os.path.join(HERE, "..")))
        beat_bank, beat = WOUNDED_BEAT
        run("spk", "extract", os.path.join(work, beat_bank), beat)
        for event, gain in HEARTBEATS.items():
            folder = os.path.join(work, event[2:])
            run("spk", "new", event, os.path.join(work, beat[2:] + ".wav"), "--unpositioned",
                "--gain", gain, "--like", os.path.join(work, LIKE), "-o", folder)
            out = os.path.join(LAYER, event[2:] + ".spk")
            run("spk", "encode", os.path.join(folder, event[2:] + ".xml"), "-o", out)
            print(os.path.relpath(out, os.path.join(HERE, "..")))
    finally:
        shutil.rmtree(work)


if __name__ == "__main__":
    main(sys.argv[1])
