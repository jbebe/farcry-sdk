# Tests for apply_inheritance, run without a JVM.
#
#   python tools/fc2re/tests/test_inheritance.py

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from apply_inheritance import (base_edges, inherited_members, own_members,
                               plan_inherited)

STRID = ("_ZTV14CGenericMemberI4Bark9CStringID"
         "18GenericTypeHandlerIS1_ELj3EE")


def ti(symbol, cls, bases=()):
    return {"symbol": symbol, "ghidra_class": cls,
            "bases": [{"typeinfo": b, "offset": o} for b, o in bases]}


def prop(owner, name, offset, kind="CGenericMember"):
    return {"owner": owner, "name": name, "offset": offset, "kind": kind,
            "handler_symbol": STRID, "flags": 0, "index": 0}


def test_base_edges_use_ghidra_spelling():
    records = [ti("_ZTI1A", "A"), ti("_ZTI1B", "B", [("_ZTI1A", 0)])]
    assert base_edges(records) == {"B": [("A", 0)]}


def test_inherited_members_shift_by_the_base_offset():
    edges = {"B": [("A", 0x10)]}
    own = own_members([prop("A", "Health", 0), prop("A", "Armour", 4)], set())
    got = plan_inherited("B", edges, own, {})
    assert [(m["offset"], m["name"]) for m in got] == [(0x10, "Health"),
                                                       (0x14, "Armour")]
    assert all(m["from"] == "A" for m in got)


def test_offsets_accumulate_through_a_chain():
    edges = {"B": [("A", 0x10)], "C": [("B", 0x20)]}
    own = own_members([prop("A", "Health", 4)], set())
    got = plan_inherited("C", edges, own, {})
    assert [(m["offset"], m["name"]) for m in got] == [(0x34, "Health")]


def test_own_members_take_precedence_over_inherited():
    edges = {"B": [("A", 0)]}
    own = own_members([prop("A", "Shared", 8), prop("A", "OnlyBase", 12),
                       prop("B", "Shared", 8)], set())
    got = plan_inherited("B", edges, own, {})
    assert [m["name"] for m in got] == ["OnlyBase"]


def test_nearest_ancestor_wins_a_shared_offset():
    edges = {"C": [("B", 0)], "B": [("A", 0)]}
    own = own_members([prop("A", "FromA", 4), prop("B", "FromB", 4)], set())
    got = plan_inherited("C", edges, own, {})
    assert [m["name"] for m in got] == ["FromB"]


def test_multiple_bases_are_placed_at_their_own_offsets():
    edges = {"C": [("A", 0), ("B", 0x40)]}
    own = own_members([prop("A", "FromA", 0), prop("B", "FromB", 4)], set())
    got = plan_inherited("C", edges, own, {})
    assert [(m["offset"], m["name"]) for m in got] == [(0, "FromA"),
                                                       (0x44, "FromB")]


def test_polymorphic_base_contributes_nothing_at_offset_zero():
    edges = {"B": [("A", 0)]}
    own = own_members([prop("A", "Bogus", 0), prop("A", "Real", 4)], {"A"})
    got = plan_inherited("B", edges, own, {})
    assert [m["name"] for m in got] == ["Real"]


def test_cycle_does_not_hang():
    edges = {"A": [("B", 0)], "B": [("A", 0)]}
    own = own_members([prop("A", "X", 4)], set())
    assert isinstance(inherited_members("A", edges, own, {}), list)


def test_class_with_no_bases_inherits_nothing():
    own = own_members([prop("A", "X", 0)], set())
    assert plan_inherited("A", {}, own, {}) == []


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

