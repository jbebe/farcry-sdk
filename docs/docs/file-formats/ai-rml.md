---
sidebar_position: 23
---

# `.ai.rml` — brain workspaces

:::info[Verified via reverse engineering]
No community write-up or tool covers this format. The layout is read out of
`CTaskPackingRepository` (the packer) and `CTaskRepository::LoadTaskRepository` /
`CTaskRepository::GetTask` (the loader) in the symbolized `FarCry2_server`, and JackAll's packer
rebuilds all 14 shipped brains from their source with every section identical. See
[intro](../intro.md) for how RE-verified and community-reported claims are distinguished.
:::

A brain workspace is the behaviour of an NPC type as a graph of tasks. An archetype names one in
its `CFCXAIComponent`:

```xml
<object type="CAgent">
  <value name="text_Brain" type="String">::MercBrain/MercBrain</value>
  <value name="text_aiwsBrainWorkspace" type="String">scripts\game\newbrains\mercbrain.ai.rml</value>
```

`Brain` is the task the agent starts; `aiwsBrainWorkspace` is the file it lives in. What the tasks
*do* is described in [AI](../engine-internals/ai.md).

Fourteen ship in `worlds.dat`, under `scripts\game\newbrains\`:

| File | Tasks | Used by |
|---|---|---|
| `mercbrain.ai.rml` | 13,583 | every soldier |
| `specialcharacter.ai.rml` | 4,162 | named characters |
| `buddyworkspace.ai.rml` | 3,690 | buddies |
| `playerbrain.ai.rml` | 3,407 | the player's pawn |
| `smartterrain.ai.rml` | 787 | smart-terrain activities |
| `boatbrain.ai.rml`, `vehiclebrain.ai.rml`, `scriptedvehiclebrain.ai.rml` | 631, 416, 32 | vehicles |
| `animalbrain.ai.rml`, `scriptedanimalbrain.ai.rml`, `smartterrainanimal.ai.rml` | 57, 38, 30 | animals |
| `merctestbrain.ai.rml`, `stoopidsoldierbrain.ai.rml`, `missionbriefingbrain.ai.rml` | 71, 10, 0 | test and trivial brains |

## Container

```
u32 kind            4 in every shipped file
u32 packedSize
u32 sourceSize
u8  packed[packedSize]     the compiled task repository
u8  source[sourceSize]     the BlackBox.AI source, as an RML document
```

`CAIWorkspaceResource::ClientProcessRawData` loads `packed` directly when `kind` is 4. For kinds 1-3
it parses `source` and compiles it at load time with `CTaskPackingRepository::CreatePackedRepository`
- so the source is not decoration, it is the authoring format the engine itself can compile. The
shipped files carry both halves; only `packed` is read.

## Source: `BlackBox.AI`

`source` is an ordinary RML document (Dunia's binary XML, the same container as `oasisstrings.rml`),
the export of Ubisoft's BlackBox AI editor:

```xml
<BlackBox.AI Version="1.1.4.10">
  <Brain Name="::BrainStoopidSoldier/StoopidBrain" Class="CBrainStoopidMerc" Looping="1" Independent="0">
    <Selectable Filter="MercBhvCombat" Task="::BrainStoopidSoldier/StoopidBrain/FollowTarget" />
    <Anchor Name="OnStart">
      <Connection Target="::BrainStoopidSoldier/StoopidBrain/CScannerVisualThreat1" TargetAnchor="Start" />
    </Anchor>
    <Exit Name="Success" />
    <Add Task="::BrainStoopidSoldier/StoopidBrain/FollowTarget" />
  </Brain>
  <Task Name="::BrainStoopidSoldier/StoopidBrain/FollowTarget/CTaskWait1" Class="CTaskWait" Looping="0" Independent="0">
    <Parameter Name="timeToWait" Value="0.5" />
    ...
```

Every top-level element is one *instance*: `Brain`, `Plan`, `Scanner` or `Task`. The tag only
groups; `Class` names the C++ task class the factory creates. Names are paths, and a node's name
starts with its plan's.

| Child | Meaning |
|---|---|
| `Parameter Name Value` | a task parameter; may nest further `Parameter`s (a fact reference, a curve) |
| `Anchor Name` | an input or life-cycle anchor (`OnStart`, `OnStop`) and the connections fired from it |
| `Exit Name` | an outcome (`Success`, `Failure`, class-specific ones like `PathFindFailed`) and its connections |
| `Event Name` | a named event and its connections |
| `Connection Target TargetAnchor` | "then trigger anchor `TargetAnchor` (`Start`, `Stop`, `Restart`) of `Target`" |
| `Add Task` | the plan owns this task: stopping the plan stops it |
| `Selectable Filter Task` | brains only: while the C++ brain reports behaviour `Filter`, it runs `Task` |

Parameter names can hold spaces, so they are not always legal XML names; JackAll's RML reader
escapes such names with `XmlConvert.EncodeLocalName` and its writer undoes it.

## Packed repository

All integers little-endian.

```
u32 blobCount
  blobCount × { u32 classHash, u32 size, u8 rml[size] }
u32 connectionBytes
u8  connections[connectionBytes]
u32 anchorCount
  anchorCount × u32 nameHash
u32 taskCount
  taskCount × { u32 nameHash, u32 nameLength, char name[nameLength],
                u32 blob, u32 offset, u32 length }
```

Hashes are CRC-32 of the string (`CStringID`), the same as FCB names.

**Blobs** are the parameters. `LoadInstance` builds one RML node per instance,

```xml
<Parameters Name=" " Looping="…" Independent="…" timeToWait="0.5" … />
```

with one attribute per `Parameter` and a child element, named after the parameter, for each
parameter that has sub-parameters. Blobs are deduplicated per class: instances whose class and bytes
match share one, in order of first appearance. `CTaskRepository` creates the class from
`classHash` through `CTaskFactory` and hands it the parsed blob.

**Anchors** are every anchor, exit, event and filter name the connections use, interned in the
order the packer first meets them.

**Tasks** are sorted by name hash; a task's index in this table is how connections refer to it. The
name string is optional - the server's packer writes length 0, the shipped files carry the names.

### Connection records

`offset`/`length` select a task's slice of `connections`, a stream of records:

| Kind | Layout | Meaning |
|---|---|---|
| `04` | `u16 task` | the plan that `Add`s this task. Written by the parent, *prepended* to the child's slice |
| `00` | `u16 task, u16 anchor` | a brain's `Selectable`: filter `anchor` runs `task` |
| `01` | `u16 anchor, u16 n, n × {u16 task, u16 anchor, u8 flag}` | connections from an `Anchor` |
| `02` | same | connections from an `Exit` |
| `03` | same | connections from an `Event` |

Records follow the instance's children in document order. `flag` is 1 when the target's name starts
with the owning task's name - a connection into its own plan - and 2 otherwise.

Where each slice sits in `connections` is the one thing JackAll does not reproduce. The shipped
files lay slices out in the iteration order of the PC packer's hash map (runs of ascending name
hash, one per bucket); the Linux server's `__gnu_cxx::hashtable` gives a different order, so the
map must be MSVC's with a bucket function not yet identified. The loader reads every slice by
offset, so the order has no effect.

## Editing

JackAll's **AI** tab browses a workspace as a tree (brain → "when *filter*" → plan → tasks), edits
parameters, and on save recompiles `packed` from the edited source and stages the file. From the
command line:

```
jackall-cli ai unpack mercbrain.ai.rml     # mercbrain.source.xml + a readable dump of the compiled half
jackall-cli ai verify *.ai.rml             # recompile each source and compare with its compiled half
```
