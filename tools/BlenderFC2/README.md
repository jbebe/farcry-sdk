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

**What works today**: importing a shipped `.xbg` into Blender — parts, LODs, materials, UVs, vertex
colours, normals, an armature from the nodes, rigid parts on their pivots, and skin weights as vertex
groups. Export is not written yet, and neither is `.mab` keyframe authoring.

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
| `addon/` | The Blender add-on: `convert.py` holds every Dunia-to-Blender convention |
| `tests/_corpus.py` | Corpus location and the skip-when-absent helper the scripts share |
| `tests/roundtrip.py` | Round-trips every retail file of a format; `--coverage` reports the opaque share |
| `tests/invariants.py` | Checks decoded meaning: palettes index real bones, derived values recompute to what shipped |
| `tests/mabcheck.py` | Resolves every `.mab` mask bit against `pelvis_ref.skeleton` and checks quaternion norms |
| `tests/quatcheck.py` | Scores the quaternion component layout against the skeleton rest pose |
| `tests/blender_import.py` | Imports the AK-47 and a character inside Blender, headless |
| `tests/render_preview.py` | Renders an imported model to a PNG, for looking at what the importer built |
| `tests/probe.py` | Dumps one file's chunk layout when a round trip fails |
| `open_model.py` / `.cmd` | Opens a model in Blender's UI, quoting-safe in cmd and PowerShell |

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

Blender 4.2+ extension. Zip the folder and install it from **Edit ▸ Preferences ▸ Get Extensions ▸
Install from Disk**, or point Blender's script path at this directory. Import via
**File ▸ Import ▸ Far Cry 2 Mesh (.xbg)**.

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

**Part placement is case-insensitive.** A rigid part sits on the node sharing its name, but only 559
of 16,876 parts match exactly — the rest need case folding (`WHEELBACK_L_STATE01` against a node
named `Wheelback_L_State01`). Skinned parts instead take node 0, the skeleton root, which is what
lifts a character off the floor.

**Dunia and Blender are both Z-up, so geometry needs no axis change.** The file winds clockwise
(D3D) in 113 of 113 sampled meshes, so triangles are reversed on import, and V is flipped. Nothing
rotates the armature — the third-party importer's 180° Z rotation has no support in the data.

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
