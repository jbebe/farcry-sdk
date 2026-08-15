# Layout-planning tests for apply_properties, run without a JVM.
#
#   python tools/fc2re/tests/test_plan_members.py

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from apply_properties import (member_type_from_handler, plan_members,
                              read_mangled_name, slot_size)

BOOL_H = "_ZTV14CGenericMemberI4Barkb18GenericTypeHandlerIbELj3EE"
STRID_H = ("_ZTV14CGenericMemberI4Bark9CStringID"
           "18GenericTypeHandlerIS1_ELj3EE")
VEC_H = ("_ZTV16CContainerMemberI4Bark9CryVectorI9BarkState6NoLock"
         "19CryVectorPropertiesILj32ELj5ELj26EEE23GenericContainerHandler"
         "I19NomadVectorProviderIS0_S6_E18NoChildTypeHandlerELj3ELb1EE")


def row(name, offset, handler=STRID_H, kind="CGenericMember", index=0,
        flags=0):
    return {"name": name, "offset": offset, "handler_symbol": handler,
            "kind": kind, "index": index, "flags": flags}


def test_reads_length_prefixed_name():
    assert read_mangled_name("4Bark9CStringID", 0) == ("Bark", 5)
    assert read_mangled_name("9CStringID", 0) == ("CStringID", 10)
    assert read_mangled_name("nope", 0) == (None, 0)


def test_builtin_member_type_is_sized():
    assert member_type_from_handler(BOOL_H) == ("bool", 1)


def test_class_member_type_is_named_but_unsized():
    assert member_type_from_handler(STRID_H) == ("CStringID", None)
    assert member_type_from_handler(VEC_H) == ("CryVector", None)


def test_member_type_rejects_non_vtable_symbols():
    assert member_type_from_handler(None) == (None, None)
    assert member_type_from_handler("vtable") == (None, None)


def test_slot_size_respects_type_then_gap():
    assert slot_size(1, 4) == 1
    assert slot_size(None, 4) == 4
    assert slot_size(None, 2) == 2
    assert slot_size(None, 1) == 1
    assert slot_size(None, 3) == 2
    assert slot_size(4, None) == 4
    assert slot_size(8, None) == 4


def test_bool_field_does_not_overrun_its_neighbour():
    members, _ = plan_members([row("Flag", 0, BOOL_H), row("Next", 1, BOOL_H)])
    assert [(m["offset"], m["size"]) for m in members] == [(0, 1), (1, 1)]


def test_gap_caps_slot_size_for_unknown_types():
    members, _ = plan_members([row("A", 0), row("B", 2)])
    assert [(m["offset"], m["size"]) for m in members] == [(0, 2), (2, 4)]


def test_polymorphic_class_keeps_offset_zero_for_the_vptr():
    rows = [row("Bogus", 0), row("Real", 4)]
    poly, skipped = plan_members(rows, polymorphic=True)
    assert [m["name"] for m in poly] == ["Real"]
    assert any("vptr" in why for _, why in skipped)
    plain, _ = plan_members(rows, polymorphic=False)
    assert [m["name"] for m in plain] == ["Bogus", "Real"]


def test_storageless_kinds_never_occupy_offset_zero():
    # CGroupMember, CConditionalGroupMember and CEnumMember report offset 0
    # in 100% of cases, CVirtualMember in 158 of 161; placing any of them
    # would corrupt the start of the class.
    for kind in ("CGroupMember", "CConditionalGroupMember", "CVirtualMember",
                 "CSerializationEvent", "CContainedMember", "CEnumMember"):
        members, skipped = plan_members([row("Bogus", 0, kind=kind)])
        assert members == [], kind
        assert len(skipped) == 1, kind


def test_real_field_still_placed_when_a_wrapper_shares_offset_zero():
    members, _ = plan_members([
        row("Wrapper", 0, kind="CGroupMember"),
        row("Real", 0, kind="CGenericMember"),
    ])
    assert [m["name"] for m in members] == ["Real"]


def test_array_elements_collapse_to_index_zero_with_siblings_named():
    members, skipped = plan_members([
        row("RedArmy", 180, kind="COffsetMember", flags=1),
        row("BlueArmy", 180, kind="COffsetMember", flags=0),
        row("GreyArmy", 180, kind="COffsetMember", flags=2),
    ])
    assert len(members) == 1
    assert members[0]["name"] == "BlueArmy"
    assert members[0]["siblings"] == ["RedArmy", "GreyArmy"]
    assert all("array element 0 of 3" == why for _, why in skipped)


def test_rows_without_offset_are_dropped():
    members, _ = plan_members([row("Ev", None), row("Real", 0)])
    assert [m["name"] for m in members] == ["Real"]


def test_duplicate_names_are_disambiguated():
    members, _ = plan_members([row("Same", 0), row("Same", 4)])
    assert [m["name"] for m in members] == ["Same", "Same_2"]


def test_members_come_back_in_offset_order():
    members, _ = plan_members([row("C", 8), row("A", 0), row("B", 4)])
    assert [m["name"] for m in members] == ["A", "B", "C"]


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

