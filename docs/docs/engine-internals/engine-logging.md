---
sidebar_position: 19
---

# `Dunia.dll` — Where Log Output Goes

:::info[Verified via reverse engineering]
Traced in the Steam v1.03 `Dunia.dll` and cross-checked against the symbol-bearing Linux
`FarCry2_server`, which names the classes and methods the PC build only numbers. Addresses are
Steam; the FCSE hooks below resolve through its address library on both PC builds. In a running
game (GOG), the console capture was confirmed, and it showed the gap this page now covers: scripts
log through `System:Log`, not `print`.
:::

Retail Far Cry 2 keeps producing log output. Mission scripts call `System:Log` constantly, the
console formats every line it shows, the script system formats every Lua error, Lua's `print`
writes to the C runtime's stdout, and a handful of sites call `OutputDebugStringA`. What the retail build lost is the far end: nothing consumes any of
it. This page is the inventory of what still writes, where each path ends, and which of them FCSE
picks up into `bin\Dunia.log`.

## The console: `CXConsole::AddLine`

Every line the developer console displays passes through one function, `CXConsole::AddLine`
(`0x10294db0`, `__thiscall(console, std::wstring&)`). `PrintLine` (`0x102956f0`, the narrow
`printf`-style entry the engine and PunkBuster use), its wide twin (`0x10295820`), the echo of a
typed line in `ExecuteString` and `DisplayHelp` all call it. `AddLine` replaces `\n` and `\r` in
the text with spaces, then:

1. calls every registered `IConsoleOutputObserver` (a vector at `console+0x7c`, count at `+0x80`),
   vtable slot 1, with the string;
2. appends the string to the console's own ring of **200** lines, under the console's lock.

The observer list is the engine's own extension point for exactly this, and `RegisterOutputObserver`
exists in the server binary — but nothing in either build registers one, so in retail the ring is
the only place a console line ends up. FCSE hooks `AddLine` itself rather than adding an observer,
because the vector is a `CryVector` with a packed capacity field and an inline hook at the function
is both simpler and build-agnostic.

Callers of `PrintLine` on PC, for the record: `ExecuteCommand` and `RunBatch` (errors and echoes),
`CCryEngine::Initialize`, `ConsoleCmdSetSetting`, `PBsdk_Out` (PunkBuster), `PrintToConsole`
(`0x10001c90`, a plain exported wrapper), and three multiplayer status printers.

## Script errors: `CScriptSystem::OnScriptError`

`CScriptSystem`'s constructor (`0x102aa140`) installs two Lua globals into the state:

| Global | Address | What it is |
|---|---|---|
| `_ERRORMESSAGE` | `0x102bffc0` | the stock Lua 4.0 traceback builder: `error: <msg>` plus a `stack traceback:` listing, handed to `_ALERT` |
| `_ALERT` | `0x102a9d90` | `CScriptSystem::ErrorHandler`: takes the message, asks `lua_getinfo("Sln")` for the failing function and source, and calls `OnScriptError` |

`CScriptSystem::OnScriptError` (`0x102a9ae0`, `__thiscall(this, source, function, line,
message)`) is the funnel. Besides `_ALERT` it is reached from `RaiseError` — the C++ bindings'
`"%s.%s wrong number of arguments"` and similar — and from `FormatAndRaiseError`, which turns a
failed `lua_dofile` into `file opening or parsing failed`, `syntax error` or `out of memory`.

What it does with the arguments is build the string `ERROR in function <name>` and the message cut
at its first newline, then destroy both. The server build's copy is identical. **A Lua error in
retail Far Cry 2 is formatted and thrown away**; the game carries on as if the script had returned.
Hooking this one function sees every script error the engine ever sees, with its source location.

## What the scripts call: `System:Log`

The shipped scripts never call `print`. Of 1,189 release `.lua` files in the game data, the ones
that log use `System:Log`, some fifty call sites in all. The main one is the `Log To Console` Domino box,
`domino\system\logtoconsole.lua`. Mission graphs place it with a string such as `"HQ exit
started"`, and its `In` handler forwards each of its inputs to `System:Log`.

`System:Log` and `System:LogToConsole` are registered by `CScriptObjectSystem`'s template setup and
dispatched through a shared thunk (`0x105f8d00`). Each is `__thiscall(scriptObject,
CFunctionHandler*)` and returns with `ret 4`:

| Binding | Steam | GOG | Body |
|---|---|---|---|
| `System:LogToConsole` | `0x105f8c30` | `0x105eb350` | argument-count check, then return |
| `System:Log` | `0x105f8c70` | `0x105eb390` | argument-count check, then return |

The body that printed was compiled out. The server build's copy is also empty: its shared
`CScriptObjectSystem::LogString` is an empty function. **Every mission-graph log line in retail
reaches this function with its text on the Lua stack, and is dropped there.**

The argument is readable without the engine's help. `CFunctionHandler` keeps the `lua_State*` at
`+4`. `GetParamCount` (`0x102be150`) is `lua_gettop` minus two, because this is Lua 4.0, where a C
closure's upvalues are pushed onto the stack after the arguments. Argument N sits at stack index
N+1, which is how the server build's named `CFunctionHandler::GetParam` overloads read it. Reading
the first argument is `lua_tostring(L, 2)` (`0x102aad40`, `__cdecl`), called only when the count is
one: with no argument, index 2 would be the upvalue, and `lua_tostring` converts in place.

The two bindings are 63-byte near-twins, so the address library's bulk matcher could not tell
their GOG counterparts apart. They were resolved by the one operand that differs, the name string
each pushes for its error message.

## stdout and stderr

`Dunia.dll` links `MSVCR80.dll` and imports its stdio: `fputs`, `fwrite`, `printf`, `fprintf`,
`vfprintf`, `fflush` and `__iob_func` (the accessor for `stdin`/`stdout`/`stderr`, 32 bytes apart).
Steam also imports `fputc`; GOG does not. Thirty-five sites read `__iob_func`. The ones that
matter:

- **Lua `print`** (`0x102c3610`, unmarked as a function in Ghidra; the stock `luaB_print`): calls
  `tostring` on each argument and writes to `stdout` with `fputs` alone, three calls through one
  register: the separating tab, the text and the closing newline. GOG's copy is the same shape.
  The text is written, to a stream that has no console attached.
- **Lua's `io` library** (`lua_iolibopen` is called in the constructor above): `io.write` and
  friends, a cluster of functions at `0x10299550`–`0x10299c72`.
- **Gear's assert** (`0x1000e880`): `printf` of `"%s(%u) : ASSERT FAILURE: %s(%s)"`, then
  `OutputDebugStringA` when the Gear runtime config allows it, then `DebugBreak`. Asserts are
  compiled out of retail, so this is reachable only from Gear's own containers.

`FarCry2.exe` is a windowed process with no console, so the CRT's `stdout` is not open and each of
these writes fails silently. `-logFile` is parsed and never used (see
[command-line args](./command-line-args.md#-logfile-appears-dead-in-the-retail-build)).

## `OutputDebugStringA`

Called directly from three places — the sound system's `Lag in RDSound3D tool buffer` and
`Play failed`, and the renderer's `D3D 10.1 detected!`/`D3D 10.0 detected!` — and from
`Gear::NativeDebugOutput` (`0x1000e700`), the default hook of Gear's `ConsoleOut`, which only
forwards when the Gear runtime config word at `0x10f92164` equals 2. Nothing sets it to 2 on the
retail path.

## Dead ends

Registered, callable, and empty:

| Path | Status |
|---|---|
| `System:Warning` | Empty in the server build; the PC copy was not traced |
| `CError::Warning` / `Error` / `CriticalError` and the `Skipable*`/`Entity*` variants | The engine's C++ warning macro. `CError::Report` writes to a `CLog*` when one is set, and shows a `CryMessageBox` for errors; the whole family is absent from the PC build (its `WARNING!`/`Cancel to debug` strings are gone) and has no callers on the server |
| `IAIEngineUtils::Log`, `NS_GameMessageLog::Log`, `PopUtils::LogFormat` | Empty |
| `activate_log` / `deactivate_log` console commands | Stubs |
| `-logFile` | Parsed, unused |

`CLog` itself — `CLogFile`, `CLogMultiple`, `CLogWithCount` — survives as a class hierarchy in the
server, but `CError::SetLog`, the only way to plug one in, is never called.

## Files the engine still writes

Not console output, and not captured: each of these is its own file with its own trigger.

| File | Writer | Trigger |
|---|---|---|
| `domino_log<N>.ddf` | `CDominoService::StartScriptDebugLog` | called from `CDominoService::DoInit`; a Domino graph trace |
| `sound_dep.log`, `benchmark_sectors.log`, `lb_<...>.log` | sound dependencies, the sector benchmark, leaderboards | their respective tools |
| network log | `Echo::CNetworkLog` | `net_log_enable` console command; multiplayer |
| online services log | `Os::LoggerImpl` with `LogAppender`s | `logFileName` in the online engine registry (`SetupOnlineEngineRegistery`); Demonware/Agora |

## What FCSE captures

`bin\Dunia.log` (see `tools/FCSE/src/engine/dunia_log.cpp`) is fed from these points, each tagged:

| Tag | Point | How |
|---|---|---|
| `script` | `System:Log`, `System:LogToConsole` | inline hooks; the first argument is read off the Lua stack as above, then the original runs |
| `console` | `CXConsole::AddLine` | inline hook, before the engine's own handling; the wide string is written as UTF-8 |
| `lua` | `CScriptSystem::OnScriptError` | inline hook; the message, then ` - in function <f> (<source>:<line>)` when known, one log line per line of traceback |
| `stdout`, `stderr` | `fputs`, `fputc`, `fwrite`, `printf`, `fprintf`, `vfprintf` | `Dunia.dll`'s import slots for `MSVCR80.dll`, redirected; a write to either standard stream is assembled into lines and logged, any other `FILE*` passes through |
| `debug` | `OutputDebugStringA` | the `kernel32.dll` import slot, redirected; assembled into lines the same way, then forwarded so a debugger still sees it |

The inline hooks go through the address library and are therefore build-agnostic. The import slots
are found by name in the mapped image, so they need no addresses at all, and each one installs on
its own: GOG has no `fputc` import, and that costs nothing, since nothing in that build calls it.
Anything missing on a build is logged in `fcse.log` and the rest still install.
