# Helpers shared by the dumpers that read decompiled C text.

import re

RE_INT_LITERAL = re.compile(r"^(?P<n>-?0x[0-9a-fA-F]+|-?\d+)$")
RE_TRAILING_ADDR = re.compile(r"_(?P<addr>[0-9a-fA-F]{6,16})$")


def to_statements(lines):
    """Decompiled C -> one whitespace-normalised statement per element.

    Ghidra wraps long calls across lines, so matching per line splits a call
    from its arguments.
    """
    joined = " ".join(line.strip() for line in lines)
    # The leading block comment repeats the signature with empty parens.
    joined = re.sub(r"/\*.*?\*/", " ", joined, flags=re.S)
    return [re.sub(r"\s+", " ", s).strip() + ";" for s in joined.split(";")]


def parse_int(text):
    m = RE_INT_LITERAL.match(text.strip())
    if not m:
        return None
    n = m.group("n")
    return int(n, 16) if "x" in n.lower() else int(n)


def symbol_trailing_addr(name):
    """PTR_Load_0a3a3468 -> 0x0a3a3468. Ghidra appends the target address."""
    m = RE_TRAILING_ADDR.search(name or "")
    return int(m.group("addr"), 16) if m else None
