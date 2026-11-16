"""Merges tagger rows (path, kind, what, tags) into the tables, then checks every table against the README's vocabulary.

Usage: python tools/asset-tags/merge.py <rows.tsv>...   then build.py, to refresh the inherited tags.
"""
import sys
from pathlib import Path

from tables import EXTENSIONS, TABLES, check, read, write


def main():
    tables = {table: read(table) for table in TABLES}
    errors, merged = [], 0
    for source in sys.argv[1:]:
        for number, line in enumerate(Path(source).read_text(encoding="utf-8").splitlines(), 1):
            if not line.strip() or line.startswith("#"):
                continue
            fields = [f.strip() for f in line.split("\t")]
            row = tables.get(EXTENSIONS.get(Path(fields[0]).suffix), {}).get(fields[0]) if len(fields) == 4 else None
            if row is None:
                errors.append(f"{source}:{number}: not four fields, or an unknown path")
                continue
            row.update(kind=fields[1], what=fields[2], tags=" ".join(dict.fromkeys(fields[3].split())))
            merged += 1
    for table, rows in tables.items():
        write(table, rows.values())
        errors += [f"{table}: {path}: not in the vocabulary: {word!r}" for path, word in check(table, rows.values())]
    print(f"{merged} rows merged")
    print("\n".join(errors[:200]))
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
