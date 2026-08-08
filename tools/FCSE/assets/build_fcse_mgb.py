#!/usr/bin/env python3
"""Generate `fcse.mgb.xml` - the Magma UI package that gives FCSE its own settings page.

Why this exists
---------------
A private page cannot borrow a shipped layout: it would share that layout's `magma::Page` with the
stock class that also binds it, and the two screens become one screen (see PLAN-own-page.md, work
item 0.5). A layout nothing else binds is the only way a private page can own its own widget tree.

`CUIPageBase::Init` resolves a page by `FindGenericObject(Id::Hash(pageName))` against the global
registry that `magma::Engine::LoadPackage` populates from every loaded package's GenericObjectTable.
So a one-area package with a one-entry GenericObjectTable is the whole trick.

What the page contains
----------------------
Nothing of its own - no materials, no fonts, no textures. Every visual is a `PageInstance` pointing
into `common.mgb`, which is always loaded by the time the Options screen appears:

  * `p_menu_nav`       -> common area 36150990 - the row list + title bar. `FetchMagmaElements`
                          requires exactly this name, and requires `l_menu_nav_list` to sit *inside*
                          it rather than be a direct child.
  * `p_slot_NN`        -> common area 652FD37C - one value-list cell, the control half of a settings
                          row. `CSettingsPage::AddBoolSetting(label, "SETTING_LABEL_LIST",
                          "FCSE_SLOT_NN", ...)` binds one of these per row.
  * `p_prompts_navbar` -> common area E58F0F6C - the B/Back prompt strip.

Row count
---------
Capped at the nav list's own viewport. `common.mgb` area 36150990's ListBox declares 20 visible
rows, and the value-control `PageInstance`s are absolutely positioned siblings that do *not* scroll
with the list - so past 20 the labels would slide out from under their controls. 20 rows at the
Network page's geometry ends at y=690 on a 768px page, which fits.

Geometry is copied from the Network tab (the highest-anchored stock settings page, hence the most
rows): nav at (83,111), controls at x=552 starting y=158, stepping 28.

Usage
-----
    python build_fcse_mgb.py <options.xml> [-o fcse.mgb.xml]

where <options.xml> is `jackall mgb decode` output for any shipped `options.mgb`. It is read for two
things only - the 166-entry type table and the pool pre-reservation counts, both build-wide
constants. The emitted XML is self-contained after that: rebuilding the binary needs only

    jackall mgb encode fcse.mgb.xml -o fcse.mgb
"""
from __future__ import annotations

import argparse
import zlib
import xml.etree.ElementTree as ET

PACKAGE = "fcse"
PAGE = "FCSE_PAGE"          # <=15 chars so the page name lands in MSVC's SSO buffer
PAGE_ALIAS = "MAINMENU_FCSE_PAGE_PC"   # stock naming convention, registered as a second key

GAME_PAGE_AREA = "#C16854EF"   # MAINMENU_OPTIONGAME_PAGE_PC's area, the structural template

# common.mgb templates. Only p_prompts_navbar's area has a recovered name; the rest stay hashes.
COMMON = "common"
NAV_LIST_AREA = "#36150990"
NAV_LIST_WIDGET = "l_menu_nav_list"
VALUE_CELL_AREA = "#652FD37C"
VALUE_CELL_WIDGET = "#D240E092"
PROMPTS_AREA = "p_prompts_navbar"

SLOT_COUNT = 20             # == the nav ListBox's BUTTONCOUNT viewport

# Geometry is measured from the Network tab in whatever options.mgb is passed in, never hardcoded:
# the 4:3 and widescreen packages differ (PAGESIZE 1024x768 vs 1280x800, and the nav list sits at
# x=83 vs x=74), so the same generator has to produce a correct package for either.
#
# The Network tab is the reference because it is the highest-anchored stock settings page, which
# leaves room for the most rows before they run off the bottom.
NETWORK_PAGE_AREA = "#400736ED"
ROW_STEP = 28

# The options-screen frame: the elements every one of the four shipped settings pages carries
# (Game, Display, Sound, Network), so they are page chrome rather than Game-specific. Each links
# only into common.mgb - the two Images through `MATERIALLINK ... PACKAGE="\common.mgb"` - so
# carrying them costs no material, texture or font of our own. Names are hashes because no
# recovered string maps to them; they are copied verbatim and never referenced by FCSE.
CHROME_BEFORE = ["#B21251FF", "#684AC59C", "#F0CC8C29"]   # backdrop, frame, notebook paper
CHROME_MID = "#5B36589B"                                   # sits between the rows and the prompts
CHROME_AFTER = "#E82DE1C0"                                 # modulate-blended scratch frame, drawn last

# The two chrome Images reference materials rather than areas. The shipped *options* pages reach
# across to `\common.mgb` for them, which does not resolve from a package the engine did not ship -
# but that is not the pattern to copy anyway. Every page inside common.mgb that draws these layers
# declares the material in its *own* package (`PACKAGE=""`), so that is what this does too.
#
# The texture paths are UI-root-relative, exactly as common.mgb stores them. That only resolves
# because magma_package.cpp names this package "UI\fcse.mgb" rather than its real absolute path -
# an earlier attempt with the absolute path made both images render as untextured white quads.
LOCAL_MATERIALS = [
    ("notebook", "\\textures\\hud\\notebook.png"),
    ("frame_color_scratch", "\\textures\\common\\frame_color_scratch.png"),
]


def find_area(root: ET.Element, name: str) -> ET.Element:
    for area in root.find("CHILDREN").findall("Area"):
        if area.find("USERDATA").get("name") == name:
            return area
    raise SystemExit(f"area {name} not found - is this really an options.mgb export?")


def hashed(name: str) -> str:
    """The `#XXXXXXXX` form of a name, which is how an export spells one it could not recover.

    `magma::Id::Hash` is a plain CRC-32 of the bare ASCII name, so this is the same value the
    encoder will compute for a name written out in full.
    """
    if name.startswith("#"):
        return name
    return f"#{zlib.crc32(name.encode('ascii')) & 0xFFFFFFFF:08X}"


def element_named(area: ET.Element, name: str) -> ET.Element:
    """Finds an element by authored name, whether the export spelled it or hashed it."""
    wanted = {name, hashed(name)}
    for el in area.find("CHILDREN").findall("Element"):
        if el.find("USERDATA").get("name") in wanted:
            return el
    raise SystemExit(f"element {name} ({hashed(name)}) not found in the template area")


def instance_element(template: ET.Element, name: str, target_area: str,
                     pos: tuple[int, int], keyframe_name: str) -> ET.Element:
    """A copy of a shipped PageInstance element, renamed, repositioned and re-targeted."""
    el = ET.fromstring(ET.tostring(template))
    el.find("USERDATA").set("name", name)

    keyframes = el.findall("KEYFRAMES/Keyframe")
    keyframes[0].set("name", keyframe_name)
    state = keyframes[0].find("ScaleState")
    state.set("POSITION.x", str(pos[0]))
    state.set("POSITION.y", str(pos[1]))

    el.find("PageInstance/LINK").set("AREA", target_area)
    return el


def full_link(slot: str, last_type: str, ids: list[str]) -> ET.Element:
    return ET.Element("LINK", {"slot": slot, "LASTOBJECTTYPE": last_type, "IDS": " ".join(ids)})


def measure_geometry(options_root: ET.Element) -> tuple[tuple[int, int], int, int]:
    """Nav-list position and first row position, read off the Network tab of this very package."""
    network = find_area(options_root, NETWORK_PAGE_AREA)
    nav, rows = None, []
    for el in network.find("CHILDREN").findall("Element"):
        instance = el.find("PageInstance")
        state = el.find("KEYFRAMES/Keyframe/ScaleState")
        if instance is None or state is None:
            continue
        link = instance.find("LINK")
        if link is None:
            continue
        pos = (int(state.get("POSITION.x")), int(state.get("POSITION.y")))
        if link.get("AREA") == NAV_LIST_AREA:
            nav = pos
        elif link.get("AREA") == VALUE_CELL_AREA:
            rows.append(pos)
    if nav is None or not rows:
        raise SystemExit("could not measure the Network tab's geometry from this options.mgb")
    rows.sort(key=lambda p: p[1])
    return nav, rows[0][0], rows[0][1]


def build(options_root: ET.Element) -> ET.Element:
    game = find_area(options_root, GAME_PAGE_AREA)
    nav_pos, row_x, row_y0 = measure_geometry(options_root)

    root = ET.Element("MagmaPackage", {
        "sentinel": options_root.get("sentinel"),
        "version": options_root.get("version"),
        "flag": options_root.get("flag"),
        # Pool pre-reservation. Copied wholesale from options.mgb: these size the Allocate*PoolChunk
        # sweep and have no effect on any file offset, so over-reserving for a 23-element package is
        # harmless while under-reserving would not be.
        "POOLCOUNTS": options_root.get("POOLCOUNTS"),
        "PAGESIZE.w": options_root.get("PAGESIZE.w"),
        "PAGESIZE.h": options_root.get("PAGESIZE.h"),
        "DISPLAYOFFSET.x": options_root.get("DISPLAYOFFSET.x"),
        "DISPLAYOFFSET.y": options_root.get("DISPLAYOFFSET.y"),
        "DEFAULTMATERIAL": "",
    })

    # The type table is a build-wide constant; keeping options.mgb's verbatim means every `slot=`
    # copied out of a shipped file below keeps its meaning.
    root.append(ET.fromstring(ET.tostring(options_root.find("TYPES"))))

    package_userdata = ET.SubElement(root, "USERDATA", {"name": PACKAGE})
    ET.SubElement(package_userdata, "PROPERTIES")

    # materialExtra is the number of *distinct* textures among the materials, which is why it is
    # sometimes lower than the material count in shipped packages. Ours are one texture each.
    materials = ET.SubElement(root, "MATERIALS",
                              {"materialExtra": str(len({t for _, t in LOCAL_MATERIALS}))})
    for name, texture in LOCAL_MATERIALS:
        ET.SubElement(materials, "Material",
                      {"name": name, "texture": texture, "REGION": "0 0 1 1"})
    for empty in ("FONTSUBSTS", "FONTS", "FONTFAMILIES"):
        ET.SubElement(root, empty)

    children = ET.SubElement(root, "CHILDREN")
    area = ET.SubElement(children, "Area", {
        "slot": game.get("slot"), "type": "Page",
        "FRAMERATE": game.get("FRAMERATE"), "CURRENTFRAME": "0",
        "STATICBOX": "0 0 0 0", "SINGLE_GLOBAL_SELECTION": "true",
    })

    # --- the page's UserData: the names CSettingsPage resolves controls through -------------
    userdata = ET.SubElement(area, "USERDATA", {"name": PAGE})
    properties = ET.SubElement(userdata, "PROPERTIES")
    ET.SubElement(properties, "PROPERTY", {"key": "LAYER", "type": "2", "value": "10"})

    def add_link(key: str, element: str, target_area: str, widget: str) -> None:
        prop = ET.SubElement(properties, "PROPERTY", {"key": key, "type": "18"})
        prop.append(full_link("66", "Focusable", [PACKAGE, PAGE, element, target_area, widget]))

    add_link("SETTING_LABEL_LIST", "p_menu_nav", NAV_LIST_AREA, NAV_LIST_WIDGET)
    for i in range(1, SLOT_COUNT + 1):
        add_link(f"FCSE_SLOT_{i:02}", f"p_slot_{i:02}", VALUE_CELL_AREA, VALUE_CELL_WIDGET)

    # --- the elements ------------------------------------------------------------------------
    # Order is draw order, and is kept identical to the stock settings pages.
    elements = ET.SubElement(area, "CHILDREN")

    def carry(name: str) -> ET.Element:
        """A stock element copied verbatim. Everything in CHROME_* appears on all four shipped
        settings pages and links only into common.mgb, so copying costs no new dependency."""
        return ET.fromstring(ET.tostring(element_named(game, name)))

    # Every stock page carries this anchor. The Game tab's copy also pushes the Display page from
    # its second keyframe; that action is dropped here so the placeholder does nothing.
    action = carry("action")
    for keyframe in action.findall("KEYFRAMES/Keyframe"):
        for executer in keyframe.findall("ACTIONEXECUTER"):
            keyframe.remove(executer)
    elements.append(action)

    for name in CHROME_BEFORE:
        elements.append(carry(name))

    nav_template = element_named(game, "p_menu_nav")
    elements.append(instance_element(nav_template, "p_menu_nav", NAV_LIST_AREA, nav_pos, "kf_menu_nav"))

    slot_template = element_named(game, "#6AB5DDF2")   # any of the Game tab's value-cell rows
    for i in range(1, SLOT_COUNT + 1):
        elements.append(instance_element(
            slot_template, f"p_slot_{i:02}", VALUE_CELL_AREA,
            (row_x, row_y0 + ROW_STEP * (i - 1)), f"kf_slot_{i:02}"))

    elements.append(carry(CHROME_MID))

    prompts = carry("p_prompts_navbar")
    prompts.find("PageInstance/LINK").set("AREA", PROMPTS_AREA)
    elements.append(prompts)

    elements.append(carry(CHROME_AFTER))

    # Initial focus. Every stock settings page points this at p_menu_nav; without it the page opens
    # with nothing selected and the first D-pad press goes nowhere.
    defaults = ET.SubElement(area, "DEFAULT_ELEMENTS")
    ET.SubElement(defaults, "DEFAULT_ELEMENT", {"CONTROLLER": "255", "ID": "p_menu_nav"})

    # Elements copied out of options.mgb carry `common` as the bare hash the exporter fell back to,
    # because a lookup seeded from that package has no way to recover the string. Spelling it out
    # costs nothing - the encoder hashes it back to the same value - and keeps the committed source
    # readable.
    for link in root.iter("LINK"):
        if link.get("PACKAGE") == hashed(COMMON):
            link.set("PACKAGE", COMMON)

    # Point the chrome Images at this package's own materials. An empty PACKAGE means "the package
    # this element lives in" - the same thing every common.mgb page does for these two layers.
    for material_link in root.iter("MATERIALLINK"):
        if material_link.get("PACKAGE") == "\\common.mgb":
            material_link.set("PACKAGE", "")

    # --- the registry entry that makes the page findable by name -----------------------------
    table = ET.SubElement(root, "GENERICOBJECTTABLE", {"name": PACKAGE})
    objects = ET.SubElement(table, "GENERICOBJECTS")
    for key in (PAGE, PAGE_ALIAS):
        obj = ET.SubElement(objects, "GENERICOBJECT", {"name": key})
        obj.append(full_link("101", "Page", [PACKAGE, PAGE]))

    return root


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("options_xml", help="`jackall mgb decode` output for a shipped options.mgb")
    parser.add_argument("-o", "--out", default="fcse.mgb.xml")
    args = parser.parse_args()

    root = build(ET.parse(args.options_xml).getroot())
    ET.indent(root, space="  ")
    ET.ElementTree(root).write(args.out, encoding="utf-8", xml_declaration=True)

    page = root.get("PAGESIZE.w") + "x" + root.get("PAGESIZE.h")
    nav, row_x, row_y0 = measure_geometry(ET.parse(args.options_xml).getroot())
    print(f"wrote {args.out}: page {page}, {SLOT_COUNT} value slots, "
          f"nav@{nav}, rows x={row_x} y={row_y0}+{ROW_STEP}n")


if __name__ == "__main__":
    main()
