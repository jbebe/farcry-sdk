"""Draws a contact sheet of the models a find.py query matches, rendering each model with its textures once.

Usage: python tools/asset-tags/render.py <term>... [--kind <kind>] [--all] [--limit <n>] [--game <install>]
Thumbnails stay in the main tree's tools/asset-tags/thumbs/ at each model's game path; the sheets go in
thumbs/sheets/, named after the query.
"""
import json
import re
import subprocess
import tempfile
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

from find import families, parser_for, select
from tables import HERE, jackall_cli, main_tree

# Gitignored, so in the main tree, where every worktree shares it.
THUMBS = main_tree() / "tools/asset-tags/thumbs"
COLUMNS, ROWS, CELL, CAPTION = 5, 4, 256, 34


def thumb(path):
    return THUMBS / Path(path.replace("\\", "/")).with_suffix(".png")


def render(paths, game, cli, blender):
    """Renders every model that has no thumbnail yet: an .fc2model pack each, then one Blender run."""
    todo = [p for p in paths if not thumb(p).exists()]
    if not todo:
        return
    with tempfile.TemporaryDirectory() as tmp:
        jobs = []
        for n, path in enumerate(todo):
            pack = Path(tmp) / f"{n}.fc2model"
            exported = subprocess.run([str(cli), "fc2model", "export", "-g", str(game), path.replace("\\", "/"), "-o", str(pack)],
                                      capture_output=True)
            if exported.returncode == 0:
                thumb(path).parent.mkdir(parents=True, exist_ok=True)
                jobs.append([str(pack), str(thumb(path))])
        (Path(tmp) / "jobs.json").write_text(json.dumps(jobs))
        subprocess.run([str(blender), "-b", "--factory-startup", "--python", str(HERE / "blender_thumbs.py"), "--",
                        str(Path(tmp) / "jobs.json")], capture_output=True)
    # Blender exits 0 when a render fails, so only the files tell.
    missing = [p for p in todo if not thumb(p).exists()]
    if missing:
        print(f"{len(missing)} not rendered, first: {missing[0]}")


def draw_sheets(rows, name):
    """Sheets of numbered thumbnails with name and size, ROWS x COLUMNS each; returns their paths."""
    try:
        font = ImageFont.truetype("arial.ttf", 13)
    except OSError:
        font = ImageFont.load_default()
    per_sheet, sheets = COLUMNS * ROWS, []
    for start in range(0, len(rows), per_sheet):
        image = Image.new("RGB", (COLUMNS * CELL, ROWS * (CELL + CAPTION)), (20, 20, 20))
        draw = ImageDraw.Draw(image)
        for i, row in enumerate(rows[start:start + per_sheet]):
            x, y = i % COLUMNS * CELL, i // COLUMNS * (CELL + CAPTION)
            if thumb(row["path"]).exists():
                image.paste(Image.open(thumb(row["path"])).convert("RGB").resize((CELL, CELL)), (x, y))
            draw.text((x + 4, y + CELL + 2), f"{start + i + 1} {Path(row['path']).stem}"[:38], fill=(255, 255, 255), font=font)
            draw.text((x + 4, y + CELL + 17), row["size"], fill=(180, 180, 180), font=font)
        sheet = THUMBS / "sheets" / f"{name}_{start // per_sheet + 1:02}.png"
        sheet.parent.mkdir(parents=True, exist_ok=True)
        image.save(sheet)
        sheets.append(sheet)
    return sheets


def main():
    parser = parser_for(__doc__.splitlines()[0])
    parser.add_argument("--limit", type=int, default=60, help="draw at most this many models")
    parser.add_argument("--game", type=Path, default=Path(r"C:\Games\Far Cry 2"), help="the install the models are read from")
    parser.add_argument("--cli", type=Path, default=jackall_cli())
    parser.add_argument("--blender", type=Path, default=Path(r"C:\Programs\Blender 5.2\blender.exe"))
    args = parser.parse_args()

    by_kind = families(select("models", args.terms, args.kind), "models", args.all)
    rows = [members[0] for kind in sorted(by_kind) for members in by_kind[kind].values()]
    if len(rows) > args.limit:
        print(f"{len(rows)} models; drawing the first {args.limit} - narrow with --kind or raise --limit")
        rows = rows[:args.limit]
    render([r["path"] for r in rows], args.game, args.cli, args.blender)
    for n, row in enumerate(rows, 1):
        print(f"{n}. {row['path']} | {row['kind']} | {row['what']} | {row['size']}")
    name = re.sub(r"[^\w-]+", "-", " ".join(args.terms + ([args.kind] if args.kind else []))).strip("-")[:60]
    for sheet in draw_sheets(rows, name):
        print(sheet)


if __name__ == "__main__":
    main()
