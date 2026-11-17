"""What an upload to Nexus Mods requires, set on the version it uploaded.

    python scripts/nexus_requires.py check [<project>...]     # what Nexus has, what a release would send
    python scripts/nexus_requires.py verify <project>         # fails while a required version is not on Nexus
    python scripts/nexus_requires.py send <project> <version> # after the upload
    python scripts/nexus_requires.py cap <fcse version>       # after an FCSE upload

A project's requirements are its assets/nexus-requires.txt and, for an FCSE plugin, the FCSE releases
whose plugin ABI is the one it builds against. RELEASING.md has the format. Every call that reaches
Nexus needs NEXUSMODS_API_KEY, and without it warns and does nothing.
"""
import functools
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
from collections import namedtuple
from dataclasses import dataclass

import modstatus
from modstatus import REPO, git, version_key

API = "https://api.nexusmods.com/v3"
ABI_HEADER = "tools/FCSE/include/fcse_api.h"
REQUIRES = "assets/nexus-requires.txt"
ALTERNATIVE = re.compile(r"^([\w.-]+)\s*>=\s*(\S+?)(?:\s*,\s*<=\s*(\S+))?$")


class Refused(Exception):
    pass


@dataclass(frozen=True)
class Range:
    prefix: str
    min: str
    max: str | None = None

    def __str__(self):
        return f"{self.prefix} >= {self.min}" + (f", <= {self.max}" if self.max else "")


def parse(text, source):
    """Requirement lines: each one a list of alternatives, `<tag prefix> >= <version>[, <= <version>]`
    joined by `|`."""
    definitions = []
    for number, line in enumerate(text.splitlines(), 1):
        line = line.split("#", 1)[0].strip()
        if not line:
            continue
        alternatives = []
        for part in line.split("|"):
            m = ALTERNATIVE.match(part.strip())
            if not m:
                raise Refused(f"{source}:{number}: '{part.strip()}' is not '<project> >= <version>[, <= <version>]'")
            alternatives.append(Range(*m.groups()))
        definitions.append(alternatives)
    return definitions


@functools.cache
def abi(ref=None):
    """FCSE_API_VERSION at a git ref, or in the working tree."""
    text = (git("show", f"{ref}:{ABI_HEADER}", check=False) if ref
            else (REPO / ABI_HEADER).read_text(encoding="utf-8"))
    m = re.search(r"#define FCSE_API_VERSION (\d+)", text)
    return int(m.group(1)) if m else None


@functools.cache
def fcse_releases():
    return modstatus.tag_versions("fcse", modstatus.local_tags())


def fcse_range(wanted, available=None, superseded=False):
    """The FCSE releases that load a plugin built against ABI `wanted`; FCSE loads only its own ABI.
    With `available`, the lowest is the lowest of those on Nexus. `superseded` caps the range even
    when no tagged release has changed the ABI yet."""
    releases = fcse_releases()
    loads = [v for v in releases if abi(f"fcse-{v}") == wanted]
    lowest = [v for v in loads if available is None or v in available]
    if not lowest:
        where = " on Nexus" if available is not None else ""
        raise Refused(f"no FCSE release{where} loads plugin ABI {wanted} yet")
    later = superseded or releases[-1] != loads[-1]
    return Range("fcse", lowest[0], loads[-1] if later else None)


def requirements(project, fcse_available=None):
    path = project.path / REQUIRES
    definitions = parse(path.read_text(encoding="utf-8"), path.relative_to(REPO)) if path.exists() else []
    if project.is_plugin:
        if any(r.prefix == "fcse" for d in definitions for r in d):
            raise Refused(f"{REQUIRES} names fcse; a plugin's FCSE range comes from its plugin ABI")
        definitions.insert(0, [fcse_range(abi(), fcse_available)])
    return definitions


def render(definitions):
    return "; ".join(" | ".join(map(str, d)) for d in definitions)


def prefixes(projects):
    return {p.tag_prefix: p for p in projects if p.tag_prefix}


def applies(project):
    return project.tag_prefix not in ("", "fcse") and (project.is_plugin or (project.path / REQUIRES).exists())


def stated_minimums(text, name):
    """The versions a page or README gives as `<name> ... x.y.z or later`."""
    text = re.sub(r"\[/?\w+(?:=[^\]]*)?\]|\*\*", "", text)
    return {m.group(1) for m in re.finditer(rf"\b{re.escape(name)}\b[^.\n]*?(\d+\.\d+\.\d+),? or later", text)}


def problems(project, projects):
    """What stops the release from tags and text alone: a required version not released, or a README
    or page that states another minimum."""
    by_prefix = prefixes(projects)
    try:
        definitions = requirements(project)
    except Refused as e:
        return [str(e)]
    tags = modstatus.local_tags()
    found = []
    for r in (r for d in definitions for r in d):
        target = by_prefix.get(r.prefix)
        if not target:
            found.append(f"'{r.prefix}' is no released project")
            continue
        found += [f"{r.prefix}-{v} is not released" for v in (r.min, r.max) if v and f"{r.prefix}-{v}" not in tags]
        for doc in ("README-release.md", "assets/nexus-description.bbcode"):
            path = project.path / doc
            stated = stated_minimums(path.read_text(encoding="utf-8"), target.name) if path.exists() else {r.min}
            if stated != {r.min}:
                found.append(f"{doc} says {target.name} {' and '.join(sorted(stated)) or 'with no version'} "
                             f"or later, not {r.min}")
    return found


# --- Nexus ----------------------------------------------------------------------------------------

def call(method, path, body=None):
    request = urllib.request.Request(
        API + path, method=method, data=None if body is None else json.dumps(body).encode(),
        headers={"apikey": os.environ["NEXUSMODS_API_KEY"], "Accept": "application/json",
                 "Content-Type": "application/json", "User-Agent": "farcry-sdk-release"})
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            data = response.read()
    except urllib.error.HTTPError as e:
        raise Refused(f"{method} {path}: {e.code} {e.read().decode(errors='replace')[:500]}") from None
    return json.loads(data) if data else None


NexusIds = namedtuple("NexusIds", "mod file")


def nexus_ids(project):
    return workflow_ids(project.workflow) if project and project.workflow else NexusIds("", "")


@functools.cache
def workflow_ids(workflow):
    """The mod and file IDs in a release workflow; the file is the upload that sets the page's version."""
    text = workflow.read_text(encoding="utf-8")
    mod = re.search(r"NEXUS_MOD_ID:\s*'(\d*)'", text)
    mod = mod.group(1) if mod else ""
    for step in re.split(r"\n\s*- name:", text):
        env = re.search(r"file_id:\s*\$\{\{\s*env\.(\w+)", step)
        if env and "update_mod_version: 'true'" in step:
            file = re.search(rf"{env.group(1)}:\s*'(\d*)'", text)
            return NexusIds(mod, file.group(1) if file else "")
    return NexusIds(mod, "")


@functools.cache
def versions(file_id):
    """The file's versions, oldest upload first."""
    return sorted(call("GET", f"/mod-files/{file_id}/versions")["data"]["versions"], key=lambda v: v["uploaded_at"])


def version_id(file_id, version):
    ids = [v["id"] for v in versions(file_id) if v["version"] == version]
    return ids[-1] if ids else None


def resolve(definitions, by_prefix):
    """The ranges as the API takes them, every version looked up on Nexus."""
    out, missing = [], []
    for definition in definitions:
        ranges = []
        for r in definition:
            file_id = nexus_ids(by_prefix.get(r.prefix)).file
            if not file_id:
                raise Refused(f"'{r.prefix}' is no project uploaded to Nexus")
            ids = [version_id(file_id, v) if v else None for v in (r.min, r.max)]
            missing += [f"{r.prefix} {v}" for v, i in zip((r.min, r.max), ids) if v and not i]
            ranges.append({"min_version_id": ids[0], "max_version_id": ids[1]})
        out.append({"ranges": ranges})
    if missing:
        raise Refused("not on Nexus yet: " + ", ".join(missing))
    return out


def planned(project, by_prefix):
    fcse_file = nexus_ids(by_prefix["fcse"]).file
    definitions = requirements(project, {v["version"] for v in versions(fcse_file)})
    return definitions, resolve(definitions, by_prefix)


def live(version):
    return call("GET", f"/mod-file-versions/{version}/dependencies/ranges")["dependency_definitions"]


def as_input(definitions):
    return [{"ranges": [{"min_version_id": r["min_version"]["id"],
                         "max_version_id": (r["max_version"] or {}).get("id")} for r in d["ranges"]]}
            for d in definitions]


def shape(body):
    return sorted(sorted((r["min_version_id"], r["max_version_id"] or "") for r in d["ranges"]) for d in body)


def describe(definitions):
    """Live definitions as text, `FCSE >= 1.3.0 | ...; ...`."""
    def one(r):
        top = f", <= {r['max_version']['version']}" if r["max_version"] else ""
        return f"{r['target_mod_file']['name']} >= {r['min_version']['version']}{top}"
    return "; ".join(" | ".join(one(r) for r in d["ranges"]) for d in definitions) or "nothing"


def put(mod_id, version, body):
    """Switches the mod to per-file requirements, sets the version's, and reads them back."""
    call("PUT", f"/mods/{mod_id}/toggle-legacy-mod-requirements", {"enabled": False})
    call("PUT", f"/mod-file-versions/{version}/dependencies/ranges", {"dependency_definitions": body})
    if shape(as_input(live(version))) != shape(body):
        raise Refused("Nexus holds other requirements than were sent")


# --- commands -------------------------------------------------------------------------------------

def check(projects, by_prefix):
    for project in projects:
        file_id = nexus_ids(project).file
        try:
            uploads = versions(file_id) if file_id else []
            now = f"{uploads[-1]['version']} requires {describe(live(uploads[-1]['id']))}" if uploads else "no Nexus file"
            sends = render(planned(project, by_prefix)[0])
        except Refused as e:
            sends = f"refused - {e}"
        print(f"{project.dir}: Nexus {now}; a release sends {sends}")


def verify(project, by_prefix):
    print(f"{project.dir}: every requirement is on Nexus - {render(planned(project, by_prefix)[0])}")


def send(project, version, by_prefix):
    mod_id, file_id = nexus_ids(project)
    if not file_id or not mod_id:
        print(f"::warning::{project.dir} has no Nexus mod or file ID - no requirements set.")
        return
    definitions, body = planned(project, by_prefix)
    # Nexus lists an upload only once it has processed it.
    for _ in range(30):
        versions.cache_clear()
        own = version_id(file_id, version)
        if own:
            break
        time.sleep(10)
    else:
        raise Refused(f"{version} is not among the file's versions on Nexus")
    put(mod_id, own, body)
    print(f"{project.dir} {version}: requires {render(definitions)}")


def cap(fcse_version, projects, by_prefix):
    """When this FCSE changed the plugin ABI, the newest upload of every plugin built for the old one
    gets an FCSE range that ends before it."""
    older = [v for v in fcse_releases() if version_key(v) < version_key(fcse_version)]
    if not older or abi(f"fcse-{older[-1]}") == abi():
        print(f"FCSE {fcse_version} keeps plugin ABI {abi()} - nothing to cap.")
        return
    fcse_file = nexus_ids(by_prefix["fcse"]).file
    available = {v["version"] for v in versions(fcse_file)}
    failed = False
    for project in (p for p in projects if p.is_plugin):
        mod_id, file_id = nexus_ids(project)
        uploads = versions(file_id) if file_id else []
        if not uploads:
            continue
        newest = uploads[-1]
        built = abi(f"{project.tag_prefix}-{newest['version']}")
        if built == abi():
            continue
        try:
            if built is None:
                raise Refused(f"no tag {project.tag_prefix}-{newest['version']} to read its plugin ABI from")
            fcse = fcse_range(built, available, superseded=True)
            current = live(newest["id"])
            keep = [d for d in current if all(r["target_mod_file"]["id"] != fcse_file for r in d["ranges"])]
            put(mod_id, newest["id"], resolve([[fcse]], by_prefix) + as_input(keep))
            print(f"{project.dir} {newest['version']}: was {describe(current)}; now {fcse}")
        except Refused as e:
            print(f"::error::{project.dir} {newest['version']}: not capped - {e}")
            failed = True
    if failed:
        raise Refused("some plugins were not capped")


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    if len(sys.argv) < 2 or sys.argv[1] not in ("check", "verify", "send", "cap"):
        sys.exit(__doc__)
    command, args = sys.argv[1], sys.argv[2:]
    if not os.environ.get("NEXUSMODS_API_KEY"):
        print("::warning::NEXUSMODS_API_KEY is not set - no Nexus requirements are read or set.")
        return
    projects = modstatus.projects()
    by_prefix = prefixes(projects)
    try:
        if command == "check":
            chosen = [modstatus.require(a, projects) for a in args] or [p for p in projects if applies(p)]
            check(chosen, by_prefix)
        elif command == "cap":
            cap(args[0], projects, by_prefix)
        else:
            project = modstatus.require(args[0], projects)
            if not applies(project):
                print(f"{project.dir} requires nothing on Nexus.")
            elif command == "verify":
                verify(project, by_prefix)
            else:
                send(project, args[1], by_prefix)
    except Refused as e:
        print(f"::error::{e}")
        sys.exit(1)


if __name__ == "__main__":
    main()
