"""python -m unittest scripts/test_nexus_requires.py"""
import sys
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).parent))
import nexus_requires as n
from nexus_requires import Range, Refused

ABIS = {"fcse-1.3.0": 7, "fcse-1.4.0": 8, "fcse-1.4.2": 8, "fcse-1.5.0": 10}


class Parse(unittest.TestCase):
    def test_lines_and_alternatives(self):
        text = "# comment\nufcp >= 1.3.1\n\njackall >= 1.8.0 | vortex >= 0.7.0, <= 0.7.2  # either\n"
        self.assertEqual(n.parse(text, "f"), [[Range("ufcp", "1.3.1")],
                                              [Range("jackall", "1.8.0"), Range("vortex", "0.7.0", "0.7.2")]])

    def test_refuses_a_line_without_a_minimum(self):
        with self.assertRaises(Refused):
            n.parse("ufcp <= 1.3.1", "f")


class StatedMinimums(unittest.TestCase):
    def test_markdown_and_bbcode(self):
        self.assertEqual(n.stated_minimums("- **FCSE 1.5.0 or later**, on Far Cry 2", "FCSE"), {"1.5.0"})
        self.assertEqual(n.stated_minimums("[url=x]FCSE[/url] 1.4.0 or later, the loader", "FCSE"), {"1.4.0"})
        self.assertEqual(n.stated_minimums("Requires FCSE (the Far Cry Script Extender) 1.5.0 or later", "FCSE"),
                         {"1.5.0"})

    def test_another_name_in_the_sentence_is_not_read(self):
        self.assertEqual(n.stated_minimums("Vortex offers FCSE. JackAll 1.8.0 or later.", "FCSE"), set())


@mock.patch.object(n, "fcse_releases", lambda: sorted(t[5:] for t in ABIS))
@mock.patch.object(n, "abi", lambda ref=None: ABIS.get(ref, 10))
class FcseRange(unittest.TestCase):
    def test_current_abi_is_open_ended(self):
        self.assertEqual(n.fcse_range(10), Range("fcse", "1.5.0"))

    def test_superseded_abi_is_capped(self):
        self.assertEqual(n.fcse_range(8), Range("fcse", "1.4.0", "1.4.2"))

    def test_lowest_is_the_lowest_on_nexus(self):
        self.assertEqual(n.fcse_range(8, available={"1.4.2"}), Range("fcse", "1.4.2", "1.4.2"))

    def test_no_release_with_the_abi(self):
        with self.assertRaises(Refused):
            n.fcse_range(9)


if __name__ == "__main__":
    unittest.main()
