#!/usr/bin/env python3
"""Check `fcse.mgb.xml` says what FCSE's native code will ask it for.

Round-tripping proves the bytes are well-formed; it says nothing about whether the names line up.
A `FullLink` naming an element that does not exist is a perfectly valid package that produces a
page with no controls on it, which is exactly the failure this catches.

    python verify_fcse_mgb.py fcse.mgb.xml [options.xml]

Passing the shipped options.xml additionally reports which Game-tab elements were left out, so
anything decorative that turns out to matter is visible rather than forgotten.
"""
from __future__ import annotations

import sys
import zlib
import xml.etree.ElementTree as ET

PAGE_NAME = "FCSE_PAGE"
SLOT_COUNT = 20  # must match build_fcse_mgb.py and kSlotCount in fcse_page.cpp


def h(name: str) -> str:
    if name.startswith("#"):
        return name
    return f"#{zlib.crc32(name.encode('ascii')) & 0xFFFFFFFF:08X}"


def main() -> int:
    root = ET.parse(sys.argv[1]).getroot()
    failures: list[str] = []

    def check(ok: bool, message: str) -> None:
        print(f"  {'PASS' if ok else 'FAIL'}  {message}")
        if not ok:
            failures.append(message)

    areas = root.find("CHILDREN").findall("Area")
    check(len(areas) == 1, f"exactly one area (found {len(areas)})")
    area = areas[0]
    check(area.get("type") == "Page", f"the area is a Page (found {area.get('type')})")

    area_name = area.find("USERDATA").get("name")
    check(h(area_name) == h(PAGE_NAME), f"the page area is named {PAGE_NAME}")

    elements = {h(el.find("USERDATA").get("name")): el
                for el in area.find("CHILDREN").findall("Element")}

    print("\nGenericObjectTable - what CUIPageBase::Init resolves against:")
    entries = root.findall(".//GENERICOBJECT")
    check(len(entries) >= 1, f"at least one registry entry (found {len(entries)})")
    for entry in entries:
        ids = entry.find("LINK").get("IDS").split()
        target_ok = len(ids) == 2 and h(ids[1]) == h(PAGE_NAME)
        check(target_ok, f"{entry.get('name')} -> {' '.join(ids)}")
    check(any(h(e.get("name")) == h(PAGE_NAME) for e in entries),
          f"a key equal to Id::Hash(\"{PAGE_NAME}\") exists, so a page named "
          f"{PAGE_NAME} resolves")

    print("\nUserData links - what CSettingsPage binds controls through:")
    props = area.find("USERDATA/PROPERTIES").findall("PROPERTY")
    links = [(p.get("key"), p.find("LINK")) for p in props if p.find("LINK") is not None]
    check(any(h(k) == h("SETTING_LABEL_LIST") for k, _ in links), "SETTING_LABEL_LIST is declared")

    for key, link in links:
        ids = link.get("IDS").split()
        problems = []
        if h(ids[1]) != h(PAGE_NAME):
            problems.append(f"second id is {ids[1]}, not this page")
        if h(ids[2]) not in elements:
            problems.append(f"element {ids[2]} does not exist in the area")
        check(not problems, f"{key} -> {ids[2]} " + ("; ".join(problems) or "resolves"))

    value_slots = [k for k, _ in links if k.startswith("FCSE_SLOT_")]
    slider_slots = [(k, link) for k, link in links if k.startswith("FCSE_SLIDER_")]
    print()
    check(len(value_slots) == SLOT_COUNT,
          f"{SLOT_COUNT} value-cell slots declared (found {len(value_slots)})")
    check(len(slider_slots) == SLOT_COUNT,
          f"{SLOT_COUNT} slider slots declared (found {len(slider_slots)})")

    # The slider bank has to start hidden: an unbound value cell is an empty ListBox and draws
    # nothing, but an unbound Slider would still draw its track at every row FCSE did not use.
    visible = [k for k, link in slider_slots
               if elements.get(h(link.get("IDS").split()[2]), ET.Element("x")).get("HIDDEN") != "true"]
    check(not visible, "every slider cell is authored HIDDEN" +
          (f" (visible: {', '.join(visible)})" if visible else ""))

    unaccounted = [k for k, _ in links
                   if h(k) != h("SETTING_LABEL_LIST") and not k.startswith("FCSE_SLOT_")
                   and not k.startswith("FCSE_SLIDER_")]
    check(not unaccounted, "no unrecognised slot properties" +
          (f" (found: {', '.join(unaccounted)})" if unaccounted else ""))

    if len(sys.argv) > 2:
        print("\nGame-tab elements not carried over (decoration unless proven otherwise):")
        options = ET.parse(sys.argv[2]).getroot()
        for other in options.find("CHILDREN").findall("Area"):
            if other.find("USERDATA").get("name") != "#C16854EF":
                continue
            for el in other.find("CHILDREN").findall("Element"):
                name = h(el.find("USERDATA").get("name"))
                if name in elements:
                    continue
                widget = el.get("type")
                link = el.find(f"{widget}/LINK")
                target = link.get("AREA") if link is not None else "-"
                print(f"    {name}  {widget:14} -> {target}")

    print()
    if failures:
        print(f"{len(failures)} check(s) FAILED")
        return 1
    print("all checks passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
