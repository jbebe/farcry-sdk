# BlenderFC2

Read/write codecs for the Far Cry 2 (Dunia) model, rig and animation formats, plus the Blender
add-on built on top of them.

`fc2fmt/` is pure Python and imports no `bpy`, so it runs under plain `python` for corpus testing
and under Blender's interpreter for the add-on.

## Status

| Format | What it is | Round trip | Bytes still opaque |
|---|---|---|---|
| `.xbg` | mesh container: geometry, LODs, nodes, materials, skinning | **3,133 / 3,133** | 98.5% |
| `.skeleton` | rig: bones, constraints, weapon sockets | **81 / 81** | **0%** |
| `.mab` | animation bank: rotation tracks and events | **4,436 / 4,436** | 99.6% |

Round trip means `write(parse(f)) == f`, with the writer regenerating each structure — chunk sizes,
payload sizes, counts, padding, and the derived duplicates inside a cluster header — rather than
echoing it.

**Read the second column with the first.** A round trip proves the *framing*, and bytes the reader
keeps as an opaque blob pass it for free. `.skeleton` is fully decoded. Most of an `.xbg` is the
vertex and index streams, and most of a `.mab` is its keyframe payload; both are currently carried
through untouched, so the pass count says nothing about them. `tests/invariants.py` covers what the
round trip cannot, and `roundtrip.py --coverage` prints the opaque share so the two numbers stay
side by side.

**Intended workflow is donor-edit**: import a shipped `.xbg`, change it, write it back. Authoring a
mesh from nothing is not supported yet — `Lod.vertex_data` and `Lod.index_data` are still raw
buffers, so a caller has to decode them with `vertex_layout()` by hand.

Not yet done: the Blender add-on itself, structured vertex/index streams, and `.mab` keyframe track
authoring.

## Layout

| Path | What it is |
|---|---|
| `fc2fmt/binary.py` | `Reader`/`Writer`, CRC32 name hashing, the descending-counter alignment fill |
| `fc2fmt/xbg.py` | `.xbg` container: chunks, `EDON` nodes, `MB2O` bind matrices, `DNKS`/`SULC` clusters, LODs |
| `fc2fmt/skeleton.py` | `.skeleton` (`LKS`) bones, constraints, anim handles |
| `fc2fmt/mab.py` | `.mab` header, bone bitmasks, the smallest-three quaternion codec |
| `tests/_corpus.py` | Corpus location and the skip-when-absent helper the scripts share |
| `tests/roundtrip.py` | Round-trips every retail file of a format; `--coverage` reports the opaque share |
| `tests/invariants.py` | Checks decoded meaning: palettes index real bones, derived links recompute to what shipped |
| `tests/mabcheck.py` | Resolves every `.mab` mask bit against `pelvis_ref.skeleton` and checks quaternion norms |
| `tests/quatcheck.py` | Scores the quaternion component layout against the skeleton rest pose |
| `tests/probe.py` | Dumps one file's chunk layout when a round trip fails |

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

## Format notes worth knowing

Full byte-level documentation belongs in `docs/docs/file-formats/`. These three are the ones that
break a parser written from the community material.

**`.xbg` chunk headers are 20 bytes and the payload is addressed backwards**, at
`chunkStart + chunkSize - payloadSize`. `DNKS` is the only chunk with a sub-chunk (`SULC`, holding
the bone clusters), which is why its own payload sits at the end rather than at a fixed offset.
Traced from `LoadGeomResource` (`FarCry2_server` 0x097fd440).

**Alignment padding is a descending byte counter**, not zeros — nine bytes of padding are written
`09 08 07 06 05 04 03 02 01`. A writer that pads with zeros produces a file the game still loads but
that no longer matches the original byte for byte, which silently destroys the round-trip gate.

**Bone palettes**: a static submesh has all 48 slots `-1` (28,217/28,217 in retail); a skinned one
holds a contiguous prefix of node indices, duplicates included, then `-1` padding
(3,953/3,953). The community rule that skinned palettes must never contain `-1` is wrong.

**`.mab` bitmasks are indexed by `.skeleton` bone id**, and a bone's slot inside a section is the
popcount of the mask below its id. Verified by resolving 303,067 mask bits against
`pelvis_ref.skeleton` with zero out of range.

**The quaternion component layout is confirmed.** Unit norm cannot discriminate a permutation, so
`tests/quatcheck.py` scores each candidate against the skeleton rest pose instead: a bone a clip
holds constant should sit at or near `m_ChildToParent`. Over 31,383 samples the engine layout scores
mean `|dot| = 0.977` with 64% inside 0.99, against 0.858 for the next candidate and 0.04 for the
wrong ones — unambiguous. The mean is below 1.0 because a clip may hold a bone at a posed constant
rather than at rest. The layout is a table (`ENGINE_LAYOUT`) that the test drives as a parameter, so
it scores the shipped decoder rather than a copy of it.

**Still open in `.mab`**: the per-track packing inside a keyframe group. The bitmasks, constant
rotations, section table and quaternion codec are decoded and validated; splitting a group into
per-bone tracks is not, so animation *import* is not yet possible. `.mab` files round-trip and can
be carried through a mod unchanged.
