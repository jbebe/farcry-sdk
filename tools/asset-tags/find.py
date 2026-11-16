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


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("terms", nargs="+")
    parser.add_argument("--textures", action="store_const", const="textures", dest="table", default="models")
    parser.add_argument("--sounds", action="store_const", const="sounds", dest="table")
    parser.add_argument("--kind")
    parser.add_argument("--all", action="store_true", help="one line per file")
    args = parser.parse_args()

    exact, shown = COLUMNS[args.table]
    table = read(args.table).values()
    words = {w for r in table for c in exact for w in r[c].split()}
    terms = [term.lower().split(",") for term in args.terms]
    rows = []
    for row in table:
        own = {w for c in exact for w in row[c].split()}
        text = " ".join(row.get(c, "") for c in TEXT).lower()
        if all(any(t in own if t in words else t in text for t in term) for term in terms) and args.kind in (None, row["kind"]):
            rows.append(row)

    by_kind = collections.defaultdict(lambda: collections.defaultdict(list))
    for row in rows:
        by_kind[row["kind"]][row["path"] if args.all else family(row, args.table)].append(row)

    brief = not args.kind and not args.all and sum(len(g) for g in by_kind.values()) > BRIEF_ABOVE
    for kind in sorted(by_kind):
        groups = by_kind[kind]
        print(f"\n## {kind} ({len(groups)})")
        for members in groups.values():
            members.sort(key=intact_first)
        if brief:
            print(", ".join(stem(m[0]["path"]) for m in groups.values()))
            continue
        for first, *others in groups.values():
            more = f" (+{len(others)}: {shortlist(stem(m['path']) for m in others)})" if others else ""
            print(f"- {first['path']}{more} | {describe(first, shown)}")
    print(f"\n{len(rows)} files" + (" - add --kind <kind> for sizes, placements and archetypes" if brief else ""))


if __name__ == "__main__":
    main()
