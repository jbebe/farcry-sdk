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
| `dump_component_uses.py` | Records creatable classes and their `GetComponent<T>` lookups |
| `build_component_schema.py` | Builds JackAll's component schema from the two dumps above; no Ghidra needed |
| `decompiled.py` | Helpers for the dumpers that read decompiled C text |
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

JackAll's component schema is a separate branch: `dump_properties` and `dump_component_uses`, then
`build_component_schema`.

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

Enum constant *names* are recoverable after all, though not from `CEnumMember::Load`, which is a
stub: `RegisterProperties` stores each label into the enum member's entry list as it registers it,
so `dump_properties.py` reads them there (86 enums).

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
pushes it into the class descriptor, or into a group member's own list:

```c
p = (CMemberBase *)CMemMng::NMalloc(0x14, 0);
p->vptr = PTR_vtable_0a415dec + 8;   // handler vtable: kind, value type, handler, flag bits
p->field_0x4 = "selDensity";         // name
p->field_0xc = 0x10;                 // byte offset in the owning class
CNomadObjectDescriptor::PushBackMember(ms_descriptor, p);
```

The dumper reads this from **p-code**, not decompiled text: it follows each `NMalloc` result through
its stores and resolves every stored value through the PIC GOT. That keeps it independent of how
`CMemberBase` is typed in the database — typing it broke the earlier text parser — and it recovers
all 5,755 members with their kind. The demangled vtable namespace, e.g.
`CGenericMember<CRigidPhysComponent,bool,GenericTypeHandler<bool>,3u>`, gives the value type, the
type handler and the flag bits (1 load, 2 save, 4 load state, 8 save state, 16 described).

What each member kind means for where its data sits in a `.fcb` node is in
`docs/docs/engine-internals/entity-component-schema.md`.

### Output

`out/register_properties.jsonl` — one row per member: `kind`, `name`, `offset`, `value_type`,
`handler`, `flags`, `parent` (the index of the group it was pushed into), `element_index` for
array elements, `setter`/`getter` for accessors and a conditional group's predicate, `callback` for
serialization events, `child_name`/`child_name_2`/`wrapped` for containers, `labels` for enums, and
`handler_symbol`. Rows that yield no usable name are kept with `complete: false`, so gaps stay
visible.

`out/register_properties_classes.jsonl` — one row per registrar, with the base classes named by
its `BASE::RegisterProperties()` calls and the class whose members it copies (`inherits`).

## `dump_component_uses.py` and `build_component_schema.py`

`dump_component_uses.py` records, per class, whether it has a `CreateObject` (the factory can build
it) and every `CEntity::GetComponent<T>` call its methods make. It needs no decompiler.

`build_component_schema.py` needs no Ghidra: it keeps the classes whose registry chain reaches
`CEntityComponent` or `CBaseEntity`, nests members the way the loader reads them, maps each C++
value type to a JackAll wire type and writes `tools/JackAll/assets/component_schema.json`. Its report
compares those types with `binary_classes.xml` wherever both name a member; every disagreement so far
is signedness or hash-versus-integer, never a different byte shape.

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
