# BlenderFC2

Read/write codecs for the Far Cry 2 (Dunia) model, rig and animation formats, plus a Blender add-on
built on them.

`fc2fmt/` is pure Python and imports no `bpy`, so it runs under plain `python` for corpus testing and
under Blender's interpreter for the add-on.

## Status

| Format | What it is | Round trip | Bytes still opaque |
|---|---|---|---|
| `.xbg` | mesh container: geometry, LODs, nodes, materials, skinning | **3,133 / 3,133** | **0.12%** |
| `.skeleton` | rig: bones, constraints, weapon sockets | **81 / 81** | **0%** |
| `.mab` | animation bank: rotation tracks and events | **4,436 / 4,436** | 99.6% |

Round trip means `write(parse(f)) == f`, with the writer regenerating each structure — chunk sizes,
payload sizes, counts, padding, and the derived duplicates inside a cluster header — rather than
echoing it.

**Read the second column with the first.** A round trip proves the *framing*; bytes kept as an opaque
blob pass it for free. `.skeleton` is fully decoded. For `.xbg`, vertex and index streams are decoded
and re-encoded losslessly (proven per buffer across the corpus), leaving only ten undetermined floats
per part. Most of a `.mab` is its keyframe payload, which is still carried through untouched.
`tests/invariants.py` covers what a round trip cannot, and `roundtrip.py --coverage` prints the
opaque share so the two numbers stay side by side.

**What works today**: importing a shipped `.xbg` into Blender — parts, LODs, UVs, vertex colours,
normals, an armature from the nodes, rigid parts on their pivots, skin weights as vertex groups, and
**textures**, by resolving each material through its `.xbm` to the `.xbt` files it names. The same
model can be packed into a self-contained [bundle](#model-bundles) and imported with no game install
present. Export is not written yet, and neither is `.mab` keyframe authoring.

## Layout

| Path | What it is |
|---|---|
| `fc2fmt/binary.py` | `Reader`/`Writer`, CRC32 name hashing, the descending-counter alignment fill |
| `fc2fmt/xbg.py` | `.xbg` container: chunks, `EDON` nodes, `MB2O` bind matrices, `DNKS`/`SULC` clusters, LODs |
| `fc2fmt/vertex.py` | Typed access to a vertex buffer: positions, UVs, normals, colours, skin weights |
| `fc2fmt/mesh.py` | Assembles the container into per-part meshes with their vertices localised |
| `fc2fmt/transform.py` | 4x4 helpers, so node world transforms need no `mathutils` |
| `fc2fmt/skeleton.py` | `.skeleton` (`LKS`) bones, constraints, anim handles |
| `fc2fmt/mab.py` | `.mab` header, bone bitmasks, the smallest-three quaternion codec |
| `fc2fmt/xbm.py` | `.xbm` material: texture slots, tiling, tint colours, and the variant an `.xbg` embeds |
| `fc2fmt/xbt.py` | `.xbt` texture: strips the header off the DDS payload Blender loads |
| `fc2fmt/assets.py` | Turns a game-relative path into bytes, against an extracted install |
| `fc2fmt/bundle.py` | `.fc2model`: one model and every file it needs, in one zip |
| `addon/` | The Blender add-on: `convert.py` holds every Dunia-to-Blender convention, `materials.py` rebuilds the Generic shader |
| `tests/_corpus.py` | Corpus location and the skip-when-absent helper the scripts share |
| `tests/roundtrip.py` | Round-trips every retail file of a format; `--coverage` reports the opaque share |
| `tests/invariants.py` | Checks decoded meaning: palettes index real bones, derived values recompute to what shipped |
| `tests/mabcheck.py` | Resolves every `.mab` mask bit against `pelvis_ref.skeleton` and checks quaternion norms |
| `tests/quatcheck.py` | Scores the quaternion component layout against the skeleton rest pose |
| `tests/blender_import.py` | Imports the AK-47, a character and a bundle inside Blender, headless |
| `tests/render_preview.py` | Renders an imported model to a PNG, for looking at what the importer built |
| `tests/bundle.py` | Builds bundles and resolves each model's whole reference graph from the bundle alone |
| `tests/probe.py` | Dumps one file's chunk layout when a round trip fails |
| `bundle_model.py` | Packs a model and its dependencies into a `.fc2model` |
| `open_model.py` / `.cmd` | Opens a model in Blender's UI, quoting-safe in cmd and PowerShell |

## Model bundles

An `.xbg` on its own is not openable. It names its materials by game-relative path, each `.xbm` names
its textures the same way, and those live in shared trees far from the model — 18 files for the
AK-47, 70 for a character, 194 for the largest shipped mesh. A `.fc2model` is a zip carrying all of
them under their game paths, so Blender never has to reach into a game install or talk to JackAll.

```
python bundle_model.py <model.xbg> [-o out.fc2model] [--root DIR]
```

Then **File ▸ Import ▸ Far Cry 2 Model Bundle (.fc2model)**.

Inside is a `manifest.json` beside every file, each stored under its game-relative path:

```json
{
  "format": "fc2model", "version": 1,
  "model": "graphics/weapons/primary/ak47/ak47.xbg",
  "entries": [
    {"path": "graphics/weapons/primary/ak47/ak47.xbg", "role": "owned"},
    {"path": "graphics/_textures/diffuse/metal/metalbrushed_d.xbt", "role": "shared"}
  ]
}
```

**The role is the part that matters for export.** `owned` files sit in the model's own directory and
exist for this model; `shared` files back many others — `metalbrushed_d.xbt` is used by 46 of the 87
shipped weapons, so editing it through one bundle would re-skin all of them. An exporter writes back
`owned` entries only. The rule is by directory, which is a proxy: 58% of retail materials are used by
exactly one model, but they all live together in `graphics\_materials`, so a single-use one is still
marked shared.

Bundles are fat on purpose. Copying the shared files in costs roughly 1.9x over storing a whole
weapon set once, and buys a bundle that opens with nothing else installed. The median is 11 files and
1.4 MB; the AK-47 is 18 files, 2.6 MB, 1.7 MB zipped.

Closure is complete for **2,922 of 2,922** shipped models — every material, every texture, and the
`_mip0` companion carrying a texture's top mip. The three meshes that embed their material in the
`.xbg` instead of naming an `.xbm` are covered by reading that inline chunk.

## Running the tests

They need the retail export under `tmp/gamefiles/` and skip cleanly without it.

```
cd tools/BlenderFC2/tests
python roundtrip.py skeleton --coverage
python roundtrip.py xbg --coverage
python roundtrip.py mab --coverage
python invariants.py
python mabcheck.py
python quatcheck.py
python bundle.py
```

The Blender test runs headless against the real add-on:

```
& "C:\Programs\Blender 5.2\blender.exe" -b --python tools/BlenderFC2/tests/blender_import.py
```

To look at a model rather than assert about it — worth doing, since a part can sit in the wrong
place and still pass every numeric check:

```
"C:\Programs\Blender 5.2\blender.exe" -b --python tools\BlenderFC2\tests\render_preview.py -- <model.xbg> out.png [--highlight <part index>]
```

To open one interactively, with the model already loaded — works the same in cmd and PowerShell:

```
tools\BlenderFC2\open_model.cmd tmp\gamefiles\worlds\worlds\graphics\weapons\primary\ak47\ak47.xbg
```

Set `BLENDER` to point at a different executable. Or call Blender directly, which is the same thing
without the wrapper:

```
"C:\Programs\Blender 5.2\blender.exe" --python tools\BlenderFC2\open_model.py -- <model.xbg> [lod]
```

Avoid `--python-expr` for this: a one-line expression full of quotes and semicolons is tokenised
differently by cmd and PowerShell, and cmd will split it on spaces.

## Installing the add-on

Blender 4.2+ extension. Build the zip with Blender itself, which validates the manifest on the way:

```
& "C:\Programs\Blender 5.2\blender.exe" --command extension build --source-dir tools\BlenderFC2 --output-dir .
```

Install the resulting `farcry2_formats-<version>.zip` from **Edit ▸ Preferences ▸ Get Extensions ▸
Install from Disk**. Import via **File ▸ Import ▸ Far Cry 2 Model Bundle (.fc2model)** or
**Far Cry 2 Mesh (.xbg)**.

Plain `zip` on the folder does not work: an extension needs `register`/`unregister` at the zip root
beside the manifest, which is what the root `__init__.py` re-exports, and `[build]` in the manifest
is what keeps `tests/` and the command-line scripts out of the package.

**An installed extension shadows this working tree.** The add-on puts its own directory on
`sys.path`, so once it is installed, anything run inside Blender imports that frozen copy of `fc2fmt`
rather than the files being edited. `tests/_corpus.py` evicts it, which covers every test script;
`open_model.py` does not, so rebuild and reinstall after changing code, or disable the extension
while developing.

The manifest declares `SPDX:Unlicense` to match the repository root; `tools/JackAll` uses a different
licence, so change it here if this should follow that instead.

## Format notes worth knowing

Full byte-level documentation lives in `docs/docs/file-formats/`. These are the ones that break a
parser written from the community material.

**`.xbg` chunk headers are 20 bytes and the payload is addressed backwards**, at
`chunkStart + chunkSize - payloadSize`. `DNKS` is the only chunk with a sub-chunk (`SULC`, holding
the bone clusters), which is why its own payload sits at the end rather than at a fixed offset.
Traced from `LoadGeomResource` (`FarCry2_server` 0x097fd440).

**Alignment padding is a descending byte counter**, not zeros — nine bytes of padding are written
`09 08 07 06 05 04 03 02 01`. A file padded with zeros still loads, but no longer matches byte for
byte, which silently destroys the round-trip gate.

**Bone palettes**: a static submesh has all 48 slots `-1` (28,217/28,217 in retail); a skinned one
holds a contiguous prefix of node indices, duplicates included, then `-1` padding (3,953/3,953). The
community rule that skinned palettes must never contain `-1` is wrong.

**`DIKS` names each part's placement node, so nothing matches names.** An entry is
`CRC32(full part name)` then `(node index or 0xFFFF) << 16 | entry position`, one per `DNKS` part —
16,885 of 16,885 across the corpus. Matching part names against node names instead disagrees on 291
parts and is wrong on all of them, because some meshes have a node whose own name ends in `_LOD0`.
A part marked `0xFFFF` sits in the root's space: every skinned one, and rigid ones already modelled
in place.

**Dunia and Blender are both Z-up, so geometry needs no axis change.** The file winds clockwise
(D3D) in 113 of 113 sampled meshes, so triangles are reversed on import, and V is flipped. Nothing
rotates the armature — the third-party importer's 180° Z rotation has no support in the data.

**A model's own texture is usually a mask, not its colour.** A Dunia surface is two shared tiling
detail maps blended by the green channel of a per-model mask, each tinted by its own colour, with the
blue channel choosing how far layer 1's tint moves from `DiffuseColorBase` towards `DiffuseColor1`.
The AK-47's `ak47_state01_m.xbt` is that mask; its wood and metal come from
`graphics\_textures\diffuse\`. Applying `DiffuseColor1` flat instead of lerping from the base leaves
everything washed out. `.xbm` is the same chunk container as `.xbg`, with the material in its `LTMD`
chunk — 2,379 of 2,379 shipped materials parse, and 2,370 name an albedo.

**Three meshes carry their material inline, in a different layout.** `bat.xbg`, `torch01.xbg` and
`rag_animready.xbg` reference `.fakemat` names that their own `LTMD` chunks define, instead of naming
an `.xbm` file. An embedded chunk leads with that name and the `DNKS` part it applies to; a
standalone `.xbm` leads with five bytes. Read one with the other's layout and it desynchronises on
the first field, which is why these three used to import untextured.

**`.mab` bitmasks are indexed by `.skeleton` bone id**, and a bone's slot inside a section is the
popcount of the mask below its id. Verified by resolving 303,067 mask bits against
`pelvis_ref.skeleton` with zero out of range.

**The quaternion component layout is confirmed.** Unit norm cannot discriminate a permutation, so
`tests/quatcheck.py` scores each candidate against the skeleton rest pose instead: a bone a clip
holds constant should sit at or near `m_ChildToParent`. Over 31,383 samples the engine layout scores
mean `|dot| = 0.977` against 0.858 for the next candidate and 0.04 for the wrong ones. The layout is
a table (`ENGINE_LAYOUT`) the test drives as a parameter, so it scores the shipped decoder rather
than a copy of it.

**Still open**: the per-track packing inside a `.mab` keyframe group, so animation import is not yet
possible; and ten floats per part in `DNKS` whose grouping is undetermined — the community layout of
a bbox min/max pair holds for only 18 of 18,533 shipped parts.
