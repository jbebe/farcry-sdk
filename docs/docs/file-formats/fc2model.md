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
arrives as JSON with flat float arrays, materials as JSON, textures as PNG, the rig and its clips as
JSON. That is what lets the Blender add-on carry no format code at all, and what lets a model gain a
part or an LOD rather than being transplanted into whatever a donor happened to have.

```
manifest.json
model/mesh.json                          parts, nodes, LODs, bounds, geometry
model/rig.json                           the .skeleton beside the model
materials/SDORE2-M-2008091137450636.json
textures/ak47_state01_m.png              full resolution, mip0 already merged in
textures/ak47_state01_m.header.bin       the .xbt header, which nothing derives
textures/ak47_state01_m_mip0.header.bin
clips/1stge_uppb_reload_+000fw_prak4_i1.json
```

Keys are `snake_case` and JSON throughout — C#'s own property casing would be a language-shaped type
leaking into a format whose other reader is Python. `sha256` is lowercase hex, which is what
`hashlib.sha256().hexdigest()` gives, so neither side has to remember to fold case. A `bytes` field
(a material's `preamble`, a clip's `tags`) is base64.

The one thing not normalised is `path`. It is the file's identity **as the game names it**, which
means it arrives however the referencing file spelled it — `GRAPHICS\_MATERIALS\SDORE2-M-….xbm`
included. Compare one case-insensitively with `\` and `/` treated alike; do not rewrite it.

## The manifest

Every entry keeps its **game path as its identity** and names the file that carries it.

```json
{
  "format": "fc2model", "version": 2, "requires_reader": 2, "generator": "JackAll",
  "model": "graphics/weapons/primary/ak47/ak47.xbg",
  "limits": {
    "max_cluster_triangles": 21845,
    "max_buffer_vertices": 65535,
    "max_palette_slots": 48,
    "max_uv_sets": 2
  },
  "entries": [
    {"path": "GRAPHICS\_MATERIALS\SDORE2-M-2008091137450636.xbm",
     "file": "materials/SDORE2-M-2008091137450636.json", "kind": "material",
     "role": "owned", "usage": 1, "usage_source": "xref", "sha256": "…"},

    {"path": "graphics\weapons\primary\ak47\ak47_state01_m.xbt",
     "file": "textures/ak47_state01_m.png", "kind": "texture",
     "role": "owned", "usage": 1, "usage_source": "xref",
     "header": "textures/ak47_state01_m.header.bin",
     "companion_header": "textures/ak47_state01_m_mip0.header.bin",
     "codec": "DXT1", "levels": 12, "sha256": "…"}
  ],
  "clips": [
    {"path": "graphics/characters/.../1stge_uppb_reload_+000fw_prak4_i1.mab",
     "file": "clips/1stge_uppb_reload_+000fw_prak4_i1.json",
     "label": "1stge_uppb_reload_+000fw_prak4_i1", "frames": 61, "rate": 30,
     "participant": "ak47", "bone": "R Hand"}
  ]
}
```

`kind` is one of `mesh | rig | material | texture | clip | note`.

### `limits` keeps format constants out of the editor

The ceilings are declared by the pack rather than hardcoded by whatever opens it, so a validator has
no second place to drift from. They are properties of the container's `u16` fields, measured over
every shipped mesh — see [Authoring ceilings](./xbm-xbg.md#authoring-ceilings).

### The pack's own conventions, not the file's

A pack is not a Dunia format, and it does not inherit Dunia's conventions. The one that catches
people:

- **V runs bottom-up**, the way a modelling tool measures it, while the `.xbg` measures it from the
  top row. `MeshDocument` flips it on the way out and back, so a reader takes `uvs` as they come and
  a writer hands them back the same way. Flipping again on top of that turns every texture upside
  down, which no numeric gate can see — a round trip stays byte-exact either way, because the two
  flips cancel.

Positions are already metres and triangles keep the file's own clockwise winding, so a bottom-up,
counter-clockwise tool reverses those itself.

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

`usage` is how many files **use** this one, counting the packed model itself. The directory half
stays because a file in the model's own folder is its own by construction; the count only ever
**promotes**, so the rule cannot get less safe as evidence improves. Measured over the shipped
weapons: of the 325 pooled materials they name, 140 have exactly one user and become `owned` -
including the sawed-off shotgun's, which the directory rule leaves `shared` and a modeler otherwise
has to go and find by hand. `metalbrushed_d.xbt` stays `shared`.

Counting the packed model itself is what makes the promotion safe. A count of one can then only ever
mean *this* model, never one other file with the model's own edge missing from the index.

**An animation bank is the one exception, and not by being given permission.** A bank always reads
`shared` — the AK-47's reload counts three users, two of them unnamed resources that *load* it rather
than models that *use* it — and the rule has no way to see that what changed inside it was one clip.
So the exception is structural instead: an editor can only rewrite the clip that fits this pack's own
rig, and [every other clip goes back byte for byte](#an-edited-clip-and-how-the-rest-of-the-bank-survives).
Applying a pack lets an edited `clip` entry through for that reason and no other.

#### A reference is not a use

The reference index answers "who points at this", and most of the answers are not users. Every world
ships a generated `<name>_depload.dat` listing what the level loads, plus an `.xml` twin restating
it, so a material one weapon uses is *referenced* by four dozen files. The shipped AK-47's materials
count **47 references and 8 users**; counted naively, nothing in the game would ever promote — 46 of
2,379 materials rather than 1,315.

So a use is counted from the edge's kind:

| Kind | Counted as |
|---|---|
| `XbgMaterial`, `XbmTexture`, `MgbTexture`, `FcbPathValue`, `FcbNameValue`, `MgbNameId` | the referencing file |
| `DepLoadDependency` | the **site**, which is the parent resource that pulled it in |
| `TextPath` | not counted |

A `depload.dat` names no bytes of its own, but it sites each dependency against the resource that
pulled it in — so the manifest is not the user and its site is. That is the one place the graph
records a user for a file nothing else mentions.

`TextPath` is dropped because the generated `_depload.xml` twins are its bulk here. What goes with
them is a path named in an `.rml` or a Lua script, which is not a rendering use.

`usage_source` records where the count came from. Only `xref` — JackAll's reference index, which
covers `.fcb`, `depload` and mesh edges — may promote a file outside the model's own directory. A
graphics-only scan is a **lower bound**: `bullettracer_d.xbt` is named from a weapon archetype's
`texTexture` field and by no mesh at all, and under-counting is the dangerous direction.

## Textures

A texture is stored as **one PNG at full resolution**, plus the `.xbt` header verbatim, as a
`.header.bin` beside it.

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
{"header_word": 2369979903, "name": "SAWEDOFF_SHOTGUN_METAL", "shader": "Weapon",
 "preamble": "AAAAAAA=", "trailing": 0,
 "textures": [{"slot": "DiffuseTexture1", "path": "graphics\\...\\state01.xbt"}],
 "floats":   [{"key": "DiffuseColor1", "value": [0.55, 0.5, 0.48]}],
 "integers": [{"key": "AlphaTestEnabled", "value": 0}]}
```

### A material a mesh embeds is not carried

An `.xbg` may hold a material inline rather than naming a file, and there is no file for the pack to
put one in. Those travel inside the mesh document as one of the chunks it carries whole, so they
survive a round trip but nothing outside JackAll can read them - an editor gets the material name and
no shader graph.

Measured over the retail set, that is **4 of 7,496 material names, across 3 of 3,133 meshes, none of
them weapons**. Small enough to name rather than design around.

## Clips

Nothing in a mesh names its animation, so a pack carries the banks it is told to carry. `clips[]` in
the manifest indexes them - enough to list what a pack holds without opening and parsing every one.
It is a **hint, not truth**: which clip in a chain belongs to a given rig must be re-derived from the
rig's bone ids on load, so a stale entry can never mispose anything.

`label` is the bank's own file name. The format carries no friendlier one, and the naming convention
(`1stge_uppb_reload_+000fw_prak4_i1` - first person, upper body, reload, no direction, AK-pattern
rifle) is not decoded here rather than guessed at.

`frames` and `rate` are the root clip's, which is the character's - a bank plays as one thing, so
that is the length to show. `participant` and `bone` come from the tag record that names this pack's
model: they say which bone the bank hangs the model from, which is the fact that decides where a
modeler's geometry belongs. A bank that keys nothing reports `0` frames.

### Finding the banks that move a model

The folders do not answer this. The ak47's banks sit under `animations/weapons/primary/ak47`, but
the spas12's sit under `franchi_spas12`, the m16's under `m-16` and the desert eagle's under
`desert_eagle_50` - mirroring the model's own folder finds the banks for 12 of the 49 shipped weapon
folders.

The banks answer it themselves. Each names what it animates besides the skeleton in its tag records,
by the model's file stem, and asking every bank is both exact and broader than the folder: 94 banks
name `ak47` against the 62 filed beside it, the rest being locomotion and cutscene banks that carry
the rifle while a character runs or talks. 44 of the 89 models under `graphics\weapons` find banks
this way; the other 45 are ammo boxes, pickups, casings and bullets, which nothing animates.

An edited clip is an entry whose bytes changed and which has grown an `origin_sha256`, like any
other. What is different about a bank is what "edited" is allowed to mean — see below.

### An edited clip, and how the rest of the bank survives

A bank's document holds every clip decoded — bones, keys, timing — and, alongside them, **each
section exactly as it arrived** under `raw`, plus the four bone masks under `masks`.

That redundancy is the whole point. Present in `raw` means "unchanged, write these bytes"; absent
means "encode it from the decoded fields". So an editor rewriting a weapon's reload **drops the four
motion sections from `raw` and clears `masks` on that one clip**, and every other clip in the chain —
the character's arms among them — goes back byte for byte.

```json
{"header": "…", "clips": [
  {"duration": 2.57, "constant_rotations": [{"bone": 3, "value": [0, 0, 0, 1]}],
   "keyframe_rotations": [{"bone": 5, "frames": [0, 8, 16], "values": [/* 4 per key */]}],
   "sections": [2, 3, 6], "chained": true,
   "masks": [[…], […], […], […]],
   "raw": {"2": "…base64…", "3": "…base64…", "6": "…base64…"}}],
 "participants": [{"name": "ak47", "bone": "R Hand", "clip": 1}]}
```

The alternative — re-encoding every clip and hoping — costs bytes on a fifth of the shipped banks,
because [some rotations were authored on an exact tie](./mab.md#what-is-still-open) and cannot be
re-encoded to the bytes they came from. Carrying them instead makes the round trip exact for all
4,436.

`participants` is derived on the way in and ignored on the way out; the tag block those records came
from travels in `raw` like any other section. It is there so a reader never has to open that block —
without it, the one thing a modeler needs from a bank, which bone the gun hangs from, is unreadable
outside JackAll.

`sections` says which slots the clip carries, which is not derivable: a clip can name a keyframe
section whose mask is empty, and 451 of 982 sampled clips do.

## Reading an older pack

Version 1 carried raw game files and no clips, and decided ownership by directory. A version 2
reader accepts it by defaulting: `path` doubles as `file`, everything is a raw game file,
`usage` is absent, `usage_source` is `directory`, and `clips` is empty.

From version 2 on, `requires.reader` names the lowest reader version that can make sense of the
pack, so every later additive change is non-breaking.
