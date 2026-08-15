# Constructor-walking tests, run without a JVM.
#
#   python tools/fc2re/tests/test_ctor_fields.py

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from dump_ctor_fields import (field_offset, is_self, merge, parse_ctor,
                              vtable_slot)

CTOR = '''
void __thiscall CFoo::CFoo(CFoo *this)

{
  CBase::CBase(this);
  this->vptr = (CFoo_vtable *)&PTR_vtable_0a400000;
  CryVector::CryVector(&this->field_0x1c);
  CStringID::CStringID(&this->field_0x28);
  *(undefined4 *)&this->field_0x2c = 0;
  *(undefined1 *)&this->field_0x30 = 0;
  return;
}
'''

DTOR = '''
void __thiscall CFoo::~CFoo(CFoo *this)

{
  CStringID::~CStringID(&this->field_0x28);
  CryVector::~CryVector(&this->field_0x1c);
  CBase::~CBase(this);
  return;
}
'''


def test_member_constructor_names_type_and_offset():
    got = {m["offset"]: m["type_name"]
           for m in parse_ctor(CTOR.splitlines(), "CFoo", ["CBase"])}
    assert got[0x1c] == "CryVector"
    assert got[0x28] == "CStringID"


def test_scalar_store_gives_offset_and_width():
    got = {m["offset"]: m["width"]
           for m in parse_ctor(CTOR.splitlines(), "CFoo", ["CBase"])}
    assert got[0x2c] == 4
    assert got[0x30] == 1


def test_known_base_at_offset_zero_is_not_a_member():
    got = parse_ctor(CTOR.splitlines(), "CFoo", ["CBase"])
    assert all(m["offset"] != 0 for m in got)


def test_unlisted_base_is_still_recorded():
    got = {m["offset"]: m["type_name"]
           for m in parse_ctor(CTOR.splitlines(), "CFoo", [])}
    assert got[0] == "CBase"


def test_own_constructor_is_not_a_member_of_itself():
    body = "void CFoo::CFoo(CFoo *this) { CFoo::CFoo(&this->field_0x8); }"
    assert parse_ctor([body], "CFoo", []) == []


def test_offset_needs_exactly_one_field_reference():
    assert field_offset("&this->field_0x1c") == 0x1c
    assert field_offset("this->field_0x4 + this->field_0x8") is None
    assert field_offset("this") is None


def test_is_self_sees_through_casts():
    assert is_self("this")
    assert is_self("(CBase *)this")
    assert is_self("&this")
    assert not is_self("&this->field_0x4")


def test_destructor_corroborates_the_constructor():
    ctor = parse_ctor(CTOR.splitlines(), "CFoo", ["CBase"])
    dtor = parse_ctor(DTOR.splitlines(), "CFoo", ["CBase"])
    got = {m["offset"]: m["evidence"] for m in merge(ctor, dtor)}
    assert got[0x1c] == "both"
    assert got[0x28] == "both"
    assert got[0x2c] == "constructor"


def test_destructor_only_field_is_kept_and_labelled():
    ctor = []
    dtor = parse_ctor(DTOR.splitlines(), "CFoo", ["CBase"])
    got = {m["offset"]: m["evidence"] for m in merge(ctor, dtor)}
    assert got[0x1c] == "destructor"


def test_absurd_offsets_are_rejected():
    assert field_offset("&this->field_0xffffffff") is None


def test_vtable_slot_needs_the_header_addend():
    # `PTR_vtable_<got> + 8` is a vptr; the bare symbol is just a pointer.
    assert vtable_slot("PTR_vtable_0a40ca84 + 8;") == 0x0a40ca84
    assert vtable_slot("(undefined **)(PTR_vtable_0a40ca84 + 8)") \
        == 0x0a40ca84
    assert vtable_slot("PTR_m_FakeHeap_0a4134d0;") is None
    assert vtable_slot("0") is None


INLINED = '''
void __thiscall CFoo::CFoo(CFoo *this)

{
  undefined *puVar1;

  this->vptr = (CFoo_vtable *)(PTR_vtable_0a400000 + 8);
  *(undefined **)&this->field_0x58 = PTR_vtable_0a40ca84 + 8;
  *(undefined4 *)&this->field_0x5c = 0;
  return;
}
'''


def test_inlined_member_vptr_names_the_member_type():
    # GCC inlines the member constructor, but its vptr store survives.
    got = {m["offset"]: m["type_name"]
           for m in parse_ctor(INLINED.splitlines(), "CFoo", [],
                               lambda s: {0x0a40ca84: "CBar",
                                          0x0a400000: "CFoo"}.get(s))}
    assert got[0x58] == "CBar"
    assert got[0x5c] is None


def test_a_class_is_not_recorded_as_its_own_member():
    # The object's own vptr store must not name CFoo as a member of CFoo.
    got = {m["offset"]: m["type_name"]
           for m in parse_ctor(INLINED.splitlines(), "CFoo", [],
                               lambda s: "CFoo")}
    assert all(t is None for t in got.values())


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

