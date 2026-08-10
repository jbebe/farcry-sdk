# FCSE Address Library generator

Far Cry 2 v1.03 ships as **two different PC builds** whose `Dunia.dll` images
place the same code at different addresses. FCSE historically baked Steam RVAs
directly into its sources, which made it silently Steam-only. This tool builds
the mapping that lets one binary support both.

Ghidra plus two DLLs in; a stable ID → per-build address table out.

```
python build_addrlib.py              # full run
python build_addrlib.py --from match # reuse the Ghidra cache (seconds)
python build_addrlib.py --calibrate  # + measure precision on held-out ground truth
```

## What it produces

| path | committed | what it is |
|---|---|---|
| `registry.csv` | **yes** | `id → reference RVA`. Append-only; the plugin ABI. |
| `overrides.csv` | **yes** | Hand-verified corrections. Always win. |
| `addrlib.toml` | **yes** | Every threshold. No magic numbers in code. |
| `../../src/engine/address_table.inc` | **yes** | Generated C++ table FCSE compiles in. |
| `cache/` | no | Ghidra extraction, keyed by DLL SHA-256. |
| `out/` | no | Intermediate maps and all reports. |

## Design

**Ghidra does the binary work.** Function hashing uses Ghidra's own
`ExactBytesFunctionHasher` / `ExactInstructionsFunctionHasher` — the same
hashers its Version Tracking correlators use. Fuzzy similarity uses **BSim**,
whose signatures are decompiler-derived feature vectors and are therefore robust
to register reallocation and instruction scheduling by construction. There is no
hand-rolled disassembler, instruction normaliser or PE parser here; this tool
owns orchestration and policy, not binary analysis.

**Everything expensive is cached.** Only `ghidra_extract` and `ghidra_bsim` need
a JVM. All matching and scoring runs offline against `cache/`, so broadening the
accepted-match bar is a config edit and a few seconds — which is the whole point
of the tool existing rather than a one-off Version Tracking session.

**Stages run in descending order of certainty**, each seeded by what earlier ones
confirmed:

| stage | evidence | tier |
|---|---|---|
| `export` | export-table names, identical in both builds | exact |
| `thunk` | thunks forwarding to the same import | exact |
| `exact_bytes` | identical function bytes, unique on both sides | exact |
| `exact_insn` | identical instructions, addresses masked | exact |
| `string_unique` | a literal referenced by exactly one function per side | near_exact |
| `symbol` | identical non-placeholder symbol name | near_exact |
| `propagate` | call-graph alignment from confirmed callers | near_exact |
| `bsim` | decompiler similarity over narrow candidate sets | near_exact |
| `data` | globals/vtables, via the functions that touch them | near_exact |

`exact_insn` is what makes a differing call target a non-difference: it hashes
instructions with addresses masked, so two builds' copies of a function that
differ only in where their `call`s point still hash identically.

Anything that fails the structural guards lands in `review` and is **not
shipped**. The guards are body-size ratio, equal call-site count, and callee-set
correspondence — the last being the one that kills plausible-but-wrong pairings,
and it is cheap because the mapping is already in hand.

## Verification

Two independent ground truths, neither derived from this pipeline:

**Exports.** An export name present in both builds names the same function by
definition. Because the `export` stage would otherwise be grading its own
homework, `--calibrate` rebuilds with the name-based stages switched off, making
the export table a genuine held-out test set for the hash, string, propagation
and BSim stages.

**String literals.** If the data map pairs two addresses and both hold a string,
the strings must be identical. This covers a large arbitrary sample of the data
map rather than a hand-picked few — and it caught a real error during
calibration, an interior pointer into a character table paired against the
table's start. That check now runs as a *filter* during construction, not only
as an audit.

`validate.py` exits non-zero when a shipping tier produces a wrong answer, so it
can gate a release.

## The ID contract

`registry.csv` is append-only and **never renumbered**. A plugin compiled against
ID 4711 must resolve the same function after any future regeneration, so
`mint_ids.py` aborts rather than write a changed `(id, reference_rva)` pair. IDs
are dense from 0, which lets the emitted table be a flat array indexed by ID —
one bounds check and one load at runtime, no search.

An entry that stops matching keeps its ID forever and resolves to
`kFcseRvaMissing`. Reusing the ID would silently repoint every plugin that ever
baked it.

## Correcting a bad entry

Wrong entries are isolated outliers, not corruption: fixing one does not disturb
any other. Add a line to `overrides.csv`, bump `mapping_version` in
`addrlib.toml`, re-run from `mint`:

```csv
steam_rva,gog_rva,reason
0x005E8CE0,0x005DB3E0,"kFileNameSetIdentifierRva confirmed by decompile-diff"
```

## Requirements

- Ghidra 12.1.2 with both DLLs imported **and analysed** into the project named
  in `addrlib.toml` (default `reverse/ghidra/project`, programs `Dunia.dll-Steam`
  and `Dunia.dll-GOG`). Program identity is checked against the recorded SHA-256
  before anything is extracted — the sampled DLL labels proved untrustworthy, so
  the tool refuses to build a mapping from a mislabelled program.
- `pyghidra`, `pefile`, Python 3.11+.
- **The Ghidra GUI must be closed.** A held project lock blocks headless access
  even for read-only work. If a run is killed, its lock is left behind; the tool
  detects that no JVM is running and tells you the lock is stale.

Nothing here writes to the Ghidra project: programs are opened read-only and
never saved.
