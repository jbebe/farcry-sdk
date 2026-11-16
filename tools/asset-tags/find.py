"""Lists tagged assets matching every term, grouped by kind, with the variants of one object on one line.

Usage: python tools/asset-tags/find.py <term>... [--textures | --sounds] [--kind <kind>] [--all]
A term that is a kind, tag, trait or category of the table matches only those; any other term matches
the path, description, archetypes or label. `a,b` matches either.
"""
import argparse
import collections
import re

from tables import VARIANT, read, shortlist

# Per table: the columns a vocabulary word matches, and the columns shown after the description.
COLUMNS = {
    "models": (("kind", "tags", "traits"), ("size", "placed", "archetypes")),
    "textures": (("kind", "tags", "inherited"), ("size", "used_by")),
    "sounds": (("kind", "tags", "category"), ("label",)),
}
TEXT = ("path", "what", "archetypes", "label", "used_by")
# Above this many objects, a query without --kind prints only their names per kind.
BRIEF_ABOVE = 60


def stem(path):
    return path.rpartition("\\")[2].rpartition(".")[0]


def family(row, table):
    """What a file is a variant of: a model's folder and unnumbered stem, a texture's stem without its
    map suffix, a sound's description."""
    if table == "sounds":
        return row["what"]
    if table == "textures":
        return re.sub(r"_?\d*_(d|n|s|m|h|mask|spec)$", "", stem(row["path"]))
    return row["path"].rpartition("\\")[0], re.sub(r"_?\d+[a-z]?$", "", VARIANT.sub("", stem(row["path"])))


def intact_first(row):
    """Sorts a family's plain mesh or diffuse texture before its variants and maps."""
    name = stem(row["path"])
    return bool(VARIANT.search(name)), not name.endswith("_d"), len(name), name


def describe(row, shown):
    parts = [row["what"] or "-"]
    for column in shown:
        value = row.get(column, "")
        if value and value != "0":
            parts.append(f"placed {value}" if column == "placed" else value[:120])
    return " | ".join(parts)


def select(table, terms, kind=None):
    """The table's rows matching every term and, given one, the kind."""
    exact, _ = COLUMNS[table]
    rows = read(table).values()
    words = {w for r in rows for c in exact for w in r[c].split()}
    terms = [term.lower().split(",") for term in terms]
    selected = []
    for row in rows:
        own = {w for c in exact for w in row[c].split()}
        text = " ".join(row.get(c, "") for c in TEXT).lower()
        if all(any(t in own if t in words else t in text for t in term) for term in terms) and kind in (None, row["kind"]):
            selected.append(row)
    return selected


def families(rows, table, every=False):
    """Kind -> family (or path, with every) -> its rows, the plain mesh or diffuse texture first."""
    by_kind = collections.defaultdict(lambda: collections.defaultdict(list))
    for row in rows:
        by_kind[row["kind"]][row["path"] if every else family(row, table)].append(row)
    for groups in by_kind.values():
        for members in groups.values():
            members.sort(key=intact_first)
    return by_kind


def parser_for(description):
    parser = argparse.ArgumentParser(description=description)
    parser.add_argument("terms", nargs="+")
    parser.add_argument("--kind")
    parser.add_argument("--all", action="store_true", help="one line per file")
    return parser


def main():
    parser = parser_for(__doc__.splitlines()[0])
    parser.add_argument("--textures", action="store_const", const="textures", dest="table", default="models")
    parser.add_argument("--sounds", action="store_const", const="sounds", dest="table")
    args = parser.parse_args()

    rows = select(args.table, args.terms, args.kind)
    by_kind = families(rows, args.table, args.all)
    brief = not args.kind and not args.all and sum(len(g) for g in by_kind.values()) > BRIEF_ABOVE
    for kind in sorted(by_kind):
        groups = by_kind[kind]
        print(f"\n## {kind} ({len(groups)})")
        if brief:
            print(", ".join(stem(m[0]["path"]) for m in groups.values()))
            continue
        for first, *others in groups.values():
            more = f" (+{len(others)}: {shortlist(stem(m['path']) for m in others)})" if others else ""
            print(f"- {first['path']}{more} | {describe(first, COLUMNS[args.table][1])}")
    print(f"\n{len(rows)} files" + (" - add --kind <kind> for sizes, placements and archetypes" if brief else ""))


if __name__ == "__main__":
    main()
