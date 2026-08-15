# Parser tests for dump_properties, run without a JVM.
#
#   python tools/fc2re/tests/test_parse_registrations.py
#   python -m pytest tools/fc2re/tests          (if pytest is installed)
#
# Fixtures are verbatim decompiler output from FarCry2_server, so drift in
# Ghidra's rendering shows up here rather than as silently wrong offsets in a
# 1,049-class harvest.

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from dump_properties import (OFF_CHILD_NAME, OFF_NAME, OFF_OFFSET, OFF_VPTR,
                             kind_from_handler, parse_int,
                             parse_registrations, symbol_trailing_addr,
                             to_statements, unescape)

# Bark::RegisterProperties (09191d00). Note puVar2 holds the base-constructor
# vtable, puVar4/puVar3 the real handler, and puVar3 is later reused for
# ms_descriptor -- so slot values must be resolved where they are stored.
BARK = r'''
/* Bark::RegisterProperties() */

void Bark::RegisterProperties(void)

{
  CMemberBase *pCVar1;
  undefined *puVar2;
  undefined *puVar3;
  undefined *puVar4;

  if (*PTR_ms_isInitialized_0a409680 == '\0') {
    *PTR_ms_isInitialized_0a409680 = 1;
    pCVar1 = (CMemberBase *)CMemMng::NMalloc(0x14,0);
    puVar2 = PTR_vtable_0a406bec + 8;
    *(undefined **)pCVar1 = puVar2;
    *(char **)(pCVar1 + 4) = "BarkEventTag";
    CStringID::SetContent((char *)(pCVar1 + 8),true,false);
    puVar3 = PTR_ms_descriptor_0a403a4c;
    puVar4 = PTR_vtable_0a411128 + 8;
    *(undefined **)pCVar1 = puVar4;
    *(undefined4 *)(pCVar1 + 0xc) = 0;
    *(undefined4 *)(pCVar1 + 0x10) = 0;
    CNomadObjectDescriptor::PushBackMember((CryVector *)puVar3,pCVar1);
    pCVar1 = (CMemberBase *)CMemMng::NMalloc(0x14,0);
    *(undefined **)pCVar1 = puVar2;
    *(char **)(pCVar1 + 4) = "SourceActorTag";
    CStringID::SetContent((char *)(pCVar1 + 8),true,false);
    *(undefined **)pCVar1 = puVar4;
    *(undefined4 *)(pCVar1 + 0xc) = 4;
    *(undefined4 *)(pCVar1 + 0x10) = 0;
    CNomadObjectDescriptor::PushBackMember((CryVector *)PTR_ms_descriptor_0a403a4c,pCVar1);
    pCVar1 = (CMemberBase *)CMemMng::NMalloc(0x14,0);
    *(undefined **)pCVar1 = puVar2;
    *(char **)(pCVar1 + 4) = "TargetActorTag";
    CStringID::SetContent((char *)(pCVar1 + 8),true,false);
    puVar3 = PTR_ms_descriptor_0a403a4c;
    *(undefined **)pCVar1 = puVar4;
    *(undefined4 *)(pCVar1 + 0xc) = 8;
    *(undefined4 *)(pCVar1 + 0x10) = 0;
    CNomadObjectDescriptor::PushBackMember((CryVector *)puVar3,pCVar1);
    pCVar1 = (CMemberBase *)CMemMng::NMalloc(0x24,0);
    *(undefined **)pCVar1 = puVar2;
    *(char **)(pCVar1 + 4) = "EnvironmentStates";
    CStringID::SetContent((char *)(pCVar1 + 8),true,false);
    puVar3 = PTR_vtable_0a40dabc + 8;
    *(char **)(pCVar1 + 0x14) = "State";
    *(undefined **)pCVar1 = puVar3;
    *(undefined4 *)(pCVar1 + 0xc) = 0xc;
    *(undefined4 *)(pCVar1 + 0x10) = 0;
    CStringID::SetContent((char *)(pCVar1 + 0x18),true,false);
    *(char **)(pCVar1 + 0x1c) = "State";
    CStringID::SetContent((char *)(pCVar1 + 0x20),true,false);
    CNomadObjectDescriptor::PushBackMember((CryVector *)PTR_ms_descriptor_0a403a4c,pCVar1);
    pCVar1 = (CMemberBase *)CMemMng::NMalloc(0x24,0);
    *(undefined **)pCVar1 = puVar2;
    *(char **)(pCVar1 + 4) = "SourceActorStates";
    CStringID::SetContent((char *)(pCVar1 + 8),true,false);
    *(undefined **)pCVar1 = puVar3;
    *(char **)(pCVar1 + 0x14) = "State";
    *(undefined4 *)(pCVar1 + 0xc) = 0x18;
    *(undefined4 *)(pCVar1 + 0x10) = 0;
    CStringID::SetContent((char *)(pCVar1 + 0x18),true,false);
    *(char **)(pCVar1 + 0x1c) = "State";
    CStringID::SetContent((char *)(pCVar1 + 0x20),true,false);
    CNomadObjectDescriptor::PushBackMember((CryVector *)PTR_ms_descriptor_0a403a4c,pCVar1);
    pCVar1 = (CMemberBase *)CMemMng::NMalloc(0x24,0);
    *(undefined **)pCVar1 = puVar2;
    *(char **)(pCVar1 + 4) = "TargetActorStates";
    CStringID::SetContent((char *)(pCVar1 + 8),true,false);
    *(undefined **)pCVar1 = puVar3;
    *(char **)(pCVar1 + 0x14) = "State";
    *(undefined4 *)(pCVar1 + 0xc) = 0x24;
    *(undefined4 *)(pCVar1 + 0x10) = 0;
    CStringID::SetContent((char *)(pCVar1 + 0x18),true,false);
    *(char **)(pCVar1 + 0x1c) = "State";
    CStringID::SetContent((char *)(pCVar1 + 0x20),true,false);
    CNomadObjectDescriptor::PushBackMember((CryVector *)PTR_ms_descriptor_0a403a4c,pCVar1);
    pCVar1 = (CMemberBase *)CMemMng::NMalloc(0x24,0);
    *(undefined **)pCVar1 = puVar2;
    *(char **)(pCVar1 + 4) = "BarkVersions";
    CStringID::SetContent((char *)(pCVar1 + 8),true,false);
    puVar3 = PTR_vtable_0a40e75c + 8;
    *(undefined4 *)(pCVar1 + 0xc) = 0x30;
    *(undefined **)pCVar1 = puVar3;
    *(undefined4 *)(pCVar1 + 0x10) = 0;
    *(char **)(pCVar1 + 0x14) = "SoundID";
    CStringID::SetContent((char *)(pCVar1 + 0x18),true,false);
    *(char **)(pCVar1 + 0x1c) = "SoundID";
    CStringID::SetContent((char *)(pCVar1 + 0x20),true,false);
    CNomadObjectDescriptor::PushBackMember((CryVector *)PTR_ms_descriptor_0a403a4c,pCVar1);
    pCVar1 = (CMemberBase *)CMemMng::NMalloc(0x14,0);
    *(undefined **)pCVar1 = puVar2;
    *(char **)(pCVar1 + 4) = "IsGeneric";
    CStringID::SetContent((char *)(pCVar1 + 8),true,false);
    puVar3 = PTR_vtable_0a401440 + 8;
    *(undefined4 *)(pCVar1 + 0xc) = 0x3c;
    *(undefined **)pCVar1 = puVar3;
    puVar3 = PTR_ms_descriptor_0a403a4c;
    *(undefined4 *)(pCVar1 + 0x10) = 0;
    CNomadObjectDescriptor::PushBackMember((CryVector *)puVar3,pCVar1);
  }
  return;
}
'''

# name, offset, alloc size, handler GOT slot, container element name
BARK_FIELDS = [
    ("BarkEventTag",      0x00, 0x14, "0a411128", None),
    ("SourceActorTag",    0x04, 0x14, "0a411128", None),
    ("TargetActorTag",    0x08, 0x14, "0a411128", None),
    ("EnvironmentStates", 0x0C, 0x24, "0a40dabc", "State"),
    ("SourceActorStates", 0x18, 0x24, "0a40dabc", "State"),
    ("TargetActorStates", 0x24, 0x24, "0a40dabc", "State"),
    ("BarkVersions",      0x30, 0x24, "0a40e75c", "SoundID"),
    ("IsGeneric",         0x3C, 0x14, "0a401440", None),
]

# The vtable every descriptor gets from its base constructor, before the real
# handler overwrites it.
BASE_CTOR_VTABLE = "0a406bec"

# CTaskCheckPosOnSpline: adds no fields, replays the base registration and
# copies the base member list.
INHERIT_ONLY = r'''
/* CTaskCheckPosOnSpline::RegisterProperties() */

void CTaskCheckPosOnSpline::RegisterProperties(void)

{
  undefined *puVar1;

  puVar1 = PTR_ms_isInitialized_0a408420;
  if (*PTR_ms_isInitialized_0a408420 == '\0') {
    CTask::RegisterProperties();
    CNomadObjectDescriptor::PushBackMembers
              ((CryVector *)PTR_ms_descriptor_0a410e18,(CryVector *)PTR_ms_descriptor_0a406200);
    *puVar1 = 1;
  }
  return;
}
'''


def parsed_fields():
    pushed, leftovers, _ = parse_registrations(BARK.splitlines())
    assert not leftovers
    out = []
    for d in pushed:
        child = d.slots.get(OFF_CHILD_NAME)
        out.append((
            (d.slots.get(OFF_NAME) or "").strip('"'),
            parse_int(d.slots.get(OFF_OFFSET) or ""),
            d.alloc_size,
            d.slots.get(OFF_VPTR) or "",
            child.strip('"') if child else None,
        ))
    return out


def test_every_descriptor_is_recovered():
    assert len(parsed_fields()) == len(BARK_FIELDS)


def test_names_offsets_and_sizes_match():
    for got, want in zip(parsed_fields(), BARK_FIELDS):
        assert (got[0], got[1], got[2], got[4]) == (want[0], want[1],
                                                    want[2], want[4])


def test_handler_vtable_is_the_last_write_not_the_base_ctor():
    for got, want in zip(parsed_fields(), BARK_FIELDS):
        assert want[3] in got[3], "%s: %r" % (got[0], got[3])
        assert BASE_CTOR_VTABLE not in got[3]


def test_reused_alias_resolves_to_its_value_at_the_store():
    # puVar3 is a vtable for EnvironmentStates and ms_descriptor by the end of
    # the function; resolving late would attribute the descriptor to the field.
    fields = {f[0]: f[3] for f in parsed_fields()}
    assert "0a40dabc" in fields["EnvironmentStates"]
    assert "ms_descriptor" not in fields["EnvironmentStates"]


def test_offsets_are_strictly_ascending():
    offsets = [f[1] for f in parsed_fields()]
    assert offsets == sorted(offsets)
    assert len(set(offsets)) == len(offsets)


def test_inherit_only_class_yields_no_members_but_names_its_base():
    pushed, leftovers, meta = parse_registrations(INHERIT_ONLY.splitlines())
    assert pushed == []
    assert leftovers == []
    assert meta["bases"] == ["CTask"]
    assert meta["copies_base_members"] is True


def test_leading_comment_is_not_read_as_a_base_class():
    _, _, meta = parse_registrations(BARK.splitlines())
    assert meta["bases"] == []
    assert meta["copies_base_members"] is False


def test_statements_join_calls_ghidra_wrapped_across_lines():
    stmts = to_statements(INHERIT_ONLY.splitlines())
    assert any("PushBackMembers" in s and s.count("(") >= 2 for s in stmts)


def test_symbol_trailing_addr():
    assert symbol_trailing_addr("PTR_vtable_0a411128") == 0x0A411128
    assert symbol_trailing_addr("PTR_Load_0a3a3468") == 0x0A3A3468
    assert symbol_trailing_addr("ms_descriptor") is None


def test_kind_uses_the_mangled_length_prefix():
    assert kind_from_handler(
        "_ZTV14CGenericMemberI4Bark9CStringID18GenericTypeHandlerIS1_ELj3EE"
    ) == "CGenericMember"
    assert kind_from_handler(
        "_ZTV16CContainerMemberI4Bark9CryVectorI9BarkState6NoLockE"
    ) == "CContainerMember"
    # The name contains no 'I', so a split on the first template marker would
    # be wrong here; the length prefix is what makes this exact.
    assert kind_from_handler("_ZTV19CSerializationEventI5CTaskE") \
        == "CSerializationEvent"
    assert kind_from_handler(None) is None
    assert kind_from_handler("vtable") is None


def test_parse_int_accepts_both_bases():
    assert parse_int("0x3c") == 0x3C
    assert parse_int("60") == 60
    assert parse_int('"nope"') is None


def test_unescape():
    assert unescape(r"a\nb") == "a\nb"
    assert unescape(r"q\"q") == 'q"q'
    assert unescape(r"\x41") == "A"


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

