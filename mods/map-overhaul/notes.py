# The previous owner's notes over the sheet in blue ballpoint: G.P. at each guard post, the safe houses,
# arms dealers and bus stops, and the places by the names the game gives them.
import itertools
import urllib.request
import zlib
from pathlib import Path

import numpy as np
from matplotlib.font_manager import FontProperties

HERE = Path(__file__).resolve().parent
# A Google font under the SIL Open Font License, fetched once into work/.
HANDWRITING = "https://raw.githubusercontent.com/google/fonts/main/ofl/nothingyoucoulddo/NothingYouCouldDo.ttf"
BALLPOINT = "#1d3b8f"
# Markers that are circled, and what is written beside the circle.
CIRCLED = {"Objectives.BusStation": "bus", "Objectives.WeaponShop": "A.D."}

# What the game's own text says of each named place, by its detail map's texture
# (docs/docs/lore/places.md).
REMARKS = {
    "town": "both factions keep offices here\none hotel still open",
    "mikesplace": "Mike's Bar - expat bar,\ncontractors",
    "slaughterhouse": "Slaughter House - UFLL camp",
    "fishingcamp": "Fresh Fish - UFLL",
    "arena": "Cock-Fights - APR",
    "lumberyard": "Lumber - APR",
    "fishing_village": "boats refuel here",
    "fuel_depot": "UFLL hold it",
    "railyard": "North Railyard - gas tank cars",
    "ranch": "Cattle Xing",
    "fool_errand": "lodge",
    "old_fort": "the legion fort",
    "villa": "\"Private Property\"",
    "greenhouses": "OGC - APR guard it",
    "gncamp": "police chief",
    "junkyard": "Scrap Salvage -\nUFLL motor pool",
    "w1d3_chem_depot": "Claes Products - old chem dump",
    "w1d3_airstrip": "Airfield - crop duster",
    "w1d4_shantytown": "shanty town",
}


def handwriting():
    path = HERE / "work/fonts" / HANDWRITING.rsplit("/", 1)[1]
    if not path.exists():
        path.parent.mkdir(parents=True, exist_ok=True)
        urllib.request.urlretrieve(HANDWRITING, path)
    return FontProperties(fname=path)


HAND = handwriting()


def rng(key):
    return np.random.default_rng(zlib.crc32(key.encode()))


def hand(ax, x, y, s, size, key, ha="left", va="center", gid="ink"):
    tilt = rng(key).uniform(-4, 4)
    return ax.text(x, y, s, fontproperties=HAND, fontsize=size * 72 / 100, color=BALLPOINT, ha=ha, va=va,
                   rotation=tilt, rotation_mode="anchor", linespacing=1.0, zorder=30, gid=gid)


def remark(ax, place, anchors, y, scale):
    # Anchors are where the remark may start, best first: an x and the alignment that goes with it.
    if place in REMARKS:
        x, ha = anchors[0]
        text = hand(ax, x, y, REMARKS[place], 34 * scale, place, ha=ha,
                    va="top" if "\n" in REMARKS[place] else "center", gid="ink-remark")
        text.anchors = anchors


def stroke(ax, points, width, key):
    jitter = rng(key).normal(0, 0.5, np.shape(points))
    line, = ax.plot(*(np.asarray(points) + jitter).T, color=BALLPOINT, linewidth=width, solid_capstyle="round",
                    solid_joinstyle="round", zorder=29, gid="ink")
    line.set_sketch_params(0.8, 40, 3)


def ring(ax, x, y, r, width, key):
    g = rng(key)
    a = np.linspace(0, 2 * np.pi + g.uniform(0.3, 0.6), 60) + g.uniform(0, 2 * np.pi)
    wobble = 1 + 0.06 * np.sin(a * 2 + g.uniform(0, 6))
    stroke(ax, np.c_[x + r * wobble * np.cos(a), y + r * 0.85 * wobble * np.sin(a)], width, key)


def untangle(ax, scale):
    # Each remark takes the first of its anchors, a little higher or lower, that clears the printed names,
    # the marks and the remarks before it; if none does, it stays where it was written.
    renderer = ax.figure.canvas.get_renderer()
    notes = [t for t in ax.texts if (t.get_gid() or "").startswith("ink")]
    taken = [t.get_window_extent(renderer) for t in ax.texts if t.get_zorder() == 20]
    taken += [t.get_window_extent(renderer) for t in notes if t.get_gid() == "ink"]
    for t in (t for t in notes if t.get_gid() == "ink-remark"):
        y = t.get_position()[1]
        height = t.get_window_extent(renderer).height / scale
        for (x, ha), dy in itertools.product(t.anchors, (0, 0.5, -0.5, 1, -1)):
            t.set_position((x, y + dy * height))
            t.set_horizontalalignment(ha)
            box = t.get_window_extent(renderer)
            if not any(box.overlaps(b) for b in taken):
                break
        else:
            t.set_position((t.anchors[0][0], y))
            t.set_horizontalalignment(t.anchors[0][1])
            box = t.get_window_extent(renderer)
        taken.append(box)


def draw(ax, entities, scale, px):
    for e in entities:
        if e["file"] != "mapsdata":
            continue
        name = e["name"] or ""
        x, y = e["pos"][:2]
        key = f"{name}{x:.0f}{y:.0f}"
        if name.startswith("Objectives.PGP."):
            r = 20
            stroke(ax, [[x, y + r], [x + r, y], [x, y - r], [x - r, y], [x, y + r * 1.1]], px(2.2), key)
            hand(ax, x + r + 8, y, "G.P.", 38 * scale, key)
        elif name.startswith("SafeHouse_"):
            r = 9
            stroke(ax, [[x - r, y - r], [x + r, y - r], [x + r, y + r * 0.4], [x, y + r * 1.3],
                        [x - r, y + r * 0.4], [x - r, y - r * 1.1]], px(1.8), key)
            hand(ax, x + r + 8, y - 2, "S.H.", 30 * scale, key)
        elif label := next((v for k, v in CIRCLED.items() if name.startswith(k)), None):
            ring(ax, x, y, 16, px(1.8), key)
            hand(ax, x + 22, y + 14, label, 30 * scale, key)

    hand(ax, 1000, 4160 + 260, "G.P. guard post      S.H. safe house      A.D. arms dealer", 40 * scale, "key")
    untangle(ax, scale)
