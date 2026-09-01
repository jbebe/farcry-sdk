# Changelog

Notable changes to the Far Cry 2 Blender add-on, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Changed
- **The animation list is sorted by name.** A pack lists its banks in export order, which for the
  Dart Rifle's 60 near-identical names is no order at all to look through. Sorted, the first- and
  third-person banks group and the action reads down the list. `open_model.py` sorts the same way,
  so the 20 it prints are the first 20 rather than an arbitrary 20.

### Fixed
- **The animation list no longer stalls the UI.** Picking a bank in **Load Far Cry 2 Animation** ran
  the enum's items callback on every redraw, and each call reopened the pack and decompressed all of
  it — 19 MB of mesh and textures for a list that only needs the manifest. Measured at **29.2 ms per
  call** on the Dart Rifle, which is what the dropdown was paying per frame. It now reads the
  manifest alone and holds the built list against the pack's mtime: **1.0 ms** to open a pack,
  **0.008 ms** after. Holding the list also fixes the enum returning strings Blender does not own,
  which is the documented way to get garbled entries or a crash.

- **A material's alpha is now wired to the surface.** Nothing linked the diffuse texture's alpha
  channel, so every material declaring `AlphaTestEnabled` or `AlphaBlendEnabled` drew fully opaque —
  the Dragunov's crosshair, a black cutout on a transparent texture, came through as a solid black
  rectangle over the sight picture. Across a weapon, a scoped weapon and a character, 13 of 46
  materials declare one of those flags. The old `blend_method = "CLIP"` it set instead has been a
  no-op since EEVEE Next: it maps to `DITHERED`, which is already the default.

### Added
- **A sight picture**, which isolates `SCOPE_HI` and views it from the player's eye. `SCOPE_HI`
  replaces the rest of the model while zoomed rather than overlaying it, so it is the whole zoomed
  view. What it catches is geometry in the wrong part, at the wrong position or the wrong size —
  none of which is a malformed file, so no rule can reach them. The eye comes out of the aim bank's
  `Camera` participant, so no character rig is needed to find it.

## [0.1.0] - 2026-08-24

### Added
- **Importing a `.fc2model` model pack** — parts and their LODs, UVs, vertex colours, the file's own
  normals, an armature built from the model's nodes, rigid parts sitting on their own pivots, and
  skin weights as vertex groups. Nothing here opens a game file: JackAll owns the byte layouts and
  this owns what a scene looks like.
- **Materials rebuilt as a node graph**, with the pack's textures wired into the slots the game's
  Generic shader actually reads.
- **Exporting edited geometry back into the pack it came from.** A model nobody touched comes back
  byte-identical to the file it was built from, so an edit costs only what was edited — the LODs you
  never opened, the nodes, the bone palettes and everything the format carries whole all survive.
- **Adding a part the model shipped without.** Select a mesh, **Add as New Part**, and export
  appends it with every part already there untouched. All 3,133 shipped meshes accept one.
- **Animation, both ways.** Load any bank the pack carries onto the rig, pose it, and **Write
  Animation** puts the Action back into the one clip that fits this model, leaving the character's
  clip and the rest of the chain byte for byte.
- **A validation panel** that checks a model against what the format allows before the game finds
  out — per-cluster triangle and per-buffer vertex ceilings, bone palette limits, UVs, and the
  channels the format silently drops. Every rule is silent on the game's own models, because retail
  is the definition of valid.
- **A motion table** showing how far each bone travels across the clips a pack carries.
- **`build.ps1`**, which builds the installable extension zip with Blender itself so the manifest is
  validated on the way.

### Known limitations
- A part can be added but not removed, and neither a node nor a whole LOD tier can be added.
- An added part exists only at the LOD it was added to, so it is not drawn once the model drops to a
  coarser one.
- A new part reuses one of the model's existing materials; nothing creates a new one.
- `.hkx` collision is not parsed, so a reshaped model keeps its donor's collision shape.
