# Tests for apply_type_sizes evidence rules, run without a JVM.
#
#   python tools/fc2re/tests/test_type_sizes.py

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from apply_type_sizes import gap_histogram, looks_like_enum, value_sizes

# CGenericMember<Owner, CStringID, ...> -- a named type with no builtin size.
STRID = ("_ZTV14CGenericMemberI4Bark9CStringID"
         "18GenericTypeHandlerIS1_ELj3EE")
# CGenericMember<Owner, bool, ...> -- a builtin, already sized.
BOOL = "_ZTV14CGenericMemberI4Barkb18GenericTypeHandlerIbELj3EE"


def rows(owner, pairs, handler=STRID):
    return [{"owner": owner, "name": "m%d" % i, "offset": off,
             "handler_symbol": handler}
            for i, off in enumerate(pairs)]


def test_gap_is_distance_to_the_next_member():
    hist = gap_histogram(rows("A", [0, 4, 8]))
    assert hist["CStringID"][4] == 2


def test_builtin_typed_members_are_not_measured():
    assert gap_histogram(rows("A", [0, 4], handler=BOOL)) == {}


def test_size_accepted_when_min_and_mode_agree():
    data = []
    for i in range(6):
        data += rows("C%d" % i, [0, 4])
    got = value_sizes(data, min_samples=5)
    assert got["CStringID"]["size"] == 4
    assert got["CStringID"]["samples"] == 6


def test_size_rejected_when_the_minimum_undercuts_the_mode():
    # One class packs the type at 4 while most leave 12 -- the stripped name
    # is covering instantiations of different sizes, so neither is safe.
    data = rows("Tight", [0, 4])
    for i in range(6):
        data += rows("Loose%d" % i, [0, 12])
    assert "CStringID" not in value_sizes(data, min_samples=3)


def test_thin_evidence_is_ignored():
    assert value_sizes(rows("A", [0, 4]), min_samples=5) == {}


def test_absurd_sizes_are_ignored():
    data = []
    for i in range(6):
        data += rows("C%d" % i, [0, 4096])
    assert value_sizes(data, min_samples=5) == {}


def test_last_member_of_a_class_contributes_no_gap():
    assert gap_histogram(rows("A", [0])) == {}


def test_a_dominant_mode_is_required():
    # CryMap's mode showed up in 3 of 15 samples: min == mode, but the type
    # plainly has no single size worth asserting.
    data = rows("Tight", [0, 28])
    for i, gap in enumerate((60, 60, 188, 96, 220, 140)):
        data += rows("Loose%d" % i, [0, gap])
    assert "CStringID" not in value_sizes(data, min_samples=3)


def test_enum_convention_matches_only_the_e_prefix():
    for good in ("EStimType", "EMoveLayer", "EEntityUpdateFlags",
                 "/Demangler/EGameRulesProcessFlag"):
        assert looks_like_enum(good), good
    # These are all in the unsized set and are nowhere near 4 bytes.
    for bad in ("/Demangler/std/_Deque_iterator", "ndRectT", "CStringID",
                "/Demangler/__gnu_cxx/__normal_iterator", "Entity",
                "random_access_iterator_tag", "EntityId"):
        assert not looks_like_enum(bad), bad


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

