---
sidebar_position: 9
---

# Engine Architecture & Main Loop

:::info[Verified via reverse engineering]
Traced live in `FarCry2_server` (the unstripped Linux dedicated-server binary — see
[Overview](./overview.md) for why it's the better-symbolized of the two programs in this project) via
GhidraMCP: decompilation + call-target disassembly, entry point outward. All addresses below are
`FarCry2_server` addresses (`0x08`/`0x09`/`0x0a` range), not `Dunia.dll`'s.
:::

A top-down map of how the engine boots and what runs every frame, stopping at the point where a
branch becomes "a specific gameplay/rendering system" rather than "engine plumbing." Individual
subsystems (physics, `.fcb`, `.spk`, Lua) get their own pages elsewhere in this section and in
[file-formats](../file-formats/fcb.md); this page is the connective tissue between them.

## Entry chain

```
main(argc, argv)                        0x08276870
  └─ RunGame(cmdline)                    0x08278df0
       └─ do {
            InitDuniaEngine(cmdline,…)   0x08277830
            g_gameFunctionProvider()     — one indirect call, launcher-supplied callback
            RunDuniaEngine()             0x08276e30
          } while RunDuniaEngine() returns true
```

`main` just concatenates `argv` back into one string and hands it to `RunGame`. `RunGame`'s loop
structure is the real story: `RunDuniaEngine` returning `true` means "reinit and go again" — this is
the resolution-change/settings-apply/level-switch restart path, not just a one-shot run. Each
iteration is a full init → play → teardown cycle.

`TickDuniaEngine` (`0x08276d00`) and `ShutdownDuniaEngine` (`0x08276e00`) are the other two members of
this exported lifecycle API; both are thin wrappers (`CXGame::Update()` / `Release()`
respectively) — present for the editor's embedded-engine use case (the standalone map editor ticks the
engine itself rather than owning the loop), not called from this binary's own `main`.

## Init (`InitDuniaEngine`)

Decompiles as ~600 lines, but almost all of it is inlined `std::string` construction (path-building
for `MyGames/FarCry2/…`) that Ghidra couldn't fold away — the actual sequence, in order:

1. **`Gear::StartEngine()`** — lowest-level bootstrap (memory/profiling instrumentation bracket; matches
   `Gear::ShutDownEngine()` at the very end of this same function, so it brackets *init itself*, not
   the whole process lifetime).
2. `CNomadPath` singleton constructed — resolves the `MyGames/FarCry2/` save-data root (see
   [save-data-path](./save-data-path.md)).
3. `CFCXGameCmdLineParser` — parses `-dedicated`, `-norender`, `-editorpc`, `-borderless`, and friends
   into globals consumed later in this same function.
4. **`CCryEngine`** singleton constructed; `InitializeCore()` then `InitializeEngineServices()` — the
   generic engine core, confirming (alongside the Lua/Havok fingerprints already in
   [Overview](./overview.md)) that Dunia's top-level class is still literally named `CCryEngine`.
5. `CNomadNotificationManager`, `CGameErrorManager` singletons constructed.
6. `CFCXGameCmdLineParser::Process()` — cmd line applied.
7. `CGamerProfileNative` constructed and initialized (local profile, not network account).
8. **`CCryEngine::Initialize()`** — the heavy init pass (as opposed to step 4's "core" init).
9. `CSoundConfig::ApplySettings()`.
10. If a renderer is present (`SceneRendererFacade::HasRenderer()` — false on this dedicated-server
    build, so this whole block is dead code here but shared with the PC client): resolve the
    `low`/`medium`/`high`/`veryhigh`/`ultrahigh`/`optimal` quality string to an enum and apply it via
    `CRenderConfig`.
11. Unless a headless/no-sound flag is set: `CSoundCache` and `CAllSounds` singletons constructed,
    `CSoundBudget::SetBudget(0x80000)`.
12. **`CXGame::CreateInstance()`** → `CXGame::Init()` — the FC2-specific game-session object (distinct
    from the generic `CCryEngine`; see "Two class families" below).
13. `MSAnim::LoadMoves()` — animation move-set table load.
14. `CFcxAI` constructed — AI subsystem instance.
15. `VerifyParentalControlAccess()` gate, then `CXGame::StartGame()` — actually starts the session.

Steps 1–8 are generic-engine bring-up; 9–15 are FC2-game bring-up layered on top. The renderer/sound
gating (10–11) is the cleanest evidence in this function that client and dedicated-server builds share
one `InitDuniaEngine`, branching only on a handful of flags.

## The main loop (`CXGame::Run`)

```
CXGame::Run(bool& outRestart)             0x08882ce0
  while (!quitFlag) {
      Update(this)                        — CXGame::Update, see below
      elapsed = CHighPerfTimer::GetTimeValue() - frameStart
      if (elapsed < targetFrameTime)
          Gear::System::Sleep(targetFrameTime - elapsed)   — frame limiter
  }
```

Straightforward fixed-ish-rate loop with a sleep-based limiter, no separate render thread — render is
called inline from the same `Update` (see below), gated by `HasRenderer()`.

### `CXGame::Update()` — per-frame fan-out

```
CXGame::Update()                          0x08882bc0
  ├─ CPerfProfiler::Capture(…) × 5         — profiling brackets around each phase below
  ├─ CMemMng::NewFrame()                   — per-frame arena/allocator reset
  ├─ CGame::Update(dt, flags)              0x092a4e10
  │    ├─ UpdateOperations(dt, flags)      — not expanded; FC2 gameplay-operation layer
  │    └─ CNetGameControllerManager::Update()
  ├─ CCryEngine::Update(flags)             0x096a7ec0   — see table below
  ├─ CDynamicEnvironmentManager::Update()  0x09162730   — weather/time-of-day/fog/wind/sky simulation
  └─ CCryEngine::Render()                  0x096a6d50   — SceneRendererFacade::Render() iff HasRenderer()
```

`CDynamicEnvironmentManager::Update` is a large (~250-line) function but architecturally a leaf: it
drives `CTimeOfDay`, `CSky`, `CEnvironmentFog`, wind/rain evaluation, all self-contained under one
category ("environment simulation"), not fanning out to further subsystems.

### `CCryEngine::Update()` — subsystem dispatch table

The real hub. Most calls are gated behind bits of an update-flags word (`param_1 & this->mask`), which
is presumably how the editor/server/client trim which phases run. Categorized:

| Category | Calls |
|---|---|
| **Physics** (Havok) | `CPhysWorld::StartOfFrame`, `CPhysWorld::ProcessExplosions`, `CPhysWorld::TimeStep`, `CPhysWorldListener::Update` |
| **World / streaming** | `CWorld::Update`, `CWorld::UpdateSync`, `C3DEngine::PreUpdate`, `C3DEngine::Update`, `CDynamicEntityLimiter` |
| **Entities** | `CEntitySystem::Update` (called twice — once pre-physics, once post-physics), `CEntitySystem::DestroyCondemnedEntities` |
| **AI** | `CAIEngine::PreUpdate` / `PostUpdate` (gated with the "Domino" flag, see below) |
| **"Domino" mission/AI scripting** | `CDominoDelayManager::Update`, `CDominoSoundManager::Update`, `CDominoSequenceManager::Update` — all under one flag bit alongside AI PreUpdate, confirming Domino is the Lua-backed mission-scripting layer named in [the file manifest](../modding/file-manifest.md#9-lua-scripts--partial) |
| **Animation** | `MSAnim::Update` |
| **Ambient / vegetation** | `CDynamicAmbientUpdateManager::UpdatePrePhysics` / `UpdatePostPhysics`, `CAmbianceManager::Update`, `RTxcManager` tick + `PostUpdate` — `RTxcManager` ("Real Tree" component manager) is what reads the `.rtx` vegetation files — see [`.rtx`](../file-formats/rtx.md) |
| **Rendering-adjacent** | `CMovieSystem::Update` (cutscenes), `CDecalManager::Update`, `CSky`/fog (via `CDynamicEnvironmentManager`), `CBufferFrameID::Increment3DFrameID` |
| **Audio** | `GetSoundSystem()->Update()` (opaque interface call — the concrete class wasn't named in this binary; likely the DARE middleware named in [the sound section of the file manifest](../modding/file-manifest.md#7-audio--partial)), `CSubtitleManager::Update` |
| **Networking** | `Echo::CNetEngine::Update` (confirms `Echo` is the network-engine namespace), `CSessionManager::Update`, `CCommandRequestManager::Update`, `CCommandManager::Update` |
| **Console / debug** | `CXConsole` update, `CDebugInfoManager::Update`, `CErrorImpl::Update`, `FatalError::Display()` (polled — see threading below) |
| **Async job join** | `CJobScheduler::Wait(...)` + `CParticlesSystemMgr::FinalizeUpdate()` — the main thread blocks here for particle work queued on the job system |
| **Game mode** | `CGameModeManager::Update` / `PostUpdate` — SP vs. the `CFCXGameMode{DeathMatch,CTF,TeamDeathMatch,VIP,Benchmark,Single,Editor}` family |

### Two class families, not one

`CGame`/`CXGame` (generic-looking names) are the actual FC2-specific game-session layer, while
`CCryEngine`/`C3DEngine`/`CWorld`/`CEntitySystem` are the reusable engine core. A third, much larger
family — `CFCXGame*` (`CFCXGameplayManager`, `CFCXGameModeDeathMatch`, `CFCXGameSoundService`,
`CFCXGameSettingsService`, `CFCXGameMessageService`, `CFCXGameStartOperation`, …) — sits above both:
"FCX" reads as an internal *Far Cry X* codename layer for UI/game-mode/session-flow glue, distinct from
both the engine core and the low-level `CGame` update. Not investigated further here; worth its own
pass if game-mode/UI flow becomes the focus.

### Middleware/subsystem namespaces identified from class names

| Prefix / namespace | Subsystem |
|---|---|
| `hk*` (`hkpConstraintQueryIn`, `hkSimpleContactConstraintData`, …) | Havok physics (confirmed at binary level here beyond the evaluation-key string in [Overview](./overview.md); `hkpMultithreadingUtil` also confirms Havok manages its own worker-thread pool independently of `CJobScheduler`) |
| `Echo::*` | The network engine (`Echo::CNetEngine`, `Echo::INetEvent`, `Echo::NetDiscoveryEvent`) |
| `Domino*` | Lua-backed mission/AI sequencing (delay/sound/sequence managers) |
| `Magma`/`CMagma*` | UI system (`CMagmaInputListener` seen here; full `.mgb` binary format traced separately in [file-formats/mgb](../file-formats/mgb.md)) |
| `Agora*` | Ubisoft's online services SDK (auth, connection tasks) |
| `*DemonWare*` | Backend-as-a-service layer used for accounts/profiles/matchmaking (`AccountDemonWareTask*`) |
| `RTxc*` | Vegetation ("Real Tree") LOD/rendering manager |
| `CFCX*` | The FC2-specific game-mode/UI/session glue layer (see above) |

## Background threads

The main loop itself is single-threaded (no separate render thread), but the engine is not: searching
for named-thread call sites (`InternalSetThreadName`, which every one of these passes its own literal
name string to) surfaces a fixed roster of long-lived worker threads:

| Thread class | Apparent role |
|---|---|
| `CPhysTimeStepThread` | Havok physics step runs off the main thread; `CPhysWorld::TimeStep` in the main loop takes an `IWhilePhysRunUpdate` callback, i.e. AI PreUpdate is deliberately run *while* physics is stepping, then joined |
| `StreamingDevice` | Async world/asset streaming from disk |
| `CRequestManagerDecompressionThread` | Decompresses streamed archive data (LZO/zlib, off the main thread) |
| `CNetThread`, `CServerThread`, `CClientThread` | Networking I/O (`CServerThread`/`CClientThread` both present even in the dedicated-server binary — likely shared source with the PC client) |
| `CVoiceThread` | Voice chat encode/decode |
| `CJobScheduler` (+ its `CJobSchedulerThread` pool) | Generic async job system; the main loop explicitly joins it once per frame for particle finalization, so at minimum `CParticlesSystemMgr` offloads work here |
| `CLoadingScreenImpl` | Keeps the loading screen animating during a blocking level load on another thread (`LoadGameFile`/`BlockingLoadGameFile`/`SaveGameFile` also route through named-thread setup, i.e. save/load I/O is its own thread too) |
| `bdThread` | Bink video decode (matches the Bink video format noted as out-of-scope/third-party in the [file manifest](../modding/file-manifest.md#8-video-bink--out-of-scope)) |
| Havok internal pool (`hkpMultithreadingUtil::initMultithreading`/`setNumThreads`/`addThread`) | Havok manages its own worker threads separately from `CJobScheduler` |

`FatalError::IsSecondaryThreadWaitingForDisplay()`, polled once per frame in `CCryEngine::Update`,
confirms the engine's crash/error-dialog path is deliberately thread-aware: a background thread can hit
a fatal error and hand off to the main thread to actually display it, rather than showing UI off-thread.

## Subsystems, one level deeper

The dispatch table above names the "what"; this section is the "how" for the branches that looked
most likely to pay off — entity update, vegetation, networking, game-mode flow, mission scripting, and
AI. (Networking is included for completeness but deliberately wasn't pursued further — this project's
focus is single-player.)

### `CEntitySystem::Update` — entities tick via a dependency-graph job scheduler, not a loop

`CEntitySystem::Update` (`0x09450080`) is not a `for each entity: entity->Update()` loop. Per update
step (pre-physics / post-physics — the two flag values `CEntitySystem::Update` is called with from
`CCryEngine::Update`), it:

1. Walks a cached per-entity task list — `IEntityTask*` pointers, one per attached
   `CEntityComponent`, rebuilt only when the entity's cache-dirty bit is set (`CEntity::UpdateCacheTaskList`,
   `0x0943cc20`, called with `this + 0x40` bit `0x40`). Rebuilding sorts the component list by a
   priority value (vtable slot `+0x7c` on each component) via `std::__introsort_loop`, then **recurses
   into attached child entities** (`CEntityProxy`-linked), so a whole parent/child hierarchy's tasks
   land in one flat, priority-ordered list.
2. For each task, calls a "ready to run" check (vtable `+4`). Tasks whose dependencies aren't satisfied
   yet are skipped and revisited; tasks that are ready but declared as needing to run off-thread get
   submitted to `CJobScheduler::ScheduleJob`; tasks that can run inline execute immediately on the main
   thread.
3. Loops polling `CJobScheduler::TryWaitAndPopAllDoneJobs`/`PopDoneJobWait`, and for each job that
   completes, unblocks whatever downstream tasks were waiting on it — a textbook dependency-DAG
   scheduler — until the whole per-frame task graph for every entity has drained.
4. Finishes with `ProcessEntitiesToRespawn` (`0x0944f8a0`) — respawn logic runs only once the entire
   entity task graph for that step is empty.

Practical read: an entity's `.fcb`-defined components (see [`.fcb`](../file-formats/fcb.md)) aren't
just data — each one that needs a per-frame tick apparently implements `IEntityTask`, and the engine is
free to run independent components of independent entities in parallel across `CJobScheduler`'s worker
pool, joining only where a real dependency exists. This is a substantially more sophisticated update
architecture than a naive entity loop, and worth keeping in mind before assuming component update order
is deterministic or single-threaded.

### `RTxcManager` — `.rtx` is a live vegetation simulation, not a static mesh

The `RTxc*`/`RTxs*` class family (`RTxcManager`, `RTxcResource`, `CRTxEngineResource`) is far richer
than a mesh format. The concrete component classes it manages:

| Class | Role |
|---|---|
| `RTxcSkeletonDyn` / `RTxcSkeletonRdr` / `RTxcSkeletonRdrUncompressed` | The branch hierarchy — a dynamic (simulation) copy and a render copy, with a compressed and uncompressed render variant |
| `RTxcSimulation` / `RTxcSimulationHRT` / `RTxcSimulationPRT` | Wind-sway physics simulation — two named techniques (`HRT`/`PRT`, not yet decoded further) |
| `RTxcLOD` | Level-of-detail selection |
| `RTxcDefoliantNode` / `RTxcDefoliantLeaf` / `RTxcDefoliantHLeaf` | Per-leaf-cluster defoliation state — this is the binary-level confirmation of FC2's marketed "shoot the leaves off trees" foliage system |
| `RTxcRegenNode` / `RTxcRegenLeaf` / `RTxcRegenHLeaf` | The inverse of defoliation — leaves regrowing over time |
| `RTxcDestruction`, `RTxsBrokenBranch` | Branch-breaking state, with broken branches tracked as their own sub-objects |
| `RTxcFire` | Burning state |

`CRTxEngineResource` (the loadable top-level resource — `CreateInstance` at `0x095bcd14` is its
factory) owns a hashtable of `SRealtreeSound`, keyed by an `int` event id — confirming individual trees
carry their own associated sounds (rustling, breaking, burning), tying this system directly into the
audio layer, not just rendering/physics. This class taxonomy is what made the format tractable: it
named the parts to look for, and `RTxcManager::LoadSkeletal` turned out to spell the layout out in
full. The geometry half is decoded — see [`.rtx`](../file-formats/rtx.md); the simulation state these
classes own is not.

### `Echo::CNetEngine` — an object-replication network engine

`Echo::CNetEngine::Update()` (`0x098f58e0`) drives four subsystems in order: `CNetObjectManager`,
`CNetChannelManager`, `COperationNotifier`, `CNetworkLog` — each with its own `RunMainUpdate()`. The
surrounding class names sketch a fairly complete home-grown replication engine:

- **Replicated objects**: `Echo::CNetObject`, keyed by `Echo::CNetObjectId`, managed by
  `CNetObjectManager`.
- **Typed, qualifier-based property serialization**: `Echo::CNetDataNumber<T, EQualifiers>`,
  `Echo::CNetDataString`, `Echo::CNetDataNdBool` — each networked field declares a qualifier enum,
  presumably controlling quantization/delta-compression per field.
- **Priority-ordered messaging**: `Echo::CNetMessageRef` sorted by
  `Echo::DescendindMessageOrderComparison` — a relevance/priority queue, highest-priority message
  first, the standard pattern for keeping bandwidth-limited replication responsive.
- **Per-connection packing**: `Echo::CPackerConnection`, and a sorted `Echo::CProtocolEntry` protocol
  table (`IsNetProtocolEntryLesserThan`).
- **Voice**: `Echo::CNetVoicePeerMessage` — ties directly to the `CVoiceThread` background thread found
  above.
- **Async operations**: `Echo::IOperation` / `COperationNotifier` — connection/matchmaking-style
  operations that complete asynchronously and notify back into `RunMainUpdate`.

Wire-format/protocol-level detail wasn't pursued further here — this is enough to know where to look
(`CNetObjectManager` for the object-relevance/replication logic, `Echo::CNetData*` templates for the
per-field serialization) if multiplayer-facing modding or protocol understanding becomes a goal.

### The `CFCXGameMode*` family — a thin FC2 layer over a generic engine strategy pattern

Confirms the "two class families" pattern from earlier holds one level deeper. `CFCXGameModeSingle`'s
constructor is exactly:

```cpp
CFCXGameModeSingle::CFCXGameModeSingle() {
    CGameModeSingle::CGameModeSingle(this);        // generic-engine base
    vtable = &CFCXGameModeSingle_vtable;
    CGameMode::SetID(this, ms_modeID);              // registers this mode's CStringID
}
```

Every `CFCXGameMode*` (`Single`, `DeathMatch`, `CTF`, `TeamDeathMatch`, `VIP`, `Benchmark`, `Editor`)
follows the same shape: `CFCXGameMode<X> : CGameMode<X> : CGameMode`, registering its own string ID.
That ID is the hashtable key `CGameModeManager`'s constructor reserves space for (see the main-loop
section above) — confirming game-mode selection is a factory-by-name lookup, not a switch statement.
`CGameModeManager::Update` itself is a one-line polymorphic delegation to whichever `CGameMode` is
currently active:

```cpp
void CGameModeManager::Update(float dt) {
    if (active) CGameMode::Update(activeMode);
}
```

All mode-specific behavior lives in the concrete subclass's override, not in the manager.
`CFCXGameplayManager` sits alongside this as a Lua-scriptable service (`GetGameModeService<T>` is a
service-locator template; `SetMapArmy` is directly callable from mission scripts via the
[function-registry](./function-registry.md)-adjacent script-binding pattern), rather than being part of
the mode-selection mechanism itself.

### "Domino" — Lua loads through the same generic VFS as every other asset

Domino has a much larger class footprint than the three managers named in the main dispatch table
suggested. The family breaks down by role:

| Class(es) | Role |
|---|---|
| `CDominoManager` | Central hub — spawns entities by script name (`SpawnDominoEntity`), reports `IsScriptAutorunEnabled` |
| `CDominoDelayManager` / `CDominoDelay` | Timed callbacks — `CreateDelay(seconds, ...)`, and on expiry calls straight into `CScriptCallbackSystem::CallCallback` (see below) |
| `CDominoSequenceManager` / `CDominoSequenceListener` | Event-driven listeners (`CreateListener`) |
| `CDominoSoundManager` / `CDominoSound` | Script-triggered sounds |
| `CDominoBoxInstance` / `CDominoBoxResource` | "Domino Box" — mission-trigger-volume system (`CreateBox`, `RegisterBox`) |
| `CDominoComponent` | Attaches Domino to an entity as a real per-frame update component — see below |
| `CTaskCheckDominoData` / `CTaskSendDominoEvent` | One-shot event delivery — both derive from `CTask`, **not** `IEntityTask` (see below) |
| `CBrainDomino`, `CScannerDominoEvent` | Direct integration points into the AI brain/sense system (see the AI section) |
| `CPawnBeautifierDominoPlayer`, `DominoPlayAnim`/`DominoCanActivateAnim`/`DominoInterruptAnim`/`ApplyNextDominoAnim` | Script-driven animation overrides — how a mission script puts a character into a specific scripted pose/anim |
| `CDominoConsoleCommandManager` | Scripts can register their own console commands |
| `CDominoWaterLevelManager` | Scripted water-level changes (flood events, etc.) |

Every `GenericFunctionToCall<...>`/`GenericFunctionToCall2<...>` template instantiation over a
`CDomino*` member function (`CreateDelay`, `CreateListener`, `SpawnDominoEntity`,
`RegisterConsoleCommand`, `LoadResource`, …) is that method being wrapped for direct Lua callability —
this is the concrete binding mechanism behind the Lua API surface documented on
[the Lua API page](./lua-api-surface.md). For what the actual `.lua` script files built on top of this
binding layer look like — Domino turns out to be node-based visual scripting, not hand-written Lua, with
a ~115-node standard library and 832 authored mission graphs — see [Domino
Scripts](./domino-scripts.md).

**Two distinct ways Domino talks to an entity.** `CDominoComponent`'s constructor sets *two* vtable
pointers on itself (multiple inheritance) — consistent with it being both a `CEntityComponent` and an
`IEntityTask`, meaning it sits directly in the per-frame task graph documented above, for continuous
per-frame checks. `CTaskSendDominoEvent`/`CTaskCheckDominoData`, by contrast, derive from `CTask` (the
AI task-tree base — see below), not `IEntityTask`. **Practical read: Domino delivers one-shot mission
events by producing tasks into the AI behavior-task system**, the same mechanism that drives regular AI
behavior, rather than a separate ad-hoc RPC layer.

**The patched-Lua question.** `lua_loadfile` (`0x09bd7850`) — the actual interpreter entry point,
called from `lua_dofile`/`luaB_loadfile` — resolves its path via `CFileManager::FileExists` /
`CFileManager::FileOpen`. That's the same generic engine file-resolution path every other asset type
goes through; there is no Lua-specific or archive-only special case. This means the community
disagreement in [Gotchas](../modding/gotchas.md) over whether a patched `.lua` file is honored at
runtime isn't really a question about Lua at all — it reduces to the same question that already governs
every other patched asset (see [archives](../file-formats/archives-fat-dat.md)), and the negative 2011
report is more plausibly explained by a wrong archive/path or a stale resource-cache entry than by the
engine special-casing script files.

`CDominoDelayManager::Update` (`0x093bf270`) is worth a specific callout: each expired `CDominoDelay`
calls `CScriptCallbackSystem::CallCallback(...)` directly — confirming a "delay" is literally a deferred
Lua function call, i.e. this is the concrete mechanism behind the reinforcement/respawn timers already
noted in [the file manifest](../modding/file-manifest.md#9-lua-scripts--partial).

### `CAIEngine` — a classic sense/decide/plan/act architecture, group-aware

`CAIEngine::PreUpdate`/`PostUpdate` (`0x09a4b460` and its counterpart) are themselves thin: gated by an
enabled flag and a reentrancy guard, then one virtual call out to whatever's stored in the engine — all
the real structure lives in what gets registered in `CAIEngine`'s constructor, which is effectively the
AI subsystem's full bootstrap. Two class hierarchies, registered through the same
`CFactory<T>::Register`-by-`CStringID` factory pattern seen elsewhere (`CGameModeManager`, resource
types):

- **AI objects**: `CNomadObject → CAIObjectRoot → CAIObject → CAgent → {CDispatcher, CCollective}`.
  Individual agents (presumably one per NPC) can belong to a `CCollective` (squad-level group AI)
  coordinated by a `CDispatcher` — group/squad behavior is a first-class part of the object model, not
  bolted on.
- **AI tasks**: `CNomadObject → CTaskRoot → {CAction, CDecision, CScanner, CPlan → CBrain}` — a textbook
  sense → decide → plan → act pipeline, with `CBrain` as the top-level per-agent orchestrator. This is
  exactly where the Domino integration points above attach: `CBrainDomino` (a brain variant) and
  `CScannerDominoEvent` (a scanner variant) let mission scripts inject behavior and perception events at
  the same level as the AI's own senses and decisions, not through a side channel.

Also registered here: `CPersonalitySystem` (very likely the code-side counterpart to the
`enemy_archetypes.xml` FOV/awareness tuning already noted in
[the file manifest](../modding/file-manifest.md#2-entity--object-binary-data-fcb--tooled) and
[Patrols](../modding/guide/patrols.md)), `CAIDebugTool`, `CPathManager`/`CPathfinderNodePool`
(pathfinding), and — worth flagging on its own — **`CNavMeshSectorResource`**, registered as a real
streamed resource type (`CResourceManager::RegisterNewResourceType` +
`StreamingManager::RegisterAllocCallBack`, the same pattern used for ordinary world-streamed data).
That's the runtime loader for `.nvm`, the one file format in [the file
manifest](../modding/file-manifest.md#6-navigation-mesh-nvm--locked) with no RE work behind it at all —
a concrete class name and load path to start from whenever `.nvm` gets picked up.

### `MSAnim` — animation is a resource-registration hub, not a per-frame animator

`MSAnim::Update` (`0x09b98460`) is almost nothing: it advances a tick counter and a delta-time
accumulator. `MSAnim`'s real substance is in its constructor, which — following the exact same
`CFactory`/`CResourceManager::RegisterNewResourceType` + `StreamingManager::RegisterAllocCallBack`
pattern seen for `CAIEngine` and `CGameModeManager` — registers a family of streamed animation resource
types: `CMovementResource`, `CAnimationPackageResource`, `CAnimationResource`, `CSkeletonResource`,
`CFrankensteinPoseResource` (procedural pose blending/assembly — the name is Dunia's own), and a facial
pair, `CFaceActorResource`/`CFaceAnimResource` (plus a `CFaceAnimModel::LoadFacialConfig()` call).
**`MSAnim` is the asset/resource-type bootstrap for animation, not the thing that actually animates
anything per frame.**

The actual per-frame work happens in `CAnimationComponent` — and it has the exact same dual-vtable
construction pattern already seen on `CDominoComponent`, confirming it's a real `CEntityComponent`/
`IEntityTask`, sitting directly in the dependency-graph job scheduler documented under
`CEntitySystem::Update` above, not a separate animation "subsystem tick." More interesting: its
constructor wires up its own internal mini pipeline of named callback stages —
`PrePhysTree`/`PostPhysTree`, `PrePhysFacial`/a matching facial stream, `RegisterStreamUpdateMatrix`/
`GetCodeUpdateMatrix` — each backed by its own `CJobStreams` instance (the same job-stream mechanism
`CJobScheduler` runs). **Animation evaluation is itself split into pre-physics and post-physics job
stages** — skeletal pose evaluation before the physics step, ragdoll/physics-driven displacement applied
back after it — with facial animation and matrix updates as their own parallel streams, rather than one
monolithic per-entity animate() call.

Locomotion decision-making (which move/animation state an entity should be in) is a separate concern
from playback: `CMoveStateMachine` (constructed per-entity via `SetMoveStateMachine`, queried by Domino
for things like whether an "interrupt" move state exists) sits on top of a more general
`CGOStateMachineDriver`/`CGOStateMachineTrack` framework — a generic named-state, named-transition
system used broadly for object behavior (not just animation), loaded from `CStateMachineResource`/
`CStateMachineBlobResource`. Locomotion is one instance of a general state-machine framework, not a
bespoke animation-only mechanism.

### Weapons & inventory — strategy objects over data-driven properties, event-driven state

**Inventory** is a straightforward typed-item hierarchy: `CInventory` (the container, per pawn) holds
`Gear::RefCountedPtr<CInventoryItem>` entries (`Gear::` — confirmed elsewhere as Dunia's low-level
runtime/utility layer, e.g. `Gear::ThreadBase`, `Gear::AdaptiveLock` — items are ref-counted at that same
foundational level). `CInventoryItem` specializes into `CInventoryItemWeapon`, `CInventoryItemGadget`
(→ `CInventoryItemEquippedGadget`/`CInventoryItemEmbeddedGadget`), `CInventoryItemEquipment`, and
`CInventoryItemAmmoPouch`. UI never touches inventory data directly — `CInventoryViewPawn` and
`CInventoryViewWeapon` are adapter/projection objects (one `CInventoryViewWeapon` is allocated directly
inside `CWeapon`'s own constructor), the same "the UI layer only ever sees a view object, never the raw
system" shape already seen for `CFCXGameplayManager` as a Lua-facing service.

**Weapons compose a fire mode as a strategy object.** `CWeapon` (`: CEquipmentBase`) doesn't hardcode
how it fires — `CWeaponFireStrategy` is a base class with one concrete subclass per fire-mode archetype:
`CWeaponFireBulletStrategy`, `CWeaponFireFlameStrategy` (flamethrower), `CWeaponFireMeleeStrategy`,
`CWeaponFireMortarStrategy`, `CWeaponFireProjectileStrategy` (rockets), `CWeaponFireIEDStrategy`
(improvised explosives). Each strategy has a matching `*Properties` class
(`CWeaponFireBulletProperties`, etc.) plus a shared `CWeaponPropertiesCommon` and a central
`CWeaponsPropertiesRepository` — this is the runtime-side counterpart of the `.fcb`-driven weapon
stat tuning already documented in [the file manifest](../modding/file-manifest.md#2-entity--object-binary-data-fcb--tooled)
(`41_WeaponProperties.xml.fcb`): the strategy object is the behavior, the Properties object is the data
that parameterizes it, and they're deliberately factored apart.

**Weapon state is event-driven, not polled.** Reload isn't a flag, it's a sequence of distinct event
objects — `CWeaponEventReload` → `CWeaponEventReloadDonePreConsumption` (the point before ammo is
actually deducted, presumably where an abort is still possible) → `CWeaponEventReloadDone`, with a
parallel `CWeaponEventReloadAbort`. Weapon degradation (jamming) is modeled the same way:
`CWeaponCanBreakEvent` / `CWeaponBrokeEvent` — this is the binary-level confirmation of FC2's
weapon-degradation mechanic, implemented as explicit state-transition events rather than a hidden
durability counter. Persistence uses a Memento pattern — `CWeaponMemento`, `CWeaponControllerMemento`,
`CWeaponProjectileMemento`, `CWeaponProjectileControllerMemento`, `CWeaponIEDMemento` — the most likely
concrete answer to the still-open "which fields does each entity class's `RegisterProperties` capture"
question in [the savegame page's Unknowns](../file-formats/savegame.md#unknowns): weapon save/restore
almost certainly serializes through these Memento objects rather than ad hoc field-by-field capture.

`CWeaponBazaar` (the arms-dealer/shop system, with `UnlockItem` exposed to Lua the same way
`CFCXGameplayManager::SetMapArmy` was) is the progression/economy layer sitting on top of all of the
above — a separate concern from how a weapon fires or persists.

### World streaming — generic resources plus a declared dependency graph, half-invisible on this binary

Two honest limits up front: `C3DEngine::PreUpdate` is an empty stub on this build, and (per
[Overview](./overview.md)) rendering itself was compiled out entirely — so whatever actually decides
"which sectors are near the camera right now" on the real PC client, driven by view/visibility, isn't
present to trace here. What *is* traceable is everything underneath that decision.

`StreamingManager` itself is small and generic: a `CStringID → allocator-callback` hashtable. Every
resource-owning subsystem traced so far — `CAIEngine` (workspace/navmesh sectors), `MSAnim` (movement/
skeleton/facial resources), `RTxcManager` — registers its own allocation callback into this one table
via `StreamingManager::RegisterAllocCallBack`. Streaming isn't a bespoke system per content type; it's
one generic allocation-callback registry that every content type plugs into, matching the same
`CFactory<T>`/`CStringID`-keyed pattern used for game modes, AI object types, and AI task types
elsewhere in the engine.

World content itself is organized into `CWorldSector` objects, which are ordinary entries in the same
generic `CResourceManager` every other resource type goes through (`GetResource<CWorldSector>`) — not a
separate bespoke loader. Loading one is driven by a declared **resource-dependency graph**
(`BuildRsrcDep`/`LoadDep`, referencing `CWorldSector` among other resource kinds) rather than purely
ad hoc runtime proximity queries, and each sector has a companion `CSectorPreloadResource` — a two-tier
preload list (a strict tier, plus a `ms_enableRelaxed`-gated wider/lower-priority tier) naming what else
should be resident while that sector is active. Practical read: streaming decides *what* to load through
declared per-sector dependency/preload manifests; the (render-coupled, not present here) visibility
system decides *when*.

### Save/load — a generic reflection framework, not per-subsystem bespoke code

The top level is a straightforward async operation: `CGameFilesService::DoLoadGameFile` creates a
`CGameFile` descriptor and hands off to a `CStorageDevice` abstraction (a save-location abstraction —
memory card / disk / cloud — inherited from the multi-platform, console-oriented heritage even though
this build targets Linux), setting a state field rather than blocking. The handler names spell out an
explicit state machine: `HandleOp_LoadGameFile_AutoLoad_SelectStorageDevice_Complete` →
`HandleOp_LoadGameFile_AutoLoad_Loading_Complete`, the same completion-callback shape as the
`Echo::IOperation`/`COperationNotifier` async-operation pattern already found in networking — save/load
is just another async operation on that same generic mechanism, not a special case.

The more interesting result is what happens per entity. `CPersistenceDB::RestoreEntity` — after cutting
through its hashtable bookkeeping (a fairly ordinary two-level `entity-id → record` lookup) — bottoms
out in one call: `CNomadObjectDescriptor::LoadState(entity, serializableNode)`. `CNomadObject` is the
*same* base class found under the AI object hierarchy and AI task hierarchy in the [`CAIEngine`
section](#caiengine--a-classic-sensedecideplanact-architecture-group-aware) above — see the next section
for why that's the real point. **Persistence isn't bespoke per-entity-class serialization code; it's one
generic reflection framework (`ISerializableNode`/`CNomadObjectDescriptor`) that anything built on
`CNomadObject` gets for free.** That's the concrete architectural answer to the still-open "what does
each entity class's `RegisterProperties` actually capture" question in [the savegame page's
Unknowns](../file-formats/savegame.md#unknowns) — it's whatever that class's `CNomadObjectDescriptor`
declares, the same mechanism every other reflectable object in the engine uses, not hand-rolled
per-class code.

### Physics — Havok is linked directly, not wrapped behind a home-grown abstraction

`CPhysWorld` is a thin FC2-side facade (`CPhysWorldImplBase` → `CPhysWorldImpl`) directly over a
genuine, statically-linked, unmodified Havok Physics SDK — `hkpWorld`, `hkpWorldObject`,
`hkpWorldOperationQueue` (bodies are added/removed from the simulation through a deferred operation
queue, not mutated mid-step), `hkpWorldCinfo`, `hkpWorldMaintenanceMgr` are all real Havok classes, not
FC2 reimplementations. `CPhysWorldThreadMemory`/`Impl` is per-thread scratch memory, tied to the
`CPhysTimeStepThread` background thread found in the main-loop section — Havok's standard thread-local
allocation pattern. The facade/impl split (`CPhysWorldImplBase` abstract, `CPhysWorldImpl` the concrete
Havok-backed implementation) is the same generic-interface/concrete-backend layering used throughout
this codebase (`CGame`/`CXGame`, `CGameMode`/`CFCXGameMode*`) — Havok is just the backend slotted behind
it, not a special case.

Entities become physical the same way they get any other capability — a `CEntityComponent` fetched via
`GetComponent<T>`. `CPhysComponent` is the base, specialized by physical-object kind:
`CCharacterPhysComponent` (pawns), `CRigidPhysComponent`, `CCompoundPhysComponent` (multi-body/
destructible), `CStaticPhysComponent`/`CStaticClusterPhysComponent` (non-moving world geometry, the
cluster variant presumably batched for efficiency), and `CVehicleWheeledPhysComponent`/
`CVehicleFloatingPhysComponent` (cars vs. boats, as distinct specializations).

### The unifying spine: `CNomadObject`

Decompiling `CEntity::GetComponent<CPhysComponent>()` surfaces its full ancestry:
`CPhysComponent : CEntityComponent : CNomadObject`. That closes a loop that's been visible piece by
piece across this whole document: `CNomadObject` is also the root of the **AI object** hierarchy
(`CAIObjectRoot → CAIObject → CAgent`) and the **AI task** hierarchy
(`CTaskRoot → {CAction, CDecision, CScanner, CPlan → CBrain}`) documented under `CAIEngine`, and it's
what makes the generic **persistence** mechanism (`CNomadObjectDescriptor::LoadState`) possible at all.

Querying the full `GetComponent<T>` instantiation list turns up the breadth of what specializes
`CEntityComponent`/`CNomadObject` in practice: AI (`CAIComponent`, `CFCXAIComponent`), animation
(`CAnimationComponent`, `CSimpleAnimationComponent`), physics (the `CPhysComponent` family above),
graphics (`CGraphicComponent`, `CBaseGraphicComponent`, `CCustomMaterialComponent`), vegetation
(`CRealtreeComponent` — the entity-side hook for the `RTxc*` system documented above), vehicles/gadgets/
weapons (`CGadget`, `CMountedWeapon`, `CWeapon`), triggers and gameplay volumes
(`CProximityTriggerComponent`, `CTriggerComponent`, `CFireRegionComponent`, `CCapturePoint`,
`CSafeHouseComponent`, `CMedicStation`), counters/economy (`CFCXCountersComponent*`,
`CEconomyComponent`), and persistence itself (`CPersistComponent` — very likely the per-entity opt-in
marker for whether an instance gets a save record at all, the other open question in
[savegame.md](../file-formats/savegame.md#unknowns)).

**The practical shape of the whole engine, stated plainly**: almost everything that isn't pure
engine-core plumbing is a `CNomadObject`. Entity components are one specialization of it, fetched by
name (`CStringID`) through `GetComponent<T>`, scheduled every frame through the dependency-graph job
system documented at the top of this page. AI objects and AI tasks are a second specialization, wired
through the same `CFactory<T>`-by-name registration pattern used for game modes and resource types.
Persistence works uniformly across all of it because it's one reflection mechanism operating on the
common base, not per-subsystem serialization code. The subsystems documented on this page — physics,
animation, AI, Domino, weapons, streaming — read as separate systems from the outside, but they're
consistently built as specializations of the same small set of patterns (`CNomadObject` composition,
`CStringID`-keyed factories, job-scheduled per-frame update), not independently-architected subsystems
that happen to coexist.
