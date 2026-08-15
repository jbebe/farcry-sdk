# Tests for derive_size_floors, run without a JVM.
#
#   python tools/fc2re/tests/test_size_floors.py

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from derive_size_floors import (base_edges, member_floors, merge, solve,
                                vptr_floors)


def ti(symbol, cls, bases=()):
    return {"symbol": symbol, "ghidra_class": cls,
            "bases": [{"typeinfo": b, "offset": o} for b, o in bases]}


def test_base_edges_resolve_through_typeinfo_symbols():
    records = [ti("_ZTI4CBase", "CBase"),
               ti("_ZTI7CDerived", "CDerived", [("_ZTI4CBase", 0)])]
    assert base_edges(records) == {"CDerived": [("CBase", 0)]}


def test_base_edges_ignore_self_reference():
    assert base_edges([ti("_ZTI1A", "A", [("_ZTI1A", 0)])]) == {}


def test_base_edges_skip_unknown_bases():
    assert base_edges([ti("_ZTI1A", "A", [("_ZTI9Missing", 0)])]) == {}


def test_derived_is_at_least_base_offset_plus_base_size():
    edges = {"CDerived": [("CBase", 0)]}
    size = solve({"CBase": 0x40}, {}, edges)
    assert size["CDerived"] == 0x40


def test_second_base_at_an_offset_pushes_the_bound_further():
    edges = {"C": [("A", 0), ("B", 0x40)]}
    size = solve({"A": 0x40, "B": 0x20}, {}, edges)
    assert size["C"] == 0x60


def test_bounds_propagate_through_a_chain():
    edges = {"B": [("A", 0)], "C": [("B", 0)], "D": [("C", 0x10)]}
    size = solve({"A": 0x30}, {}, edges)
    assert size["D"] == 0x40


def test_allocation_size_wins_when_larger_than_the_bound():
    edges = {"CDerived": [("CBase", 0)]}
    size = solve({"CBase": 0x40, "CDerived": 0x100}, {}, edges)
    assert size["CDerived"] == 0x100


def test_solver_terminates_on_a_cycle():
    edges = {"A": [("B", 4)], "B": [("A", 4)]}
    size = solve({"A": 8}, {}, edges)
    assert size["A"] >= 8


def test_vptr_floor_from_a_secondary_subobject_table():
    rows = [{"ghidra_class": "CFoo", "subobjects": [
        {"offset_to_top": 0}, {"offset_to_top": -0x54}]}]
    assert vptr_floors(rows) == {"CFoo": 0x58}


def test_member_floor_uses_the_last_member():
    rows = [{"owner": "CFoo", "offset": 0}, {"owner": "CFoo", "offset": 0x3C},
            {"owner": "CFoo", "offset": None}]
    assert member_floors(rows) == {"CFoo": 0x40}


def test_merge_marks_derived_bounds_as_not_exact():
    rows = merge([{"class": "A", "size": 0x40, "sites": 3}],
                 {"A": 0x40, "B": 0x20}, {"A"})
    by = {r["class"]: r for r in rows}
    assert by["A"]["exact"] is True
    assert by["B"]["exact"] is False
    assert by["B"]["size"] == 0x20


def test_merge_never_shrinks_an_allocation_size():
    rows = merge([{"class": "A", "size": 0x100, "sites": 2}],
                 {"A": 0x40}, {"A"})
    assert rows[0]["size"] == 0x100


def main():
    tests = [n for n in globals() if n.startswith("test_")]
    failed = 0
    for name in sorted(tests):
        try:
            globals()[name]()
            print("ok   %s" % name)
        except Exception as e:
            failed += 1
            print("FAIL %s: %s" % (name, e))
    print("\n%d passed, %d failed" % (len(tests) - failed, failed))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())

