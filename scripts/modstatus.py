"""The state of every mod, for /status, /release-check and the affected-mods check.

What is decided by hand comes from each project's STATUS.md; everything else - tags, commits since
them, open issues, staleness, dependencies - is computed here on every call, so it cannot go stale.
The STATUS.md format is documented in .claude/skills/status/SKILL.md.
"""
import concurrent.futures
import datetime as dt
import fnmatch
import json
import os
import re
import subprocess
from dataclasses import dataclass, field
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent

STATES = ("in progress", "new", "done", "on hold", "postponed", "scrapped")
OPEN_STATES = ("in progress", "new")
RECORDS = ("played", "page text", "simplify")
PAGE_TEXT = ("assets/nexus-summary.txt", "assets/nexus-description.bbcode")
SUMMARY_LIMIT = 350
IMAGE_SUFFIXES = (".png", ".jpg", ".jpeg", ".gif", ".webp")

# Inside a project, what is neither code nor data: changing it leaves a "played" approval current and
# needs no changelog line.
NOT_CODE = ("*.md", "assets", ".gitignore")


def is_code(inside):
    """Whether a path inside a project (relative to it) is code or data."""
    return not any(fnmatch.fnmatch(inside, p) or inside.startswith(p + "/") for p in NOT_CODE)

# What a mod runs on without building against it: FCSE's loader for a plugin, and JackAll's and
# Vortex's install path for a layer. A change here can change a mod's behaviour with no rebuild.
PLUGIN_RUNTIME = ("tools/FCSE/src/**",)
LAYER_RUNTIME = (
    "tools/JackAll/src/JackAll.Core/Mods/**",
    "tools/JackAll/src/JackAll.Core/Format/*Merge*.cs",
    "tools/JackAll/src/JackAll.Core/Format/**/*Merge*.cs",
    "tools/JackAll/src/JackAll.ModInstaller/**",
    "tools/JackAll/src/JackAll.Cli/Commands/Mod/**",
    "tools/vortex-farcry2/src/**",
)
# Shared by every release rather than built into any mod.
RELEASE_TOOLING = (".github/**", "scripts/changelog.ps1", "scripts/nexus-page.ps1")

BUILD_FILES = ("CMakeLists.txt", "*.cmake", "*.ps1", "*.py", "CMakePresets.json", "package.json",
               "webpack.config.js")
OUTPUT_DIRS = {"bin", "obj", "out", "node_modules", "dist", "__pycache__"}


def git(*args, check=True):
    result = subprocess.run(["git", "-C", str(REPO), *args], capture_output=True, text=True,
                            encoding="utf-8", errors="replace")
    if check and result.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)}: {result.stderr.strip()}")
    return result.stdout


def parallel(fn, items):
    with concurrent.futures.ThreadPoolExecutor(max_workers=12) as pool:
        return list(pool.map(fn, items))


def matches(path, patterns):
    return any(fnmatch.fnmatch(path, p) or path == p.rstrip("/*") for p in patterns)


@dataclass
class Record:
    kind: str
    sha: str
    date: str
    note: str


@dataclass
class Feature:
    state: str
    text: str

    @property
    def issues(self):
        return [int(n) for n in re.findall(r"#(\d+)", self.text)]


@dataclass
class Status:
    name: str = ""
    purpose: str = ""
    stage: str = ""
    features: list = field(default_factory=list)
    records: dict = field(default_factory=dict)
    waits: list = field(default_factory=list)
    collisions: dict = field(default_factory=dict)
    errors: list = field(default_factory=list)


FEATURE = re.compile(r"^- \[([a-z ]+)\]\s+(.+)$")
RECORD = re.compile(r"^- ([a-z ]+):\s+([0-9a-f]{7,40})\s+(\d{4}-\d{2}-\d{2})(?:\s+-\s+(.*))?$")
TAG = re.compile(r"^[A-Za-z0-9][\w.-]*-\d+\.\d+\.\d+(?:-[\w.]+)?$")


def parse_status(text):
    status = Status()
    section = ""
    for number, raw in enumerate(text.splitlines(), 1):
        line = raw.strip()
        if line.startswith("# "):
            status.name = line[2:].strip()
        elif line.startswith("## "):
            section = line[3:].strip().lower()
        elif line.startswith("Purpose:") and not section:
            status.purpose = line[8:].strip()
        elif line.startswith("Stage:") and not section:
            status.stage = line[6:].strip()
        elif not line.startswith("- "):
            continue
        elif section == "features":
            m = FEATURE.match(line)
            if m and m.group(1) in STATES:
                status.features.append(Feature(m.group(1), m.group(2)))
            else:
                status.errors.append(f"line {number}: not '- [<state>] <feature>' with a known state")
        elif section in ("approvals", "checks"):
            m = RECORD.match(line)
            if not m or m.group(1) not in RECORDS:
                status.errors.append(f"line {number}: not '- <kind>: <sha> <YYYY-MM-DD> - <note>'")
                continue
            record = Record(m.group(1), m.group(2), m.group(3), m.group(4) or "")
            current = status.records.get(record.kind)
            if current is None or record.date >= current.date:
                status.records[record.kind] = record
        elif section == "release order":
            body = line[2:].strip()
            after = re.match(r"after\s+(\S+)$", body)
            status.waits.append((after.group(1), body) if after and TAG.match(after.group(1))
                                else (None, body))
        elif section == "collisions":
            m = re.match(r"^- ([\w.-]+):\s+(.+)$", line)
            if m:
                status.collisions[m.group(1).lower()] = m.group(2)
            else:
                status.errors.append(f"line {number}: not '- <mod folder>: <decision>'")
    return status


@dataclass
class Project:
    dir: str
    status: Status
    tag_prefix: str = ""

    @property
    def key(self):
        return self.dir.rsplit("/", 1)[-1]

    @property
    def label(self):
        return "mod:" + self.key.lower()

    @property
    def name(self):
        return self.status.name or self.key

    @property
    def path(self):
        return REPO / self.dir

    @property
    def is_plugin(self):
        return "tools/FCSE/include" in build_deps(self)

    @property
    def has_layer(self):
        return (self.path / "layer").is_dir()

    def code_pathspecs(self):
        return [self.dir] + [f":(exclude){self.dir}/{p}" for p in NOT_CODE]

    def changelog(self):
        """The changelog's versions, newest first, and how many lines its Unreleased section holds."""
        path = self.path / "CHANGELOG.md"
        if not path.exists():
            return None, 0
        versions, unreleased, section = [], 0, None
        for line in path.read_text(encoding="utf-8").splitlines():
            m = re.match(r"^## \[([^\]]+)\]", line)
            if m:
                section = m.group(1)
                if section.lower() != "unreleased":
                    versions.append(section)
            elif section and section.lower() == "unreleased" and line.strip().startswith("-"):
                unreleased += 1
        return versions, unreleased


def tag_prefixes():
    """Each project directory's tag prefix, read from the release workflow that tags it."""
    prefixes = {}
    for workflow in (REPO / ".github" / "workflows").glob("*-release.yml"):
        text = workflow.read_text(encoding="utf-8")
        directory = re.search(r"^\s*directory:\s*(\S+)", text, re.M)
        prefix = re.search(r"^\s*tag_prefix:\s*(\S+)", text, re.M)
        if directory and prefix:
            prefixes[directory.group(1).strip("'\"")] = prefix.group(1).strip("'\"")
    return prefixes


def projects():
    prefixes = tag_prefixes()
    found = []
    for path in sorted((REPO / "mods").glob("*/STATUS.md")) + sorted((REPO / "tools").glob("*/STATUS.md")):
        directory = path.parent.relative_to(REPO).as_posix()
        status = parse_status(path.read_text(encoding="utf-8"))
        found.append(Project(directory, status, prefixes.get(directory, "")))
    return found


def find(name, candidates=None):
    """The project a user's word names: its folder, title, tag prefix or label, or a unique prefix."""
    candidates = candidates if candidates is not None else projects()
    word = name.lower().removeprefix("mod:").strip()
    for p in candidates:
        if word in (p.key.lower(), p.name.lower(), p.tag_prefix.lower()):
            return p
    hits = [p for p in candidates
            if p.key.lower().startswith(word) or p.name.lower().startswith(word)]
    return hits[0] if len(hits) == 1 else None


# --- dependencies ---------------------------------------------------------------------------------

def _normalise(path):
    """A repository path cut at its first build-output folder, or at the nearest folder that exists."""
    parts = []
    for part in path.relative_to(REPO).parts:
        if part.lower() in OUTPUT_DIRS:
            break
        parts.append(part)
    candidate = REPO.joinpath(*parts) if parts else REPO
    while candidate != REPO and not candidate.exists():
        candidate = candidate.parent
    return candidate


def _csproj_closure(csproj, seen):
    folder = csproj.parent
    if folder in seen:
        return
    seen.add(folder)
    for ref in re.findall(r'<ProjectReference\s+Include="([^"]+)"', csproj.read_text(encoding="utf-8")):
        target = (folder / ref.replace("\\", "/")).resolve()
        if target.exists():
            _csproj_closure(target, seen)


_deps_cache = {}


def _code_only(file):
    """The build file's text without comments, so a path named in prose is not a dependency."""
    text = file.read_text(encoding="utf-8", errors="replace")
    if file.suffix == ".ps1":
        text = re.sub(r"<#.*?#>", "", text, flags=re.S)
    if file.suffix == ".py":
        text = re.sub(r'""".*?"""', "", text, flags=re.S)
    if file.suffix in (".json", ".js"):
        return re.sub(r"(?m)^\s*//.*$", "", text)
    return re.sub(r"(?m)(^|\s)#.*$", r"\1", text)


def build_deps(project):
    """Repository paths outside the project that its build files point at, sorted.

    Read from CMakeLists, build scripts and the like rather than declared anywhere, so a new include
    directory or script is picked up the moment the build uses it.
    """
    if project.dir in _deps_cache:
        return _deps_cache[project.dir]
    files = [f for pattern in BUILD_FILES for f in project.path.glob(pattern)]
    files += list((project.path / "cmake").glob("*.cmake"))
    found = set()
    for file in files:
        text = _code_only(file)
        refs = [(file.parent / m.rstrip(".")).resolve()
                for m in re.findall(r"(?<![\w.])((?:\.\.[\\/])+[\w.+-]+(?:[\\/][\w.+-]+)*)", text)]
        refs += [REPO / m.rstrip(".").replace("\\", "/")
                 for m in re.findall(r"(?<![\w.])((?:tools|mods|scripts)[\\/][\w.+-]+(?:[\\/][\w.+-]+)*)", text)]
        if file.suffix == ".py":
            refs += [REPO / "scripts" / f"{m}.py"
                     for m in re.findall(r"^\s*(?:from|import)\s+(\w+)", text, re.M)
                     if (REPO / "scripts" / f"{m}.py").exists()]
        for ref in refs:
            try:
                target = _normalise(ref)
            except ValueError:
                continue
            if target.suffix == ".csproj":
                folders = set()
                _csproj_closure(target, folders)
                found.update(folders)
            else:
                found.add(target)
    deps = set()
    for target in found:
        rel = target.relative_to(REPO).as_posix() if target != REPO else ""
        if rel.count("/") < 1 or rel == project.dir or rel.startswith(project.dir + "/"):
            continue
        if project.dir.startswith(rel + "/"):
            continue
        deps.add(rel)
    # A path under another dependency adds nothing.
    result = sorted(d for d in deps if not any(d.startswith(o + "/") for o in deps if o != d))
    _deps_cache[project.dir] = result
    return result


def is_tool_dep(path):
    """A tool the build or install runs - JackAll's CLI, a third-party tool, a check script - rather
    than code or data built into the mod."""
    return (path.startswith(("tools/JackAll/src/JackAll.Cli", "tools/third-party/"))
            or path == "tools/JackAll" or re.search(r"(^|/)verify_\w+\.(py|ps1)$", path) is not None)


def compiled_deps(project):
    return [d for d in build_deps(project) if not is_tool_dep(d)]


def runtime_patterns(project):
    patterns = []
    if project.is_plugin:
        patterns += PLUGIN_RUNTIME
    if project.has_layer:
        patterns += LAYER_RUNTIME
    return patterns


# --- git state ------------------------------------------------------------------------------------

def local_tags():
    tags = {}
    for line in git("for-each-ref", "refs/tags", "--format=%(refname:short) %(*objectname) %(objectname)").splitlines():
        parts = line.split()
        if parts:
            tags[parts[0]] = parts[1] if len(parts) == 3 else parts[-1]
    return tags


def version_key(version):
    base, _, pre = version.partition("-")
    numbers = tuple(int(n) if n.isdigit() else 0 for n in base.split("."))
    return numbers, pre == "", pre


def last_tag(project, tags):
    if not project.tag_prefix:
        return None
    prefix = project.tag_prefix + "-"
    versions = [t[len(prefix):] for t in tags if t.startswith(prefix) and re.match(r"\d", t[len(prefix):])]
    if not versions:
        return None
    version = max(versions, key=version_key)
    return prefix + version, version


def has_commit(sha):
    return subprocess.run(["git", "-C", str(REPO), "cat-file", "-e", f"{sha}^{{commit}}"],
                          capture_output=True).returncode == 0


def commits(since, pathspecs):
    """(sha, subject) of each commit after `since` (or ever, if None) touching the paths."""
    rng = [f"{since}..HEAD"] if since else ["HEAD"]
    out = git("log", "--format=%h%x09%s", *rng, "--", *pathspecs, check=False)
    return [tuple(line.split("\t", 1)) for line in out.splitlines() if "\t" in line]


_porcelain = None


def dirty_paths():
    """Paths with uncommitted changes, tracked or new."""
    global _porcelain
    if _porcelain is None:
        _porcelain = []
        for line in git("status", "--porcelain", "-uall").splitlines():
            path = line[3:].split(" -> ")[-1].strip('"')
            _porcelain.append(path)
    return _porcelain


def dirty_under(pathspecs):
    includes = [p for p in pathspecs if not p.startswith(":(exclude)")]
    excludes = [p[len(":(exclude)"):] for p in pathspecs if p.startswith(":(exclude)")]
    hits = []
    for path in dirty_paths():
        if not any(path == i or path.startswith(i.rstrip("/") + "/") for i in includes):
            continue
        if any(fnmatch.fnmatch(path, e) or path.startswith(e.rstrip("/") + "/") for e in excludes):
            continue
        hits.append(path)
    return hits


def staleness(record, pathspecs):
    """Why a record no longer holds - commits or uncommitted edits to its paths since - or ''."""
    if record is None:
        return "none"
    if not has_commit(record.sha):
        return f"commit {record.sha} not found"
    changed = commits(record.sha, pathspecs)
    dirty = dirty_under(pathspecs)
    reasons = []
    if changed:
        reasons.append(f"{len(changed)} commit{'s' if len(changed) != 1 else ''} since")
    if dirty:
        reasons.append("uncommitted edits")
    return "stale: " + ", ".join(reasons) if reasons else ""


def is_ancestor(older, newer):
    return subprocess.run(["git", "-C", str(REPO), "merge-base", "--is-ancestor", older, newer],
                          capture_output=True).returncode == 0


# --- GitHub ---------------------------------------------------------------------------------------

def _repo_slug():
    url = git("remote", "get-url", "origin").strip()
    m = re.search(r"github\.com[:/]([^/]+)/(.+?)(?:\.git)?$", url)
    return (m.group(1), m.group(2)) if m else (None, None)


@dataclass
class Issue:
    number: int
    title: str
    labels: list

    @property
    def priority(self):
        return next((l for l in self.labels if re.fullmatch(r"p[1-3]", l)), "")


def github():
    """Open issues and the remote's tags in one call, or (None, {}) when GitHub cannot be reached."""
    owner, name = _repo_slug()
    if not owner:
        return None, {}
    query = """query($owner:String!, $name:String!) { repository(owner:$owner, name:$name) {
      issues(states:OPEN, first:100) { nodes { number title labels(first:30) { nodes { name } } } }
      refs(refPrefix:"refs/tags/", first:100, orderBy:{field:TAG_COMMIT_DATE, direction:DESC}) {
        nodes { name target { oid ... on Tag { target { oid } } } } } } }"""
    try:
        result = subprocess.run(["gh", "api", "graphql", "-f", f"query={query}", "-F", f"owner={owner}",
                                 "-F", f"name={name}"], capture_output=True, text=True, encoding="utf-8",
                                timeout=20)
    except (OSError, subprocess.TimeoutExpired):
        return None, {}
    if result.returncode != 0:
        return None, {}
    repo = json.loads(result.stdout)["data"]["repository"]
    issues = [Issue(n["number"], n["title"], [l["name"] for l in n["labels"]["nodes"]])
              for n in repo["issues"]["nodes"]]
    tags = {}
    for n in repo["refs"]["nodes"]:
        target = n["target"]
        tags[n["name"]] = (target.get("target") or {}).get("oid") or target["oid"]
    return issues, tags


# --- Nexus ----------------------------------------------------------------------------------------

def nexus_last_capture():
    """When Nexus pages were last captured: the newest of seen.json and Downloads/nexus-*.json."""
    stamps = []
    seen = REPO / "tools" / "nexus-inbox" / "seen.json"
    if seen.exists():
        stamps.append(seen.stat().st_mtime)
    downloads = Path.home() / "Downloads"
    if downloads.is_dir():
        stamps += [f.stat().st_mtime for f in downloads.glob("nexus-*.json")]
    return dt.datetime.fromtimestamp(max(stamps)) if stamps else None


def nexus_due(now=None):
    """(due, last capture): due on a Saturday or Sunday when the last capture is over 7 days old."""
    now = now or dt.datetime.now()
    last = nexus_last_capture()
    stale = last is None or (now - last).days > 7
    return now.weekday() >= 5 and stale, last


# --- checks ---------------------------------------------------------------------------------------

@dataclass
class Check:
    name: str
    state: str
    detail: str = ""
    fix: str = ""


def feature_check(project):
    open_ = [f for f in project.status.features if f.state in OPEN_STATES]
    if not open_:
        return Check("features", "pass", f"{len(project.status.features)} listed, none new or in progress")
    counts = {s: sum(f.state == s for f in open_) for s in OPEN_STATES}
    detail = ", ".join(f"{n} {s}" for s, n in counts.items() if n)
    return Check("features", "fail", detail,
                 "finish them, or mark each on hold, postponed or scrapped in STATUS.md")


def record_check(project, kind, pathspecs, label, fix):
    reason = staleness(project.status.records.get(kind), pathspecs)
    if not reason:
        record = project.status.records[kind]
        return Check(label, "pass", f"{record.sha} {record.date}")
    if reason == "none":
        return Check(label, "fail", "not recorded", fix)
    return Check(label, "stale" if reason.startswith("stale") else "fail", reason, fix)


def simplify_check(project, tag):
    record = project.status.records.get("simplify")
    check = record_check(project, "simplify", project.code_pathspecs(), "simplify",
                         "run /simplify over everything since the last tag, then record it")
    if check.state == "pass" and tag and tag[2] and not is_ancestor(tag[2], record.sha):
        return Check("simplify", "stale", f"{record.sha} ran before {tag[0]}", check.fix or
                     "run /simplify over everything since the last tag, then record it")
    return check


def assets_check(project):
    # A mod keeps its pictures in assets/images/ (the skeleton); a tool's assets/ is laid out its own way.
    folder = project.path / "assets" / ("images" if project.dir.startswith("mods/") else "")
    missing = [p.split("/")[-1] for p in PAGE_TEXT if not (project.path / p).exists()]
    images = [f for f in folder.rglob("*") if f.suffix.lower() in IMAGE_SUFFIXES] if folder.is_dir() else []
    problems = missing + ([] if images else [f"no images in {folder.relative_to(project.path).as_posix()}/"])
    summary = project.path / PAGE_TEXT[0]
    if summary.exists():
        length = len(" ".join(summary.read_text(encoding="utf-8").split()))
        if length > SUMMARY_LIMIT:
            problems.append(f"summary is {length} characters, over {SUMMARY_LIMIT}")
    if problems:
        return Check("assets", "fail", ", ".join(problems), "add them to assets/")
    return Check("assets", "pass", f"page text and {len(images)} images")


def skeleton_check(project):
    """The mod against docs/docs/modding/mod-skeleton.md, on what git tracks."""
    tracked = {line[len(project.dir) + 1:] for line in git("ls-files", project.dir).splitlines()}
    required = ["README.md", "README-release.md", "CHANGELOG.md", "STATUS.md", ".gitignore", *PAGE_TEXT]
    plugin = project.is_plugin
    if plugin:
        required += ["CMakeLists.txt", "CMakePresets.json", "build.ps1", "src/main.cpp"]
    problems = [f"no {p}" for p in required if p not in tracked]
    if not any(p.startswith("assets/images/") and p.lower().endswith(IMAGE_SUFFIXES) for p in tracked):
        problems.append("no assets/images/")
    strays = sorted(p for p in tracked if p.startswith("assets/") and p not in PAGE_TEXT
                    and not p.startswith("assets/images/"))
    if strays:
        problems.append(f"{len(strays)} other file{'s' if len(strays) > 1 else ''} in assets/ ({strays[0]})")
    has_layer = any(p.startswith("layer/") for p in tracked)
    if not plugin and not has_layer:
        problems.append("no layer/")
    if plugin and has_layer:
        ignore = (project.path / ".gitignore").read_text(encoding="utf-8") if ".gitignore" in tracked else ""
        if not re.search(r"^/?layer/plugins/\S+\.dll\s*$", ignore, re.M):
            problems.append("the built DLL under layer/plugins/ is not gitignored")
    if not project.tag_prefix:
        problems.append("no release workflow")
    if f"`{project.dir}`" not in (REPO / "RELEASING.md").read_text(encoding="utf-8"):
        problems.append("no row in RELEASING.md")
    if problems:
        return Check("skeleton", "fail", "; ".join(problems), "lay it out as docs/docs/modding/mod-skeleton.md says")
    return Check("skeleton", "pass", "matches docs/docs/modding/mod-skeleton.md")


def notes_check(project, tag):
    versions, notes = project.changelog()
    if versions is None:
        return Check("changelog", "fail", "no CHANGELOG.md", "add one with an [Unreleased] section")
    prepared = versions and (tag is None or version_key(versions[0]) > version_key(tag[1]))
    code = [*project.code_pathspecs(), *compiled_deps(project)]
    fetched = tag is None or tag[2]
    if fetched and not prepared and not notes and commits(tag[2] if tag else None, code):
        return Check("changelog", "fail", "code or data changed since the tag, no notes under [Unreleased]",
                     "add a line under [Unreleased] for each change a player would notice")
    return Check("changelog", "pass", f"{versions[0]} prepared" if prepared else f"{notes} unreleased notes")


def quick_checks(project, tag):
    """The release-bar rows cheap enough for the session-start board."""
    checks = [feature_check(project),
              record_check(project, "played", project.code_pathspecs(), "played",
                           "play it, and record the user's approval in STATUS.md"),
              simplify_check(project, tag),
              notes_check(project, tag),
              assets_check(project),
              record_check(project, "page text", [f"{project.dir}/{p}" for p in PAGE_TEXT], "page text",
                           "have the user read nexus-description.bbcode and nexus-summary.txt, then record it")]
    if project.dir.startswith("mods/"):
        checks.append(skeleton_check(project))
    return checks


# --- the whole picture ----------------------------------------------------------------------------

@dataclass
class State:
    project: Project
    tag: tuple = None
    prepared: str = ""
    unreleased: list = field(default_factory=list)
    issues: list = field(default_factory=list)
    checks: list = field(default_factory=list)
    waits: list = field(default_factory=list)


def gather(selected=None, online=True):
    """Every project's computed state, plus the open issues (None when offline)."""
    all_projects = projects()
    chosen = selected or all_projects
    issues, remote = github() if online else (None, {})
    tags = local_tags()
    for name, sha in remote.items():
        tags.setdefault(name, sha)

    def one(project):
        state = State(project)
        found = last_tag(project, tags)
        if found:
            sha = tags[found[0]]
            state.tag = (found[0], found[1], sha if has_commit(sha) else "")
        versions, _ = project.changelog()
        if versions and (not found or version_key(versions[0]) > version_key(found[1])):
            state.prepared = versions[0]
        since = state.tag[2] if state.tag else None
        if state.tag and not since:
            state.unreleased = []
        else:
            specs = [project.dir, *compiled_deps(project), f":(exclude){project.dir}/STATUS.md"]
            state.unreleased = commits(since, specs)
        state.issues = [i for i in issues or [] if project.label in i.labels]
        state.checks = quick_checks(project, state.tag)
        state.waits = [(tag, text, tag is None or tag in tags) for tag, text in project.status.waits]
        return state

    return parallel(one, chosen), issues, all_projects
