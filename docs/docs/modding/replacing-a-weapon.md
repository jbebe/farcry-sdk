---
sidebar_position: 6
---

# Replacing an existing weapon

:::caution[Work in progress]
This page is being written **while the mod is being built**, so it is a running log as much as a
guide. Sections marked **Open** are questions the build has not answered yet. Everything else is
measured or traced, and says which.

The worked example is a **VSS Vintorez replacing the Dart Rifle**. Where a step is specific to that
pair it says so; the reasoning is written to transfer to any other pair.
:::

[Adding a new weapon](./adding-a-weapon.md) covers standing up a weapon that did not previously
exist. This page is the cheaper and far more common job: taking a weapon the game already ships and
making it something else. Nothing here needs a free `iAnimationValue` slot, a `movemgr.bin` writer,
or a new `depload` entry — the donor already has all of that.

## Choosing the donor

The donor decides more than the silhouette. It fixes, and you inherit whether you want to or not:

| What you inherit | Where it comes from | Changeable? |
| --- | --- | --- |
| Hand and arm motion | the `.mab` character clip | **No** — see [hand placement](#hand-placement-is-baked-into-the-animation) |
| Which parts animate, and how | the `.mab` weapon clip, addressed by **bone id** | Only by swapping the whole set |
| Part names | `CGraphicComponent`, CRC32 **exact-case** | No |
| Collision shape | the `.hkx`, which no tool parses | No |
| Inventory slot, shop entry, HUD icon | `gamemodesconfig.xml`, `sName` | Yes, but each is its own edit |

So pick for **behaviour first, silhouette second**. A donor whose animation set already does what
your weapon does is worth more than one that merely looks similar.

### The donor is two roles, and they can be two weapons

:::warning[Settle the animation set before fitting any geometry]
Every alignment — grip, support hand, scope axis — is made against **whatever animation actually
plays**. Fit the mesh first and then change `iAnimationValue`, and all of it silently becomes wrong.
:::

The worked example needed exactly that split:

| Role | Weapon | Why |
| --- | --- | --- |
| **Archetype** | Dart Rifle | `bIsSilent = True`, Special slot, 4 archetypes instead of 12, no `_Merc` variant |
| **Geometry + animation** | Dragunov | its reload actually moves the magazine |

That second row is a measurement, not a preference. Per-clip motion on the two candidates:

```
Dart Rifle   1stge_uppb_reload      SLIDE 40deg/0.081m         <- CLIP never moves
Dragunov     1stge_uppb_reload      CLIP 49deg/0.508m
Dragunov     1stge_uppb_shootcycle  SLIDE 0.115m
```

The Dart Rifle's reload works the bolt and never touches the magazine — it feeds one round. For any
weapon with a detachable box magazine that reload is simply wrong, and no stat edit fixes it.

So the mesh is built in the **Dragunov's** pack, whose part names match the skeleton its clips
address, and written out to the **Dart Rifle's** `.xbg` path. The archetype then points at the
Dragunov's rig and animation index. The two donors differ in every alignment constant:

| | Dart Rifle clips | Dragunov clips |
| --- | ---: | ---: |
| `R Hand` y | −0.1565 | −0.0729 |
| `L Hand` y | +0.2143 | +0.2630 |
| hand span (sets scale) | 0.3708 | 0.3359 |
| sight-line z | +0.1579 | +0.1300 |

### Why the Dart Rifle, for a VSS

:::info[Measured against the shipped entity library and mesh corpus]
Counts below come from decoding `patch/worlds/tmpla/generated/entitylibrary.fcb` with
`jackall-cli fcb decode`, and from `jackall-cli xbg export` over
`worlds/worlds/graphics/weapons/`.
:::

Four scoped rifles ship: Dart Rifle, Dragunov, AS50, M1903. The Dart Rifle wins on three counts.

**It is the only silent one.** `CommonProperties.bIsSilent` is `True` on
`WeaponProperties.Special.Dart_Rifle` and `False` on all three others. The VSS Vintorez is defined by
being integrally suppressed, so this single flag carries the AI-reaction behaviour, the sound bank
and the muzzle particle across for free.

**It has the fewest archetypes to edit.**

| Weapon | `WeaponProperties.*` | `weapons.*` | Total |
| --- | ---: | ---: | ---: |
| **Dart Rifle** | 2 | 2 | **4** |
| AS50 | 3 | 3 | 6 |
| M1903 | 3 | 3 | 6 |
| Dragunov | 6 | 6 | **12** |

The Dragunov's twelve include `.AI`, `.Dragunov_Merc`, `.Mikes_Rusty` and `.Persistent`.
`Mikes_Rusty` is a story-unique weapon, so replacing the Dragunov means either breaking it or
maintaining a variant you did not want.

**Nothing else carries it.** There is no `Dart_Rifle_Merc`, so no NPC spawns with it. Third-person
rig mismatches cannot bite you, and when the new model appears in your hands that is unambiguous
proof the change worked rather than ambient noise.

It is also fully wired in single-player already — weapon-bazaar crate (`availability="1"`, cost 10),
operation and repair manuals, challenges, kill-message code `~DA`, HUD icon `hud_icon_sniperdart`.

### The one thing the Dart Rifle does *not* teach

:::note[Corrects the assumption this build started from]
The Dart Rifle was picked partly to demonstrate "how to give a weapon a scope". It cannot, because
it already has one — and so does every other scoped rifle.
:::

All four scoped rifles ship a `SCOPE_HI` mesh part and `CFCXWeapon.bUseHiResScope = True`. Replacing
one with another teaches scope *preservation*: your mesh must supply a `SCOPE_HI` part whose
geometry lines up with the reticle, because `bUseHiResScope` renders through it.

Adding a scope to a weapon that has none is **not currently achievable with the supported
toolchain**. It needs a new `SCOPE_HI` part, and `tools/BlenderFC2` 0.1.0 can add a part only to a
single LOD — the scope would vanish as soon as the weapon dropped to LOD1. Do not plan a section
around it.

## Stats: what actually changes

The values below are the ones that make a bolt-action tranquilizer rifle behave like a semi-automatic
suppressed marksman rifle. Every one lives on `WeaponProperties.Special.Dart_Rifle` (and its
`.Multi` twin — **edit both**).

| Field | Dart Rifle ships | VSS wants | Why |
| --- | --- | --- | --- |
| `selFireRateMode` | `2` (PrepareShot) | `0` (SingleShot) | 2 is the bolt-cycle mode; the VSS is gas-operated semi-auto |
| `iAmmoInClip` | `1` | `10` or `20` | VSS box magazine |
| `iFireRate` | `120` | ~`600` | RPM |
| `ammoAmmoType` | `FC2096BC` (darts) | `7D6BD5F2` (sniper pool) | otherwise it draws from the dart pool |
| `sDisplayName` | `Dart` | `VSS Vintorez` | literal string, **not** a localization id |
| `bIsSilent` | `True` | `True` | already correct |
| `selWeaponClass` | `5` (Sniper) | `5` | already correct |
| `selCategory` | `3` (Special) | `3` | already correct |

The `sel*` enums are self-describing in the file — each is followed by its own value list — so none
of these indices has to be guessed. See [adding-a-weapon](./adding-a-weapon.md#weaponproperties--the-stat-archetype).

Beyond the table, making the weapon lethal rather than tranquilizing means rewriting the
`WeaponStims` / `ImpactStims` blocks and repointing the projectile off `dart.xbg` (a visible dart)
onto the rifle bullet and tracer. That is the largest single chunk of data work in this build.

### How to actually apply an archetype edit

Decode the library, edit, and stage **one fragment per archetype**:

```
jackall-cli fcb decode  patch\worlds\tmpla\generated\entitylibrary.fcb
```

That writes an index plus ~42 group files (`41_WeaponProperties.xml`, `42_weapons.xml`, …). Edit the
archetype inside its group file — and **scope the edit to the `Entity` node whose `hidName` matches**
rather than to a line range, because neighbouring archetypes carry the same field names a few lines
away and a window will silently catch the wrong gun.

A fragment's id is the dotted `hidName` mapped onto a path, and the fragment itself is the
`EntityPrototype` node (type hash `256A1FF9`). So the layer holds:

```
mods\worlds\tmpla\generated\entitylibrary.fcb\WeaponProperties\Special\Dart_Rifle.xml
mods\worlds\tmpla\generated\entitylibrary.fcb\WeaponProperties\Special\Dart_Rifle\Multi.xml
mods\worlds\tmpla\generated\entitylibrary.fcb\weapons\Special\Dart_Rifle.xml
mods\worlds\tmpla\generated\entitylibrary.fcb\weapons\Special\Dart_Rifle\Multi.xml
```

`jackall-cli mod inspect` confirms it read them as fragments rather than as loose files, and
**`mod lint` is worth running every time** — it reports archetype edits that a later entity library
overrides, which change nothing in game and are otherwise invisible.

:::note[`fcb encode` will not take the decoded form back]
`decode` splits an entity library into group files; `encode` refuses multi-file XML. So there is no
round trip through the whole container, and fragments are the route. Applying them still re-encodes
the container, which inflates it — `patch.dat` went 9.9 → 27.6 MB for four small edits. See
`docs/design/fcb-deep-fragments.md`.
:::

### Changing the animation set is three fields, and one hash

Switching to another weapon's clips means all of:

| Field | On | Value |
| --- | --- | --- |
| `iAnimationValue` | `weapons.*` | the new weapon's `EquippedWeapon` index (Dragunov `23`) |
| `sPartName` | `CSimpleAnimationComponent` | the new animation key (`dragunov`) |
| `fileSkeleton` | `CSimpleAnimationComponent` | the new rig's hash (`1BF80FFB`) |

And one that is easy to miss: **`BulletCaseBone` is a CRC32 of a bone name, and the new rig may spell
it differently.** The Dart Rifle's rig has `FX_CASING`, the Dragunov's has `FX_Casing` — exact-case
hashing makes those different bones, so the field has to be recomputed (`D431B68E` → `2365743E`).
Validate any CRC32 implementation against a known pair before trusting it; `FX_CASING` → `D431B68E`
is a good one.

## Animation: `iAnimationValue` and the bone-id trap

:::info[Verified against the shipped skeletons and animation banks]
Bone lists come from `tmp/fielddump/skeleton.jsonl`; clip contents from `tmp/fielddump/mab.jsonl`.
The bone-id addressing is documented in [`.mab`](../file-formats/mab.md).
:::

`CFCXWeapon.iAnimationValue` is an index into the `EquippedWeapon` enum in
[`movemgr.bin`](../file-formats/move.md). The Dart Rifle is `39`; the Dragunov is `23`. A VSS
should move like a Dragunov, not like a dart gun, so the instinct is simply to write `23`.

**That instinct is right, and doing only that would break the weapon.**

A `.mab` bank holds one clip per skeleton in the animation — the character first, then the weapon —
and **bones are addressed by their `.skeleton` bone id, carrying no names of their own**. The two
weapons order their bones differently:

| id | `dart_rifle_ref.skeleton` | `dragunov_ref.skeleton` |
| ---: | --- | --- |
| 0 | `Dart_Rifle` (root) | `Dragunov` (root) |
| 1 | **`FRAME`** | **`CLIP`** |
| 2 | **`CLIP`** | **`FRAME`** |
| 3 | **`SLIDE`** | **`FX_Casing`** |
| 4 | `FX_FIRE` | `FX_FIRE` |
| 5 | **`FX_CASING`** | **`SCOPE_HI`** |
| 6 | — | `SLIDE` |
| 7 | — | `ACCESSORY02` |

Only id 0 and id 4 agree. Playing a Dragunov reload against the dart rifle's rig would drive `FRAME`
— the entire gun body — along the path meant for the magazine, and reference ids 6 and 7 that do not
exist on that skeleton.

Confirmed by reading the bank directly. `1stge_uppb_reload_+000fw_spdra_i1.mab` holds two clips:

```
clip[0]  bones 0…101      the character — pelvis_ref.skeleton, arms and fingers included
clip[1]  bones 0,1,2,3,4,6  the weapon — root, CLIP, FRAME, FX_Casing, FX_FIRE, SLIDE
```

Note `FX_Casing` on the Dragunov against `FX_CASING` on the dart rifle. Name hashes are CRC32
**exact-case**, so the casing bone is a different bone to the engine, not just a different spelling.

**So swapping the animation set is a three-part change, not a one-field change:**

1. `CFCXWeapon.iAnimationValue` → `23`
2. `CSimpleAnimationComponent.sPartName` → `dragunov` (the key `depload` and the MOVE graph bind clips by)
3. `CSimpleAnimationComponent.fileSkeleton` → the Dragunov's skeleton, hash `1BF80FFB`

and then the mesh's part names must match that skeleton's bones — `CLIP`, `FRAME`, `FX_Casing`,
`FX_FIRE`, `SCOPE_HI`, `SLIDE`, `ACCESSORY02` — byte for byte.

The cleaner alternative, since `.skeleton` round-trips byte-exactly on all 81 shipped files, is to
**author a `vss_ref.skeleton` that keeps the Dragunov's bone order and names but carries VSS
transforms**. Clip ids then line up by construction, while `FX_FIRE` sits at the VSS's muzzle and
`FX_CASING` at its ejection port.

**Open:** which of the two routes is less work in practice. Both are being tried.

### The animation set and the fire mode are one decision

Both weapons are in the same animation family — the `sp*` group under
`animations/weapons/special/`, Dart Rifle `sp389` (43 clips), Dragunov `spdra` (40). Diffing the two
clip lists with the weapon code stripped shows most of the difference is naming drift for the same
action (`shootingcycle` vs `shootcycle`, `shootregular` vs `shootreg`, `reload_nodir` vs
`reload_+000fw`).

Three clips are genuinely absent from the Dragunov set, and they are exactly the right ones:

```
1stge_uppb_jamcycle2unjamfail_+000fw
3rdge_uppb_prepareweaponiron_+000fw
3rdge_uppb_prepareweaponreg_+000fw
```

`prepareweapon` **is the bolt-cycle animation.** The Dart Rifle has it because
`selFireRateMode = 2` (PrepareShot); the Dragunov does not because it is semi-automatic. So dropping
to `selFireRateMode = 0` and moving to `iAnimationValue = 23` are two halves of one coherent change
— the stat block stops asking for a bolt cycle at the same moment the animation set stops offering
one. Doing only one of the two would leave the weapon asking for a clip that is not there.

**Open:** the Dragunov set also lacks a `jamcycle2unjamfail` clip while both weapons declare
`selJamType = 0`. Whether the MOVE graph falls back cleanly or this needs watching is a playtest
question.

## Hand placement is baked into the animation

This is the question that decides how much freedom the whole job has, so it is worth stating
precisely.

**There is no general grip socket.** `HAND_PLACEMENT` exists as a bone in only three of the 41
weapon skeletons — `ithaca`, `dlc1_silenced_shotgun` and `deserteagle` — and those are the cases
where a hand has to *follow a moving part* (a pump, a slide). It is not the mechanism by which the
player's hands find the gun.

The actual mechanism is that **`clip[0]` of every weapon bank animates the character's arms and
hands directly**, against the `pelvis_ref.skeleton` that every human in the game shares. In the
reload bank above, that clip touches bones 55–101 — clavicles, forearms, hands and every finger. The
grip is not computed at runtime; it is authored, per weapon, per clip.

Worse for repositioning: a clip's bone transform **replaces the rest transform rather than adding to
it** (see [`.mab`](../file-formats/mab.md)). So for any bone a clip animates, editing that bone's
rest pose in the `.skeleton` changes nothing at animation time — the clip wins.

### What that leaves you

**You fit the gun to the hands. You cannot fit the hands to the gun.**

Which is less limiting than it sounds, because the lever you do keep is the useful one:

| Lever | Free? | What it moves |
| --- | --- | --- |
| **Mesh geometry in each part's local space** | **Yes** | everything you actually need — this is the fitting tool |
| Rest pose of bones no clip animates (`SCOPE_HI`, `ACCESSORY02`) | Yes | scope and accessory placement |
| Rest pose of animated bones (`FRAME`, `CLIP`, `SLIDE`, FX) | No | overridden by the clip |
| Character arm/hand motion | No | would need rewriting 46 clips |

Concretely: the `CLIP` bone will travel the Dragunov's magazine-drop path no matter what. But your
VSS magazine *geometry* is authored in that bone's local space, so modelling it 2 cm forward puts the
magazine where the VSS's magazine belongs while it still drops along the inherited trajectory. The
same applies to the bolt, the muzzle and the whole frame.

The practical workflow, then, is to import the donor's pack in Blender, load a clip onto the rig, and
**model the VSS against the posed hands** — grip, handguard and trigger placed under where the hands
already are.

:::note[Why re-authoring the arms is not an option today]
`tools/BlenderFC2` can write animation back, but **only the weapon's clip** — it puts the Action
into "the one clip that fits this model, leaving the character's clip and the rest of the chain byte
for byte". The character clip is exactly the one that holds the hands. Editing arm motion is
therefore outside the supported toolchain, not merely tedious.
:::

## The mesh

:::info[Measured]
Source model parsed from its glTF; donor measured with `jackall-cli xbg export`.
:::

```
VSS source (scene.gltf)   18,153 tris · 52 primitives · 1 material · 106 nodes
                          attrs: POSITION, NORMAL, TANGENT, TEXCOORD_0
                          textures: baseColor 3.8 MB JPEG, metallicRoughness 11.9 MB PNG,
                                    normal 20.9 MB PNG
Dart Rifle (donor)         9,404 tris LOD0 · 14,572 all LODs · 5 materials · 5 LOD tiers
                          parts: FRAME, CLIP, SLIDE, SCOPE_HI
```

### Authoring ceilings, and where retail sits

The format's hard limits are in [`.xbm` / `.xbg`](../file-formats/xbm-xbg.md#authoring-ceilings):
21,845 triangles per cluster, 65,535 vertices per LOD buffer. But the *effective* limit is tighter,
because `tools/BlenderFC2` writes **one cluster per part object**, taking the material from the
object's first slot. So 21,845 is a per-object ceiling, enforced before export:

```
ERROR  cluster.too-many-triangles
'FRAME' draws 31200 triangles; the limit is 21845.
```

Retail first-person weapons run **5,018–12,455 triangles at LOD0** across all 21 of them, the
heaviest being the MGL-140. At 18,153 the VSS clears every format ceiling but sits about 1.5× above
anything Ubisoft shipped, so LOD0 is being decimated to roughly 11–12k to stay in band.

## Splitting your model into the donor's parts

This is the part of the job nobody will hand to a script — it is your model, and the cuts are
judgement calls. What follows is the whole procedure, in order, with the rules that make it work.

### Step 0 — build the tools

Both tools are built from source in this repo.

```
cd tools\JackAll && dotnet build      # jackall-cli lands in src\JackAll.Cli\bin\Debug\net10.0\
.\tools\BlenderFC2\build.ps1
```

Install the resulting `farcry2_formats-<version>.zip` through **Edit ▸ Preferences ▸ Get Extensions ▸
Install from Disk**. It is a Blender 4.2+ extension.

:::warning
Any prebuilt `jackall-cli.exe` lying around in `tools/JackAll/publish/` may predate the `fc2model`
branch. If `jackall-cli fc2model --help` says *Unknown command*, you are running a stale binary —
publish again.
:::

### Step 1 — export the donor as a pack

Blender never touches `.xbg`. JackAll owns every byte layout and hands Blender a `.fc2model` **pack**
— JSON with flat float arrays, materials as JSON, textures as PNG.

```
jackall-cli fc2model export graphics/weapons/special/dart_rifle/dart_rifle.xbg ^
    --game "C:\Games\Far Cry 2" --clips
```

`--clips` reads every animation bank in the install and carries the ones naming this model. It is
opt-in because it is the only slow part of an export — nothing in a mesh names its animation, so the
only exact answer is to ask all 4,436 banks. **Take the cost.** You need those clips in step 3.

`--rig` overrides which skeleton travels with the pack, which is how you would carry the Dragunov's
rig instead of the dart rifle's.

### Step 2 — find out which part is which

**Do this before cutting anything.** Import the pack with **File ▸ Import ▸ Far Cry 2 Model Pack
(.fc2model)**. The importer offers three options: **LOD** (default 0), **Build armature** and **Load
textures**. Leave all three alone for now.

Then open the sidebar with <kbd>N</kbd>, go to the **Far Cry 2** tab, and in the **Animation** panel
press **Measure motion**.

It reports, per bone, the worst rotation and translation across every bank the pack carries. On the
AK-47 that reads:

```
FRAME    0.0 deg, 0.000 m
SLIDE    0.0 deg, 0.140 m
CLIP    45.0 deg, 0.390 m
```

**This is the single most useful thing the add-on can tell you, and it is not guessable from the
mesh.** The bone that does not move is where the body of the gun belongs. A bone that swings is a
moving part. Put geometry on the wrong bone and the gun tears itself apart on the first reload —
otherwise a playtest discovery, and a confusing one.

On the Dart Rifle it reads:

```
CLIP        178.9 deg, 1.038 m      the magazine, thrown clear on an unjam
Dart_Rifle  122.9 deg, 0.705 m      the root: the whole weapon, on draw
SLIDE        85.4 deg, 0.555 m      the bolt
FRAME         0.0 deg, 0.000 m      never moves
FX_FIRE       0.0 deg, 0.000 m      a socket, not geometry
```

So the split is not a matter of taste. The motion table dictates it, and a stray receiver face on
`CLIP` gets flung a metre across the screen:

| Part | Gets | Because |
| --- | --- | --- |
| `FRAME` | receiver, barrel, stock, handguard, suppressor, trigger group | it never moves |
| `CLIP` | magazine only | it swings and drops on reload |
| `SLIDE` | bolt / charging handle | it cycles on every shot |
| `SCOPE_HI` | **the eyepiece only** — see below | rendered through when aiming |

### Step 3 — count the donor's *clusters*, not its parts

:::warning[The donor has more objects than it has parts]
This is the single most surprising thing about the import, and everything downstream depends on it.
:::

The Dart Rifle looks like four parts. At LOD0 it imports as **eight objects**, because a part is one
object *per cluster* and each cluster carries its own material:

```
slot 0  FRAME     lens material          40 tris
slot 1  FRAME     metal                5,617
slot 2  FRAME     wood / camo            691
slot 3  CLIP      metal                  196
slot 4  SLIDE     metal                  760
slot 5  SCOPE_HI  crosshair               12
slot 6  SCOPE_HI  lens                    72
slot 7  SCOPE_HI  metal                2,016
```

So "put the frame on `FRAME`" really means "split your frame across three objects by material". The
donor's stock is a separate cluster from its receiver because the stock is wood and the receiver is
metal — and that split is the one your model has to follow, whatever its own material layout is.

### `SCOPE_HI` overlays the scope; it is not a piece of it

:::danger[Confirmed the hard way, in game]
`SCOPE_HI` is a 3.7 cm assembly at the *rear* of the scope — eyepiece, lens and crosshair quad — and
**the complete scope, eyepiece included, is already there in `FRAME` underneath it.** 564 of the
donor's 6,348 `FRAME` faces fall inside the `SCOPE_HI` bounding box.

So the two **overlap on purpose**. `SCOPE_HI` is drawn when the player looks through the sight; it is
not drawn in the ordinary view, which is where the trap is.
:::

Carve your eyepiece *out* of `FRAME` and move it into `SCOPE_HI` and the model looks correct in
Blender, passes **Check** with zero findings, exports, installs — and in game the back of the scope
is simply missing, with a ragged spray of triangles where the cut ran. Nothing catches it, because
nothing is wrong with the file: the geometry is all present, just in the part that is not drawn.

**The rule that came out of four failed attempts:**

- Put your **whole scope on `FRAME`**, uncut.
- **Never move or resize the lens and crosshair.** Their size is calibrated to where the aim clips
  put the eye, not to the tube around them, so scaling them scales the sight picture.
- **Replace the eyepiece housing with a copy of yours.** `SCOPE_HI` is not only optics: on the
  Dragunov it is five clusters, two of which (1008 and 936 triangles) model the eyepiece *housing*.
  Those sit coincident with that gun's own eyepiece, so only a rim shows. Leave them in place under a
  differently-shaped eyepiece and the donor's housing protrudes all the way round yours as a ring —
  and looking through gives you the ring, not your tube.

| Attempt | In Blender | In game |
| --- | --- | --- |
| Eyepiece moved out of `FRAME` into `SCOPE_HI` | looks right, 0 findings | back of the scope missing, spray of triangles |
| Optic left at the donor's position while the tube moved | looks right, 0 findings | pane of glass hanging in mid-air |
| Optic scaled up to fill the wider eyepiece | looks right, 0 findings | sight picture fills the whole screen when zoomed |
| Donor's eyepiece housing kept as shipped | looks right, 0 findings | housing engulfs the tube; no reticle when zoomed |

Every one of those exported clean, because none of them is a malformed file. Which is the reason for
the next section.

### Render the sight picture before you install

Most of the above cost a round trip through the game that was avoidable. The aim camera's position is
measurable, so the scope picture can be rendered in Blender directly:

1. Pose a character with the weapon's aim bank (below) and read the `Camera` bone in weapon space.
   On the Dragunov's `1stge_uppb_aimironcycle` that is `(0, −0.2884, +0.1311)`.
2. Put a Blender camera there, pointed along `+Y`, and render.
3. Do the same for the untouched donor and compare.

The donor works in game, so any difference between the two images is your bug — and it takes seconds
instead of a launch, a weapon purchase and a reload. The engulfing-housing defect above is glaring
in that render and invisible in every numeric check.

Every one of those exported clean. The validator has nothing to say about geometry that is in the
wrong part, or correct geometry the wrong size — it checks what the *format* allows, and all three
were legal files.

To measure the overlap on your own donor rather than assuming: take the `SCOPE_HI` bounding box and
count how many `FRAME` faces have their centre inside it. More than zero means overlay. Rendering the
`SCOPE_HI` objects on their own also helps — a 2,016-triangle object hiding inside a tube is
invisible in a wide shot.

### Step 4 — identify your own pieces, by looking

A model bought or downloaded rarely names anything useful. The VSS used here arrives as 52 mesh
primitives, every one of them called `defaultMaterial`.

Bounding-box arithmetic will get you close and will also quietly put the trigger on the magazine.
The reliable method is to **colour candidate groups and render them**: assign a flat colour per
group, render an orthographic side view, and look. Two or three passes settles it. On the VSS this
immediately separated the magazine from the trigger guard and the magazine catch — three pieces that
sit within a couple of centimetres of each other and belong to two different parts.

The pieces that are genuinely ambiguous are worth rendering one colour each, with a printed legend.
That is how the VSS's bolt carrier was told apart from the safety lever and the receiver side panel,
all of which are right-side details at similar heights.

### Step 5 — the rule that governs everything else

An imported object carries an `fc2_submesh` custom property naming which part of the pack it *is*.
Export walks the collection and **skips every mesh object that does not have one**:

```python
if obj.type != "MESH" or PROP_SUBMESH not in obj:
    continue
```

**So you must replace the geometry *inside* the imported object. Never delete the imported object and
put your own in its place** — the replacement has no `fc2_submesh`, export silently ignores it, and
you ship the donor's mesh wondering why nothing changed. The `part.unknown-object` rule exists to
catch exactly this, and it blocks the export.

In practice, per part:

1. Select your VSS geometry for that part **and** the imported part object, with the imported one
   active.
2. <kbd>Ctrl</kbd>+<kbd>J</kbd> to join — the custom properties of the active object survive.
3. Enter Edit Mode, delete the donor's original vertices, leave yours.

Or work in Edit Mode throughout and paste your geometry in. Either way the object identity is
preserved.

:::danger[Object-mode transforms are silently discarded]
Export writes vertex positions in the part's own space and **ignores the object's transform
entirely**. Moving, rotating or scaling a part in Object Mode changes nothing in the file, while
looking perfectly correct in the viewport.

**All positioning must happen in Edit Mode**, on the vertices themselves. The validator catches this
one, but do not rely on remembering to run it.
:::

### Step 6 — align to the hands, not to the origin

From [hand placement](#hand-placement-is-baked-into-the-animation): the hands are fixed by the
animation set and cannot be moved. So the gun comes to them.

In the **Animation** panel, press **Load Far Cry 2 Animation** and pick a bank — an idle or aim clip
is the right one to model against. The armature poses, the donor's parts move with it, and now you
can see where the hands actually are.

Position your VSS geometry so its grip, trigger and handguard land under those hands. This is
ordinary modelling work in Edit Mode: translate, rotate and scale the vertices until the gun sits in
the hands correctly.

#### Measure where the hands are, rather than inferring it

The weapon's pack carries only the weapon's rig, so the hands are not in it. Get them by posing a
character with the same bank:

```
jackall-cli fc2model export graphics/characters/mercenaries/merc_kit.xbg ^
    --game "C:\Games\Far Cry 2" ^
    --rig  graphics/characters/_common/pelvis_ref.skeleton ^
    --clip <the weapon's 1stge_uppb_aimcycle bank>
```

Import that, load the clip, and read `R Hand`, `L Hand` and `Camera` **in the weapon's own frame** —
the bank hangs a prop marker on the attach bone whose transform is the weapon's root, so measure
relative to that marker rather than to any bone.

Two things this reveals that guessing does not:

- **The weapon root is not the hand.** On the Dart Rifle the right hand sits 15.6 cm *behind* the
  origin. Assuming the origin was the grip put that gun 15.6 cm out and left the support hand short
  of the handguard entirely.
- **The attach bone changes with the clip.** The weapon hangs off `R Hand` in 49 of the Dart Rifle's
  banks and off **`Camera`** in the 10 aim banks — which is how aiming aligns the sight with the
  view, and why the scope's placement is worth as much care as the grip's. Note also that
  `aimcycle` is the shoulder-ready pose; the zoomed one is `aimironcycle`.

#### Scale from the hand span

**FC2 models are real-scale** — the Dragunov measures 1.231 m against a real SVD's 1.225 m — but do
not scale to real dimensions anyway. Scale so your **grip-to-handguard span matches the animation's
hand span**, because that is what decides whether both hands land on the gun. The VSS came out at
0.941 m that way, near its real 0.894 m.

Then place it with three anchors:

- **Fore and aft** — put your grip on `R Hand`, at its measured offset from the origin.
- **Height** — put your bore on the donor's. `FX_FIRE` gives it exactly; on the Dragunov the muzzle
  is 0.070 m above the origin.
- **Facing** — check which way the donor points before anything else. The Dart Rifle's muzzle is
  `+Y`; the VSS pointed `−Y` and needed a 180° turn about Z, which conveniently also swapped its
  left and right to match.

**The scope eyepiece is a fourth anchor, and it is not optional.** The aim clips were authored so the
eye looks through the donor's eyepiece, so the scope has to move to that spot — **in all three axes,
not just along the gun**. On the VSS the scope's rear opening was 13.4 cm too far back *and* 2.3 cm
too high; correcting only the first leaves the eye looking at the underside of your tube. Slide the
whole scope until its rear opening sits on the donor's optic assembly, then check the scope has not
sunk into the receiver on the way down.

**Do not resize the optics to match your tube.** It is the obvious next move and it is wrong. The
crosshair and lens are sized for **where the aim clips put the eye**, not for the diameter of the
tube around them. Scale them up to fill a wider eyepiece and you scale up the angle they subtend:
the sight picture swallows the entire screen when you zoom. The VSS's eyepiece is 6.3 cm across
against the Dart Rifle's 3.2 cm, and closing that gap by doubling the optic made it unusable.

A lens smaller than the tube it sits in is not a defect — real scopes recess theirs.

**Leave the whole `SCOPE_HI` assembly exactly as it shipped.** Move your tube to it. That assembly is
the sight picture and it is tuned to a camera you cannot see; every attempt to improve it here made
things worse, in a way that only showed up in game.

Where donor and source genuinely disagree, the animation wins.

### Step 7 — materials, channels and seams

Three constraints bite here. All three are silent failures rather than errors, and the third one is
the one that will waste your afternoon.

**A cluster draws with exactly one material — the object's first slot.** The exporter reads
`obj.data.materials[0]` and nothing else. Extra slots produce `material.assignment-ignored`. You
cannot create a material either, so retexturing means replacing the *textures the pack carries*, not
adding slots. Which is why the donor's material split (Step 3) decides how your model is divided.

**Fill every channel the donor's buffer declares — not every channel your model has.** The VSS
carries one UV set and no vertex colours. The Dart Rifle's buffer declares **two UV sets and a colour
array**. A channel the exporter cannot supply is **left alone rather than cleared**, so the previous,
now far too short array stays behind a grown vertex count. Nothing warns. It surfaces later as:

```
IndexError: list index out of range      # part.uvs1[loop.vertex_index], on re-import
```

After joining your geometry in, the donor's `UVMap1` and `Colour` layers exist but your half of the
mesh has no data in them — zeroed UVs sample one texel, and a zeroed colour layer can render the part
black or fully transparent. **Copy `UVMap` into `UVMap1`, and fill the colour layer** with the value
the donor used. The buffer's layout belongs to the donor, not to your model.

**A seam is a duplicated vertex.** The format stores UV, normal and colour per *vertex*, not per
corner, so where corners disagree the first one wins and the seam collapses. Two obvious approaches
both fail:

| Approach | What happens |
| --- | --- |
| Never merge — a vertex per corner | the buffer triples, and the decimator has nothing to collapse across |
| Merge everything by position | hard edges and UV seams collapse: **8,092** `normal.split` findings on one part |

The rule that works is to merge only where position, normal **and** UV all agree — which is what
"a seam is a duplicated vertex" means in practice. If you use **Merge by Distance**, know that it
merges on position alone; mark your sharp edges and UV seams first so they survive it.

And **keep your model's own normals**. If a rebuild drops custom split normals, Blender recomputes
corner normals from face geometry, every shared vertex ends up with corners that disagree, and
`normal.split` fires across the whole model. Carrying the source normals across took the worked
example from 8,092 findings to zero.

### Step 8 — check before you export

**Far Cry 2 ▸ Check** runs every rule against what an export would actually write, and lists findings
with a **Select** button that jumps to the offending object, vertex or material.

An **ERROR** blocks the export. Everything else warns, because retail itself breaks plenty of
guidelines and refusing those would make the add-on wrong about the game it is for. The gate keeping
this honest is that **every rule is silent on models exactly as they shipped** — a rule that fires on
retail is a wrong rule.

The findings that matter most while splitting a model:

| Code | Blocks? | Means |
| --- | --- | --- |
| `part.unknown-object` | yes | an object export would skip — you replaced instead of edited |
| `part.duplicate` | yes | two objects claiming one part |
| `cluster.zero-triangles` | yes | a part draws nothing; no shipped cluster does |
| `cluster.too-many-triangles` | yes | over 21,845 in one part |
| `buffer.too-many-vertices` | yes | over 65,535 across the LOD |
| `uv.missing` / `uv.unwrapped` | yes | no UV layer, or every UV at the origin |
| `skin.unweighted-vertex` | yes | only on skinned parts; weapons are rigid |
| `material.assignment-ignored` | no | a material slot the file will ignore |
| `channel.*` | no | channels the format does not carry — metalness, roughness, emission |
| `texture.too-large` / `texture.non-power-of-two` | no | outside what all 4,283 shipped textures do |

The check is as slow as an export and regenerates tangents, so it is a button rather than something
that runs continuously.

### Step 9 — export and apply

**File ▸ Export ▸ Far Cry 2 Model Pack (.fc2model)** writes the collection's parts back into the pack
they came from. **The pack is edited, not rebuilt**: nodes, materials, bone palettes, LODs you never
imported and every document you did not touch all survive, and only an entry that actually changed
grows an `origin_sha256`.

That is worth internalising — it means an untouched export returns the shipped game file byte for
byte, so anything that *did* change is genuinely your edit and not exporter drift.

To eyeball the result without the game:

```
tools\BlenderFC2\open_model.cmd vss.fc2model 0 aim
```

A part can sit in the wrong place and still pass every numeric check, so look at it.

Then get it into the game. Three commands, and none of them needs the GUI:

```
jackall-cli fc2model extract vss.fc2model -o mylayer
jackall-cli mod build --game "C:\Games\Far Cry 2" --layer mylayer
jackall-cli mod restore --game "C:\Games\Far Cry 2"     # undoes it
```

`extract` writes the changed files under a reserved **`mods\`** folder — that wrapper *is* the layer
contract, and anything outside it is ignored, so do not flatten it away. JackAll.App does the same
from **Apply .fc2model**.

`build` recompiles `patch.dat` from the vanilla backup plus your layers, so building twice produces
identical bytes and `restore` genuinely removes everything. `common.dat`, `worlds.dat` and the rest
are opened read-only and never written.

Expect the build to report your file as **added** rather than **overridden**. The weapon meshes live
in `worlds.dat`; `patch.dat` wins over it at load time, so adding an entry there is what a
replacement looks like.

**Verify what the game will actually load**, rather than trusting the build line — read the file back
out of the patch and compare it:

```
jackall-cli archive extract "C:\Games\Far Cry 2\Data_Win32\patch.fat" --names --filter dart_rifle -o check
```

On the worked example that came back byte-identical to the exported file, at 980,084 bytes against
the shipped 592,950.

### Seeing it in game

The Dart Rifle is a weapon-bazaar purchase behind an unlock, which is a slow way to look at a mesh.
The launch option below unlocks everything available in the current map, so it can simply be bought:

```
farcry2.exe -GameProfile_AllWeaponsUnlock 1
```

Four things are worth checking, and they are the four the geometry work was aiming at:

1. **Does it sit in the hands** — both of them, in idle and while aiming.
2. **Does the magazine leave cleanly on reload** — this is `CLIP`, the bone that travels furthest.
3. **Does the bolt cycle** — `SLIDE`, and the weapon-break animation exercises it hardest.
4. **Does the scope aim** — whether the eye lands on the eyepiece you moved to the donor's.

The world pickup and the third-person model draw from the same `.xbg`, so they change too.

### Step 10 — the other LODs

You imported LOD 0. The weapon has five tiers, and LOD depth is **per part** — the Dart Rifle carries
`FRAME_LOD0..4`, `CLIP_LOD0..3`, `SLIDE_LOD0..3`, `SCOPE_HI_LOD0`. Coarser tiers drop the smaller
parts: `SCOPE_HI` exists only at LOD0, and by LOD4 only `FRAME` survives.

Repeat steps 3–9 with **LOD** set to 1, 2, 3, 4, each time supplying a decimated version of the same
geometry. Match the donor's triangle counts tier for tier rather than inventing a budget — those are
what the authored LOD distances were balanced against.

:::danger[The cluster index is not stable between tiers]
LOD0–3 give `FRAME` three clusters (lens, metal, wood); **LOD4 gives it two**. So slot 1 is metal at
LOD0 and *wood* at LOD4.

Anything that remembers "slot 1 is the receiver" transplants the stock onto the receiver at the
coarsest tier. **Identify each cluster by its material every time you change LOD**, and re-read the
list rather than carrying it over.
:::

Two practical notes from doing it:

- **Export chains.** An export edits the pack it came from, so each tier must be imported from the
  pack the previous tier wrote. Start each one from the donor and you will hand back a file
  containing only the last tier's work.
- **Decimation has a floor.** Blender will not collapse below roughly four faces per disconnected
  shell. The VSS frame is 40-odd shells, so LOD4 came out at 311 triangles against a target of 57.
  At that draw distance it costs a few pixels; merging the shells before decimating is the fix if you
  care.

:::note[Do not use **Add as New Part** here]
That operator exists for giving a model a part it never shipped with, and an added part exists only
at the LOD it was added to. A weapon *replacement* fills parts that already exist at every tier, so
it is the wrong tool — reach for it only if your weapon genuinely needs a fifth part the donor has
no slot for, and then accept that it disappears at distance.
:::

### What the worked example came out at

:::info[Done and validated]
The VSS geometry is transplanted across all five LOD tiers. **LOD0 passes Check with zero findings**,
and the pack round-trips: exported, re-imported, bounds identical.
:::

| Tier | FRAME metal | FRAME wood | CLIP | SLIDE | SCOPE_HI | Findings |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| LOD0 | 15,027 | 2,252 | 318 | 556 | 632 | **0** |
| LOD1 | 2,369 | 423 | 12 | 272 | — | 4 warnings |
| LOD2 | 1,102 | 264 | 12 | 76 | — | 4 warnings |
| LOD3 | 311 | 129 | 10 | 40 | — | 4 warnings |
| LOD4 | 311 | 41 | — | — | — | 4 warnings |

The LOD1–4 warnings are around 20 `normal.split` and 10 `uv.split` per tier, produced by the
decimator merging vertices that disagreed. That is inherent to decimation and they are warnings, not
errors.

For scale, the same job done wrong: welding by position alone produced **8,092** `normal.split`
findings on `FRAME` at LOD0 by itself, and not welding at all stranded **28,676** loose vertices at
LOD4. Both exported without an error.

### What is left after the mesh

- **PBR → legacy conversion.** FC2's Generic shader reads diffuse / normal / specular; a modern
  source model ships metallic-roughness. This is a conversion, not a copy. Roughness is the lossy
  direction: Blender has no `SpecularPower` and Dunia has no roughness.
- **Textures to `.xbt`.** All 4,283 shipped graphics textures are power-of-two and none exceeds
  2048; the codecs are DXT1, DXT5 and DXT3. Nothing enforces this — the block compressor pads an odd
  size rather than refusing — so it warns rather than blocks, but retail is the safe target.

## Known constraints, collected

Things that are settled, so nobody re-derives them:

- **The hands are fixed** by `iAnimationValue`. Fit the gun to them — and the scope eyepiece too,
  because the aim clips were authored for the donor's.
- **Bone ids, not names**, bind clips to weapon parts. Changing animation set without changing
  skeleton silently animates the wrong parts.
- **`FX_Casing` ≠ `FX_CASING`.** Hashes are exact-case throughout the engine.
- **A part is one object per cluster**, each with its own material, and the cluster count and order
  change between LOD tiers. Identify clusters by material, never by index.
- **`SCOPE_HI` is the eyepiece, not the scope.** The tube is `FRAME`.
- **Fill every channel the donor's buffer declares**, not the ones your model has. An unsupplied
  channel is left stale rather than cleared, and nothing warns.
- **A seam is a duplicated vertex.** Merge only where position, normal and UV all agree.
- **Do not scale to real-world size.** Engine units are not metres — the Dart Rifle is 1.148 m,
  28% over a real VSS. Match the donor's envelope.
- **A part can be added but not removed**, and an added part exists only at the LOD it was added to.
- **Collision keeps the donor's shape** — `.hkx` is not parsed by any tool here.
- **Both the base archetype and its `.Multi` twin** need every stat edit.

## Open questions

- Whether to repoint `fileSkeleton` at the Dragunov's rig or author a VSS rig with matching bone
  order. Leaning toward authoring, because the FX sockets then land on VSS geometry.
- Whether the Dragunov's magazine-drop trajectory reads acceptably with a VSS magazine modelled into
  it, or whether the mismatch is visible enough to want a different donor animation set.
- Whether the `depload` `CAnimationPackageResource` needs to move from `dart_rifle` to `dragunov`, or
  whether `sPartName` alone is sufficient. The parents array is CRC32-sorted and must be re-sorted
  after any insert — see [depload](../file-formats/depload.md).
