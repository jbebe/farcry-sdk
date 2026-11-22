"""Holds each of Sky Overhaul's weathers in turn and takes a picture of it, at noon and at night;
with --cells, puts a storm cell far ahead, near ahead and overhead under one held weather instead.

    python weather.py [--only C1,S3] [--hours 12,22] [--spots save,road,ranch] [--cells C4]

Needs Sky Overhaul with its Weather on, and DevTools. "save" is where the weather save leaves the
player, facing as it was saved; the others look out a little above the horizon from one of
scenarios\\spots\\world1.json's places.
"""

import argparse
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import Run

ROWS = ["C1", "C2", "C3", "C4", "C5", "C6", "C7", "F1", "F2", "F3", "O1", "O2", "O3", "S1", "S2",
        "S3", "S4", "W1", "W2"]
# x, y, z, yaw and the view's pitch, or None to stay where the save left the player.
SPOTS = {
    "save": None,
    "road": (1293.73, 3119.01, 32.0, 109.0, 8.0),
    "ranch": (1876.16, 3523.59, 37.0, 187.0, 2.0),
    "paper": (2604.02, 2108.09, 23.0, 0.0, 2.0),
}
# Where a cell is put for --cells, and how far up the view looks at it.
CELLS = [("far", "ahead 12000", 4.0), ("near", "ahead 4000", 10.0), ("over", "over", 35.0)]
# The rain eases in over five seconds and the cell map is redrawn each second; the rest of a weather
# is there at once.
SETTLE_SECONDS = 6.0


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--only", help="weather ids, comma-separated")
    parser.add_argument("--hours", default="12,22", help="game hours, comma-separated")
    parser.add_argument("--spots", default="save", help=f"of {', '.join(SPOTS)}, comma-separated")
    parser.add_argument("--cells", metavar="ROW", help="the weather held under the storm cells")
    options = parser.parse_args()
    rows = options.only.split(",") if options.only else ROWS
    hours = [int(hour) for hour in options.hours.split(",")]

    run = Run("weather", mods=["SkyOverhaul"])
    with run.launch("weather") as game:
        game.option("Invincibility", 1)
        game.wait_ready()
        for name in options.spots.split(","):
            spot = SPOTS[name]
            if spot is not None:
                game.goto(*spot[:4])
                game.wait_ready()
            yaw = spot[3] if spot is not None else game.state()["yaw"]
            for hour in hours:
                game.fix_light(hour)
                if options.cells:
                    game.sky_weather(options.cells)
                    for label, where, pitch in CELLS:
                        game.look(yaw, pitch)
                        game.sky_cell(where)
                        time.sleep(SETTLE_SECONDS)
                        run.shot(game, f"{name} {hour:02d}h {options.cells} cell {label}")
                    game.sky_cell("off")
                    continue
                for row in rows:
                    game.sky_weather(row)
                    if spot is not None:
                        game.look(spot[3], spot[4])
                    time.sleep(SETTLE_SECONDS)
                    run.shot(game, f"{name} {hour:02d}h {row}")
        game.sky_weather("timeline")
    run.expect("every weather was held and pictured", True)
    run.finish()


if __name__ == "__main__":
    main()
