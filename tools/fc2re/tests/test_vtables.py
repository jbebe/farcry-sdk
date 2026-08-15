# Pure-logic tests for dump_vtables, run without a JVM.
#
#   python tools/fc2re/tests/test_vtables.py

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from dump_vtables import demangled_class, jint


def test_jint_is_signed_32_bit():
    assert jint(0) == 0
    assert jint(8) == 8
    assert jint(0xFFFFFFFF) == -1
    # offset-to-top for a secondary base subobject is negative
    assert jint(0xFFFFFFF8) == -8
    assert jint(0x7FFFFFFF) == 0x7FFFFFFF
    assert jint(0x80000000) == -0x80000000


def test_demangled_class_plain():
    assert demangled_class("_ZTV7CEntity", "_ZTV") == "CEntity"
    assert demangled_class("_ZTI17IEntitySystemSink", "_ZTI") \
        == "IEntitySystemSink"


def test_demangled_class_keeps_template_tail():
    got = demangled_class(
        "_ZTV14CGenericMemberI4Barkb18GenericTypeHandlerIbELj3EE", "_ZTV")
    assert got.startswith("CGenericMember<")
    assert got.endswith(">")


def test_demangled_class_nested_scopes():
    assert demangled_class("_ZTVN9CryVectorE", "_ZTV") == "CryVector"
    assert demangled_class("_ZTVN13CEntitySystem12DeathRowCellE", "_ZTV") \
        == "CEntitySystem::DeathRowCell"
    assert demangled_class("_ZTVN4Echo18CNetHandlerAdapterE", "_ZTV") \
        == "Echo::CNetHandlerAdapter"


def test_demangled_class_rejects_unparseable():
    assert demangled_class("_ZTV", "_ZTV") is None
    assert demangled_class("_ZTV99CShort", "_ZTV") is None
    assert demangled_class("_ZTVzzz", "_ZTV") is None


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

