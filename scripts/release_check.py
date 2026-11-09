"""The release bar for one mod: each row pass, fail, stale or review, with what to do about it.

    python scripts/release_check.py <mod>               # every row, the build and tests included
    python scripts/release_check.py <mod> --hotfix      # all but the user's page-text re-check
    python scripts/release_check.py <mod> --no-build    # skip the build and unit tests
    python scripts/release_check.py <mod> --no-game     # skip the game tests in the mod's tests/*.py

Two rows only flag candidates - debug lines and unused parts - for a reader to go through; they
show as "review" until that is done. The build and tests are the steps the mod's release workflow
runs, read from the workflow, so this cannot test something the release does not.
"""
import argparse
import os
import re
import subprocess
import sys
import tempfile
from collections import defaultdict
from pathlib import Path

import collisions
import modstatus
from modstatus import REPO, Check

SOURCE = (".cpp", ".h", ".hpp", ".inl", ".fx", ".hlsl", ".fxh")
SCRIPT = (".py", ".ps1", ".cmake", ".txt", ".json", ".js", ".ts")
LAYER_TEXT = (".xml", ".ini", ".json", ".lua", ".txt", ".rml")

LOG_CALL = re.compile(r"\b(?:Logf|Log|OutputDebugStringA?|printf|puts|fprintf\s*\(\s*std(?:err|out)\s*,)\s*\(?"
                      r"[^\"\n]*\"([^\"]*)")
FAILURE = re.compile(r"cannot|can't|could not|couldn't|not found|fail|refus|missing|unable|error|invalid|"
                     r"skip|not hook|unknown|unsupported|not match|denied|crash|abort|without|no longer|"
                     r"gave up|out of|too (?:many|few|long)|disabled|\bno\b|never|instead|as the game", re.I)
MARKER = re.compile(r"\b(?:TODO|FIXME|XXX|HACK)\b|#\s*if\s+0\b|#\s*ifn?def\s+\w*DEBUG|__debugbreak|"
                    r"\bDebugBreak\s*\(|\b(?:g_debug\w*|kDebug\w*|DEBUG_\w+)\b|std::c(?:out|err)")
COMMENTED_CODE = re.compile(r"^\s*//\s*(?:(?:if|for|while|switch)\s*\(.*|return\b[^.]*;|"
                            r"[\w:.\->\[\]]+\s*(?:\(.*\)|[+\-*/]?=\s*[^=].*|\+\+|--)\s*;)\s*$")
NOT_A_TYPE = r"(?!\s*(?:return|else|throw|case|delete|new|goto|co_return|if|for|while|switch|catch|do)\b)"
FUNCTION_DEF = re.compile(rf"^{NOT_A_TYPE}\s*(?:[\w:<>,*&~]+\s+)+?[*&]*"
                          r"(?:\w+::)*(\w+)\s*\([^;{}]*\)\s*(?:const\s*)?(?:noexcept\s*)?(?:override\s*)?\{?\s*$")
GLOBAL_DEF = re.compile(rf"^{NOT_A_TYPE}\s*(?:[\w:<>,()]+[\s*&]+)+(k[A-Z]\w*|g_\w+)\s*(?:\[[^\]]*\]\s*)?(?:=|\{{|;)")
DECLARATION = re.compile(rf"^{NOT_A_TYPE}\s*(?:[\w:<>,*&]+\s+)+[*&]*(\w+)\s*\([^;{{}}]*\)\s*(?:const\s*)?;\s*$")
PY_DEF = re.compile(r"^def (\w+)|^([A-Z][A-Z0-9_]{2,})\s*=")
NULL_SETTING = re.compile(r"\{\s*\"([^\"]+)\"\s*,\s*FCSE_\w+\([^)]*\)\s*,\s*(?:nullptr|NULL|0)\s*[,}]")
ENTRY_POINTS = {"main", "DllMain", "FCSE_Load", "FCSE_OnRegisterFunctions", "FCSE_Unload", "WinMain"}


def sources(project, suffixes, folder=""):
    """The project's files with these suffixes, as paths - what git tracks or does not ignore."""
    return [project.path / p for p in project.files()
            if p.lower().endswith(suffixes) and (not folder or modstatus.under(p, folder))]


def lines_of(path):
    try:
        return path.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:
        return []


def rel(path):
    return path.relative_to(REPO).as_posix()


def run_logged(project, name, argv, cwd):
    """Runs one step into a log of its own: None when it passed, else (name, log, its last lines)."""
    logs = Path(tempfile.gettempdir()) / "release-check" / project.key
    logs.mkdir(parents=True, exist_ok=True)
    log = logs / f"{re.sub(r'[^a-z0-9]+', '-', name.lower()).strip('-')}.log"
    with open(log, "w", encoding="utf-8", errors="replace") as out:
        result = subprocess.run(argv, cwd=cwd, stdout=out, stderr=subprocess.STDOUT)
    return None if result.returncode == 0 else (name, log, lines_of(log)[-15:])


# --- debug lines ----------------------------------------------------------------------------------

def debug_candidates(project):
    found = []
    for path in sources(project, SOURCE, "src") + sources(project, SOURCE, "include"):
        for number, line in enumerate(lines_of(path), 1):
            log = LOG_CALL.search(line)
            if log and not FAILURE.search(log.group(1)) and "//" not in line[:log.start()]:
                found.append((path, number, "log that is not a failure", line.strip()))
            elif MARKER.search(line):
                found.append((path, number, "debug marker", line.strip()))
            elif COMMENTED_CODE.match(line):
                found.append((path, number, "commented-out code?", line.strip()))
    for path in sources(project, (".py",), "data"):
        for number, line in enumerate(lines_of(path), 1):
            if re.search(r"\b(?:TODO|FIXME|XXX|HACK)\b", line):
                found.append((path, number, "debug marker", line.strip()))
    return found


# --- unused parts ---------------------------------------------------------------------------------

def unused_candidates(project, vanilla):
    files = {path: lines_of(path) for path in sources(project, SOURCE + SCRIPT + LAYER_TEXT)}

    definitions, kinds = defaultdict(set), {}
    for path, lines in files.items():
        suffix = path.suffix.lower()
        for number, line in enumerate(lines, 1):
            if suffix in SOURCE:
                for regex, kind in ((FUNCTION_DEF, "function"), (GLOBAL_DEF, "constant or global")):
                    m = regex.match(line)
                    if m and m.group(1) not in ENTRY_POINTS and not m.group(1).startswith("FCSE_"):
                        definitions[m.group(1)].add((path, number))
                        kinds.setdefault(m.group(1), (kind, path, number))
                declaration = DECLARATION.match(line)
                if declaration:
                    definitions[declaration.group(1)].add((path, number))
            elif suffix == ".py":
                m = PY_DEF.match(line)
                if m:
                    name = m.group(1) or m.group(2)
                    definitions[name].add((path, number))
                    kinds.setdefault(name, ("function" if m.group(1) else "constant", path, number))

    # Only the names defined above are ever looked up, so only they are indexed.
    lines_with = defaultdict(set)
    for path, lines in files.items():
        for number, line in enumerate(lines, 1):
            for token in set(re.findall(r"\w+", line)) & kinds.keys():
                lines_with[token].add((path, number))

    found = []
    for name, (kind, path, number) in sorted(kinds.items(), key=lambda i: (rel(i[1][1]), i[1][2])):
        if not lines_with[name] - definitions[name]:
            found.append((path, number, f"{kind} {name} is never used", files[path][number - 1].strip()))

    for path, lines in files.items():
        if path.suffix.lower() == ".cpp":
            for number, line in enumerate(lines, 1):
                for m in NULL_SETTING.finditer(line):
                    found.append((path, number, f"setting \"{m.group(1)}\" has no callback, so nothing reads it",
                                  line.strip()))

    text = "\n".join(line for lines in files.values() for line in lines).lower()
    unnamed = defaultdict(list)
    for inside, path in sorted(collisions.layer_files(project, plugins=True).items()):
        name = path.name.lower()
        if path.suffix.lower() in (".dll", ".pdb") or name.startswith("."):
            continue
        if inside.startswith("mods/") and (vanilla is None or modstatus.container(inside) or name in vanilla):
            continue
        if path.stem.lower() in text or name in text:
            continue
        # A world's generated data is read by convention (nv_0.nvm, nv_1.nvm ...), never by name.
        world = re.search(r"/(?:levels|worlds)/([^/]+)/", inside)
        if world and (world.group(1).lower() in text or world.group(1).lower() in (vanilla or ())):
            continue
        unnamed[path.parent].append(path)
    # Hashed or generated names cannot be found as text, so these are listed a folder at a time.
    for folder, paths in sorted(unnamed.items()):
        where = rel(folder)[len(project.dir) + len("/layer/"):]
        reason = ("named nowhere in the mod's code" if where.startswith("plugins")
                  else "not game files, and no script or text in the mod names them")
        names = ", ".join(p.name for p in paths[:2]) + (f" and {len(paths) - 2} more" if len(paths) > 2 else "")
        found.append((folder, 0, f"{len(paths)} layer file{'s' if len(paths) > 1 else ''} {reason}", names))
    return found


def vanilla_names():
    """Every file and folder name in the extracted retail game, or None when tmp/gamefiles is absent."""
    root = REPO / "tmp" / "gamefiles"
    if not root.is_dir():
        return None
    return {name.lower() for _, folders, names in os.walk(root) for name in folders + names}


# --- build and tests ------------------------------------------------------------------------------

def workflow_steps(path):
    """(name, command, working directory) of each single-line build, test or check command the
    workflow runs - the ones that start a script, dotnet or npm."""
    lines = path.read_text(encoding="utf-8").splitlines()
    default = ""
    for i, line in enumerate(lines):
        if re.match(r"^\s*working-directory:", line) and any("defaults:" in l for l in lines[max(0, i - 3):i]):
            default = line.split(":", 1)[1].strip()
    steps, current = [], None
    for line in lines:
        start = re.match(r"^\s*-\s+(name|uses|run):\s*(.*)$", line)
        if start:
            current = {"name": "", "run": "", "dir": default}
            steps.append(current)
            line = f"{start.group(1)}: {start.group(2)}"
        if current is None:
            continue
        key = re.match(r"^\s*(name|run|working-directory):\s*(.*)$", line)
        if key:
            current["dir" if key.group(1) == "working-directory" else key.group(1)] = key.group(2).strip()
    return [(step["name"] or step["run"], step["run"].removeprefix("pwsh "), step["dir"] or ".")
            for step in steps
            if step["run"] and step["run"] != "|" and re.match(r"^(?:\.[\\/]|pwsh |dotnet |npm )", step["run"])]


def build_steps(project):
    """The release workflow's build and test commands, then the byte patterns for a plugin."""
    steps = workflow_steps(project.workflow) if project.workflow else []
    if not project.workflow and (project.path / "build.ps1").exists():
        steps = [("build.ps1", r".\build.ps1", project.dir)]
    if project.is_plugin and (project.path / "src").is_dir():
        steps.append(("byte patterns", f"python scripts/verify_patterns.py {project.dir}/src", "."))
    return steps


def build_and_test(project, steps):
    if not steps:
        return Check("build", "fail", "no release workflow and no build.ps1", "add the project's release workflow"), []
    failures = [f for name, command, directory in steps
                if (f := run_logged(project, name, ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass",
                                                    "-Command", command], REPO / directory))]
    if failures:
        detail = "; ".join(f"{name} failed, log {log}" for name, log, _ in failures)
        return Check("build", "fail", detail, "fix the build or test, then run this again"), failures
    return Check("build", "pass", ", ".join(name for name, _, _ in steps)), []


def unit_tests(project, steps):
    """A mod's tests/*.cpp count only if its release build runs them; a tool's are its workflow's."""
    if not project.is_mod:
        return Check("unit tests", "pass", "the release workflow's own test steps, run in the build row")
    tests = sorted((project.path / "tests").glob("*.cpp"))
    if not tests:
        return Check("unit tests", "pass", "none: only a plugin with a lot of pure logic has them")
    if not any("-Tests" in command for _, command, _ in steps):
        return Check("unit tests", "fail", f"{len(tests)} test files the release build does not run",
                     "pass -Tests in the release workflow's build step")
    return Check("unit tests", "pass", f"{len(tests)} test files, run in the build row")


def game_tests(project, skip):
    """Each tests/*.py, an fc2drive script that launches the game and exits 1 on a failed check."""
    tests = sorted((project.path / "tests").glob("*.py"))
    if not tests:
        return Check("game tests", "pass", "none"), []
    if skip:
        return Check("game tests", "fail", f"{len(tests)} not run (--no-game)",
                     "deploy the mod, get the user's OK to launch the game, then run without --no-game"), []
    failures = [f for test in tests
                if (f := run_logged(project, f"game test {test.stem}", [sys.executable, str(test)], REPO))]
    if failures:
        return Check("game tests", "fail", f"{len(failures)} of {len(tests)} failed",
                     "read the logs below and the run folders under tools/fc2drive/runs"), failures
    return Check("game tests", "pass", f"{len(tests)} passed"), []


# --- the bar --------------------------------------------------------------------------------------

def release_order(state):
    waiting = [text for _, text, met in state.waits if not met]
    if waiting:
        return Check("release order", "fail", "; ".join(waiting), "release those first, or settle the wait")
    return Check("release order", "pass", "all met" if state.waits else "nothing to wait for")


def changelog_section(state):
    if not state.prepared:
        return Check("changelog", "fail", "no version section newer than the last tag",
                     "add ## [x.y.z] - date above the others (RELEASING.md)")
    return Check("changelog", "pass", f"{state.prepared} prepared")


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("mod")
    parser.add_argument("--hotfix", action="store_true")
    parser.add_argument("--no-build", action="store_true")
    parser.add_argument("--no-game", action="store_true")
    args = parser.parse_args()

    projects = modstatus.projects()
    project = modstatus.require(args.mod, projects)
    states, _ = modstatus.gather(projects, [project])
    state = states[0]
    checks = {c.name: c for c in state.checks}

    debug = debug_candidates(project)
    unused = unused_candidates(project, vanilla_names())
    steps = build_steps(project)
    if args.no_build:
        build, failures = Check("build", "fail", "not run (--no-build)", "run without --no-build"), []
    else:
        build, failures = build_and_test(project, steps)
    game, game_failures = game_tests(project, args.no_game)
    failures += game_failures

    rows = [
        checks["features"],
        Check("debug lines", "review" if debug else "pass", f"{len(debug)} candidates" if debug else "none found",
              "go through the candidates below"),
        Check("unused parts", "review" if unused else "pass", f"{len(unused)} candidates" if unused else "none found",
              "go through the candidates below"),
        checks["simplify"],
        build,
        unit_tests(project, steps),
        game,
        checks["played"],
        checks["skeleton"],
        checks["assets"],
        Check("page text", "pass", "not needed for a hotfix") if args.hotfix else checks["page text"],
        changelog_section(state),
        release_order(state),
    ]

    mode = " (hotfix)" if args.hotfix else ""
    tag = state.tag[0] if state.tag else "never released"
    print(f"## Release check: {project.name}{mode}, last tag {tag}\n")
    print("| Row | Result | Detail | What to do |\n|---|---|---|---|")
    for c in rows:
        print(f"| {c.name} | {c.state} | {c.detail} | {c.fix if c.state != 'pass' else ''} |")

    for title, items in (("Debug-line candidates", debug), ("Unused-part candidates", unused)):
        if items:
            print(f"\n### {title}\n")
            for path, number, why, text in items:
                at = f"{rel(path)}:{number}" if number else rel(path)
                print(f"- {at} ({why}): `{text[:140]}`")
    for name, log, tail in failures:
        print(f"\n### {name} failed - last lines of {log}\n\n```")
        print("\n".join(tail))
        print("```")

    if all(c.state == "pass" for c in rows):
        print(f"\n**{project.name} is ready to release.** Push main, then run Actions -> "
              f"{project.name} release -> Run workflow.")
        return 0
    left = [c.name for c in rows if c.state != "pass"]
    print(f"\nNot ready: {', '.join(left)}.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
