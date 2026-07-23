---
sidebar_position: 1
---

# Far Cry 2 Modding

This site collects everything known about modding Far Cry 2 (2008) — practical workflow, file
formats, and engine internals — in one place.

## How this is organized

- **[Modding](/docs/modding/getting-started)** — the practical side: toolchain setup, an
  "Almost Complete Guide" to common edits (reproduced from the community, with attribution), a
  survey of existing mods, and known gotchas.
- **[File Formats](/docs/category/file-formats)** — every known FC2 container/asset format. Where a format
  has been independently confirmed by reverse engineering the engine, that's marked; where a claim
  is community-reported and not yet RE-verified, that's marked too.
- **[Engine Internals](/docs/category/engine-internals)** — notes from reverse-engineering `Dunia.dll` and
  the `FarCry2`/`FarCry2_server` executables: the function-callback registry, command-line parsing,
  the Lua API surface exposed to mods, and more.

## Reading the provenance callouts

Throughout the file-format and engine pages you'll see callouts like:

:::info[Verified via reverse engineering]
Confirmed by tracing the actual code path in a disassembler — byte layouts, function names, and
call chains are as accurate as the binary allows.
:::

:::note[Community-reported]
Sourced from forum posts, existing tools, or community guides. Treated as the most probable
explanation where it hasn't been independently verified, but not yet confirmed against the engine
itself.
:::

Where reverse engineering has directly contradicted an earlier community claim, the page says so
explicitly rather than silently dropping the old information.
