# Allocation-site parsing tests for dump_class_sizes, run without a JVM.
#
#   python tools/fc2re/tests/test_class_sizes.py

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from dump_class_sizes import (base_class_name, find_sites, is_class_name,
                              is_constructor, reconcile)

SIMPLE = '''
void Foo(void)
{
  pCVar1 = (CBark *)CMemMng::NMalloc(0x40,0);
  CBark::CBark(pCVar1);
  return;
}
'''

DERIVED = '''
void Bar(void)
{
  puVar2 = (CDerived *)CMemMng::NMalloc(0x120,0);
  CDerived::CDerived(puVar2,param_1);
  return;
}
'''

# A computed size says nothing about the class.
COMPUTED = '''
void Baz(void)
{
  puVar1 = (char *)CMemMng::NMalloc(count * 4,0);
  CThing::CThing(puVar1);
  return;
}
'''

# Ghidra wraps long calls, and the constructor may sit several statements on.
WRAPPED = '''
void Qux(void)
{
  pCVar1 = (CWidget *)CMemMng::NMalloc(0x2c,0);
  iVar2 = 0;
  CWidget::CWidget
            (pCVar1,param_1,param_2);
  return;
}
'''

TEMPLATED = '''
void Tpl(void)
{
  p = (CryVector<int> *)CMemMng::NMalloc(0x18,0);
  CryVector<int,NoLock>::CryVector(p);
  return;
}
'''

DESTRUCTOR_ONLY = '''
void Dtor(void)
{
  p = (CThing *)CMemMng::NMalloc(0x10,0);
  CThing::~CThing(p);
  return;
}
'''


def sizes(text):
    return [(s["class"], s["size"]) for s in find_sites(text.splitlines())]


def test_constructor_gives_the_size():
    assert sizes(SIMPLE) == [("CBark", 0x40)]


def test_most_derived_constructor_wins():
    assert sizes(DERIVED) == [("CDerived", 0x120)]


def test_computed_size_is_ignored():
    assert sizes(COMPUTED) == []


def test_constructor_found_across_wrapped_lines_and_gaps():
    assert sizes(WRAPPED) == [("CWidget", 0x2c)]


def test_template_arguments_are_stripped():
    assert sizes(TEMPLATED) == [("CryVector", 0x18)]


def test_destructor_is_not_mistaken_for_a_constructor():
    # Falls back to the cast rather than recording ~CThing as the class.
    got = find_sites(DESTRUCTOR_ONLY.splitlines())
    assert [(s["class"], s["evidence"]) for s in got] == [("CThing", "cast")]


def test_base_class_name():
    assert base_class_name("A::B::C") == "C"
    assert base_class_name("CryVector<int,NoLock>") == "CryVector"
    assert base_class_name("Outer<A::B>::Inner") == "Inner"


def test_is_constructor():
    assert is_constructor("CBark", "CBark")
    assert is_constructor("CryVector<int,NoLock>", "CryVector")
    assert not is_constructor("CBark", "~CBark")
    assert not is_constructor("CBark", "Init")


def test_reconcile_takes_the_largest_not_the_majority():
    # A base-class cast in front of a derived allocation credits the derived
    # size to the base. Undersizing makes Ghidra wrap into `this[1].Field`,
    # so the safe direction is up even when the majority says otherwise.
    rows = [{"class": "A", "size": 8}, {"class": "A", "size": 8},
            {"class": "A", "size": 12}, {"class": "B", "size": 4}]
    out = {r["class"]: r for r in reconcile(rows)}
    assert out["A"]["size"] == 12
    assert out["A"]["majority_size"] == 8
    assert out["A"]["conflicting_sizes"] == [8, 12]
    assert out["A"]["agreement"] == round(2 / 3.0, 3)
    assert out["B"]["conflicting_sizes"] == []


def test_reconcile_drops_primitive_and_undefined_casts():
    rows = [{"class": "char", "size": 0x114},
            {"class": "undefined4", "size": 4},
            {"class": "uint", "size": 8},
            {"class": "CReal", "size": 0x20}]
    assert [r["class"] for r in reconcile(rows)] == ["CReal"]


def test_is_class_name():
    assert is_class_name("CEntity")
    assert is_class_name("bdString")
    assert is_class_name("SDevice3DFvF")
    assert not is_class_name("char")
    assert not is_class_name("undefined4")
    assert not is_class_name("uint")
    assert not is_class_name("")


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

