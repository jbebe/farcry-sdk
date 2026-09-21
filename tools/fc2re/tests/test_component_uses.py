# Tests for dump_component_uses, run without a JVM.

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from dump_component_uses import collect, lookup_target


def test_lookup_target():
    assert lookup_target("GetComponent<CGraphicComponent>") == "CGraphicComponent"
    assert lookup_target("GetComponentNoLookup") is None
    assert lookup_target(None) is None


def test_collect_groups_methods_and_drops_self_lookups():
    rows = collect([
        ("CRigidPhysComponent", "Finalize", "GetComponent<CGraphicComponent>"),
        ("CRigidPhysComponent", "SetGraphicMatrixToIdentity", "GetComponent<CGraphicComponent>"),
        ("CRigidPhysComponent", "Finalize", "GetComponent<CGraphicComponent>"),
        ("CRigidPhysComponent", "Update", "GetComponent<CRigidPhysComponent>"),
    ], {"CRigidPhysComponent", "CSoundComponent"})
    assert rows == [
        {"class": "CRigidPhysComponent", "creatable": True,
         "uses": {"CGraphicComponent": ["Finalize", "SetGraphicMatrixToIdentity"]}},
        {"class": "CSoundComponent", "creatable": True, "uses": {}},
    ]


if __name__ == "__main__":
    tests = [v for k, v in sorted(globals().items()) if k.startswith("test_")]
    for t in tests:
        t()
    print("%d tests passed" % len(tests))
