---
name: implementation
description: Use when the user asks to implement, add, alter, extend, refactor, or fix something in this repo
---

## Comment rules

Applies to every language and every file. No exceptions per-language.

**Placement**

- A comment goes on its own line, directly above what it describes - never on the same line as an
  expression.
- Attach it to the thing itself: a class comment sits on the class, not above the enclosing
  namespace.
- Never comment a namespace.
- Never close a scope with a comment (`} // namespace FCSE`, `#endif // GUARD`).

**Length**

- 1 line is the norm, 2-3 lines rare, 4-5 lines extra rare. 6+ lines means it is over-written: cut
  it, or move the knowledge to the docs.
- Comments in header files may run longer than in implementation files, but stay concise and
  relevant.

**Content**

- State what the thing is, then stop. Cut rationale, history, decompile provenance, and surveys of
  who calls it.
- Self-documenting code gets no comment. `Register(void* fn, const char* name)` needs none.
- Spend more length only when the code is genuinely complex, or looks simple but does something
  surprisingly different.
- Knowledge about the code belongs in a comment. Knowledge about how Dunia.dll behaves belongs in
  `docs/docs/engine-internals/`.

## Bias to subtraction

Writing more is the easy failure. Noticing what could be shared, collapsed, or deleted is the hard
one, so spend the effort there.

- Search for an existing implementation before writing a new one. Duplicated logic is the default
  failure mode, not a rare one.
- Prefer changing a call site over adding a parameter, and a parameter over a new function.
- A change that only adds lines is suspect. Say what it made redundant - or say that nothing did,
  and why.
- When the change is complete, run `/simplify` and apply what it finds. The pass that wrote the
  code is anchored on it, so the review needs a different objective than the one that produced it.

## Hooking Dunia.dll

- Before declaring a detour's signature, find the function's `RET n` and make the detour pop exactly
  n bytes. Ghidra drops stack arguments a function never reads, and a wrong pop drifts the stack
  every frame until the game drops to desktop with nothing in the crash log.
- Before a mid-hook reads a stack argument away from the entry, list every `[esp+X]` write between
  the entry and the hook site, converted to entry-relative offsets: MSVC reuses an argument's slot
  once the argument lives in a register. A pointer-looking `0x42xxxxxx` is a float in a reused slot.
- When a hook crashes, read `bin\fcse.log` first: it logs the faulting module, offset and registers.
  `dumpbin /disasm` of the plugin maps the offset to the instruction.

## Tests

- Ground truth is retail files only. Community samples (`tools/misc/format-samples/`, files from
  Discord) may be modded or from another Dunia game: never pin a test or validate a parser on them.
- A heuristic that removes things from view (filtering, collapsing, culling, dedup) is run over the
  whole retail corpus before it ships, printing per-object before and after counts. At least one
  test uses the shape that dominates the data, not a synthetic one.
- A format test reads 2, at most 3, deliberately chosen special-case files, named one per row.
  Never loop over every file of a type: a sweep only grows, and the suite must stay under a minute.
- JackAll tests read retail files from `src/JackAll.Tests/Fixtures/<Format>/` (gitignored, via
  `Fixture.Locate`). Never from `tmp/`, never by enumerating a directory of game files.
- A new edge case is a new fixture file copied out of `tmp/` plus a new row. Copying out is the only
  thing a test change does to `tmp/`.
