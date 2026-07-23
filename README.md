# Far Cry 2 Modding

Community modding knowledge and reverse-engineering notes for Far Cry 2, published as a
[Docusaurus](https://docusaurus.io/) site.

This is an unofficial, fan-made research project. It is not affiliated with or endorsed by Ubisoft.

## What's here

- **`docs/`** — the whole Docusaurus site: config, and the actual content at `docs/docs/` (practical
  modding workflow, file-format references merged from community findings and direct reverse
  engineering with each claim's provenance marked, and engine-internals notes traced from
  disassembly of `Dunia.dll` / `FarCry2.exe`).
- **`research/reference-files/`** — supporting binary samples, hash lists, and tool archives used
  while researching the formats documented in `docs/docs/`.
- **`reverse/`** — the underlying Ghidra reverse-engineering project.
- **`tools/`** — modding tools maintained in this repo (`JackAll`, `modpatcher`, `sbao`).

## Running the docs site locally

```bash
cd docs
npm install
npm start
```

This starts a local dev server with live reload. `npm run build` produces the static site into
`docs/build/`.

## Status

This repo is being prepped for wider community involvement (PRs) but isn't fully open yet — expect
things to move around as the docs get organized.
