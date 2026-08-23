# BlenderFC2

A Blender add-on for editing Far Cry 2 models. It reads and writes `.fc2model` **packs** — the
decoded form JackAll produces — and holds no Dunia format code at all.

That split is the design. JackAll owns every byte layout (`.xbg`, `.xbm`, `.xbt`, `.skeleton`,
`.mab`) and this owns what a scene looks like: parts, bones, materials, conventions, and what a
modeler is allowed to do. There is one implementation of each format, in one language, with the
corpus gates beside it — see [`tools/JackAll`](../JackAll/README.md) and
[`docs/docs/file-formats/`](../../docs/docs/file-formats/).

A pack arrives as JSON with flat float arrays, materials as JSON and textures as PNG. Nothing here
quantises, packs a chunk or hashes a name. See
[`.fc2model`](../../docs/docs/file-formats/fc2model.md).

## Getting a pack

```
jackall-cli fc2model export graphics/weapons/primary/ak47/ak47.xbg --game "C:\Games\Far Cry 2" --clips
jackall-cli fc2model export path\to\ak47.xbg          # a loose file, no install needed
```

`--clips` reads every animation bank in the install and carries the ones that name this model. It is
opt-in because it is the only part of an export that is not instant: nothing in a mesh names its
animation, so the only exact answer is to ask the banks. JackAll.App does the same from **Export as
.fc2model** on any `.xbg`.

Applying one back is **Apply .fc2model** in JackAll.App, which stages the changed files into the
workspace, or `jackall-cli fc2model extract` for a folder to drop into a mod layer.

## Layout

| Path | What it is |
|---|---|
| `addon/pack.py` | Reads and writes `.fc2model`: the manifest, the entries, and what may be edited |
| `addon/model.py` | The pack's mesh document as per-part meshes: node transforms, palettes resolved to bone names, flat arrays grouped into points |
| `addon/transform.py` | 4x4 helpers, so node world transforms need no `mathutils` |
| `addon/convert.py` | Every Dunia-to-Blender convention: winding, V flip, bone tails |
| `addon/import_xbg.py` | Builds the scene: parts, armature, rigid parts on their pivots, skin weights as vertex groups |
| `addon/materials.py` | Rebuilds the Generic shader as a node graph |
| `addon/rig.py` | Reparents the mesh's node tree onto the rig's — the knee fix below |
| `addon/import_mab.py` | Builds an Action from a bank, and marks what it attaches |
| `addon/export_xbg.py` | Writes edited geometry back into the pack |
| `addon/rules.py` | What a model is allowed to be, with no bpy and no format constants in it |
| `addon/validate.py` | Runs the rules against a scene, through the code an export would take |
| `addon/motion.py` | How far each bone travels across the clips a pack carries |
| `addon/panel.py` | The sidebar: the model, the check list, the motion table, export |
| `tests/_corpus.py` | Where a test pack comes from, and the skip-when-absent helpers |
| `tests/blender_import.py` | Imports the AK-47 and a character inside Blender, headless |
| `tests/blender_export.py` | Exports back through JackAll and requires the shipped `.xbg` bytes |
| `tests/blender_anim.py` | Poses a character and a weapon, and reads the rotations and offsets back off the rig |
| `tests/blender_check.py` | Requires every rule to be silent on retail, then fire on exactly one violation |
| `tests/render_preview.py` | Renders an imported model to a PNG, for looking at what the importer built |
| `open_model.py` / `.cmd` | Opens a pack, and optionally a clip, in Blender's UI; quoting-safe in cmd and PowerShell |

## What works

Importing a pack: parts, LODs, UVs, vertex colours, normals, an armature from the nodes, rigid parts
on their pivots, skin weights as vertex groups, and textures wired into the Generic shader graph.
Exporting edited geometry back. Loading any animation bank the pack carries onto the rig. Checking
the model against what the format allows, from a sidebar panel, before the game finds out.

## Export

**File ▸ Export ▸ Far Cry 2 Model Pack (.fc2model)** writes the collection's parts back into the pack
they came from. The pack is edited, not rebuilt: nodes, materials, bone palettes, the LODs that were
never imported and every document not touched all survive, and **only an entry that actually changed
grows an `origin_sha256`** — which is what stops applying a pack from recompressing a texture nobody
edited.

The gate is that **an untouched export returns the shipped game file**, all the way round: Blender to
pack, JackAll to `.xbg`, bytes compared. It holds for the AK-47, the 37-part buggy and a skinned
character. `tests/blender_export.py` also moves a single vertex and checks that exactly one vertex
moves in the file, so a writer that quietly copied its input would fail.

**A part's topology can change.** Give a part more or fewer vertices and the tangent frame is
regenerated from the UVs — Blender's frame agrees with the file's convention, within 0.9 dot on 89 to
96% of retail vertices, the rest being the seam and smoothing differences any regeneration produces.
`tests/blender_export.py` subdivides a part from 805 vertices to 2,637 and checks the result.

Whenever geometry moves, each part's sphere and box are refitted, and so are the model's. Culling
reads them, so a stale one makes a part vanish in game. Nothing is refitted when nothing moved, which
is what keeps the untouched round trip byte-exact.

What it cannot do yet:

- **Split UVs, normals or colours.** The format stores all three per vertex, not per corner, so a
  seam has to be a duplicated vertex. The first corner touching a vertex wins.
- **Add or remove parts, nodes or LODs.** JackAll can author a container from decoded content alone
  (3,133 of 3,133 shipped meshes), so the format no longer stands in the way — the scene-to-document
  side is what is missing.
- **Add a vertex component a buffer has no room for.** A second UV set on a part whose buffer carries
  one is dropped rather than invented.

The exception to "nothing here quantises" that is worth knowing: the file's normals are not unit
length, Blender normalises the ones it shades with, and re-encoding those would move about half of
them by one step. The original direction therefore rides along in an `fc2_normal` attribute, which
export prefers and falls back from when a mesh has been rebuilt.

## Animation

A pack's manifest indexes the banks it carries, so **Object ▸ Load Far Cry 2 Animation** offers them
as a list — name, length, rate and the bone the model hangs from — with no file dialog and no hunting
for a skeleton.

Four things a reader has to get right or the pose comes out mangled. They are why this file exists
rather than a generic JSON-to-Action script:

- **A bank is not one clip.** It carries one clip per skeleton taking part, so a weapon rig has to
  reach past the character's clip to its own. Which one is decided by the rig's own bone ids, not by
  position — an index would silently mispose the model when it went stale.
- **The mesh's bone tree is not the tree clips animate.** On `pelvis_ref` four bones differ: the
  mid-joint helpers `L/R Knee` and `L/R Elbow` hang off the `Pelvis` in the mesh but off the thigh
  and upper arm in the rig. Animate them on the mesh's tree and a knee helper stays by the hip while
  the leg swings, tearing the mesh into spikes. Reparenting keeps every head, tail and roll, so the
  bind pose is untouched; it takes the worst edge stretch around those bones from **10.5x to 2.5x**,
  which is less than the 5.3x the rest of the character reaches on the same sprint.
- **A rotation replaces the bone's rest rotation rather than adding to it**, so the pose bone gets
  `rest⁻¹ · clip`, and the armature is built with each bone oriented like its own node instead of
  aimed at its children. Aiming at children bakes in a twist that no later correction removes. The
  same holds for offsets, and since a pose bone's location is measured in its own rest frame, the
  offset is rotated into that frame before it is keyed.
- **Blender parents an object to a bone's tail**, so an attachment's parent inverse cancels the
  bone's length and it lands on the head, which is the frame the clip is written in.

`tests/blender_anim.py` checks this from the other side: it evaluates the posed rig and reads each
bone's rotation and offset relative to its parent back out, requiring what the pack stores. Worst
difference across four clips is `5.0e-07` on rotation and `2.0e-07` metres on offset.

**A character has no rig of its own.** 74 of the 78 skinned shipped meshes have no sibling
`_ref.skeleton` and share `characters\_common\pelvis_ref.skeleton`; it is the best name match for 70
of them and a tie for five, so `fc2model export --rig` names it rather than guessing.

Sixteen bones carry an orientation constraint and no clip ever keys them — those four helpers plus
twelve arm twists. Where the engine evaluates them has not been traced, so nothing here poses them
and they simply follow their parents. Reading them as world-space blends was tried and measurably
made the mesh worse, so it is not shipped.

Not written: **authoring** a clip. JackAll can encode one (99.9% of shipped banks rebuild with their
framing intact), but nothing here turns an edited Action back into keyframe tracks.

## Running the tests

They need JackAll built and a Far Cry 2 install, because a test pack is built by JackAll rather than
checked in — a pack is a contract between two codebases, and a fixture written by hand would only
test this side's idea of it. Proprietary game content is never committed, so there is nothing to
check in either. Set `FC2_GAME` to point elsewhere than `C:\Games\Far Cry 2`; the scripts skip
cleanly when either is missing.

```
& "C:\Programs\Blender 5.2\blender.exe" -b --python tools\BlenderFC2\tests\blender_import.py
& "C:\Programs\Blender 5.2\blender.exe" -b --python tools\BlenderFC2\tests\blender_export.py
& "C:\Programs\Blender 5.2\blender.exe" -b --python tools\BlenderFC2\tests\blender_anim.py
& "C:\Programs\Blender 5.2\blender.exe" -b --python tools\BlenderFC2\tests\blender_check.py
```

To look at a model rather than assert about it — worth doing, since a part can sit in the wrong place
and still pass every numeric check:

```
"C:\Programs\Blender 5.2\blender.exe" -b --python tools\BlenderFC2\tests\render_preview.py -- <model.fc2model> out.png [--highlight <part index>]
```

To open one interactively, with the model already loaded — works the same in cmd and PowerShell:

```
tools\BlenderFC2\open_model.cmd ak47.fc2model
tools\BlenderFC2\open_model.cmd ak47.fc2model 0 reload
```

With no clip named it lists the banks the pack carries. Set `BLENDER` to point at a different
executable, or call Blender directly, which is the same thing without the wrapper:

```
"C:\Programs\Blender 5.2\blender.exe" --python tools\BlenderFC2\open_model.py -- <model.fc2model> [lod] [clip]
```

Avoid `--python-expr` for this: a one-line expression full of quotes and semicolons is tokenised
differently by cmd and PowerShell, and cmd will split it on spaces.

## Installing the add-on

Blender 4.2+ extension. Build the zip with Blender itself, which validates the manifest on the way:

```
& "C:\Programs\Blender 5.2\blender.exe" --command extension build --source-dir tools\BlenderFC2 --output-dir .
```

Install the resulting `farcry2_formats-<version>.zip` from **Edit ▸ Preferences ▸ Get Extensions ▸
Install from Disk**. Import via **File ▸ Import ▸ Far Cry 2 Model Pack (.fc2model)**.

Plain `zip` on the folder does not work: an extension needs `register`/`unregister` at the zip root
beside the manifest, which is what the root `__init__.py` re-exports, and `[build]` in the manifest
is what keeps `tests/` and the command-line scripts out of the package.

**An installed extension shadows this working tree.** The add-on puts its own directory on
`sys.path`, so once it is installed, anything run inside Blender imports that frozen copy rather than
the files being edited. `tests/_corpus.py` evicts it, which covers every test script; `open_model.py`
does not, so rebuild and reinstall after changing code, or disable the extension while developing.

The manifest declares `SPDX:Unlicense` to match the repository root; `tools/JackAll` uses a different
licence, so change it here if this should follow that instead.

## The viewport tells the truth

Warning that a channel is unsupported is only honest if the supported ones are visible, so the
material graph wires everything the format actually carries: the two tiling detail maps blended by
the mask, each tinted, the base tint lerp, the normal map through a Normal Map node (1,656 of 2,379
shipped materials carry one), the specular map inverted into Roughness with `SpecularPower` as the
floor (1,889 carry one), and vertex colour multiplied into both blend weights where
`VertexColorEnabled` is set (2,159).

Roughness is the one approximation. Blender has no `SpecularPower` and Dunia has no roughness, so a
bright specular texel becomes a smooth one — which is the difference between seeing a specular edit
and seeing nothing.

**Every node the importer makes carries an `fc2_slot` tag** naming the slot it stands for. That is
what lets the validator tell the graph it built from one a modeler rewired: without it, driving
Roughness from a specular map would make the plugin warn about every shipped material at once — which
is exactly what `tests/blender_check.py` caught the first time this was wired.

## Checking a model

**View3D sidebar ▸ Far Cry 2 ▸ Check** runs every rule against what an export would write, and lists
what it finds with a Select button that jumps to the object, vertex or material each one is about.
An **ERROR** blocks the export; everything else is a warning, because retail itself breaks plenty of
guidelines and refusing those would make the plugin wrong about the game it is for.

The gate that keeps it honest is that **every rule, warnings included, says nothing about a model
exactly as it shipped** — checked against the rifle, a 37-part vehicle and a skinned character.
Retail is the definition of valid, so a rule that fires on it is a wrong rule, and one wrong rule
blocking a legitimate export would destroy trust in the whole feature. `tests/blender_check.py` then
introduces one violation at a time and requires that exact code and no other.

What it catches today: an object export would silently skip, two objects claiming one part, a part
that draws nothing, a part moved in object mode (export writes vertex positions only, so the move is
discarded), a cluster over the triangle or palette ceiling, a buffer over the vertex ceiling, an
unweighted vertex on a skinned part, editing a file shared with other models, and the material
channels the format does not carry — metalness, roughness maps, emission, subsurface and the rest —
each with what to do instead.

The ceilings come from the pack's own `limits`, so there is no second place for them to drift.

## The motion table

**Far Cry 2 ▸ Animation ▸ Measure motion** reports, per bone, the worst rotation and translation
across every bank the pack carries. On the AK-47 that is `FRAME` at 0° over 0 m, `SLIDE` at 0.14 m
and `CLIP` at 45° over 0.39 m.

That is the single most useful thing the add-on can say to a weapon modeler and it is not guessable
from the mesh: the bone that does not move is where the body of the gun belongs, and one that swings
is a moving part. Put geometry on the wrong bone and the gun tears itself apart on the first reload,
which is otherwise a playtest discovery.

Bone to part is a name match, which holds for weapon rigs (`FRAME`, `CLIP`, `SLIDE`, `ACCESSORY`) and
is meaningless for characters.

## Conventions this file owns

Everything byte-level moved to [`docs/docs/file-formats/`](../../docs/docs/file-formats/). What is
left here is what a scene has to get right, and all of it is measured rather than assumed.

**Dunia and Blender are both Z-up, so geometry needs no axis change.** The file winds clockwise (D3D)
in 113 of 113 sampled meshes, so triangles are reversed on import, and V is flipped. Nothing rotates
the armature — the third-party importer's 180° Z rotation has no support in the data.

**A model's own texture is usually a mask, not its colour.** A Dunia surface is two shared tiling
detail maps blended by the green channel of a per-model mask, each tinted by its own colour, with the
blue channel choosing how far layer 1's tint moves from `DiffuseColorBase` towards `DiffuseColor1`.
The AK-47's `ak47_state01_m.xbt` is that mask; its wood and metal come from
`graphics\_textures\diffuse\`. Applying `DiffuseColor1` flat instead of lerping from the base leaves
everything washed out.

**A part with no placement node sits in the root's space**, not at the origin — every skinned part,
and any rigid one already modelled in place. Skipping the root transform drops a character through
the floor and turns it ninety degrees, which is exactly what the bounds check catches.

**A cluster's bone palette is what a vertex group has to resolve to.** A group naming a bone outside
the palette cannot be written, so export refuses rather than silently dropping the influence.
