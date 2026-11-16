"""The asset tag tables: their files and columns, the README's vocabulary, and reading, writing and checking them."""
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
HAND = ("kind", "what", "tags")
# Per table: its columns, then the README sections that hold its kinds and its tags.
TABLES = {
    "models": (("path", *HAND, "size", "placed", "places", "archetypes", "textures", "traits", "archive"),
               "Model kinds", ("Tags",)),
    "textures": (("path", *HAND, "size", "users", "used_by", "inherited", "archive"), "Texture kinds", ("Tags",)),
    "sounds": (("path", *HAND, "category", "label"), "Sound kinds", ("Tags", "Sound tags")),
}
EXTENSIONS = {".xbg": "models", ".xbt": "textures", ".spk": "sounds"}
# A broken, LOD or multiplayer variant suffix in a model's file name.
VARIANT = re.compile(r"_(bk|lod|mp|broken|destroyed|dest)\d*[a-z]?(?=_|$)")


def read(table):
    """The table's rows by path; empty when the file does not exist yet."""
    path = HERE / f"{table}.tsv"
    if not path.exists():
        return {}
    lines = path.read_text(encoding="utf-8").splitlines()
    header = lines[0].lstrip("# ").split("\t")
    return {r[0]: dict(zip(header, r)) for r in (line.split("\t") for line in lines[1:] if line)}


def write(table, rows):
    columns = TABLES[table][0]
    clean = lambda v: str(v).replace("\t", " ").replace("\n", " ")
    lines = ["# " + "\t".join(columns)] + ["\t".join(clean(r.get(c, "")) for c in columns) for r in rows]
    (HERE / f"{table}.tsv").write_text("\n".join(lines) + "\n", encoding="utf-8")


def vocabulary():
    """The backticked words of each ### section under the README's Vocabulary."""
    text = (HERE / "README.md").read_text(encoding="utf-8")
    body = text.split("\n## Vocabulary\n", 1)[1].split("\n## ", 1)[0]
    sections = {}
    for section in body.split("\n### ")[1:]:
        title, words = section.split("\n", 1)
        sections[title.strip()] = set(re.findall(r"`([a-z-]+)`", words))
    return sections


def check(table, rows):
    """(path, word) for every kind or tag of the rows that the table's vocabulary lacks; an empty kind is one."""
    words = vocabulary()
    _, kinds, tag_sections = TABLES[table]
    tags = set().union(*(words[s] for s in tag_sections))
    bad = []
    for row in rows:
        if row["kind"] not in words[kinds]:
            bad.append((row["path"], row["kind"]))
        bad += [(row["path"], t) for t in row["tags"].split() if t not in tags]
    return bad


def main_tree():
    """The main working tree, which holds the gitignored game files and the Release jackall-cli."""
    sys.path.insert(0, str(HERE.parents[1] / "scripts"))
    from modstatus import MAIN_TREE
    return MAIN_TREE


def jackall_cli():
    return main_tree() / "tools/JackAll/src/JackAll.Cli/bin/Release/net10.0/jackall-cli.exe"


def shortlist(items, limit=3):
    items = list(items)
    return ", ".join(items[:limit]) + (f" +{len(items) - limit}" if len(items) > limit else "")
