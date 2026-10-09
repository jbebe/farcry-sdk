---
sidebar_position: 17
---

# Loading a Save

What happens between choosing a save and playing it, and what each step waits on. Everything here was
read on the GOG 1.03 `Dunia.dll`, with names from the Linux server's symbols **(RE-verified)**. No
timing was measured; the waits below are the ones the code fixes, not how long a load takes.

The frame-level background — which subsystems an operation lets run, the game clock, the physics step —
is in [the architecture page](./architecture.md#which-subsystems-a-frame-runs).

## Every load path ends in one call

| Path | Where it starts |
|---|---|
| Continue | the story-mode page, after a fixed 1.5 s wait (`0x108B8B60`) |
| The Load Game page, from the menu or the pause menu | `CLoadGamePage::OnHandleCurrentPageState` (`0x10854CB0`) |
| F9 and other in-game loads | `OnLoadOpDone` → `FinalizeGameFileLoad` |
| Reloading after death | `CGameOverPage::HandleLoadLastSave_Loading` (`0x108450A0`) |

All four call `CFCXSingleGameFilesService::RunLoadedCampaignGameFile` (`0x10728D10`). It builds a
`CFCXGOSingleMatchNode` whose builder is `CFCXGOBuilderSingle`, then switches the game's context to
it. The other builder, `CFCXGOBuilderSingleLoad`, is used only by the `-load` command-line option.

## The operations, in order

`CFCXGOBuilderCommon::PreBuild` (`0x107ED5A0`) queues these. The container runs one at a time: execute,
then update until done, then the next. So even an operation that finishes at once costs two
updates. Each runs with its own engine update mask, which decides what the rest of the engine does
meanwhile:

| # | Operation | Mask |
|---|---|---|
| 1 | `CFCXInitializeTerminalsOperation` | `0x10` |
| 2 | `CFCXPrepareLoadingScreenOperation` | `0x12` |
| 3 | `CFCXUnloadWorldOperation`, only when a world is loaded | `0xC00` |
| 4 | `CFCXDeleteSessionOpCtn`: shut the game mode's network down, delete the session, idle and shut down the net engine | `0xC00` |
| 5 | `CFCXLoginOperation` | `0xC00` |
| 6 | `CFCXCreateSessionOpCtn`: start the net engine, NAT traversal, create the session | `0xC00` |
| 7 | `CFCXCreateGameModeOperation`: `CFCXGameModeChange`, then `CFCXPostGameModeChange` | `0xC00` |
| 8 | `CFCXRunBatchFileOperation` | `0xC00` |
| 9 | `CFCXLoadWorldOperation`: pre-load, load, `LoadWorldSynch`, post-load and their callbacks | `0x41` |
| 10 | `CFCXSkipFramesOperation` | `0xFFFF` |
| 11 | `CFCXPrepareRendererOperation` | `0x1` |
| 12 | `CFCXGameStartOperation` | `0xFFFF` |
| 13 | `CFCXUnloadLoadingScreenOperation` | `0xFFFF` |

Travel start and stop operations wrap steps 3 and 9 only when travelling to another world, which a
save load does not. The `-load` and benchmark builders add `CFCXLoadGameStartOperation` before step 9.

## What each step waits on

**Deleting the session costs a second.** Step 4 runs on every load. A single-player session is type 1
(the `CGOSessionNode` constructor sets it, and no single-player code changes it). Type 1 only skips
the network disconnect. The delete then lingers until no client is left and
`CSessionDeleteGameOperation::ms_idleTime`, **1.0 s**, has run down, whatever the session type.
Every flow creates a session in step 6, so there is always one to delete.

**Changing the game mode** has no timer. It waits for the new mode to load: `CGameMode::OnLoaded`,
then `FinalizeLoad` on the mode's next update, then the operation reloads the action map and
completes. Which services hold that up was not traced.

**Streaming the world** is `CFCXLoadWorldSynchOp::DoExecute` (`0x10791C20`), a loop with no time
limit in single player. Each pass does the following:

- **Streams.** It resets the streaming budget, then updates the world and the request manager twice,
  with a `Sleep(0)` between.
- **Keeps sound going.** The sound system updates every 0.05 s.
- **Keeps the rest alive.** Every 0.2 s it updates the session, the net engine and the message pump,
  and flushes pending physics.

It ends when `CWorld::IsLoadPending` turns false: no requests pending and nothing left queued in the
world. In multiplayer the operation adds a 150 s timeout.

**The request thread polls faster during a load.** `CResourceStreamer`'s one `RequestThread` waits on
its stop event between passes: 16 ms normally, 1 ms while a loading screen is up. The loading
screen's show and hide raise and lower a counter, and the world flush does the same. This thread
only schedules requests and copies finished blocks; the reads run on the streaming manager's own
thread. `Dunia.dll` never raises the Windows timer resolution, so a 1 ms wait lasts as long as the
system's timer allows. UFCP's high-precision timer fix raises it to 1 ms.

**Preparing the renderer** walks sub-states, each tick a frame (`0x10790F50`). It first sets the
game clock's delta clamp to 0.0001 s, and puts it back at the end, so game time barely moves while it
runs. The first tick of each sub-state does nothing.

| Sub-state | What it does | Ends after |
|---|---|---|
| 1 | shadows on (cascades 20, 60, 240 m), camera turned to six directions | 8 ticks |
| 2 | shadows off, the same six directions | 8 ticks |
| 3 | first pass only: waits until no shader is pending, then runs 1 and 2 again | no shader pending |
| 4 | sector ambient: far plane pulled in, ambient texture reset, under two render-config conditions | the first ambient update, or a tick limit |
| 5 | spawns copies of the player's weapons and gadgets 0.5 m in front of the camera (0.4 m for gadgets) | all loaded, or 3 ticks; then removes them |
| 6 | resets bloom and completes | |

**Shaders are created on a budget.** Sub-state 3's wait is on the renderer's "pending" flag. That flag
is set while the shader manager has programs whose component objects do not exist yet. They are
created on the background loop's frames: `PrecachePrograms` (`0x1040F160`) looks at up to
`MaxShaderObjectToProcessLoadtime` programs and creates up to `MaxShaderObjectToCreateLoadtime`
**shader objects** a frame. Ordinary frames create up to the runtime budget, the field registered
beside those two and presumably `MaxShaderObjectToCreateRuntime`. `engine\settings\defaultrenderconfig.xml`,
in `common` and `patch`, ships 100, 10 and 5 for the three.

At the background loop's 30 frames a second, that is at most 300 objects a second. The cap applies
only while asynchronous creation is on; `AllowAsynchShaderLoading` is `1`, though which flag it sets
was not confirmed. The [command line](./command-line-args.md) can name these settings as
`-RenderProfile_<name> <value>`; that it takes effect is untested.

**The rest is short.** Skipping frames is two updates. Starting the game completes on its first
update for a save load: its 10 s wait is for a new game's avatar selection, and on timeout it
completes rather than fails. Hiding the loading screen completes at once.

## Who draws while it loads

The render thread runs in one of two modes (`SceneRendererFacade::SetLoopEnabled`, `0x10330770`):

- **Driven:** the main thread posts each frame.
- **Background loop:** `RenderLoop` (`0x103307C0`) redraws by itself, polling with `Sleep(2)` until
  1/30 s has passed.

| Who switches it | When | To |
|---|---|---|
| The loading screen | shown | background |
| `CFCXPrepareRendererOperation` | its setup | driven |
| `CFCXPrepareRendererOperation` | sub-state 3, first pass only | background, then driven again at its end |
| `CFCXPrepareRendererOperation` | its restore | whatever it was before |
| The loading screen | hidden | driven |

Unloading a world and teleporting also switch to driven for one forced frame and back. With the render
thread on, the load-time shader precache runs only on background frames: the loading screen's own,
and sub-state 3's wait.

## Elsewhere during a load

- **PunkBuster** is a service of the main-menu, console and four multiplayer game modes, never the
  single-player one. While its DLL is missing, the main menu's service retries loading it every update.
- **The `-load` option** uses `CFCXGOBuilderSingleLoad`, which adds `CFCXLoadGameStartOperation`;
  see [the command line](./command-line-args.md#save-loading---load).

## Unknowns

- How long any step takes. Nothing here was timed.
- Which services hold up the game-mode change.
- What the pre-load and post-load world operations wait on; they were not read.
- Whether `-RenderProfile_MaxShaderObjectToCreateLoadtime` and its siblings take effect from the
  command line.
