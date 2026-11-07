# Draws world 1 as a military topographic sheet from what extract_world.py read out of the game:
# height-band tints, contours, water, roads, tracks, buildings, spot heights, names and a 1 km grid.
import argparse
import functools
import json
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.patheffects as effects
import matplotlib.pyplot as plt
import numpy as np
from matplotlib.collections import LineCollection, PolyCollection
from matplotlib.font_manager import FontProperties
from matplotlib.patches import Rectangle
from PIL import Image
from scipy import ndimage
from scipy.cluster.hierarchy import fclusterdata

import notes
import paper

HERE = Path(__file__).resolve().parent
WORLD = 5120
# The neat line: the playable square the game's own world map covers, and a sector either side; the
# paper's edge, a margin of 300 m outside it; and the scale the sheet is printed at, which sheet.py makes
# the paper's size from, 1.06 m, and Map Overhaul's plugin its icons' places with: hold.py holds it where that
# size fills the view, out on all but straight arms.
NEAT = (960, 4160)
MARGIN = 300
EDGE = (NEAT[0] - MARGIN, NEAT[1] + MARGIN)
SCALE = 3600
# World X and Y to grid eastings and northings, in metres.
GRID_ORIGIN = (612000, 1404000)
DPI = 100

PAPER = "#f4f0e2"
INK = "#1d1b19"
BROWN = "#94603c"
BLUE = "#3a79ad"
WATER = "#b3d3e3"
ROAD = "#ffffff"
# Height bands: their upper bounds in metres and their tints, low ground green to high ground brown.
BANDS = [(20, "#cfdcb4"), (40, "#dde3ba"), (60, "#e9e7c3"), (80, "#ece0bb"), (100, "#e6d4ab"),
         (150, "#dbc39c"), (200, "#cfb08c"), (300, "#c19d7f")]

FONT = {style: FontProperties(fname=f"C:/Windows/Fonts/{name}")
        for style, name in (("regular", "ARIALN.TTF"), ("bold", "ARIALNB.TTF"),
                            ("italic", "ARIALNI.TTF"), ("bolditalic", "ARIALNBI.TTF"))}

# Each named place, by its detail map's texture, as a survey sheet would print it.
PLACES = {
    "town": ("PALA", "town"),
    "fishing_village": ("SHWASANA", "village"),
    "w1d4_shantytown": ("MOKUBA", "village"),
    "fuel_depot": ("PetroSahel Fuel Terminal", "works"),
    "railyard": ("Railway Yard", "works"),
    "ranch": ("Cattle Ranch", "works"),
    "fool_errand": ("Goka Falls", "feature"),
    "old_fort": ("Fort (ruin)", "works"),
    "king_oasis": ("Oasis", "feature"),
    "villa": ("Villa", "works"),
    "greenhouses": ("Greenhouses (OGC)", "works"),
    "gncamp": ("Police Post", "works"),
    "junkyard": ("Scrapyard", "works"),
    "w1d3_chem_depot": ("Chemical Works", "works"),
    "w1d3_airstrip": ("Airstrip", "works"),
    "w1d4_dsoasis": ("Oasis", "feature"),
    "slaughterhouse": ("Abattoir", "works"),
    "fishingcamp": ("Fish Camp", "works"),
    "lumberyard": ("Sawmill", "works"),
}
# Models that are drawn as buildings: by folder, then a footprint no smaller than this, in square metres.
BUILDING_FOLDERS = ("graphics\\buildings\\",)
BUILDING_SKIP = ("window", "door", "frame", "stairs", "antenna", "sign", "awning", "gazebo")
MIN_FOOTPRINT = 6.0


def pt(px):
    return px * 72 / DPI


def hermite(points, step=2.0):
    out = []
    for a, b in zip(points, points[1:]):
        p0, p1 = np.array(a["pos"][:2]), np.array(b["pos"][:2])
        m0, m1 = np.array(a["tan"][:2]), np.array(b["tan"][:2])
        n = max(2, int(np.linalg.norm(p1 - p0) / step))
        t = np.linspace(0, 1, n)[:, None]
        out.append((2 * t**3 - 3 * t**2 + 1) * p0 + (t**3 - 2 * t**2 + t) * m0
                   + (-2 * t**3 + 3 * t**2) * p1 + (t**3 - t**2) * m1)
    return np.vstack(out)


def placed(e, local):
    yaw = np.radians(e["ang"][2] if e["ang"] else 0)
    rot = np.array([[np.cos(yaw), -np.sin(yaw)], [np.sin(yaw), np.cos(yaw)]])
    return np.asarray(local) @ rot.T + e["pos"][:2]


def footprint(e):
    x0, y0, _, x1, y1, _ = e["box"]
    return placed(e, [[x0, y0], [x1, y0], [x1, y1], [x0, y1]])


def models(entities, accept):
    return [e for e in entities if e.get("model") and accept(e["model"].lower())]


def is_building(model):
    return model.startswith(BUILDING_FOLDERS) and not any(s in model for s in BUILDING_SKIP)


def is_bridge(model):
    return "_bridgekit\\" in model and "bridge" in model.split("\\")[-1] and "base" not in model


def bridges(entities, roads):
    # A road stops at each riverbank; the decks between two road ends join them as one road.
    decks = [footprint(e) for e in models(entities, is_bridge)]
    ends = np.array([p for r in roads for p in (r[0], r[-1])])
    groups = fclusterdata(np.array([d.mean(0) for d in decks]), t=12, criterion="distance", method="single")
    links = []
    for g in np.unique(groups):
        corners = np.vstack([d for d, k in zip(decks, groups) if k == g])
        centre = corners.mean(0)
        near = ends[np.linalg.norm(ends - centre, axis=1) < np.linalg.norm(corners - centre, axis=1).max() + 15]
        if len(near) >= 2:
            gaps = np.linalg.norm(near[:, None] - near[None], axis=2)
            i, j = np.unravel_index(np.argmax(gaps), gaps.shape)
            links.append(np.array([near[i], near[j]]))
    return links


def is_track(model):
    name = model.split("\\")[-1]
    return name.startswith("infra_train_track_") and "detail" not in name


def track(e):
    # A straight piece runs along its X axis; a curve from its origin to its far corner.
    x0, y0, _, x1, y1, _ = e["box"]
    name = e["model"].lower().split("\\")[-1]
    if name.startswith("infra_train_track_b"):
        far = max(((x, y) for x in (x0, x1) for y in (y0, y1)), key=lambda p: np.hypot(*p))
        return placed(e, [[0, 0], [far[0] - np.sign(far[0]) * 2, far[1] - np.sign(far[1]) * 2]])
    return placed(e, [[x0, 0], [x1, 0]])


def water_depth(terrain):
    flagged = (terrain["still"] > 0) | (terrain["river"] > 0)
    level = np.where(flagged, terrain["level"], -1e3)
    level = np.repeat(np.repeat(level, 64, 0), 64, 1)
    level = np.pad(level, ((0, 1), (0, 1)), mode="edge")
    return level - terrain["heights"]


def spot_heights(heights, region):
    x0, y0, x1, y1 = region
    peaks = (heights == ndimage.maximum_filter(heights, size=161)) & (heights > ndimage.uniform_filter(heights, 161) + 12)
    ys, xs = np.nonzero(peaks)
    keep = (xs > x0) & (xs < x1) & (ys > y0) & (ys < y1)
    return [(x, y, heights[y, x]) for x, y in zip(xs[keep], ys[keep])]


def text(ax, x, y, s, size, style="regular", color=INK, halo=PAPER, ha="center", va="center", zorder=20):
    return ax.text(x, y, s, fontproperties=FONT[style], fontsize=pt(size), color=color, ha=ha, va=va,
                   zorder=zorder,
                   path_effects=[effects.withStroke(linewidth=pt(size * 0.18), foreground=halo)] if halo else None)


def margins(ax, px, scale):
    mid = (NEAT[0] + NEAT[1]) / 2
    top, bottom = NEAT[1], NEAT[0]
    t = lambda x, y, s, size, style="regular", **kw: text(ax, x, y, s, size * scale, style, halo=None, **kw)

    # Above the map: series and sheet, the title and its scale, the edition, and the classification.
    t(NEAT[0], top + 110, "SERIES M712   SHEET 3", 26, "bold", ha="left")
    t(mid, top + 115, "LEBOA-SAKO", 72, "bold")
    t(mid, top + 185, f"Scale 1:{SCALE:,}".replace(",", " "), 28)
    t(NEAT[1], top + 110, "EDITION 2-LSD", 26, ha="right")
    t(mid, top + 255, "RESTRICTED", 30, "bold")
    first = NEAT[0] + (-(GRID_ORIGIN[0] + NEAT[0])) % 1000
    t(first, top + 62, f"{GRID_ORIGIN[0] + first}m E.", 18)

    # Below it: the legend in a row, the scale bar, the notes and the classification again.
    x, y = NEAT[0] + 10, bottom - 90
    rows = [
        ("Road", lambda a, b, c: [ax.plot([a, b], [c, c], color=INK, linewidth=px(6), solid_capstyle="butt"),
                                  ax.plot([a, b], [c, c], color=ROAD, linewidth=px(4), solid_capstyle="butt")]),
        ("Track or footpath", lambda a, b, c: ax.plot([a, b], [c, c], color=INK, linewidth=px(1.3), dashes=(7, 4))),
        ("Railway", lambda a, b, c: [ax.plot([a, b], [c, c], color=INK, linewidth=px(2.6))] +
                                    [ax.plot([v, v], [c - 4, c + 4], color=INK, linewidth=px(1.6)) for v in range(int(a) + 10, int(b), 20)]),
        ("Buildings", lambda a, b, c: [ax.add_patch(Rectangle((a + o, c - 7), 14, 14, color=INK)) for o in (0, 24, 48)]),
        ("Water", lambda a, b, c: ax.add_patch(Rectangle((a, c - 12), b - a, 24, facecolor=WATER, edgecolor=BLUE,
                                                         linewidth=px(0.9)))),
        ("Contour, index contour", lambda a, b, c: [ax.plot([a, b], [c - 6, c - 6], color=BROWN, linewidth=px(0.7)),
                                                    ax.plot([a, b], [c + 6, c + 6], color=BROWN, linewidth=px(1.7))]),
        ("Spot height", lambda a, b, c: [ax.plot(a + 10, c, "o", color=INK, markersize=px(3)),
                                         t(a + 22, c, "238", 18, "italic", ha="left")]),
    ]
    for i, (label, symbol) in enumerate(rows):
        row_x = x + i * 330
        symbol(row_x, row_x + 70, y)
        t(row_x + 85, y, label, 20, ha="left")

    # Scale bar: 500 metres in hundreds left of zero, a whole kilometre right of it.
    zero, bar_y, h = NEAT[0] + 520, bottom - 185, 18
    for i in range(5):
        ax.add_patch(Rectangle((zero - 500 + i * 100, bar_y), 100, h, facecolor=INK if i % 2 else PAPER,
                               edgecolor=INK, linewidth=px(1.2), zorder=20))
    ax.add_patch(Rectangle((zero, bar_y), 1000, h, facecolor=INK, edgecolor=INK, linewidth=px(1.2), zorder=20))
    for bar_x, label in ((zero - 500, "500"), (zero, "0"), (zero + 500, "500"), (zero + 1000, "1000")):
        t(bar_x, bar_y + h + 20, label, 18)
    t(zero + 1060, bar_y + h / 2, "Metres", 18, ha="left")
    t(NEAT[0] + 10, bottom - 245, "CONTOUR INTERVAL 10 METRES. HEIGHTS IN METRES ABOVE MEAN SEA LEVEL.", 18, "bold",
      ha="left")
    t(NEAT[0] + 10, bottom - 275, "Compiled from air photography 1974. Roads and buildings revised from ground "
      "survey 2006.", 16, ha="left")
    t(mid + 600, bottom - 260, "RESTRICTED", 30, "bold")

    # Height bands in a row, lowest on the left.
    x, y = NEAT[1] - 780, bottom - 185
    t(x, y + 50, "LAYER TINTS", 18, "bold", ha="left")
    for i, ((upper, colour), lower) in enumerate(zip(BANDS, [0] + [b for b, _ in BANDS[:-1]])):
        ax.add_patch(Rectangle((x + i * 96, y - 14), 88, 28, facecolor=colour, edgecolor=INK, linewidth=px(0.8)))
        t(x + i * 96 + 44, y - 34, f"{lower}-{upper}" if i < len(BANDS) - 1 else f"{lower}+", 15)

    # North points in the corner below and right of the map, their angles drawn larger than they are.
    x, y = NEAT[1] + 170, bottom - 250
    for angle, label in ((0, "TN"), (-9, "GN"), (-24, "MN")):
        a = np.radians(angle)
        ax.plot([x, x + 170 * np.sin(a)], [y, y + 170 * np.cos(a)], color=INK, linewidth=px(1.2))
        t(x + 190 * np.sin(a), y + 190 * np.cos(a), label, 16, "bold")
    t(x - 20, y - 22, "MN 3°12′ W of GN", 13)
    t(x - 20, y - 40, "Angles not to scale", 13, "italic")


@functools.cache
def world():
    terrain = dict(np.load(HERE / "work/world1/terrain.npz"))
    features = json.loads((HERE / "work/world1/features.json").read_text())
    return terrain, features, ndimage.gaussian_filter(terrain["heights"], 1.5)


def draw(region, scale, out, clean, symbols=None):
    # Symbols, lines and lettering are sized in pixels of the sheet at 1 m a pixel; `symbols` scales them
    # apart from the ground, so a town plan can draw its streets large and its lettering as the sheet's.
    symbols = symbols or scale
    m = symbols / scale
    terrain, features, heights = world()
    x0, y0, x1, y1 = region

    fig = plt.figure(figsize=((x1 - x0) * scale / DPI, (y1 - y0) * scale / DPI), dpi=DPI, facecolor=PAPER)
    ax = fig.add_axes([0, 0, 1, 1])
    ax.set_xlim(x0, x1)
    ax.set_ylim(y0, y1)
    ax.axis("off")
    neat = Rectangle((NEAT[0], NEAT[0]), NEAT[1] - NEAT[0], NEAT[1] - NEAT[0], transform=ax.transData)
    px = lambda n: pt(n * symbols)

    # Only the part of the world the region needs, a little wider so lines run off its edge.
    pad = 64
    sx0, sy0 = max(0, int(x0) - pad), max(0, int(y0) - pad)
    sx1, sy1 = min(WORLD, int(x1) + pad), min(WORLD, int(y1) + pad)
    h = heights[sy0:sy1 + 1, sx0:sx1 + 1]
    xs, ys = np.arange(sx0, sx1 + 1), np.arange(sy0, sy1 + 1)

    levels = [-10] + [b for b, _ in BANDS]
    tints = ax.contourf(xs, ys, h, levels=levels, colors=[c for _, c in BANDS], zorder=1, antialiased=True)
    tints.set_clip_path(neat)
    minor = ax.contour(xs, ys, h, levels=[v for v in range(10, 300, 10) if v % 50], colors=BROWN,
                       linewidths=px(0.7), zorder=3)
    index = ax.contour(xs, ys, h, levels=list(range(50, 300, 50)), colors=BROWN, linewidths=px(1.7), zorder=3)
    for c in (minor, index):
        c.set_clip_path(neat)
    for label in ax.clabel(index, fmt="%d", inline=True, inline_spacing=4 * symbols):
        label.set_fontproperties(FONT["regular"])
        label.set_fontsize(px(13))
        label.set_clip_path(neat)

    depth = water_depth(terrain)[sy0:sy1 + 1, sx0:sx1 + 1]
    lake = ax.contourf(xs, ys, depth, levels=[0, 1e4], colors=[WATER], zorder=4)
    shore = ax.contour(xs, ys, depth, levels=[0], colors=BLUE, linewidths=px(0.9), zorder=5)
    lake.set_clip_path(neat)
    shore.set_clip_path(neat)

    entities = features["entities"]
    paths = [hermite(s) for s in features["PathSplines"] if len(s) > 1]
    roads = [hermite(s) for s in features["RoadSplines"] if len(s) > 1]
    roads += bridges(entities, roads)
    rails = [track(e) for e in models(entities, is_track)]
    ticks = []
    for a, b in rails:
        along = (b - a) / max(np.linalg.norm(b - a), 1e-6)
        across = np.array([-along[1], along[0]]) * 4 * m
        for t in np.arange(10 * m, np.linalg.norm(b - a) - 4 * m, 20 * m):
            ticks.append([a + along * t - across, a + along * t + across])
    buildings = [footprint(e) for e in models(entities, is_building)
                 if (e["box"][3] - e["box"][0]) * (e["box"][4] - e["box"][1]) >= MIN_FOOTPRINT]
    collections = [
        LineCollection(paths, colors=INK, linewidths=px(1.3), linestyles=(0, (7, 4)), zorder=6),
        LineCollection(roads, colors=INK, linewidths=px(6), capstyle="round", zorder=7),
        LineCollection(roads, colors=ROAD, linewidths=px(4), capstyle="round", zorder=8),
        LineCollection(rails, colors=INK, linewidths=px(2.6), zorder=9),
        LineCollection(ticks, colors=INK, linewidths=px(1.6), zorder=9),
        PolyCollection(buildings, facecolors=INK, edgecolors="none", zorder=10),
    ]
    for c in collections:
        ax.add_collection(c)
        c.set_clip_path(neat)

    for x, y, z in spot_heights(heights, region):
        if NEAT[0] < x < NEAT[1] and NEAT[0] < y < NEAT[1]:
            ax.plot(x, y, "o", color=INK, markersize=px(3), zorder=12)
            text(ax, x + 6 * m, y - 6 * m, f"{z:.0f}", 14 * symbols, "italic", ha="left", va="top")

    # The world's summit is the mountain dialogue names "one mile northeast of Pala".
    inside = heights[NEAT[0]:NEAT[1], NEAT[0]:NEAT[1]]
    sy, sx = np.unravel_index(np.argmax(inside), inside.shape)
    text(ax, sx + NEAT[0], sy + NEAT[0] + 60 * m, "MT THABAMOLAETSA", 26 * symbols, "bolditalic")

    # A place can have more than one detail-map entity on the same texture; it is named once.
    named = set()
    for e in entities:
        if e.get("tex") and e["tex"] not in named:
            named.add(e["tex"])
            place = Path(e["tex"]).stem.removeprefix("majorlocation_")
            name, kind = PLACES.get(place, (None, "works"))
            size, style = {"town": (40, "bold"), "village": (28, "bold"),
                           "works": (18, "regular"), "feature": (20, "italic")}[kind]
            x, y = e["pos"][0], e["pos"][1] + e["h"] / 2 + size * 0.8 * m
            if name:
                label = text(ax, x, y, name, size * symbols, style)
                if not clean:
                    half = label.get_window_extent(fig.canvas.get_renderer()).width / scale / 2 + 14 * m
                    notes.remark(ax, place, [(x + half, "left"), (x - half, "right")], y, scale, symbols)
            elif not clean:
                notes.remark(ax, place, [(x, "center")], y, scale, symbols)

    for axis, origin in enumerate(GRID_ORIGIN):
        for g in range(NEAT[0] + (-(origin + NEAT[0])) % 1000, NEAT[1], 1000):
            vertical = axis == 0
            ax.plot([g, g] if vertical else NEAT, NEAT if vertical else [g, g], color=INK,
                    linewidth=px(1.1), zorder=15)
            number = f"{(g + origin) // 1000 % 100:02d}"
            for edge, off in ((NEAT[0], -30), (NEAT[1], 30)):
                text(ax, g if vertical else edge + off, edge + off if vertical else g, number, 26 * symbols,
                     halo=None)
    ax.add_patch(Rectangle((NEAT[0], NEAT[0]), NEAT[1] - NEAT[0], NEAT[1] - NEAT[0], fill=False,
                           edgecolor=INK, linewidth=px(2.5), zorder=16))
    margins(ax, px, symbols)
    if not clean:
        notes.draw(ax, entities, scale, symbols, px)

    Image.fromarray(paper.finish(*layers(fig, ax))).save(out)
    plt.close(fig)
    print(f"Wrote {out}")


def layers(fig, ax):
    # The figure drawn twice: the print without the ink, then the ink alone.
    ink = [a for a in ax.get_children() if (a.get_gid() or "").startswith("ink")]
    for a in ink:
        a.set_visible(False)
    fig.canvas.draw()
    printed = np.asarray(fig.canvas.buffer_rgba()).copy()
    for a in ax.get_children():
        a.set_visible(a in ink)
    fig.patch.set_visible(False)
    fig.canvas.draw()
    return printed, np.asarray(fig.canvas.buffer_rgba()).copy()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--region", type=float, nargs=4, default=(0, 0, WORLD, WORLD),
                        metavar=("X0", "Y0", "X1", "Y1"), help="world metres to draw")
    parser.add_argument("--scale", type=float, default=1.0, help="pixels per metre")
    parser.add_argument("--out", default=str(HERE / "assets/world1-sheet.png"))
    parser.add_argument("--symbols", type=float, help="how large lines and lettering are drawn, the sheet's being 1"
                                                         " (default: the scale)")
    parser.add_argument("--clean", action="store_true", help="the sheet as printed, without the owner's notes")
    args = parser.parse_args()
    draw(args.region, args.scale, args.out, args.clean, args.symbols)


if __name__ == "__main__":
    main()
