# Tests for the decompiled-text helpers, run without a JVM.

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from decompiled import parse_int, symbol_trailing_addr, to_statements


def test_statements_join_calls_ghidra_wrapped_across_lines():
    stmts = to_statements(["/* X::F() */", "  CNomadObjectDescriptor::PushBackMembers",
                           "            ((CryVector *)a,(CryVector *)b);"])
    assert any("PushBackMembers" in s and s.count("(") >= 2 for s in stmts)
    assert not any("X::F" in s for s in stmts)


def test_symbol_trailing_addr():
    assert symbol_trailing_addr("PTR_vtable_0a411128") == 0x0A411128
    assert symbol_trailing_addr("PTR_Load_0a3a3468") == 0x0A3A3468
    assert symbol_trailing_addr("ms_descriptor") is None


def test_parse_int_accepts_both_bases():
    assert parse_int("0x3c") == 0x3C
    assert parse_int("60") == 60
    assert parse_int('"nope"') is None


if __name__ == "__main__":
    tests = [v for k, v in sorted(globals().items()) if k.startswith("test_")]
    for t in tests:
        t()
    print("%d tests passed" % len(tests))
