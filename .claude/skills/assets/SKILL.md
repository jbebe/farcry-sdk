---
name: assets
description: Use when the user wants to dress a scene, author a world or mission, or asks which models, props, textures or sounds the game has for a theme or place (a house interior, a market, a bus and people packing to leave) - lists them from the tagged asset tables in tools/asset-tags
---

# Assets

Every retail model, texture and sound bank is tagged in `tools/asset-tags/` (`README.md` has the
columns and the vocabulary). Query with the PowerShell tool; Python is blocked in Bash.

```powershell
python tools/asset-tags/find.py home interior               # every term must match
python tools/asset-tags/find.py travel,refugees,bus-station # a,b matches either
python tools/asset-tags/find.py kitchen --kind kitchenware
python tools/asset-tags/find.py market --sounds             # --textures for textures
```

## Answering a scene

1. Turn the wish into tags from the vocabulary. "A house interior" is `home interior`, then the
   rooms (`kitchen`, `bedroom`, `living-room`, `bathroom`); "a bus and people packing" is
   `travel,refugees,bus-station`, plus `--kind vehicle bus` and `--kind wreck bus`. Run one query
   per part of the scene rather than one wide one.
2. Give the user a short list grouped by kind: the object, its size, and how it is placed - the
   archetype name for the Map tab's library, or the mesh path where there is none (`StaticObject`).
   Lead with what the campaign places (`placed` above 0); say how many more there are.
3. Add the sounds (`--sounds`) and, if surfaces matter, the textures (`--textures`) that fit.

## Fixing a tag

A wrong or missing tag is fixed in the table's hand columns (`kind`, `what`, `tags`), with words from
the README's vocabulary; a new word goes into the vocabulary first. `merge.py` takes rows of
`path, kind, what, tags` and checks them; `build.py` then refreshes the generated columns.
