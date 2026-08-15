---
sidebar_position: 12
---

# Data-Root Derivation

:::info[Verified via reverse engineering]
Traced through `Dunia.dll` (GOG) with the `FarCry2_server` symbol build as the naming reference.
:::

Dunia locates its data tree from the **path of the process executable**. The working directory plays
no part on Windows, which matters for any tool that hosts the engine from its own executable rather
than from `FarCry2.exe`.

## The rule

`CNomadPath`'s constructor (`FUN_10226de0`) runs the following:

1. `GetModuleFileNameW(GetModuleHandleW(NULL), …)` — the full path of the running executable.
2. `RemoveFileSpec` — strip the filename, leaving the directory with no trailing separator.
3. **If the remaining string contains the substring `bin` anywhere, truncate at its last
   occurrence.** Otherwise strip back to the last `\` or `/`.
4. Append `data_win32\` and `data\` to produce the two data roots.

`SHGetFolderPathW` then supplies the personal paths, and `"generated"` the cooked-output subfolder
name.

So for a stock install:

```
C:\Games\Far Cry 2\bin\FarCry2.exe
  → C:\Games\Far Cry 2\bin        (RemoveFileSpec)
  → C:\Games\Far Cry 2\           (truncate at last "bin")
  → C:\Games\Far Cry 2\data_win32\
```

## Consequences

Step 3 is a plain substring search, not a path-component match, which makes the behaviour sharper
than "the executable must be in `bin\`":

| Executable location | Derived root | Works |
|---|---|---|
| `<game>\bin\host.exe` | `<game>\` | yes |
| `<game>\bin\myfolder\host.exe` | `<game>\` | yes — the last `bin` is still the game's |
| `<game>\bin\Cabinet\host.exe` | `<game>\bin\Ca` | **no** — `Cabinet` contains a later `bin` |
| `C:\Tools\JackAll\host.exe` | `C:\Tools\` | no — no `bin`, so `data_win32\` is missing |

A host placed outside the install therefore loads `Dunia.dll` successfully and then dies inside
`InitDuniaEngine` with an access violation, because the data roots point at directories that do not
exist.

## Where it is decided

`InitDuniaEngine` calls the constructor at `1000496b`, near its own entry — not from a CRT static
initializer. The path is therefore fixed *during* `InitDuniaEngine`, after `Dunia.dll` is already
mapped, so anything that wants to influence the result has a window between loading the library and
the first engine call.

`GetModuleFileNameW` has exactly two call sites in the binary, and only this one feeds the path
logic.

## What the working directory does control

`GetCurrentDirectoryA` supplies the fallback root for the `game:`, `dvd:` and `host:` virtual
filesystem mount points (`FUN_1002dd50`), all three of which ship with an empty configured root. That
is a separate mechanism from the data roots above and does not affect `data_win32\` resolution.

The Linux dedicated server differs here: `CNomadPath::DetermineExePath` reads `/proc/self/exe`, and
the constructor additionally calls `chdir()` to the executable's directory before deriving
`../data_linux/` and `../data/`. The Windows build has no `chdir` equivalent and imports no
`SetCurrentDirectory`.

## Path templates

A separate expander (`FUN_10012230`) substitutes tokens in configured path strings:

| Token | Expands to |
|---|---|
| `$N` / `$n` | executable basename without extension |
| `$P` / `$p` | the literal `win32` |

In the retail build this is only reachable through debug-dump names such as `disk:$n.gml`, so the
executable's *name* has no bearing on data loading — only its *location* does.
