"""Check every FCSE::Pattern in UFCP's sources against the shipped Dunia.dll builds.

A pattern is the only thing standing between a feature and the wrong bytes, and both ways it can be
wrong are silent. Matching in two places resolves to nothing at runtime, because FCSE reports
ambiguity as absence - the feature quietly does not apply. Matching nowhere on either build means it
was mistyped or the code moved, and again nothing happens and nothing is obviously broken. Neither
shows up in a build, and in a game both look exactly like "that fix does not work on my machine".

So the rule enforced here is: at least one build must match exactly once, and no build may match
more than once. A pattern deliberately present in only one build - the predecessor-tapes gate is a
privileges call on Steam and a registry read on GOG - passes on one and scores zero on the other,
which is correct rather than a failure.

Patterns are parsed out of the .cpp files rather than listed here, so this checks what is actually
compiled into UFCP.dll and cannot drift away from it.

    python verify_patterns.py
    python verify_patterns.py --uplay <path to Dunia.dll> --retail <path to Dunia.dll>

Needs pefile. Run it after adding or editing a pattern, and after any game update.
"""
import argparse
import glob
import os
import re
import sys

import pefile

DEFAULT_UPLAY = r"C:\Program Files (x86)\Steam\steamapps\common\Far Cry 2\bin\Dunia.dll"
DEFAULT_RETAIL = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                              "..", "..", "tmp", "compare-dlls", "dlls", "1.03.gog.Dunia.dll")

# FCSE::Pattern("aa bb" "cc dd") - adjacent string literals are concatenated by the compiler, and a
# long pattern is usually wrapped across two of them, so every quoted chunk in the call is joined.
PATTERN_CALL = re.compile(r"FCSE::Pattern\(\s*((?:\"[^\"]*\"\s*)+)\)", re.S)


def code_section(path):
    """The bytes of the executable section - the only place FCSE's own scanner looks."""
    pe = pefile.PE(path, fast_load=True)
    return next(s for s in pe.sections if s.Name.rstrip(b"\x00") == b".text").get_data()


def to_regex(pattern):
    return b"".join(b"." if token == "??" else re.escape(bytes([int(token, 16)]))
                    for token in pattern.split())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--uplay", default=DEFAULT_UPLAY, help="Steam/Ubisoft Connect Dunia.dll")
    parser.add_argument("--retail", default=DEFAULT_RETAIL, help="GOG/retail Dunia.dll")
    args = parser.parse_args()

    builds = {}
    for name, path in (("uplay", args.uplay), ("retail", args.retail)):
        if not os.path.exists(path):
            print(f"error: no {name} Dunia.dll at {path}")
            return 2
        builds[name] = code_section(path)

    source_root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "src")
    failures = 0
    checked = 0

    for path in sorted(glob.glob(os.path.join(source_root, "**", "*.cpp"), recursive=True)):
        with open(path, encoding="ascii") as handle:
            source = handle.read()
        for call in PATTERN_CALL.finditer(source):
            pattern = "".join(re.findall(r"\"([^\"]*)\"", call.group(1)))
            regex = to_regex(pattern)
            counts = {name: len(re.findall(regex, code, re.DOTALL))
                      for name, code in builds.items()}

            ok = max(counts.values()) == 1 and all(count <= 1 for count in counts.values())
            checked += 1
            failures += 0 if ok else 1
            print(f"{'ok  ' if ok else 'FAIL'}  {os.path.basename(path):<20} "
                  f"{len(pattern.split()):>2} bytes  "
                  + "  ".join(f"{name}={count}" for name, count in counts.items()))
            print(f"        {pattern}")

    if failures:
        print(f"\n{failures} of {checked} patterns would resolve wrongly or not at all")
        return 1
    print(f"\nall {checked} patterns resolve unambiguously")
    return 0


if __name__ == "__main__":
    sys.exit(main())
