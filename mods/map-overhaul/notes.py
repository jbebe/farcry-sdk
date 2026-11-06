# The previous owner's notes over the sheet: guard posts in red grease pencil, and in pencil the safe
# houses, arms dealers, bus stops and masts, and the places by the names the game gives them.
import zlib

import matplotlib.patheffects as effects
import numpy as np
from matplotlib.font_manager import FontProperties

HAND = FontProperties(fname="C:/Windows/Fonts/Inkfree.ttf")
PENCIL = "#2b2e34"
GREASE = "#c22b25"

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
SUPPLIES = {"Ammo": "ammo", "Health": "medical", "Explosives": "explos.", "Fuel": "fuel"}
MARKERS = {"Objectives.WeaponShop": "guns", "Objectives.BusStation": "bus", "Objectives.CellTower": "mast"}


def rng(key):
    return np.random.default_rng(zlib.crc32(key.encode()))


def hand(ax, x, y, s, size, colour, key, ha="left", va="center"):
    # Ink Free's strokes are hairlines at this size; an outline in the same colour gives them a pencil's weight.
    tilt = rng(key).uniform(-4, 4)
    ax.text(x, y, s, fontproperties=HAND, fontsize=size * 72 / 100, color=colour, alpha=0.9, ha=ha, va=va,
            rotation=tilt, rotation_mode="anchor", linespacing=1.05, zorder=30,
            path_effects=[effects.withStroke(linewidth=size * 0.05 * 72 / 100, foreground=colour, alpha=0.9)])


def remark(ax, place, x, y, scale, ha):
    if place in REMARKS:
        hand(ax, x, y, REMARKS[place], 27 * scale, PENCIL, place, ha=ha, va="top" if "\n" in REMARKS[place] else "center")
        ax.texts[-1].set_gid("remark")


def untangle(ax, scale):
    # Each remark steps down until it clears the printed names, the marks' labels and the remarks before it.
    renderer = ax.figure.canvas.get_renderer()
    notes = [t for t in ax.texts if t.get_zorder() >= 30]
    taken = [t.get_window_extent(renderer) for t in ax.texts if t.get_zorder() == 20]
    taken += [t.get_window_extent(renderer) for t in notes if t.get_gid() != "remark"]
    for t in (t for t in notes if t.get_gid() == "remark"):
        box = t.get_window_extent(renderer)
        for _ in range(8):
            if not any(box.overlaps(b) for b in taken):
                break
            x, y = t.get_position()
            t.set_position((x, y - box.height / scale / 2))
            box = t.get_window_extent(renderer)
        taken.append(box)


def stroke(ax, points, colour, width, key):
    jitter = rng(key).normal(0, 0.6, np.shape(points))
    line, = ax.plot(*(np.asarray(points) + jitter).T, color=colour, alpha=0.85, linewidth=width,
                    solid_capstyle="round", solid_joinstyle="round", zorder=29)
    line.set_sketch_params(1.2, 40, 3)


def ring(ax, x, y, r, colour, width, key):
    g = rng(key)
    a = np.linspace(0, 2 * np.pi + g.uniform(0.3, 0.6), 60) + g.uniform(0, 2 * np.pi)
    wobble = 1 + 0.06 * np.sin(a * 2 + g.uniform(0, 6))
    stroke(ax, np.c_[x + r * wobble * np.cos(a), y + r * 0.85 * wobble * np.sin(a)], colour, width, key)


def draw(ax, entities, scale, px):
    markers = [e for e in entities if e["file"] == "mapsdata"]
    guns = np.array([e["pos"][:2] for e in entities
                     if any("MountedWeapons." in (s or "") for s in (e.get("tpl"), e["name"]))])

    for e in markers:
        name = e["name"] or ""
        x, y = e["pos"][:2]
        key = f"{name}{x:.0f}{y:.0f}"
        if name.startswith("Objectives.PGP."):
            supply = SUPPLIES[name.split(".")[2].split("_")[1]]
            mounted = int((np.linalg.norm(guns - (x, y), axis=1) < 50).sum())
            r = 20
            stroke(ax, [[x, y + r], [x + r, y], [x, y - r], [x - r, y], [x, y + r * 1.1]], GREASE, px(3.2), key)
            label = f"GP {supply}" + (f"\n{mounted} MG" if mounted else "")
            hand(ax, x + r + 8, y, label, 25 * scale, GREASE, key)
        elif name.startswith("SafeHouse_"):
            r = 9
            stroke(ax, [[x - r, y - r], [x + r, y - r], [x + r, y + r * 0.4], [x, y + r * 1.3],
                        [x - r, y + r * 0.4], [x - r, y - r * 1.1]], PENCIL, px(2.2), key)
            hand(ax, x + r + 6, y - 2, "SH", 23 * scale, PENCIL, key)
        else:
            label = next((v for k, v in MARKERS.items() if name.startswith(k)), None)
            if label:
                ring(ax, x, y, 16, PENCIL, px(2.2), key)
                hand(ax, x + 22, y + 14, label, 24 * scale, PENCIL, key)

    x, y = 1000, 4160 + 260
    hand(ax, x, y, "red = them.  GP guard post, what they keep, MGs\nSH safe house", 30 * scale, PENCIL, "key")
    untangle(ax, scale)
