# fc2re

Tooling that turns the symbolized `FarCry2_server` binary into a fully typed Ghidra database:
real structs, inheritance, resolved virtual calls, correct signatures.

## Why the server binary

`FarCry2_server` (Linux, GCC 4.2.4) is **96.8% symbolized** — 121,656 of 125,726 functions carry
real names — and it is a non-inlined build of the *entire* engine, renderer frontend, Magma UI and
FCX editor included. Only the D3D9 backend and Win32 layer are absent. Shipping `Dunia.dll` has
zero named game classes by comparison, so structure recovery happens here and is carried across
later.

There is no DWARF (`.debug_info` is 423 bytes). Names, signatures, vtable order and the inheritance
graph are all recoverable as ABI-specified data; field layouts are the part that needs work.

## Layout

| Path | What it is |
|---|---|
| `dump_properties.py` | Extracts Nomad property descriptors from `CLASS::RegisterProperties` |
| `dump_class_sizes.py` | Recovers exact `sizeof(T)` from allocation sites |
| `dump_vtables.py` | Harvests vtables and the RTTI inheritance graph |
| `derive_size_floors.py` | Combines size evidence into bounds; no Ghidra needed |
| `apply_properties.py` | Writes recovered field layouts into the placeholder structs |
| `apply_vtables.py` | Creates vtable structs and points each class's vptr at one |
| `apply_inheritance.py` | Replays a base's members into its derived classes |
| `apply_type_sizes.py` | Sizes placeholder types so parameter storage resolves |
| `export_checkpoint.py` | Writes a `.gzf` snapshot to `reverse/ghidra/` |
| `tests/` | Logic tests, no JVM needed |
| `out/` | Generated artifacts |

Dumpers are read-only and open no transaction. Only the three `apply_*` scripts mutate the
database, and all are dry-run unless given `--write`.

### Order

Each pass feeds the next, so run them in this order:

```
dump_properties  ─┐
dump_class_sizes ─┼─► derive_size_floors ─► apply_properties ─► apply_vtables ─► apply_inheritance
dump_vtables     ─┘
```

`apply_properties` needs `--vtables` so it leaves offset 0 free in polymorphic classes, and it and
`apply_vtables` both take `--sizes out/class_sizes_merged.jsonl`.

The order among the appliers is not cosmetic. `apply_inheritance` only fills offsets nothing has
named yet, so it must run last for the other two to win any conflict — and because
`apply_properties` clears its own members when re-run, re-running it means re-running the two
after it.

`apply_type_sizes.py` is independent of that chain and can run at any point.

## What `apply_type_sizes.py` does, and why signatures were never the problem

Ghidra's demangler has already applied all 125,441 function signatures, and the decompiler honours
them — on a sample of 120 functions with fully-assigned storage, 120 matched. What breaks is
**storage**: if one parameter's type is a 1-byte placeholder, Ghidra cannot lay the parameter out,
and it discards the whole prototype rather than part of it. That is why
`CEntitySystem::Update(float, EEntityUpdateStep)` decompiled as `(undefined4, int)` despite the
correct signature sitting in the database.

Sizing 257 types took the count of functions with an unassignable parameter from **5,331 to 3,042**.

Sizes are asserted with `undefined<N>` — the claim is how much room a value takes, not what it
means. Two rules and one assumption:

- **member gap**: a gap bounds `sizeof` from above, so the smallest is the tightest bound; accepted
  only when minimum == mode and the mode holds ≥60% of samples
- **enum by name**: the `E`-prefix convention (`EStimType`, `EMoveLayer`) sized 4, GCC's x86 default
- **445 types left unsized** rather than guessed. Every unsized typedef is *not* an enum —
  `std::_Deque_iterator`, `__gnu_cxx::__normal_iterator` and `ndRectT` are all nearer 16 bytes, and
  sizing one at 4 would misplace every parameter after it.

Enum constant *names* are not recoverable from this binary: `CEnumMember`'s spare descriptor slot
is a bitfield, `CEnumMember::Load` is a stub, and `GetEnumeratedTypeEntries` belongs to the
`magma::` UI type system rather than the game.

Scripts follow the conventions in `tmp/compare-dlls/*.py`: PyGhidra rather than Jython, a dual
entry point so each runs both headless and in the Script Manager, lazy Java type binding, `jstr()`
on anything textual, and — for anything that writes — dry-run by default, `SourceType.ANALYSIS`
rather than `USER_DEFINED`, and per-row status written back so reruns skip completed work.

## Running

Ghidra bootstraps its own environment; run `support/pyghidraRun.bat` once and accept the venv
offer. It lands at `%APPDATA%\ghidra\ghidra_12.1.2_PUBLIC\venv`.

Ghidra holds an exclusive lock on the project, so **close the GUI before running headless**:

```
%APPDATA%\ghidra\ghidra_12.1.2_PUBLIC\venv\Scripts\python.exe dump_properties.py ^
    out C:\Projects\FarCry2\reverse\ghidra\project fc2 /FarCry2_server
```

Or run it from the Script Manager with the output directory as the script argument.

Tests need no Ghidra at all:

```
python tests/test_parse_registrations.py
```

## What `dump_properties.py` recovers

1,049 classes have a `RegisterProperties()` that builds one `CMemberBase` per serialized field and
pushes it into the class descriptor:

```c
p = (CMemberBase *)CMemMng::NMalloc(0x14, 0);
*(char **)(p + 4) = "BarkEventTag";
*(undefined4 *)(p + 0xc) = 0;            // byte offset in the owning class
*(undefined ***)p = &PTR_Load_0a3a3468;  // handler vtable, names the member type
CNomadObjectDescriptor::PushBackMember((CryVector *)ms_descriptor, p);
```

So each row carries a real **field name, byte offset and type** — `Bark` comes out as
`BarkEventTag` at 0x00, `SourceActorTag` at 0x04, through `IsGeneric` at 0x3C. The handler vtable
resolves to a `_ZTV14CGenericMember...` symbol whose template arguments name the owner and member
type.

The parser keys records by the variable holding the pointer, bracketed by `NMalloc` and
`PushBackMember`, so interleaved construction still resolves and the real handler vtable overrides
the one the base constructor wrote.

### Descriptor kinds

Not every descriptor is a field at an offset. The handler's `_ZTV` name gives the kind, read off
the Itanium length prefix rather than by splitting on the first template marker:

| kind | meaning |
|---|---|
| `CGenericMember` | plain field |
| `COffsetMember` | field reached by an offset adjustment |
| `CContainerMember` | container field, carries the element name |
| `CSerializationEvent` | load/save hook — **not a field, legitimately has no offset** |
| `CGroupMember`, `CConditionalGroupMember` | grouping wrappers over other members |
| `CVirtualMember` | accessor-backed, no direct storage |

Because grouping wrappers and their members can point at the same offset, offsets are not unique
within a class — treat a duplicate as a grouping relationship, not a conflict.

### Output

`out/register_properties.jsonl` — one row per descriptor, with `kind`, `name`, `offset`, `flags`,
`alloc_size`, `handler_symbol` and container child names. Rows that yield no usable name are kept
with `complete: false` rather than dropped, so gaps stay visible.

`out/register_properties_classes.jsonl` — one row per registrar, with the base classes named by
its `BASE::RegisterProperties()` calls. Classes that add no fields of their own still appear here,
which makes this an independent cross-check on the `_ZTI` inheritance graph.

The field-name hashes (`CStringID`) are what **fcb** data files key on, which makes this table
useful to JackAll and the file-format docs as well.

## What `dump_class_sizes.py` recovers

An allocation is immediately followed by the constructor for the class being built, so the pair
pins the class's true size:

```c
pCVar1 = (CMemberBase *)CMemMng::NMalloc(0x14,0);
CFoo::CFoo(pCVar1, ...);            // sizeof(CFoo) == 0x14
```

The constructor invoked at the site is the most-derived one, so `new Derived` records `Derived`
rather than the base it chains to. Sites with a computed size are ignored — an array says nothing
about the class. `CMemMng::NMalloc` has ~5,448 callers, which bounds the scan well below the full
125,726 functions.

Output is `out/class_sizes.jsonl` (one row per class, with `agreement` and any
`conflicting_sizes`) and `out/class_size_sites.jsonl` (one row per site, so a disagreement can be
traced back). Conflicts are expected where placement new, pooled allocation, or a base-typed
factory is involved, so the reconciliation reports agreement instead of silently picking a winner.

### Why sizes matter to the layouts

An undersized struct is the dangerous case. Ghidra renders an access past the end by indexing a
phantom next element — `this[1].DisableTerrain`, or `this[3].vptr` — attaching a real member name
to an offset it does not describe. Oversizing only leaves undefined bytes. So every size decision
here rounds up: conflicting allocation sites reconcile to the largest, and `derive_size_floors.py`
supplies lower bounds where no allocation site exists.

## What `dump_vtables.py` and `apply_vtables.py` do

`dump_vtables.py` reads both vtables and RTTI in one pass, since they share `.data.rel.ro` and
point at each other. It needs no decompiler, so it finishes in about a minute: **9,814 vtables,
109,786 virtual slots, 9,851 typeinfo records, 9,574 inheritance edges with exact base offsets**.

Two things about the format are easy to get wrong. Each secondary subobject table restarts with its
own offset-to-top — a small negative integer, not a pointer — so a reader that stops at the first
non-pointer truncates every multiply-inheriting class to its primary table. And a vtable carries a
table per base subobject *including inherited ones*, while typeinfo lists only *immediate* bases,
so the two counts legitimately disagree.

`apply_vtables.py` turns each table into a `<Class>_vtable` struct of named slots and places a vptr
field at the offset the table's offset-to-top implies, naming secondary vptrs as well as primary
ones. Slots share one generic `vfunc` definition: signatures are not recovered yet, so per-slot
types would add ~110k types for no information. Virtual calls then read as
`(*p->vptr->GetHierarchyInfo)(p)`.

Class lookup goes through **Ghidra's** demangled name — the `<Class>::vtable` symbol's parent
namespace — not one parsed from the mangled string, which gets template arguments wrong.

## What `derive_size_floors.py` does

Allocation sites only cover classes something calls `new` on. For the rest it combines three
sources of real evidence and propagates them to a fixpoint: a base at offset O of size S implies
O+S, a secondary subobject table at offset-to-top −T implies T+4, and the last recovered property
member implies its own end. That took coverage from **2,231 sized classes to 10,255**. Allocation
sizes are never shrunk, and derived entries are marked `exact: false` so struct descriptions say
"lower bound" instead of claiming precision.
