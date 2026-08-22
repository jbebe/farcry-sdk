---
title: .fc2model — Model Packs
---

# `.fc2model` — Model Packs

:::info[A repo format, not a game one]
Nothing in Far Cry 2 reads this. It exists so a modeller can open one model, edit it, and hand it
back without knowing any of the Dunia formats — JackAll writes it, JackAll applies it, and Blender
is the only other thing that opens it.
:::

A `.fc2model` is a zip holding **one model, decoded**. No Dunia bytes survive inside it: the mesh
arrives as JSON plus flat float buffers, materials as JSON, textures as PNG, the rig and its clips
as JSON. That is what lets the Blender add-on carry no format code at all, and what lets a model
gain a part or an LOD rather than being transplanted into whatever a donor happened to have.

```
manifest.json
model.json                 parts, nodes, LODs, bounds, vertex formats
buffers/lod0-pos.bin       flat float32, one file per component per LOD
materials/SDORE2-M-2008091137450636.json
textures/sawed_off_shotgun_state01.png
textures/sawed_off_shotgun_state01.header.xml
rig.json
clips/1stge_uppb_reload_nodir_dlc1sawedoff_i1.json
README.txt
```

## The manifest

Every entry keeps its **game path as its identity** and names the file that carries it.

```json
{
  "format": "fc2model", "version": 2, "requires": {"reader": 2},
  "generator": {"name": "JackAll", "version": "0.x"},
  "model": "graphics/weapons/dlc/sawed_off_shotgun/dlc1_sawedoff_shotgun.xbg",
  "credits": {"title": "...", "author": "...", "license": "CC-BY-4.0"},
  "limits": {
    "max_cluster_triangles": 21845,
    "max_buffer_vertices": 65535,
    "max_palette_slots": 48,
    "max_uv_sets": 2
  },
  "entries": [
    {"path": "graphics/_materials/SDORE2-M-2008091137450636.xbm",
     "file": "materials/SDORE2-M-2008091137450636.json", "kind": "material",
     "role": "owned", "usage": 1, "usage_source": "xref", "sha256": "..."},

    {"path": "graphics/.../sawed_off_shotgun_state01.xbt",
     "file": "textures/sawed_off_shotgun_state01.png", "kind": "texture",
     "role": "owned", "usage": 1, "usage_source": "xref",
     "header": "textures/sawed_off_shotgun_state01.header.xml",
     "companion_header": "textures/sawed_off_shotgun_state01_mip0.header.xml",
     "codec": "DXT1", "levels": 12, "sha256": "..."}
  ],
  "clips": [
    {"path": "graphics/characters/.../reload.mab", "file": "clips/reload.json",
     "label": "reload (1st person)", "frames": 61, "rate": 30}
  ]
}
```

`kind` is one of `mesh | rig | material | texture | clip | note`.

### `limits` keeps format constants out of the editor

The ceilings are declared by the pack rather than hardcoded by whatever opens it, so a validator has
no second place to drift from. They are properties of the container's `u16` fields, measured over
every shipped mesh — see [Authoring ceilings](./xbm-xbg.md#authoring-ceilings).

### The two fields that must be carried

Almost everything in an `.xbg` is bookkeeping the writer regenerates. Two things are not, and the
pack carries them verbatim:

- **`header_words[0]`**, a per-file value that is not a CRC32 of the file name, the stem in any
  casing, or the body.
- **the material list's trailing word**, zero in 3,114 of 3,133 files and 1 to 3 on nineteen grass
  meshes, with nothing to say which.

Everything else is derived on the way back out. See
[A container can be authored](./xbm-xbg.md#a-container-can-be-authored-not-just-edited).

### `sha256`, and what counts as edited

Every entry carries the hash of the bytes in the zip. An entry the editor changed also carries
`origin_sha256`, the hash it arrived with — so **an entry is modified exactly when
`origin_sha256` is present**. That is what lets an applier write only what changed, and warn that
the install differs from what the pack was built against before touching anything.

### Ownership

```
owned  =  same_directory_as_model  OR  usage == 1
```

`usage` is how many other files reference this one. The directory half stays because a file in the
model's own folder is its own by construction; the count only ever **promotes**, so the rule cannot
get less safe as evidence improves. `metalbrushed_d.xbt` backs 46 of the 87 shipped weapons and
stays `shared`; a single-use `.xbm` pooled in `graphics/_materials` finally becomes `owned`, which
the directory rule alone gets wrong for 58% of retail materials.

`usage_source` records where the count came from. Only `xref` — JackAll's reference index, which
covers `.fcb`, `depload` and text edges as well as meshes — may promote a file outside the model's
own directory. A graphics-only scan is a **lower bound**: `bullettracer_d.xbt` is named from a weapon
archetype's `texTexture` field and by no mesh at all, and under-counting is the dangerous direction.

## Textures

A texture is stored as **one PNG at full resolution**, plus the `.xbt` header bytes in the XML form
`jackall-cli xbt extract` already emits.

The header is carried because it cannot be synthesized: `Reserved` is a bitfield the streaming
loader consumes, and `Hash` is a stable per-asset id nothing derives — see [`.xbt`](./xbt.md#header).

`mip0` is the trap this design removes. Around half of all textures split their top level into a
sibling `<name>_mip0.xbt`, and the two are not what they look like: the **base file holds the
complete mip chain of the half-resolution image**, and the companion holds a **single level at twice
the dimensions**. Inverted, the texture is half or double resolution *in game only* — an editor
loads the companion and shows the right thing either way. So the pack stores the merged image and
the applier re-splits it, asserting the 2× relationship on both axes.

`codec` records what to re-encode as, so a trip through the pack cannot silently change compression,
and `levels` records how many mip levels the chain held.

## Materials

An ordered list, not a map. One shipped material repeats a key inside a section, so a map-keyed
reader loses it and its writer cannot put the file back.

```json
{"name": "SAWEDOFF_SHOTGUN_METAL", "shader": "Weapon",
 "preamble": [0, 0, 0, 0, 0], "trailing": 0,
 "textures": [{"slot": "DiffuseTexture1", "path": "graphics/.../state01.xbt"}],
 "floats":   [{"key": "DiffuseColor1", "value": [0.55, 0.5, 0.48]}],
 "integers": [{"key": "AlphaTestEnabled", "value": 0}]}
```

## Clips

`clips[]` in the manifest is an index for the editor — enough to list what a pack carries without
opening and parsing every bank. It is a **hint, not truth**: which clip in a chain belongs to a
given rig must be re-derived from the rig's bone ids on load, so a stale entry can never mispose
anything.

An edited clip is simply an entry whose bytes changed and which has grown an `origin_sha256`.
Defining `clips[]` now is what lets clip authoring arrive later without a format change.

## Reading an older pack

Version 1 carried raw game files and no clips, and decided ownership by directory. A version 2
reader accepts it by defaulting: `path` doubles as `file`, everything is a raw game file,
`usage` is absent, `usage_source` is `directory`, and `clips` is empty.

From version 2 on, `requires.reader` names the lowest reader version that can make sense of the
pack, so every later additive change is non-breaking.
