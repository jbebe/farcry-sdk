---
sidebar_position: 4
---

# Dunia.dll — User Save-Data Folder Resolution

Part of the Dunia.dll note set — see [the overview](./overview.md) for the binary identification.

## Confirmed behavior: Shell API, not `%USERPROFILE%`

`Dunia.dll` embeds the literal strings `"My Games\Far Cry 2\"` (`0x10e09da0`) and `"\My Games"`
(`0x10e0f4a0`). The first is confirmed used: `InitDuniaEngine` (`0x10004900`) pushes it at
`0x10004954` and passes it into `FUN_10003840` (`0x10003840`), which concatenates it with a
`"\"` separator (`DAT_10e09b60`) via generic `std::string` append plumbing (`FUN_100031d0`/
`FUN_10002b20`, both templated STL internals, not registry-specific). This builds the
**relative** path component `My Games\Far Cry 2\` — the root it gets joined onto isn't resolved
in this same call (the temporaries `FUN_10003840` builds are stack-local and destroyed before
return, so either Ghidra is missing a hidden RVO/return-by-reference parameter in its signature,
or the persisted destination is a global written through a pointer not yet traced).

**The root is Shell-API-resolved, not an environment variable.** The strings `"SHGetFolderPathW"`
(`0x10f8da48`), `"SHGetFolderPathA"` (`0x10f8da6c`), and `"SHELL32.dll"` (`0x10f8da7e`) sit
contiguously in a data table — the same layout as every other Win32 API this binary uses, all of
which are resolved **dynamically via `GetProcAddress` off a name-string table**, not through the
PE static import table (confirmed: `list_imports` never surfaces `SHGetFolderPathA/W`, and
`get_function_xrefs`/`get_xrefs_to` return no hits for `GetProcAddress`, `getenv`, or
`GetEnvironmentVariableA` either — Ghidra can't statically resolve xrefs into this lazy-loader
table's entries, consistent with a generic "walk a name array, `GetProcAddress` each one into a
function-pointer table" loader rather than per-call `LEA`s). **`getenv` and
`GetEnvironmentVariableA` are both present in the binary but have zero resolved call sites** —
strong (not yet 100%-conclusive, since the same lazy-loader blind spot could in principle be
hiding a real caller) evidence that `%USERPROFILE%` is *not* how the save folder is built.

**Conclusion**: the folder shown to the user as `Documents\My Games\Far Cry 2\` is built by calling
`SHGetFolderPathA`/`W` (almost certainly with `CSIDL_PERSONAL`/`CSIDL_MYDOCUMENTS`, i.e. asking
Shell32 for the "My Documents" special folder — the exact CSIDL constant wasn't pinned down, the
call site itself hasn't been located past the lazy-import table) and appending the
`My Games\Far Cry 2\` string documented above. This is the standard Vista-era "Documents\My
Games\<title>" convention shared by most licensed middleware of this generation, and matches the
[known gotchas](../modding/gotchas.md)'s community-sourced note that custom maps install to
`Documents\My Games\FarCry2\usermaps\`. Since `SHGetFolderPathA` (not a raw env-var read) is doing
the resolution, the effective path tracks the OS's actual "My Documents" location (which normally
lives under `%USERPROFILE%\Documents` but can differ if the user has redirected that folder), not
a literal `%USERPROFILE%` string substitution.

**Still open**: the exact call site that invokes the resolved `SHGetFolderPathA` pointer and the
CSIDL constant passed to it; where `FUN_10003840`'s built string actually ends up persisted (its
own locals are stack-scoped and destroyed before return, so either a missed hidden-return-pointer
parameter or an untraced global write is responsible); how `"\My Games"` (the shorter, standalone
string at `0x10e0f4a0`) is used — its only xrefs land inside unfinished/uncreated `Function`
regions (`~0x10047300`), not a proper Ghidra function yet.
