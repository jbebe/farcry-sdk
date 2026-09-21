# Logic tests for dump_properties, run without a JVM.
#
#   python tools/fc2re/tests/test_parse_registrations.py
#   python -m pytest tools/fc2re/tests          (if pytest is installed)
#
# The event lists mirror what events_for() reads out of real FarCry2_server
# registrars, trimmed to the members each test is about.

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from dump_properties import (OFF_NAME,
                             OFF_OFFSET, OFF_VPTR, assemble, describe_handler,
                             member_pointer, split_template)

VT = "CGenericMember<CRigidPhysComponent,bool,GenericTypeHandler<bool>,3u>"


def vtable(demangled, addr="0xa000000"):
    return ("vtable", demangled, "_ZTV_" + demangled[:8], addr)


def member(mid, name, demangled, offset=None, **slots):
    events = [("alloc", mid, 0x14),
              ("slot", mid, OFF_VPTR, vtable("CMemberBase")),
              ("slot", mid, OFF_NAME, ("str", name)),
              ("slot", mid, OFF_VPTR, vtable(demangled))]
    if offset is not None:
        events.append(("slot", mid, OFF_OFFSET, ("int", offset)))
    for off, value in slots.items():
        events.append(("slot", mid, int(off[1:], 16), value))
    return events


def rows_of(events, owner="CRigidPhysComponent"):
    rows, klass = assemble(events, owner, owner + "::RegisterProperties", "0")
    return {r["name"]: r for r in rows}, klass


def test_split_template_respects_nesting():
    assert split_template("A<B,C<D,E>,3u>") == ("A", ["B", "C<D,E>", "3u"])
    assert split_template("CEnumMember") == ("CEnumMember", [])
    assert split_template(None) == (None, [])


def test_describe_generic_member():
    assert describe_handler(VT) == {"kind": "CGenericMember", "value_type": "bool",
                                    "handler": "GenericTypeHandler<bool>", "flags": 3,
                                    "wrapped": None}


def test_container_flags_are_the_last_numeric_argument_not_the_bool():
    d = describe_handler("CContainerMember<CX,CryVector<unsigned_int,NoLock,"
                         "CryVectorProperties<32u,5u,26u>>,GenericContainerHandler<P>,3u,true>")
    assert d["kind"] == "CContainerMember"
    assert d["value_type"].startswith("CryVector<unsigned_int")
    assert d["flags"] == 3
    assert d["wrapped"] is True


def test_group_kinds_carry_flags_but_no_value_type():
    assert describe_handler("CGroupMember<true,3u>") == {
        "kind": "CGroupMember", "value_type": None, "handler": None, "flags": 3,
        "wrapped": None}
    d = describe_handler("CConditionalGroupMember<CGraphicComponent,false,1u>")
    assert (d["kind"], d["value_type"], d["flags"]) == ("CConditionalGroupMember", None, 1)
    assert describe_handler("CEnumMember")["flags"] is None


def test_member_pointer_decodes_itanium_virtual_offsets():
    assert member_pointer(("func", "CX::GetFoo")) == "CX::GetFoo"
    assert member_pointer(("int", 0xAD)) == "virtual+0xac"
    assert member_pointer(("int", 0)) is None
    assert member_pointer(None) is None


def test_last_vptr_store_wins_over_the_base_constructor():
    rows, _ = rows_of(member(0, "bDisabledAtStart", VT, 0x140) + [("push", 0, None)])
    r = rows["bDisabledAtStart"]
    assert (r["kind"], r["value_type"], r["offset"], r["flags"]) == ("CGenericMember", "bool", 0x140, 3)
    assert r["complete"] and r["parent"] is None


def test_enum_labels_attach_to_the_enum_pushed_before_them():
    events = (member(0, "selCollisionLayer", "CGenericMember<CX,unsigned_int,"
                     "BasicTypeHandlerEnum<unsigned_int>,3u>", 0x11C)
              + [("push", 0, None)]
              + member(1, "enumCollisionLayer", "CEnumMember", 0)
              + [("push", 1, None), ("label", "Static"), ("label", "Dynamic")]
              + member(2, "Stim", VT, 0xFC)
              + [("push", 2, None), ("label", "CTriggerSimpleEvent")])
    rows, _ = rows_of(events)
    assert rows["enumCollisionLayer"]["labels"] == ["Static", "Dynamic"]
    assert rows["selCollisionLayer"]["labels"] is None
    assert rows["Stim"]["labels"] is None


def test_members_pushed_into_a_group_name_it_as_parent():
    events = (member(0, "Ambient", "CConditionalGroupMember<CGraphicComponent,false,2u>",
                     0, x18=("func", "CGraphicComponent::HasNoRealtreeComponent"))
              + [("push", 0, None)]
              + member(1, "hidGroundColor", "CGenericMember<CGraphicComponent,unsigned_int,"
                       "GenericTypeHandler<unsigned_int>,2u>", 0x150)
              + [("push", 1, 0)])
    rows, _ = rows_of(events, "CGraphicComponent")
    assert rows["hidGroundColor"]["parent"] == rows["Ambient"]["index"]
    assert rows["Ambient"]["getter"] == "CGraphicComponent::HasNoRealtreeComponent"


def test_accessors_offset_members_containers_and_events():
    events = (member(0, "bUseFastCollision", "CVirtualMember<CX,bool,GenericTypeHandler<bool>,3u>",
                     0, x10=("func", "CX::SetFast"), x18=("int", 0xB5))
              + [("push", 0, None)]
              + member(1, "hidSkyOcclusion2", "COffsetMember<CX,unsigned_int[4],GenericTypeHandler,1u>",
                       0x140, x10=("int", 2))
              + [("push", 1, None)]
              + member(2, "hidEffectBones", "CContainerMember<CX,CryVector<unsigned_int>,H,3u,true>",
                       0xF0, x14=("str", "hidBone"))
              + [("push", 2, None)]
              + [("alloc", 3, 0x14), ("slot", 3, OFF_NAME, ("str", "SerializationEvent")),
                 ("slot", 3, OFF_VPTR, vtable("CSerializationEvent<CX,1u>")),
                 ("slot", 3, OFF_OFFSET, ("func", "CX::PostSerialize")), ("push", 3, None)])
    rows, _ = rows_of(events, "CX")
    assert (rows["bUseFastCollision"]["setter"], rows["bUseFastCollision"]["getter"]) == ("CX::SetFast", "virtual+0xb4")
    assert rows["hidSkyOcclusion2"]["element_index"] == 2
    assert rows["hidEffectBones"]["child_name"] == "hidBone"
    event = rows["SerializationEvent"]
    assert (event["offset"], event["callback"], event["complete"]) == (None, "CX::PostSerialize", True)


def test_class_row_names_bases_and_counts_unpushed():
    events = ([("base", "CEntityComponent"), ("inherit", "CEntityComponent"), ("base", "CX")]
              + member(0, "Orphan", VT, 4))
    rows, klass = rows_of(events, "CX")
    assert rows == {}
    assert klass["bases"] == ["CEntityComponent"]
    assert klass["copies_base_members"] is True
    assert klass["unpushed"] == 1


if __name__ == "__main__":
    tests = [v for k, v in sorted(globals().items()) if k.startswith("test_")]
    for t in tests:
        t()
    print("%d tests passed" % len(tests))
