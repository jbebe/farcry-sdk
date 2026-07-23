---
sidebar_position: 3
---

# Dunia.dll — Command-Line Parsing and Full Flag List

Part of the Dunia.dll note set — see [the overview](./overview.md) for the binary identification.

## Confirmed behavior: command-line parsing and full flag list

Traced live from `RunGame` (`0x10006510`) down through `InitDuniaEngine` (`0x10004900`) and its
callees, all fully decompiled and read. Two independent parsing mechanisms exist:

1. **Ad-hoc `strstr(cmdline, "-flagname")` checks** scattered across several functions — presence-only
   flags, or (via the helper `FUN_1003f7f0(cmdline, "-flagname", &outBuf)`) flags that also capture
   the following token as a value.
2. **A generic `-key value` tokenizer**, `ParseGamerProfileArgs` (`0x10661950`, renamed from
   `FUN_10661950`) — walks the whole command line splitting on `-`/space and stuffing every pair into
   a `"GamerProfile"` property-bag object via `FUN_106616e0(profile, key, value)`. This means *any*
   `-key value` pair is accepted generically; the flags below are just the ones with confirmed,
   dedicated consumers reading back out of that bag (or checked directly via `strstr`).

Call graph, top to bottom:

```
RunGame (0x10006510)
  strstr(cmdline, "-openautomate") -> FUN_10005fa0 (QA automation loop, separate from normal play)
  else: InitDuniaEngine(hInstance, 0, 0, cmdline, ...)
          ParseGameConfigFlags(cmdline)         [0x10662c70, was FUN_10662c70]
          ...
          DispatchLaunchMode(cmdline)           [0x10663d40, was FUN_10663d40]
            -> ParseLoadSaveArgs                [0x10661f50, "-load"]
            -> ParseWorldAndBenchmarkArgs        [0x10662f90, "-benchmark"/"-world"/"-map"/spawn*]
            -> DispatchNetworkMode               [0x10663af0, "-host"/"-client"/"-join"/"-wait"]
                 -> ParseOnlineSessionArgs        [0x10662440, "-login" etc.]
                 -> ParseJoinArgs                 [0x10662b40, "-join <ip>"]
            -> (editorpc / default paths, not further split out)
```

### Window / bootstrap flags (checked directly in `InitDuniaEngine`, before engine subsystems init)

| Flag | Effect |
|---|---|
| `-borderless` | Borderless window |
| `-dedicated` | Dedicated-server mode; combined with `-norender` skips window/render init entirely |
| `-norender` | No rendering (only takes the headless branch together with `-dedicated`) |
| `-editorpc` | Remote-editor/PC connection mode — routes into `DispatchLaunchMode`'s editorpc branch (`FUN_10661b40`, not further explored) |
| `-xpos <n>` / `-ypos <n>` | Window position (only read when not `-host`/`-client`) |
| `-host` / `-client` | Presence checked here to build a window-title suffix (`"host : client"`); real network handling happens later in `DispatchNetworkMode` |
| `-d3dmts` | Sets a D3D multithread-safety flag (`DAT_10f92043 = 1`) |
| `-3dplatform <d3d10a\|d3d10\|d3d9>` | Forces the render backend |

### Config toggles (`ParseGameConfigFlags`, `0x10662c70` — runs once at the very start of `InitDuniaEngine`)

| Flag | Effect |
|---|---|
| `-cmdfile <path>` | Load additional arguments from a file, recursively re-parsed (`FUN_10661cf0`) |
| `-logFile <path>` | **Parsed but appears non-functional in retail** — see note below |
| `-nomouse` | Disable mouse |
| `-noexmouse` | Disable "extended" mouse (raw input?) |
| `-nopad` | Disable gamepad |
| `-nobf` | Boolean toggle, name unexplored — likely a netcode/"battlefield" layer disable |
| `-nocompile` | Disable shader (or script) compilation |
| `-norender` | Also read here (duplicate of the bootstrap check above) |
| `-runscriptindebug` | Run Lua scripts in debug mode |
| `-zombieai` | Boolean AI toggle, name unexplored — plausibly a simplified/dumb-AI debug mode |
| `-usearchivecache` / `-noarchivecache` | Force-enable/disable the packed-archive read cache |

### World / spawn / save (`ParseWorldAndBenchmarkArgs` 0x10662f90, `ParseLoadSaveArgs` 0x10661f50 — reached via `DispatchLaunchMode`)

| Flag | Effect |
|---|---|
| `-world <name>` / `-map <name>` | Load directly into a level, skipping the main menu |
| `-spawn <name>` | Spawn at a named spawn point |
| `-spawnpos <x,y,z>` | Spawn at an explicit position |
| `-spawnangle <yaw,pitch,roll>` | Spawn with an explicit orientation (values scaled by a deg→rad-style constant `DAT_10e0eed8`) |
| `-load <savename>` | Load a specific save game directly (walks `PersistenceDB`, reads back a `"PlayerPos"` property afterward) |
| `-bfname <name>` | Session/"battlefield" display name (also read again in the online-session parser) |

### Benchmark harness (`ParseWorldAndBenchmarkArgs`, entered whenever `-benchmark` is present)

| Flag | Effect |
|---|---|
| `-benchmark <playback\|record\|sectors\|spawnpoints\|path>` | Selects benchmark sub-mode. Any other value (without `-benchmarkinputname` also present) is a hard usage error: `"Invalid parameters for -benchmark. Ex; -benchmark {playback\|record\|sectors\|spawnpoints\|path} -world worldname -map mapname"` |
| `-benchmarkinputname <name>` | Alternate trigger accepted instead of `-benchmark playback` |
| `-benchmarkloop <n>` | Loop count for the benchmark (default 1) |
| `-benchmarkfixedframerate` | Force a fixed frame rate during the benchmark |
| `-benchmarkdisableai` | Disable AI during the benchmark (also pokes an engine flags int, `+0x20 = 0xfff7`) |
| `-benchmarkid <id>` | Numeric benchmark ID |

### Networking (`DispatchNetworkMode` 0x10663af0 → `ParseOnlineSessionArgs` 0x10662440 / `ParseJoinArgs` 0x10662b40)

`-host`, `-client`, `-join`, and `-wait` all route into `DispatchNetworkMode`, which in turn always
calls `ParseOnlineSessionArgs` — and that function hard-aborts if `-login` is absent
(`"Invalid parameters for -host, missing (-login)"`), regardless of which of the four triggered entry.

| Flag | Effect |
|---|---|
| `-host` | Launch as server (requires `-login`) |
| `-client` | Launch as a network client (also requires `-login`, same gate) |
| `-join <ip>` | Connect to a specific server IP; validated with `inet_addr` — invalid/missing IP produces the exact usage strings `"Trying to launch an online client with an invalid ip (-join xxx.xx.xx.xx)"` / `"...but ip is missing..."` |
| `-wait` | Wait-for-connection mode, otherwise same gating as `-host`/`-join` |
| `-login <name>` | Required Ubi.com/Agora login name |
| `-password <pw>` | Login password |
| `-keyonline <key>` | Likely a CD-key/license token for online auth |
| `-sessionuid <uid>` | Numeric session ID (parsed with `_strtoui64`) |
| `-team` / `-ctf` / `-vip` | Selects game mode constant |
| `-online` / `-lan` | Network scope (`-online` = Agora-backed online, code `2`; default/`-lan` = `3`) |
| `-noagora` | Forces non-Agora networking; **combined with `-online` is a hard error**: `"Cannot launch an online game (-online) without using Agora (-noagora was detected)"` |
| `-ranked` | Marks the session ranked |
| `-dedicated` | Also read here, marks the session as a dedicated server for the online layer specifically |

### Misc, read in `DispatchLaunchMode` itself before branching

| Flag | Effect |
|---|---|
| `-exec <file>` | Execute a console/Lua command file at boot |
| `-notracking` | Disables the telemetry/tracking client |

### QA automation path (`-openautomate`, handled entirely separately by `FUN_10005fa0` — not renamed, not further explored)

If `-openautomate` is present, `RunGame` skips the normal game loop entirely and enters a
numeric-command dispatch loop (`FUN_10299a00` returns a case 0–6, dispatching to
`FUN_10006710`/`FUN_10008620`/`FUN_10007050`/`FUN_100075a0`/`FUN_100065b0`/`FUN_10006600`) — an
internal QA/automation harness, not reachable through normal `-flag` parsing. None of these 6
handlers have been examined yet.

**Not yet resolved**: the exact semantics of `-nobf` and `-zombieai` (booleans read but never
named beyond their flag string); what `-client` does differently from `-join` given both share the
same `ParseOnlineSessionArgs` gate; the `-editorpc` handler `FUN_10661b40` itself.

### `-logFile` appears dead in the retail build

Live-tested (`.\FarCry2.exe -logFile C:\path\engine.log`): **no file is created.** Traced why in
`ParseGameConfigFlags` (`0x10662c70`) and its caller `InitDuniaEngine` (`0x10004900`):

- The flag is genuinely parsed — `FUN_1003f7f0(cmdline, "-logFile", param_1 + 0x13)` captures the
  path into a dedicated `std::string` field of the config object `ParseGameConfigFlags` constructs
  (called twice, redundantly, at `0x10004965`-ish region — harmless duplicate, not a clue by itself).
- That config object is constructed directly on `InitDuniaEngine`'s own stack frame (`this` = the
  address loaded into `ECX` right before the `0x10662c70` call, confirmed via disassembly — the three
  boolean fields immediately after it, `nomouse`/`noexmouse`/`nopad`, are read back a few instructions
  later at fixed stack offsets and drive `DAT_10fd42c0..c2`, so the frame layout is confirmed, not
  guessed). The logfile string sits at `this+0x4c` in that same frame.
- **Full disassembly of `InitDuniaEngine` was read end to end looking for any read of that `this+0x4c`
  stack slot after the parse — none exists.** No `CreateFileA`/`fopen`/log-write call anywhere in the
  function takes that buffer as an argument.
- The RTTI-derived class list (`list_classes`) does contain a `CLog` — but its mangled RTTI name is
  `.?AVCLog@MassiveAdClient3@@`: it belongs to the third-party **MassiveAdClient3** in-game-advertising
  SDK linked into this DLL, not an engine logging facility. No other `Log`-named class or function
  exists in the binary.
- Consistent with the [known gotchas](../modding/gotchas.md)'s independently-sourced community
  finding that **Far Cry 2 retail has no in-game dev console at all** — debug/dev-facing
  instrumentation (console, and very plausibly file logging alongside it) reads as compiled-out or
  stubbed for the shipped build, not merely hidden behind a flag.

**Conclusion**: `-logFile`'s value is captured and then goes nowhere within the boot path — it looks
like vestigial parsing left over from a development build whose actual log sink was stripped for
retail, rather than a flag the user is invoking wrong. Not proven for the entire 20MB DLL (this traced
one function's disassembly exhaustively, not every one of the ~90k functions in the binary), but no
plausible consumer turned up anywhere reachable from boot.
