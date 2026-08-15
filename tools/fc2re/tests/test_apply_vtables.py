# Planning tests for apply_vtables, run without a JVM.
#
#   python tools/fc2re/tests/test_apply_vtables.py

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from apply_vtables import (field_name, plan, split_scopes, type_name,
                           vptr_field)


def sub(offset_to_top, names):
    return {"offset_to_top": offset_to_top,
            "functions": [{"index": i, "offset": i * 4, "name": n}
                          for i, n in enumerate(names)]}


def test_split_scopes_ignores_colons_inside_templates():
    assert split_scopes("CFoo") == ["CFoo"]
    assert split_scopes("Echo::CNet") == ["Echo", "CNet"]
    # The nested :: belongs to the template argument, not the scope chain.
    assert split_scopes(
        "CryVector<CEntitySystem::DeathRowCell,NoLock>::CodeObject"
    ) == ["CryVector<CEntitySystem::DeathRowCell,NoLock>", "CodeObject"]
    assert split_scopes("A<B<C::D>>::E") == ["A<B<C::D>>", "E"]


def test_type_name_distinguishes_subobject_tables():
    assert type_name("CFoo", 0) == "CFoo_vtable"
    assert type_name("CFoo", -8) == "CFoo_vtable_at_8"
    assert type_name("Echo::CNet", 0) == "Echo::CNet_vtable"


def test_vptr_offset_is_the_negated_offset_to_top():
    assert vptr_field(0) == ("vptr", 0)
    assert vptr_field(-8) == ("vptr_8", 8)
    assert vptr_field(-0x54) == ("vptr_54", 0x54)


def test_field_name_strips_the_class_scope():
    taken = set()
    assert field_name("CFoo::Update", taken) == "Update"


def test_both_destructor_entries_survive():
    # Itanium emits the complete-object and deleting destructors as separate
    # slots, both demangling to ~CFoo.
    taken = set()
    a = field_name("CFoo::~CFoo", taken)
    b = field_name("CFoo::~CFoo", taken)
    assert a == "dtor_CFoo"
    assert b == "dtor_CFoo_2"


def test_field_name_never_starts_with_a_digit():
    taken = set()
    assert not field_name("9Weird", taken)[0].isdigit()


def test_plan_covers_every_subobject_table():
    row = {"class": "CFoo", "subobjects": [
        sub(0, ["CFoo::A", "CFoo::B"]),
        sub(-8, ["CFoo::C"]),
    ]}
    got = plan(row)
    assert [s["vptr_offset"] for s in got] == [0, 8]
    assert [s["type_name"] for s in got] == ["CFoo_vtable",
                                             "CFoo_vtable_at_8"]
    assert got[0]["slots"] == ["A", "B"]


def test_plan_skips_unnamed_classes_and_empty_tables():
    assert plan({"class": None, "subobjects": [sub(0, ["X::Y"])]}) == []
    assert plan({"class": "CFoo", "subobjects": [sub(0, [])]}) == []


def test_plan_rejects_absurdly_large_tables():
    row = {"class": "CFoo",
           "subobjects": [sub(0, ["CFoo::M%d" % i for i in range(5000)])]}
    assert plan(row) == []


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

