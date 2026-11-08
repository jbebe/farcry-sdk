"""Which mods a change affects, run before every commit.

    python scripts/affected_mods.py <path>...    # the paths about to be committed
    python scripts/affected_mods.py              # every uncommitted change, other sessions' included

A mod is affected when the change is in its folder, in code its build points at, in what it runs on
(FCSE's loader, JackAll's and Vortex's install path), or when it now collides with another mod. Each
affected mod needs a line under [Unreleased] in its CHANGELOG.md; each collision with no decision in
either STATUS.md needs the user's decision recorded in both. Exits 1 while either is missing.
"""
import sys
from pathlib import Path

import modstatus
from modstatus import REPO

def changed_paths(args):
    if not args:
        return sorted(set(modstatus.dirty_paths()))
    paths = []
    for arg in args:
        path = Path(arg)
        path = path.resolve().relative_to(REPO) if path.is_absolute() else path
        paths.append(path.as_posix().rstrip("/"))
    return paths


def under(path, prefix):
    return path == prefix or path.startswith(prefix + "/")


def affected(paths, projects):
    """{project key: [reason, ...]} for owners and builds, {key: [path]} for what they run on, the
    owners' changed paths, and paths no mod depends on."""
    hits, runs_on, owned, loose = {}, {}, {}, []
    # A whole project named as a build tool (a script saying "build tools\JackAll") is too coarse to
    # follow; what installs a layer is in modstatus.LAYER_RUNTIME.
    roots = {p.dir for p in projects}
    for path in paths:
        reached = False
        for p in projects:
            if under(path, p.dir):
                reached = True
                if modstatus.is_code(path[len(p.dir) + 1:]):
                    owned.setdefault(p.key, []).append(path)
                    hits.setdefault(p.key, [])
                    if "its folder" not in hits[p.key]:
                        hits[p.key].insert(0, "its folder")
                continue
            for dep in modstatus.compiled_deps(p):
                if under(path, dep):
                    reached = True
                    reason = f"builds against {dep}"
                    if reason not in hits.setdefault(p.key, []):
                        hits[p.key].append(reason)
            tools = [d for d in modstatus.build_deps(p) if modstatus.is_tool_dep(d) and d not in roots]
            if modstatus.matches(path, modstatus.runtime_patterns(p)) or any(under(path, d) for d in tools):
                reached = True
                runs_on.setdefault(p.key, set()).add(path)
        if not reached and not modstatus.matches(path, modstatus.RELEASE_TOOLING):
            loose.append(path)
    return hits, runs_on, owned, loose


def touched_kinds(paths):
    kinds = set()
    for path in paths:
        parts = path.split("/")
        if "src" in parts:
            kinds.add("hook")
        if "layer" in parts or "data" in parts:
            kinds.update(("layer", "control", "string"))
    return kinds


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    paths = changed_paths(sys.argv[1:])
    if not paths:
        print("Nothing changed.")
        return 0
    projects = modstatus.projects()
    by_key = {p.key: p for p in projects}
    hits, runs_on, owned, loose = affected(paths, projects)
    changed = set(paths)
    missing = 0

    print(f"Affected mods for {len(paths)} changed path{'s' if len(paths) != 1 else ''}:")
    for key, reasons in sorted(hits.items(), key=lambda i: i[0].lower()):
        p = by_key[key]
        changelog = f"{p.dir}/CHANGELOG.md"
        versions, notes = p.changelog()
        if versions is None:
            note = "no CHANGELOG.md - create one with an [Unreleased] section and the line"
            missing += 1
        elif changelog in changed and notes:
            note = "changelog line staged"
        else:
            note = "needs a line under [Unreleased] in its CHANGELOG.md"
            missing += 1
        print(f"- {p.name} ({key}): {', '.join(reasons)} - {note}")
        if "its folder" in reasons and f"{p.dir}/STATUS.md" not in changed:
            print("  finishes or changes a feature? its STATUS.md line changes in this commit")
    for key, files in sorted(runs_on.items(), key=lambda i: i[0].lower()):
        if key not in hits:
            sample = sorted(files)[0] + (f" and {len(files) - 1} more" if len(files) > 1 else "")
            print(f"- {by_key[key].name} ({key}): runs on or is built with {sample} - a changelog line only "
                  "if it now needs the new version")
    tooling = [p for p in paths if modstatus.matches(p, modstatus.RELEASE_TOOLING)]
    if tooling:
        print(f"- every project's release: {', '.join(tooling)} is release tooling")
    if loose:
        print(f"- no mod: {', '.join(loose[:8])}{' ...' if len(loose) > 8 else ''}")

    kinds = touched_kinds([p for files in owned.values() for p in files])
    if kinds and owned:
        import collisions
        found = [c for c in collisions.compute(projects)
                 if c.kind in kinds and any(c.involves(k) for k in owned)]
        open_ = [c for c in found if not collisions.decided(c, by_key)]
        if found:
            print(f"\nCollisions of the changed mods: {len(found) - len(open_)} decided in STATUS.md, "
                  f"{len(open_)} not")
        for c in open_:
            missing += 1
            via = "".join(f"; {m} as {n}" for m, n in c.via)
            print(f"- {c.a} <-> {c.b}: {c.what}{via} - ask the user (deploy apart, or fixed by ...) and "
                  f"record it under ## Collisions in both STATUS.md files")
    return 1 if missing else 0


if __name__ == "__main__":
    sys.exit(main())
