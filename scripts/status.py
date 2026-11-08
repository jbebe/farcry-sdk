"""The mod board: where every mod stands, or one mod in full.

    python scripts/status.py                 # one table of every mod
    python scripts/status.py aiming          # one mod: features, approvals, checks, issues
    python scripts/status.py --session       # the board plus the session rules, for the SessionStart hook
    python scripts/status.py --fit           # each mod's purpose beside its features, to judge the fit
    python scripts/status.py --offline ...   # without GitHub (no issues, local tags only)
"""
import argparse
import datetime as dt
import sys
from collections import defaultdict

import modstatus

SESSION_RULES = """\
Rules for this session (format and details: .claude/skills/status/SKILL.md):
- If the board above flags a Nexus check, say so in your first reply.
- When the user says a mod played well or is good, or that its BBCode and short message are checked,
  record the approval in that mod's STATUS.md at the current commit and tell them in one line.
- A feature or idea that comes up mid-work goes into its mod's STATUS.md as `new` right away (one-line
  note to the user); a commit that finishes or changes a feature updates its line in the same commit."""


def short(check):
    """A failing or stale check in a few words, for the board's cell."""
    if check.name == "features":
        return check.detail
    if check.state == "stale":
        return f"{check.name} stale"
    if check.name == "changelog":
        return "no changelog" if check.detail.startswith("no CHANGELOG") else "no changelog notes"
    if check.name == "assets":
        return f"{check.name}: {check.detail}"
    return check.name


def version_cell(state):
    released = state.tag[1] if state.tag else ""
    if state.prepared:
        return f"{released} -> {state.prepared} prepared" if released else f"{state.prepared} prepared"
    return released or "never released"


def bugs_cell(state):
    bugs = [i for i in state.issues if "bug" in i.labels]
    counts = defaultdict(int)
    for issue in bugs:
        counts[issue.priority or "?"] += 1
    return " ".join(f"{p}:{counts[p]}" for p in ("p1", "p2", "p3", "?") if counts[p])


def waits_cell(state):
    return "; ".join(text if tag is None else tag for tag, text, met in state.waits if not met)


def phrase(collision, mods=2):
    if not collision.what.startswith("hook "):
        return collision.what
    return ("all " if mods > 2 else "both ") + collision.what


def collision_lines(projects, only=None):
    try:
        import collisions
        found = collisions.compute(projects)
    except Exception as error:
        return [f"- collisions not computed: {error}"], []
    by_key = {p.key: p for p in projects}
    undecided, decided = {}, []
    for c in found:
        if only and not c.involves(only):
            continue
        decision = collisions.decided(c, by_key)
        if decision:
            decided.append((c, decision))
            continue
        first, mods = undecided.setdefault((c.kind, c.thing, c.what), (c, {}))
        for mod in (c.a, c.b):
            mods.setdefault(mod, "")
        mods.update(dict(c.via))
    lines = []
    for first, mods in undecided.values():
        names = ", ".join(f"{m} ({n})" if n else m for m, n in sorted(mods.items(), key=lambda i: i[0].lower()))
        lines.append(f"- {names}: {phrase(first, len(mods))}")
    return sorted(lines), decided


def board(states, issues, projects, session=False):
    now = dt.datetime.now()
    out = [f"## Mod board, {now:%Y-%m-%d %a %H:%M}", ""]
    due, last = modstatus.nexus_due(now)
    if due:
        age = f"{last:%Y-%m-%d}, {(now - last).days} days ago" if last else "never"
        out += [f"**Nexus check due**: last capture {age}. Capture the Posts, Bugs and PM pages with the "
                "bookmarklet, then run /nexus-inbox.", ""]
    if issues is None:
        out += ["GitHub unreachable: no issues, and only local tags.", ""]
    out += ["| Mod | Stage | Version | Since tag | Bugs | Missing for release | Waiting on |",
            "|---|---|---|---|---|---|---|"]
    for state in states:
        p = state.project
        missing = ", ".join(short(c) for c in state.checks if c.state != "pass")
        since = str(len(state.unreleased)) if state.tag else f"all {len(state.unreleased)}"
        if state.tag and not state.tag[2]:
            since = "tag not fetched"
        errors = f" (STATUS.md: {len(p.status.errors)} bad lines)" if p.status.errors else ""
        out.append(f"| {p.name} | {p.status.stage}{errors} | {version_cell(state)} | {since} | "
                   f"{bugs_cell(state)} | {missing} | {waits_cell(state)} |")
    homeless = [i for i in issues or [] if "needs-home" in i.labels]
    untracked = [i for i in issues or [] if not any(l.startswith("mod:") for l in i.labels) and i not in homeless]
    for label, found in (("without a mod label", untracked), ("needing a home (a new mod?)", homeless)):
        if found:
            out += ["", f"Open issues {label}: {len(found)} ({', '.join('#' + str(i.number) for i in found)})"]
    lines, _ = collision_lines(projects)
    if lines:
        out += ["", "Collisions with no decision in either STATUS.md:"] + lines
    out += ["", "Details: /status <mod>. Release bar: /release-check <mod>."]
    if session:
        out += ["", SESSION_RULES]
    return "\n".join(out)


def detail(state, projects):
    p = state.project
    s = p.status
    out = [f"## {p.name} ({p.dir})", ""]
    if s.purpose:
        out.append(f"Purpose: {s.purpose}")
    tag = f"released {state.tag[1]} ({state.tag[0]})" if state.tag else "never released"
    prepared = f", {state.prepared} prepared in the changelog" if state.prepared else ""
    out += [f"Stage: {s.stage}, {tag}{prepared}, {len(state.unreleased)} commits since", ""]
    for error in s.errors:
        out.append(f"- STATUS.md {error}")

    counts = ", ".join(f"{sum(f.state == st for f in s.features)} {st}" for st in modstatus.STATES
                       if any(f.state == st for f in s.features))
    out += [f"### Features ({counts})", ""]
    out += [f"- [{f.state}] {f.text}" for f in s.features] + [""]

    out += ["### Release bar (the cheap rows; /release-check runs the rest)", ""]
    for c in state.checks:
        fix = f" - {c.fix}" if c.state != "pass" and c.fix else ""
        out.append(f"- {c.state}: {c.name}, {c.detail}{fix}")
    out.append("")

    if state.waits:
        out += ["### Release order", ""]
        out += [f"- {'met' if met else 'waiting'}: {text}" for tag, text, met in state.waits] + [""]

    lines, decided = collision_lines(projects, only=p.key)
    if lines or decided or s.collisions:
        out += ["### Collisions", ""]
        out += [f"- decided ({decision}): {c.other(p.key)}, {phrase(c)}" for c, decision in decided]
        out += [f"- undecided: {line[2:]}" for line in lines]
        recorded = {c.other(p.key).lower() for c, _ in decided}
        out += [f"- recorded, not detected now: {other}: {text}" for other, text in s.collisions.items()
                if other not in recorded and not any(other in line.lower() for line in lines)]
        out.append("")

    if state.issues:
        out += ["### Open issues", ""]
        out += [f"- #{i.number} {i.title} [{', '.join(i.labels)}]" for i in state.issues] + [""]

    if state.unreleased:
        out += [f"### Commits since {state.tag[0] if state.tag else 'the start'}", ""]
        out += [f"- {sha} {subject}" for sha, subject in state.unreleased[:25]]
        if len(state.unreleased) > 25:
            out.append(f"- ... and {len(state.unreleased) - 25} more")
    return "\n".join(out)


def fit(projects, issues):
    out = ["## Purpose and features, mod by mod", "",
           "For each feature that does not serve its mod's purpose, and each issue below with no mod, suggest "
           "the mod it fits, or a new mod (name and one-line purpose) when none does. Skip scrapped features.", ""]
    for p in projects:
        out += [f"### {p.name} ({p.key})", f"Purpose: {p.status.purpose or '(none written)'}"]
        out += [f"- [{f.state}] {f.text}" for f in p.status.features if f.state != "scrapped"] + [""]
    loose = [i for i in issues or [] if not any(l.startswith("mod:") for l in i.labels)]
    if loose:
        out += ["### Issues with no mod", ""]
        out += [f"- #{i.number} {i.title} [{', '.join(i.labels)}]" for i in loose]
    return "\n".join(out)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("mod", nargs="?")
    parser.add_argument("--session", action="store_true")
    parser.add_argument("--fit", action="store_true")
    parser.add_argument("--offline", action="store_true")
    args = parser.parse_args()

    projects = modstatus.projects()
    if args.fit:
        print(fit(projects, None if args.offline else modstatus.github()[0]))
        return 0
    if args.mod:
        project = modstatus.find(args.mod, projects)
        if project is None:
            print(f"No mod matches '{args.mod}'. Known: {', '.join(p.key for p in projects)}")
            return 1
        states, _, _ = modstatus.gather([project], online=not args.offline)
        print(detail(states[0], projects))
        return 0
    states, issues, _ = modstatus.gather(online=not args.offline)
    print(board(states, issues, projects, session=args.session))
    return 0


if __name__ == "__main__":
    sys.exit(main())
