---
sidebar_position: 6
---

# Replacing an existing weapon

Taking a weapon the game already ships and making it something else: a new mesh, new textures, new
stats, a new name and a new icon, riding on the animation set and the inventory slot the original
already had. [Adding a new weapon](./adding-a-weapon.md) covers standing up a weapon that did not
exist before. This page is the cheaper and far more common job, and it needs no free
`iAnimationValue` slot, no `movemgr.bin` writer and no new `depload` entry — the weapon you replace
already has all of that.

:::tip[This procedure is a shipped mod]
`mods/vss-vintorez` in this repo — a **VSS Vintorez replacing the Dart Rifle** — is in the game and
working: correct model, correct animation set, semi-automatic, ten rounds, and a scope that aims. It
runs through this page as the worked example. Everything here is measured against a running game or
traced in the binary, except [the shot sound](#step-9--the-shot-sound), which is replaced and
verified on disk but not yet heard in game.

The art the finished mesh then wears is a page of its own: [texturing a replaced
weapon](./texturing-a-weapon.md).
:::

## The shape of the job

A replacement is two pieces of work that fail in completely different ways:

- **The archetype** — what the engine reads to decide what this weapon *is*. Data, staged into the
  world's entity library.
- **The mesh** — what that archetype then draws. Geometry, built in Blender inside the donor's own
  model pack.

**Do the archetype first, and prove it with the donor's own unmodified mesh.** When the weapon you
are replacing already behaves exactly like the donor, every field is right, and the mesh becomes a
pure geometry problem with no wiring left to doubt. Change both halves at once and every symptom has
a candidate cause on each side, which is the slowest way to debug any of this.

The steps, in the order to do them:

| | Step | Produces |
| --- | --- | --- |
| 0 | [Build the tools](#step-0--build-the-tools) | `jackall-cli`, the Blender add-on |
| 1 | [Choose the donor](#step-1--choose-the-donor) | which weapon you replace, and whose animations you use |
| 2 | [Stand up the layer](#step-2--stand-up-the-layer-and-prove-it-reaches-the-game) | an edit that provably reaches the game |
| 3 | [The weapon archetype](#step-3--the-weapon-archetype) | the right parts, skeleton and animation set |
| 4 | [The stats archetype](#step-4--the-stats-archetype) | fire mode, ammo, zoom, reliability |
| 5 | [The mesh](#step-5--the-mesh) | your model, in the donor's parts, at every LOD tier |
| 6 | [The bounding boxes](#step-6--regenerate-the-baked-bounding-boxes) | culling that matches the new mesh |
| 7 | [The weapon on the ground](#step-7--the-weapon-on-the-ground) | a correct pickup and dropped model |
| 8 | [Name and icons](#step-8--the-name-and-the-icons) | the bazaar, the HUD, eleven languages |
| 9 | [The shot sound](#step-9--the-shot-sound) | first- and third-person banks |
| 10 | [Check it in game](#step-10--check-it-in-game) | the five things that can still be wrong |

## Step 0 — build the tools

Both tools are built from source in this repo.

```
cd tools\JackAll && dotnet build      # jackall-cli lands in src\JackAll.Cli\bin\Debug\net10.0\
.\tools\BlenderFC2\build.ps1
```

Install the resulting `farcry2_formats-<version>.zip` through **Edit ▸ Preferences ▸ Get Extensions
▸ Install from Disk**. It is a Blender 4.2+ extension.

:::warning[Check you are not running a stale binary]
Any prebuilt `jackall-cli.exe` lying around in `tools/JackAll/publish/` may predate the `fc2model`
work. If `jackall-cli fc2model --help` says *Unknown command*, publish again.
:::

## Step 1 — choose the donor

The donor decides more than the silhouette. It fixes, and you inherit whether you want to or not:

| What you inherit | Where it comes from | Changeable? |
| --- | --- | --- |
| Hand and arm motion | the `.mab` character clip | **No** — see [aligning to the hands](#h-align-to-the-hands-not-to-the-origin) |
| Which parts animate, and how | the `.mab` weapon clip, addressed by **bone id** | Only by swapping the whole set |
| Part names | `CGraphicComponent`, CRC32 **exact-case** | No |
| Collision shape | the `.hkx`, which no tool parses | No |
| Inventory slot, shop entry, HUD icon | `gamemodesconfig.xml`, `sName` | Yes, but each is its own edit |

So pick for **behaviour first, silhouette second**. A donor whose animation set already does what
your weapon does is worth more than one that merely looks similar.

### The donor is two roles, and they can be two weapons

The weapon you **replace** decides the inventory slot, the shop entry and how many archetypes you
have to maintain. The weapon whose **animation set** you adopt decides how the gun moves. They do
not have to be the same weapon.

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
weapon with a detachable box magazine that reload is simply wrong, and no stat edit fixes it. So the
mesh is built in the **Dragunov's** pack, whose part names match the skeleton its clips address, and
written out to the **Dart Rifle's** `.xbg` path.

Two donors also means two sets of alignment constants, and they disagree in every one:

| | Dart Rifle clips | Dragunov clips |
| --- | ---: | ---: |
| `R Hand` y | −0.1565 | −0.0729 |
| `L Hand` y | +0.2143 | +0.2630 |
| hand span (sets scale) | 0.3708 | 0.3359 |
| sight-line z | +0.1579 | +0.1300 |

### The fire mode comes with the animation set

Clip sets are not interchangeable across fire modes. Diffing the Dart Rifle's `sp389` set (43 clips)
against the Dragunov's `spdra` (40) leaves three clips genuinely absent from the Dragunov, and they
are the telling ones:

```
1stge_uppb_jamcycle2unjamfail_+000fw
3rdge_uppb_prepareweaponiron_+000fw
3rdge_uppb_prepareweaponreg_+000fw
```

`prepareweapon` **is the bolt-cycle animation**. The Dart Rifle has it because `selFireRateMode = 2`
(PrepareShot); the Dragunov does not, because it is semi-automatic. So moving to the semi-automatic
`iAnimationValue` and dropping to `selFireRateMode = 0` are two halves of one change — the stat
block stops asking for a bolt cycle at the same moment the animation set stops offering one. Do one
without the other and the weapon asks for a clip that is not there.

Most of the rest of the diff is naming drift for the same action (`shootingcycle` vs `shootcycle`,
`shootregular` vs `shootreg`, `reload_nodir` vs `reload_+000fw`).

### Count the donor's archetypes before you commit

Every variant is a separate archetype you either edit or break:

| Weapon | `WeaponProperties.*` | `weapons.*` | Total |
| --- | ---: | ---: | ---: |
| **Dart Rifle** | 2 | 2 | **4** |
| AS50 | 3 | 3 | 6 |
| M1903 | 3 | 3 | 6 |
| Dragunov | 6 | 6 | **12** |

The Dragunov's twelve include `.AI`, `.Dragunov_Merc`, `.Mikes_Rusty` and `.Persistent`.
`Mikes_Rusty` is a story-unique weapon, so replacing the Dragunov means either breaking it or
maintaining a variant you never wanted. `.Multi` is the multiplayer twin and single-player does not
need it.

A weapon with no `_Merc` variant has a second advantage: **no NPC spawns with it**, so third-person
rig mismatches cannot bite you, and the new model appearing in your hands is unambiguous proof the
change worked rather than ambient noise.

### A scope can be preserved, not added

All four scoped rifles — Dart Rifle, Dragunov, AS50, M1903 — ship a `SCOPE_HI` mesh part and
`CFCXWeapon.bUseHiResScope = True`. Replacing one scoped rifle with another keeps the scope, on one
condition: your mesh has to supply a complete `SCOPE_HI` sight picture, because [that part is the
entire zoomed view](#e-scope_hi-is-drawn-instead-of-the-rest-of-the-gun).

Adding a scope to a weapon that has none is **not achievable with the current toolchain**. It needs
a new `SCOPE_HI` part, and `tools/BlenderFC2` 0.1.0 can add a part only to a single LOD — the scope
would vanish as soon as the weapon dropped to LOD1.

## Step 2 — stand up the layer, and prove it reaches the game

Before any real edit, get one trivial change into a running game. Everything downstream is diagnosed
against this working baseline.

### The container the game actually reads

:::danger[Get this wrong and nothing else on this page matters]
An install ships **many** entity libraries, and most of them contain the weapon archetypes without
ever being read. An edit staged into the wrong one is silently dead — it builds, it lints clean, and
it does nothing.
:::

```
worlds\world1\generated\entitylibrary.fcb     act 1, Leboa-Sako
worlds\world2\generated\entitylibrary.fcb     act 2, Bowa-Seko
```

The **suffix-less** library, and it is **per world** — patch `world1` and `world2` both.

`CXGame::LoadArchetypes` picks the base library on a flag whose meaning is not known:

```
if (flag at +0xC4 == 0)  load  \entitylibrary.fcb          1,419 archetypes
else                     load  \entitylibrary_full.fcb     5,566, a strict superset
                         load  generated\EntityLibraryPatchOverride.fcb
                         then a loop over further libraries (DLC)
```

The base is either/or, never both. Three containers that look like the answer and are not:

- **`entitylibrary_full.fcb`** is the client-only library, absent from the dedicated server binary.
  That makes it not a *server* library; it does not make it what the campaign reads, and it is not.
- **`EntityLibraryPatchOverride.fcb`** does not exist in every edition. It is absent from the GOG
  Fortune's Edition, whose `patch.fat` holds 215 entries with exactly one entity library. Its name
  is in the hashlist, so a present file would have been resolved — it is genuinely not there.
- **`worlds\tmpla\generated\entitylibrary.fcb`** is the retail patch's only entity-library entry,
  which looks like strong evidence that `tmpla` is the campaign world. Whatever `tmpla` is for, the
  campaign does not read it.

:::warning[A clean `mod lint` is not evidence an edit is live]
`jackall-cli mod lint` reports *"No dead archetype edits — every edited archetype is the copy the
game reads"* for containers the game never opens. It models the override chain *within* the
containers you name and is silent about whether the game opens them.
:::

:::tip[Settling "which file is live", for any format]
Stage the **same edit into every candidate container at once, each carrying a different value**, and
launch once — the value you observe names the winner. That is how the list above was settled: six
containers, six magazine sizes, one launch, `world2` on the HUD. It is cheaper than a bisection and
cannot be fooled by a plausible-sounding inference. Pick something visible without ambiguity — a
magazine size or an ammo count, not a name some other subsystem might supply.
:::

### Stage the edit as fragments

Decode the library, edit, and stage **one fragment per archetype**:

```
jackall-cli fcb decode worlds\world1\generated\entitylibrary.fcb
```

That writes an index plus ~42 group files (`41_WeaponProperties.xml`, `42_weapons.xml`, …). Edit the
archetype inside its group file, and **scope the edit to the `Entity` node whose `hidName` matches**
rather than to a line range — neighbouring archetypes carry the same field names a few lines away,
and a window will silently catch the wrong gun.

A fragment's id is the dotted `hidName` mapped onto a path, and the fragment itself is the
`EntityPrototype` node (type hash `256A1FF9`). So the layer holds:

```
mods\worlds\world1\generated\entitylibrary.fcb\WeaponProperties\Special\Dart_Rifle.xml
mods\worlds\world1\generated\entitylibrary.fcb\weapons\Special\Dart_Rifle.xml
mods\worlds\world2\generated\entitylibrary.fcb\WeaponProperties\Special\Dart_Rifle.xml
mods\worlds\world2\generated\entitylibrary.fcb\weapons\Special\Dart_Rifle.xml
```

`jackall-cli mod inspect` confirms it read them as fragments rather than as loose files.

:::danger[Write fragments as UTF-8 **without** a BOM]
`mod build` refuses a fragment that starts with one, and the message names no file:

```
at the root level is invalid. Line 1, position 1.
```

The trap is that the file looks fine everywhere else — .NET's `[xml]`, browsers and editors all skip
a BOM silently, so a fragment can validate as XML and still be rejected. **Windows PowerShell 5.1's
`Set-Content -Encoding UTF8` writes one**, as do `>`, `>>` and `Out-File`. Use:

```powershell
[IO.File]::WriteAllLines($path, $lines, (New-Object Text.UTF8Encoding($false)))
```

`sed -i` and shell redirection do not add one. To check: `head -c 3 fragment.xml | xxd -p` gives
`efbbbf` for a BOM and `202020` for the leading spaces of a clean fragment.
:::

:::note[There is no round trip through the whole container]
`fcb decode` splits an entity library into group files; `fcb encode` refuses multi-file XML.
Fragments are the route. Applying them re-encodes the container, which inflates it — `patch.dat`
went 9.9 → 49.7 MB once both worlds were covered. See `docs/design/fcb-deep-fragments.md`.
:::

### Build, verify, restore

```
jackall-cli mod build   --game "C:\Games\Far Cry 2" --layer mylayer
jackall-cli mod restore --game "C:\Games\Far Cry 2"       # undoes it
```

`build` recompiles `patch.dat` from the vanilla backup plus your layers, so building twice produces
identical bytes and `restore` genuinely removes everything. `common.dat`, `worlds.dat` and the rest
are opened read-only and never written.

Expect the build to report your file as **added** rather than overridden. The weapon meshes and
`world2` live in `worlds.dat`, and `patch.dat` wins over it at load time, so adding an entry there
is what a replacement looks like.

**Nothing in the build output tells you an archetype edit is correct.** Read the container the game
will load back out of the built archive and look at it:

```
jackall-cli archive extract "C:\Games\Far Cry 2\Data_Win32\patch.fat" ^
    --names --filter entitylibrary -o check
jackall-cli fcb decode check\worlds\world1\generated\entitylibrary.fcb
```

Then grep the decoded group file for the value you set. The same check works for the mesh later
(`--filter dart_rifle`), and it should come back byte-identical to what you exported.

### The canary

**A display-name change is the cheapest possible proof.** Set `sDisplayName` on
`WeaponProperties.<slot>.<Weapon>` to something unmistakable, build, and look at the HUD. If it does
not show, the container is wrong and no other symptom you observe means anything.

:::note[An already-owned weapon keeps its old archetype]
Loading a save where the weapon was already in inventory gives you the **new mesh on the old
archetype**. Buy or pick the weapon up again after installing and it binds correctly. Worth knowing
before you conclude a build failed.
:::

## Step 3 — the weapon archetype

`weapons.<slot>.<Weapon>` is the entity: its parts, its skeleton, its animation set, its collision.

:::danger[Copy the donor's entire archetype. Do not edit yours in place.]
Take the **whole** `weapons.*` node from the weapon whose mesh and animations you are adopting, and
change only its identity and the model path. That carries the part list, the baked descriptor, the
skeleton, `sPartName`, `iAnimationValue` and `bUseHiResScope` across in one piece, so no field can
be forgotten.
:::

The identity is three fields — `Name`, `hidName` and `disEntityId`, taken from the archetype you are
**replacing**, not the donor's. Everything else stays as the donor wrote it.

### Why it has to be a copy: the archetype names the parts to draw

**The engine does not draw whatever the `.xbg` contains.** The archetype carries a baked description
of the model, and only what *it* names is drawn. Two parallel lists live inside it:

1. A **resource entry per part** — `hidIndex`, `objModel` (CRC32 of the `.xbg` path), `hidMeshName`,
   `hidNodeName` (CRC32 of the part name) and `hidNodeNameLOD0` (CRC32 of `<PART>_LOD0`).
2. A **baked `GraphicComponent`** inside `CFileDescriptorComponent.hidDescriptor`, an Rml blob: one
   `<object index meshName boneName bboxMin bboxMax>` per part, a `<resource fileName bbox>`, a
   `RigidPhysComponent` naming the `.hkx`, and **a full `<skeleton>` copy with every bone's position
   and rotation**.

Change the three animation fields on the Dart Rifle's own archetype and all of that still describes
a dart rifle:

| | Dart Rifle, animation fields changed | Dragunov |
| --- | --- | --- |
| `fileSkeleton` / `sPartName` / `iAnimationValue` | correct | same |
| baked objects | FRAME, CLIP, SLIDE, SCOPE_HI | CLIP, FRAME, SCOPE_HI, SLIDE, **ACCESSORY02** |
| baked skeleton | **the Dart Rifle's own bones**, incl. `FX_CASING` and `Dart_Rifle_DART` | the Dragunov's |

`ACCESSORY02` would never be drawn — the suppressor and handguard missing in every build, with no
amount of mesh work able to fix it — and `SCOPE_HI`, though listed, would carry the Dart Rifle's
bone position and a 3 cm bounding box, so the zoomed view could not work.

Two details for when you do touch the lists:

- **`hidResourceCount` counts resource *files*, not parts.** It is 4 on both weapons despite the
  Dragunov having five parts. Do not touch it.
- **Validate any CRC32 routine against hashes already in the file** — `FRAME`, `FRAME_LOD0`, `CLIP`,
  `SLIDE`, `SCOPE_HI`, `SCOPE_HI_LOD0` — before trusting it. Ones you may need that are not already
  there: `ACCESSORY02` = `D8D2CBA5`, `ACCESSORY02_LOD0` = `BACEEF7A`.

### The animation set is three fields and one hash

Copying the donor's archetype brings the first three across already. They are listed because they
are what "changing the animation set" means, and because the fourth line is on a different archetype
and easy to miss:

| Field | On | Value |
| --- | --- | --- |
| `iAnimationValue` | `CFCXWeapon` | the animation set's `EquippedWeapon` index (Dragunov `23`, Dart Rifle `39`) |
| `sPartName` | `CSimpleAnimationComponent` | the animation key `depload` and the MOVE graph bind clips by (`dragunov`) |
| `fileSkeleton` | `CSimpleAnimationComponent` | the rig's hash (`1BF80FFB`) |
| `BulletCaseBone` + twin | `WeaponProperties` | CRC32 of the ejection bone **on the new rig** |

`BulletCaseBone` is the one that slips through. The Dart Rifle's rig has `FX_CASING`, the Dragunov's
has `FX_Casing`; hashing is exact-case, so those are different bones and the field has to be
recomputed (`D431B68E` → `2365743E`).

`iAnimationValue` is the integer written into the `EquippedWeapon` channel that the animation graph
in [`movemgr.bin`](../file-formats/move.md) tests.

### Bone ids, not names

:::danger[The skeleton and the animation set are one choice]
A `.mab` bank holds one clip per skeleton in the animation — the character first, then the weapon —
and **bones are addressed by their `.skeleton` bone id, carrying no names of their own.** Change
`iAnimationValue` without changing `fileSkeleton` and the clips drive whatever bone happens to sit
at that id.
:::

The two weapons order their bones differently:

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

Only id 0 and id 4 agree. `1stge_uppb_reload_+000fw_spdra_i1.mab` holds:

```
clip[0]  bones 0…101         the character — pelvis_ref.skeleton, arms and fingers included
clip[1]  bones 0,1,2,3,4,6   the weapon — root, CLIP, FRAME, FX_Casing, FX_FIRE, SLIDE
```

Played against the dart rifle's rig, that reload would drive `FRAME` — the entire gun body — along
the path meant for the magazine, and reference id 6 which does not exist there.

So your mesh's part names must match the bone names in the skeleton `fileSkeleton` points at, **byte
for byte**: `CLIP`, `FRAME`, `FX_Casing`, `FX_FIRE`, `SCOPE_HI`, `SLIDE`, `ACCESSORY02` for the
Dragunov.

:::note[Authoring your own rig is possible, at a new path]
`.skeleton` round-trips byte-exactly on all 81 shipped files, so you can author a rig that keeps the
donor's bone order and names but carries your weapon's transforms — clip ids then line up by
construction, with `FX_FIRE` at your muzzle. **Give it a new path and point `fileSkeleton` at
that.** Do not rewrite the donor's file in place: every other archetype naming it, including the MP
maps, still reads it, and a rig with different transforms handed to clips that address bones by id
is the trap above.
:::

### Moving an FX socket without touching the rig

`FX_FIRE` and `FX_CASING` exist twice: as bones in the `.skeleton`, and as `<bone>` entries in the
archetype's own baked `<skeleton>`. **Editing the baked copy moves the muzzle flash and changes
nothing else** — which is the route to take, given the rig is shared.

Place it against the **donor's geometry**, not at your muzzle tip: the Dragunov's socket sits 7.3 cm
behind its muzzle, at the base of the flash hider, with its z matching the bore to within 0.1 mm.
Preserving that offset cannot leave the flash hanging in front of the gun.

### Clips at paths the game does not ship

`sPartName` alone resolves the animation package — no `depload` edit — **as long as every clip you
play is one the game already lists.** That holds because the donor ships in the same worlds; a donor
from a world your weapon does not appear in may not be so forgiving.

A clip at a path the game never shipped **does not load**, and the weapon enters the state and stays
there — a lock-up mid-reload. Two ways out:

- **Reuse a path the game already ships** — one of the replaced weapon's own. `move clips` lists
  what an index plays and flags every clip another weapon plays too, so a clip *absent* from the
  `--shared-only` list is exclusive to your weapon and safe to overwrite. No `depload` edit, but it
  needs a spare real path per clip, so it does not scale.
- **Register the new path** with `jackall-cli depload add`, staged as a layer fragment. The package
  is the one your **`sPartName`** names, not the weapon whose slot you took: the VSS sets
  `sPartName = dragunov`, so its clips go under `dragunov`; filing them under `dart_rifle` is
  accepted and does nothing. See
  [depload](../file-formats/depload.md#animations-are-not-like-textures).

The MOVE graph then has to point at the new clip too. `move repoint` rewrites only the sites the
weapon governs and reports shared ones instead of touching them; stage the result as **per-state
fragments** rather than as the whole graph:

```
jackall-cli move clips     movemgr.bin --weapon 39 --shared-only
jackall-cli move repoint   movemgr.bin out.bin --weapon 39 --map vss.tsv
jackall-cli move fragments out.bin --base movemgr.bin --out layer
jackall-cli move assemble  movemgr.bin layer --expect out.bin
```

Whole-file copies of a 1.8 MB graph silently overwrite each other between mods; per-state fragments
named for the weapon index cannot collide.

## Step 4 — the stats archetype

`WeaponProperties.<slot>.<Weapon>` is edited field by field, in **each world's** library. The
`.Multi` twin is multiplayer and does not need any of it.

Write **both halves** of every hash-backed field — the string and its exact-case CRC32 — or the
field keeps its old meaning. The `sel*` enums are self-describing in the decoded file, each followed
by its own value list, so no index has to be guessed.

The values that turn a bolt-action silent sniper into a semi-automatic suppressed marksman rifle:

| Field | Dart Rifle ships | VSS wants | Why |
| --- | --- | --- | --- |
| `selFireRateMode` | `2` (PrepareShot) | `0` (SingleShot) | 2 is the bolt-cycle mode |
| `iAmmoInClip` | `1` | `10` | box magazine |
| `iFireRate` | `120` | `85` | RPM — take the donor's measured value, not a guess |
| `iMaxAmmo{Casual,…}` | `9/4/3/2` | `40/20/20/10` | otherwise a 10-round magazine has a 9-round reserve |
| ammo string + `ammoAmmoType` | `darts` / `FC2096BC` | `sniperrifle` / `7D6BD5F2` | **both halves**, or it still draws from the dart pool |
| `BulletCaseBone` + twin | `FX_CASING` / `D431B68E` | `FX_Casing` / `2365743E` | the new rig spells it differently |
| `fIronsightFOV` | `0.3` | `0.28` | the zoom framing — see below |
| `fUnjamTime` | `4.4` | `2.5` | how long clearing a jam takes |
| `nForcedFailure{Min,Max}*` | `0` on every difficulty | the Dragunov's `2/2, 1/1, 0/1, 0/0` | the Dart Rifle never fails at all — see [jamming and breaking](#jamming-and-breaking) |
| `sDisplayName` | `Dart` | `VSS Vintorez` | a literal string, **not** a localization id |
| `bIsSilent` | `True` | `True` | already correct |
| `selWeaponClass` | `5` (Sniper) | `5` | already correct |
| `selCategory` | `3` (Special) | `3` | already correct |

:::warning[Whichever weapon's `SCOPE_HI` you draw, take its `fIronsightFOV` too]
`fIronsightFOV` is the angle the zoomed view is framed at, so it decides how much of the screen the
`SCOPE_HI` assembly fills — and it lives on `WeaponProperties`, not on the mesh. Keeping the
replaced weapon's `0.3` while drawing the donor's optic leaves the assembly visibly small with empty
screen all round it, with the eye position and the geometry both already correct. They are one
setting in two files.
:::

:::danger[Do not copy a lethal rifle's stims wholesale]
`MuzzleStims.fRadius` is what decides how far the AI hears the shot: **2.5 m on the Dart Rifle
against 150 m on the Dragunov**, with `ImpactStims.fRadius` 2 against 6. That, not `bIsSilent`
alone, is the suppression. Lift damage numbers from a loud weapon and you can take its audibility
with them without noticing until a compound turns on you.
:::

:::note[Read your donor's lethality rather than its name]
The Dart Rifle is not a tranquilizer, whatever the name suggests. `selFireStrategy` is `0` =
`Bullet`, the same as the Dragunov's — no dart entity is spawned, and nothing in either archetype
references `props.Props.Dart`. `selHitLocation_Torso_Severity` and `_Limb_Severity` are both `4` =
**`Kill`** against the Dragunov's `3` and `2`, and `bSingleHitHealthFailure` is `True`, so it is set
*more* lethal per hit than the rifle you would copy from. What is low is `Stim_ImpactDamage.nLevel`,
5 against 25, and `fPhysImpulse`, 10 against 60 — it kills a soft target, does little to a tough
one, and barely moves a body.
:::

### Jamming and breaking

These are two systems, in two archetypes, and neither lives where the stat block suggests.
`WeaponProperties` carries `fForcedReliability`, `fInitialJamCounter`, `selJamType`, `bIsBreakable`
and eight `nForcedFailure*` fields; none of them is the main dial.

**Jamming is per reload**, and it lives on the **weapon** archetype in `ReliabilityLevelsData` —
four condition levels, each with its own probability and recoil penalty:

```
                          High   Medium   Low   Failure
fJamProbabilityPerReload    0      0.04   0.08    0.16
fHorizontalRecoilPerShot  0.5      0.6     0.7     0.8
fVerticalRecoilPerShot      1      1.25    1.5    1.75
```

**It is exactly zero at `High`.** A pristine weapon cannot jam at any number of rounds. To force one
for testing, set `fJamProbabilityPerReload` to 1 at every level and reload.

**Breaking is a plain counter** on `WeaponProperties`: `iClipsForSelfDestruct`, 20 on a normal
weapon. At a ten-round magazine that is 200 rounds, so a weapon will not come apart during casual
testing.

The whole recipe for a decrepit weapon is two fields.
`WeaponProperties.Primary.Dragunov.Mikes_Rusty` is the story weapon Ubisoft authored to be falling
apart, and diffed against the ordinary Dragunov it changes:

| | normal | Mike's rusty |
| --- | ---: | ---: |
| `iClipsForSelfDestruct` | 20 | **2** |
| `fForcedReliability` | 0 | **−10** |

Everything else — `nForcedFailure*`, `bIsBreakable`, `selJamType`, `fInitialJamCounter` and the
whole `ReliabilityLevelsData` block — is **identical** between a pristine weapon and a decrepit one.

:::note[`nForcedFailure*` is not understood — copy your donor's]
The Dart Rifle carries `0` on all four difficulties where the Dragunov carries 2/2, 1/1, 0/1, 0/0.
Raising the Dart Rifle's alone produced no failures at all until `fJamProbabilityPerReload` changed,
so what the fields govern is still open. Take the values from the weapon whose reliability you want
rather than reasoning about them.
:::

## Step 5 — the mesh

Blender never touches `.xbg`. JackAll owns every byte layout and hands Blender a `.fc2model`
**pack** — JSON with flat float arrays, materials as JSON, textures as PNG. You import the donor's
pack, replace the geometry inside it, and export the same pack back.

### a. Export the donor as a pack

```
jackall-cli fc2model export graphics/weapons/special/dart_rifle/dart_rifle.xbg ^
    --game "C:\Games\Far Cry 2" --clips
```

`--clips` reads every animation bank in the install and carries the ones naming this model. It is
opt-in because it is the only slow part of an export — nothing in a mesh names its animation, so the
only exact answer is to ask all 4,436 banks. **Take the cost**: the clips are what tell you which
bone is which, and what the hands are doing.

`--rig` overrides which skeleton travels with the pack, which is how you carry the Dragunov's rig
instead of the dart rifle's.

### b. What you are allowed to build

The format's hard limits are in [`.xbm` / `.xbg`](../file-formats/xbm-xbg.md#authoring-ceilings):
**21,845 triangles per cluster**, **65,535 vertices per LOD buffer**. The effective limit is
tighter, because `tools/BlenderFC2` writes **one cluster per part object**, taking the material from
the object's first slot — so 21,845 is a per-object ceiling, enforced before export:

```
ERROR  cluster.too-many-triangles
'FRAME' draws 31200 triangles; the limit is 21845.
```

Retail first-person weapons run **5,018–12,455 triangles at LOD0** across all 21 of them, the
heaviest being the MGL-140. Going above that band is a choice, not an error: the first-person
viewmodel sits half a metre from the camera and never leaves LOD0, so it is the one mesh whose
detail the player actually looks at. The worked example ships 20,390 at LOD0, undecimated. **The
tiers below it are a different question** — see [the LOD tiers](#m-the-other-lod-tiers).

### c. Find out which part is which

**Do this before cutting anything.** Import the pack with **File ▸ Import ▸ Far Cry 2 Model Pack
(.fc2model)** — the importer's three options (**LOD**, default 0; **Build armature**; **Load
textures**) can stay as they are for now. Then open the sidebar with <kbd>N</kbd>, go to the **Far
Cry 2** tab, and in the **Animation** panel press **Measure motion**.

It reports, per bone, the worst rotation and translation across every bank the pack carries:

```
CLIP        178.9 deg, 1.038 m      the magazine, thrown clear on an unjam
Dart_Rifle  122.9 deg, 0.705 m      the root: the whole weapon, on draw
SLIDE        85.4 deg, 0.555 m      the bolt
FRAME         0.0 deg, 0.000 m      never moves
FX_FIRE       0.0 deg, 0.000 m      a socket, not geometry
```

**This is not guessable from the mesh, and it dictates the split.** The bone that does not move is
where the body of the gun belongs; a bone that swings is a moving part. A stray receiver face on
`CLIP` gets flung a metre across the screen on the first reload.

| Part | Gets | Because |
| --- | --- | --- |
| `FRAME` | receiver, stock, trigger group, **and the external scope tube** | it never moves |
| `ACCESSORY02` | barrel, handguard, suppressor, front sight | it is the break-off piece |
| `CLIP` | magazine only | it swings and drops on reload |
| `SLIDE` | bolt / charging handle | it cycles on every shot |
| `SCOPE_HI` | a **complete, self-contained sight picture** | it is drawn *instead of* the rest while zoomed |

:::warning[Read the part list off your own donor]
A donor's parts are not a convention. The Dart Rifle has four; **the Dragunov has five**, and the
fifth carries the forward 56% of the rifle — barrel, handguard and front sight, at model y
0.286…0.976 against a `FRAME` that stops at 0.302. `ACCESSORY02` also has the largest motion of any
bone on the weapon, **175.1° / 2.406 m** in the `break` bank: it is the piece that flies away when a
degraded weapon comes apart.
:::

### d. Count the donor's *clusters*, not its parts

**A part is one object per cluster, and each cluster carries its own material.** The Dart Rifle
looks like four parts; at LOD0 it imports as **eight objects**:

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
metal, and that split is the one your model has to follow whatever its own material layout is.

### e. `SCOPE_HI` is drawn *instead of* the rest of the gun

:::danger[Not an overlay — an alternate]
Meshes on **`SCOPE_HI`** are what you see **while zoomed**. Meshes on **`FRAME`** are what you see
**while not zoomed**. The two are **mutually exclusive**: the engine swaps between them rather than
compositing one over the other.
:::

:::info[RE-verified in `CFCXWeapon::ShowHiResScope`]
At `0x088d6cf0` in `FarCry2_server`, the function walks every object in the weapon's
`CGraphicComponent` and sets each one's visibility from a single comparison:

```c
CSID_ScopeHi = 0xF1FFCE61;                  // CRC32("SCOPE_HI"), the archetype's hidNodeName
for (i = 0; i < component->objectCount; i++) {
    obj     = CGraphicComponent::GetObject(component, i);
    visible = zoomed;
    if (obj->nameId != CSID_ScopeHi)
        visible = !zoomed;                  // everything that is not SCOPE_HI gets the inverse
    CGraphicComponent::SetObjectVisible(component, i, visible);
}
```

Zooming hides `FRAME`, `CLIP`, `SLIDE` and `ACCESSORY02` outright; un-zooming hides `SCOPE_HI`.
There is no compositing and no depth trick — one boolean per part, driven by an exact-case name
hash. The guard is `bUseHiResScope`: with it `False` the whole loop is skipped and `SCOPE_HI` never
shows.
:::

The donor's own cluster table shows what that forces `SCOPE_HI` to contain. Part positions are
stored in each part's *own* space, so node translations have to be applied before two parts can be
compared; in model space, the Dragunov at LOD0:

```
part      cluster  material              tris   model-space y      model-space z
FRAME     c0       mgl140_lens             40   -0.049 .. 0.280    0.109 .. 0.144
FRAME     c1       state01/02_m metal    1220   -0.178 .. 0.240    0.064 .. 0.150
FRAME     c2       metalbrushed            48   -0.009 ..-0.007    0.066 .. 0.074
FRAME     c3       wood / clay            710   -0.256 .. 0.059   -0.060 .. 0.069
FRAME     c4       main metal            3633   -0.206 .. 0.302   -0.010 .. 0.162
SCOPE_HI  c0       dragunov_lens01        132    0.011 .. 0.015    0.109 .. 0.143
SCOPE_HI  c1       crosshair               12    0.014             0.114 .. 0.127
SCOPE_HI  c2       state01/02_m metal    1008   -0.076 .. 0.012    0.101 .. 0.151
SCOPE_HI  c3       metalbrushed           936    0.012 .. 0.014    0.108 .. 0.145
SCOPE_HI  c4       main metal             144   -0.045 ..-0.041    0.110 .. 0.142
```

Three facts fall out of it, and together they prove `SCOPE_HI` is self-sufficient:

1. **It carries its own tube.** `c2` is a 1,008-triangle housing 8.8 cm long — a complete assembly
   occupying the same volume as `FRAME`'s scope, not a slice of it.
2. **The crosshair material appears on no `FRAME` cluster.** The reticle exists only under
   `SCOPE_HI`.
3. **The wood material appears on no `SCOPE_HI` cluster.** You never see the stock while zoomed.

The sight picture itself is a tube (`c2`) spanning y −0.076…0.012, a flat ring at y −0.043 (`c4`),
and at the far end a stack of coplanar discs at y ≈ 0.013 — lens (`c0`), brushed ring (`c3`),
crosshair (`c1`). Their centre sits at z ≈ 0.126 against the aim camera's 0.130, so they are on the
aim axis.

What that means for your model:

- **Build the optic twice.** An external scope on `FRAME`, seen unzoomed, and a separate
  self-contained sight picture on `SCOPE_HI` — tube, lens and reticle — seen zoomed.
- **Leave the shipped lens and crosshair discs exactly as they are**, in position, size and
  material. They are calibrated to where the aim clips put the eye, not to the tube around them. A
  lens smaller than the tube it sits in is not a defect; real scopes recess theirs.
- **The housing is the only part that is yours to replace** — `c2`, and arguably the ring `c4`.

### f. Identify your own pieces, by looking

A model bought or downloaded rarely names anything useful — the VSS used here arrives as 52 mesh
primitives, every one of them called `defaultMaterial`.

Bounding-box arithmetic will get you close and will also quietly put the trigger on the magazine.
The reliable method is to **colour candidate groups and render them**: assign a flat colour per
group, render an orthographic side view, and look. Two or three passes settles it. That is what
separates a magazine from the trigger guard and the magazine catch — three pieces sitting within a
couple of centimetres of each other and belonging to two different parts — or a bolt carrier from
the safety lever and the receiver side panel. Give genuinely ambiguous pieces one colour each and a
printed legend.

### g. Replace geometry *inside* the imported objects

An imported object carries an `fc2_submesh` custom property naming which part of the pack it *is*,
and export walks the collection skipping every mesh object that does not have one:

```python
if obj.type != "MESH" or PROP_SUBMESH not in obj:
    continue
```

:::danger[Never delete an imported object and put your own in its place]
The replacement has no `fc2_submesh`, export silently ignores it, and you ship the donor's mesh
wondering why nothing changed. The `part.unknown-object` rule catches this and blocks the export.
:::

In practice, per part:

1. Select your geometry for that part **and** the imported part object, with the imported one
   active.
2. <kbd>Ctrl</kbd>+<kbd>J</kbd> to join — the custom properties of the active object survive.
3. Enter Edit Mode, delete the donor's original vertices, leave yours.

Or work in Edit Mode throughout and paste your geometry in. Either way the object identity is
preserved.

:::danger[Object-mode transforms are silently discarded]
Export writes vertex positions in the part's own space and **ignores the object's transform
entirely**. Moving, rotating or scaling a part in Object Mode changes nothing in the file while
looking perfectly correct in the viewport. **All positioning happens in Edit Mode**, on the vertices
themselves.
:::

### h. Align to the hands, not to the origin

**You fit the gun to the hands. You cannot fit the hands to the gun.**

There is no general grip socket. `HAND_PLACEMENT` exists as a bone in only three of the 41 weapon
skeletons — `ithaca`, `dlc1_silenced_shotgun` and `deserteagle` — and those are the cases where a
hand has to *follow a moving part*. The actual mechanism is that **`clip[0]` of every weapon bank
animates the character's arms and hands directly**, against the `pelvis_ref.skeleton` every human in
the game shares; in the reload bank quoted earlier that clip touches bones 55–101, clavicles through
fingers. The grip is not computed at runtime, it is authored, per weapon, per clip. And a clip's
bone transform **replaces the rest transform rather than adding to it** (see
[`.mab`](../file-formats/mab.md)), so editing an animated bone's rest pose in the `.skeleton`
changes nothing at animation time.

What you keep is the lever that matters:

| Lever | Free? | What it moves |
| --- | --- | --- |
| **Mesh geometry in each part's local space** | **Yes** | everything you actually need — this is the fitting tool |
| Rest pose of bones no clip animates (`SCOPE_HI`, `ACCESSORY02`) | Yes | scope and accessory placement |
| Rest pose of animated bones (`FRAME`, `CLIP`, `SLIDE`, FX) | No | overridden by the clip |
| Character arm/hand motion | No | would need rewriting 46 clips, and the add-on writes only the weapon's clip |

Concretely: the `CLIP` bone will travel the donor's magazine-drop path no matter what, but your
magazine *geometry* is authored in that bone's local space, so modelling it 2 cm forward puts the
magazine where your weapon's magazine belongs while it still drops along the inherited trajectory.

So the workflow is: in the **Animation** panel press **Load Far Cry 2 Animation** and pick a bank —
an idle or aim clip is the right one to model against. The armature poses, the donor's parts move
with it, and you model your geometry against the posed hands in Edit Mode.

#### Measure where the hands are

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
  origin. Assume the origin is the grip and the gun ends up 15.6 cm out, with the support hand short
  of the handguard entirely.
- **The attach bone changes with the clip.** The weapon hangs off `R Hand` in 49 of the Dart Rifle's
  banks and off **`Camera`** in the 10 aim banks — which is how aiming aligns the sight with the
  view. Note that `aimcycle` is the shoulder-ready pose; the zoomed one is `aimironcycle`.

#### Scale and place it

**FC2 models are real-scale** — the Dragunov measures 1.231 m against a real SVD's 1.225 m — but
scale to the animation anyway: match your **grip-to-handguard span to the animation's hand span**,
because that is what decides whether both hands land on the gun. The VSS came out at 0.941 m that
way, near its real 0.894 m.

Then place it with three anchors:

- **Fore and aft** — put your grip on `R Hand`, at its measured offset from the origin.
- **Height** — put your bore on the donor's. `FX_FIRE` gives it exactly; on the Dragunov the muzzle
  is 0.070 m above the origin.
- **Facing** — check which way the donor points before anything else. The Dart Rifle's muzzle is
  `+Y`; the VSS pointed `−Y` and needed a 180° turn about Z, which conveniently also swapped its
  left and right to match.

**The scope is a fourth anchor, and it is two jobs.** Your tube on `FRAME` and the sight picture on
`SCOPE_HI` are never on screen together, so they are aligned for different reasons:

- **The `FRAME` tube** only has to read correctly on the gun, unzoomed. Still put its rear opening
  near the donor's optic, and check it has not sunk into the receiver on the way down.
- **The `SCOPE_HI` assembly is the sight picture**, and it stays exactly where the donor put it.

:::danger[Do not resize the optics to match your tube, and never move them]
Scaling the lens and crosshair up to fill a wider eyepiece is the obvious next move and it is wrong:
they are sized for **where the aim clips put the eye**, so scaling them scales the angle they
subtend and the sight picture swallows the entire screen. The VSS's eyepiece is 6.3 cm across
against the Dart Rifle's 3.2 cm, and closing that gap by doubling the optic made it unusable. The
discs are tuned to a camera you cannot see; where donor and source disagree, the animation wins.
:::

### i. Materials, channels and seams

Three constraints bite here, all of them silent failures rather than errors.

**A cluster draws with exactly one material — the object's first slot.** The exporter reads
`obj.data.materials[0]` and nothing else; extra slots produce `material.assignment-ignored`. You
cannot create a material either, so retexturing means replacing the *textures the pack carries*.
This is why the donor's material split decides how your model is divided.

**Fill every channel the donor's buffer declares — not every channel your model has.** The VSS
carries one UV set and no vertex colours; the Dart Rifle's buffer declares **two UV sets and a
colour array**. A channel the exporter cannot supply is **left alone rather than cleared**, so the
previous, now far too short array stays behind a grown vertex count. Nothing warns, and it surfaces
later as:

```
IndexError: list index out of range      # part.uvs1[loop.vertex_index], on re-import
```

After joining your geometry in, the donor's `UVMap1` and `Colour` layers exist but your half of the
mesh has no data in them — zeroed UVs sample one texel, and a zeroed colour layer can render the
part black or fully transparent. **Copy `UVMap` into `UVMap1`, and fill the colour layer** with the
value the donor used. The buffer's layout belongs to the donor, not to your model.

**A seam is a duplicated vertex.** The format stores UV, normal and colour per *vertex*, not per
corner, so where corners disagree the first one wins and the seam collapses. Both obvious approaches
fail:

| Approach | What happens |
| --- | --- |
| Never merge — a vertex per corner | the buffer triples, and the decimator has nothing to collapse across |
| Merge everything by position | hard edges and UV seams collapse: **8,092** `normal.split` findings on one part |

Merge only where position, normal **and** UV all agree. **Merge by Distance** merges on position
alone, so mark your sharp edges and UV seams first if you use it.

And **keep your model's own normals**. If a rebuild drops custom split normals, Blender recomputes
corner normals from face geometry, every shared vertex ends up with corners that disagree, and
`normal.split` fires across the whole model. Carrying the source normals across is the difference
between 8,092 findings and zero.

### j. Check

**Far Cry 2 ▸ Check** runs every rule against what an export would actually write, and lists
findings with a **Select** button that jumps to the offending object, vertex or material.

An **ERROR** blocks the export. Everything else warns, because retail itself breaks plenty of
guidelines and refusing those would make the add-on wrong about the game it is for. The gate keeping
this honest is that **every rule is silent on models exactly as they shipped** — a rule that fires
on retail is a wrong rule.

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

### k. Render it, and compare against the donor

:::danger[Nothing numeric catches a part in the wrong place]
The validator checks what the *format* allows. It has nothing to say about geometry in the wrong
part, correct geometry in the wrong place, or an optic the wrong size on screen — all of which are
legal files that pass every gate with **zero findings**. The donor works in game, so **any
difference between your render and the donor's, in the same view, is your bug**.
:::

Three views are worth rendering for any pack: a flat-shaded side view, a lit one, and the sight
picture from where the aim bank puts the eye. The last one is measurable rather than guessable:

1. Pose a character with the weapon's aim bank and read the `Camera` bone in weapon space. On the
   Dragunov's `1stge_uppb_aimironcycle` that is `(0, −0.2884, +0.1311)`.
2. Put a Blender camera there, pointed along `+Y`, and render.
3. Do the same for the untouched donor, and compare.

**Far Cry 2 ▸ Sight picture** does that for you: it isolates `SCOPE_HI` and views it from the
player's eye, deriving the camera from the aim bank's `Camera` participant. Use it before every
build. Rendering the `SCOPE_HI` objects on their own is worth doing too — a 1,008-triangle housing
hiding inside a tube is invisible in a wide shot.

Two things to get right if you write your own version of this:

- **The sight view needs a material engine.** The reticle is an alpha-tested quad; a flat-shaded
  render shows grey and the reticle is simply not there.
- **`SCOPE_HI` isolation must set `hide_render`, not `hide_set`.** The latter is viewport-only, so a
  background render still draws the whole gun and hands you a convincing picture of the wrong thing.

For a quick look at the whole weapon without the game:

```
tools\BlenderFC2\open_model.cmd vss.fc2model 0 aim
```

### l. Export and apply

**File ▸ Export ▸ Far Cry 2 Model Pack (.fc2model)** writes the collection's parts back into the
pack they came from. **The pack is edited, not rebuilt**: nodes, materials, bone palettes, LODs you
never imported and every document you did not touch all survive, and only an entry that actually
changed grows an `origin_sha256`. So an untouched export returns the shipped game file byte for
byte, and anything that *did* change is genuinely your edit rather than exporter drift.

Then into the layer, and into the game:

```
jackall-cli fc2model extract vss.fc2model -o mylayer
jackall-cli mod build --game "C:\Games\Far Cry 2" --layer mylayer
```

`extract` writes the changed files under a reserved **`mods\`** folder — that wrapper *is* the layer
contract, and anything outside it is ignored, so do not flatten it away. JackAll.App does the same
from **Apply .fc2model**. Then [read the mesh back out of the built patch](#build-verify-restore)
and check it is byte-identical to what you exported.

### m. The other LOD tiers

You imported LOD 0. The weapon has five tiers, and **LOD depth is per part**: the Dart Rifle carries
`FRAME_LOD0..4`, `CLIP_LOD0..3`, `SLIDE_LOD0..3`, `SCOPE_HI_LOD0`; on the Dragunov, `FRAME` has five
tiers and `CLIP` four while **`SLIDE`, `SCOPE_HI` and `ACCESSORY02` exist only at LOD0**.

:::danger[A part that stops at LOD0 does not stop existing — it folds into `FRAME`]
The donor does not drop the bolt and the barrel at distance. It **merges their geometry into
`FRAME`** from LOD1 down, so the gun keeps a full silhouette and only the ability to animate those
pieces goes away. Keep the suppressor on `ACCESSORY02` at every tier instead and it is simply absent
below LOD0 — a defect you will only ever see by backing away from a dropped weapon.
:::

Repeat the steps above with **LOD** set to 1, 2, 3, 4, each time supplying a decimated version of
the same geometry, and match the donor's triangle counts tier for tier rather than inventing a
budget — those are what the authored LOD distances were balanced against.

:::warning[Budget by the donor's **part**, not by its cluster]
A donor spreads a part across several clusters — the Dragunov's `FRAME` at LOD1 is 406 + 422 + 2,198
— while a transplant draws the whole part through **one**, because a cluster takes one material and
you only have one. Target a cluster's share and every middle tier lands about a quarter under the
weapon it replaces, which reads as the model popping harder than the donor at the same distance.
This is easy to misread as the decimator missing: it is not. Ask for 2,198 and Blender returns 2,198
exactly.
:::

:::danger[The cluster index is not stable between tiers]
LOD0–3 give `FRAME` three clusters (lens, metal, wood); **LOD4 gives it two**, so slot 1 is metal at
LOD0 and *wood* at LOD4. Anything that remembers "slot 1 is the receiver" transplants the stock onto
the receiver at the coarsest tier. **Identify each cluster by its material every time you change
LOD.**
:::

Three more things that only show up while doing it:

- **Export chains.** An export edits the pack it came from, so each tier must be imported from the
  pack the previous tier wrote. Start each one from the donor and you hand back a file containing
  only the last tier's work.
- **A decimated model cannot reach a hand-authored coarse budget.** The donor's coarse tiers are
  low-poly models somebody made; yours is a collapse of the full-resolution mesh, and a collapse
  **reduces a disconnected shell to a sliver rather than removing it**. The VSS is some forty
  shells, so asking for the donor's 278 triangles at LOD3 returns forty slivers — the weapon renders
  as confetti, at full triangle cost. **Find the coarsest tier that holds together and floor the
  ones below it there.** Welding the shells together first makes it worse, not better: LOD3 and LOD4
  went *up*, to 814 and 818, because merged geometry gives the collapse less to work with.
- **You will only see that by rendering the coarse tiers.** Nothing numeric distinguishes forty
  slivers from a simplified gun — both are 353 triangles with zero findings.

:::note[Do not use **Add as New Part** here]
That operator exists for giving a model a part it never shipped with, and an added part exists only
at the LOD it was added to. A weapon *replacement* fills parts that already exist at every tier, so
it is the wrong tool.
:::

What the worked example came out at, against the Dragunov it draws through:

| Tier | Distance | Dragunov | VSS | |
| --- | ---: | ---: | ---: | --- |
| LOD0 | 2 m | 9,926 | 20,390 | the source model, undecimated by choice |
| LOD1 | 3 m | 3,410 | 3,346 | |
| LOD2 | 5 m | 1,428 | 1,429 | |
| LOD3 | 8 m | 278 | 1,428 | floored at LOD2's budget |
| LOD4 | 55 m | 96 | 1,189 | floored |

LOD1–4 carry around 20 `normal.split` and 10 `uv.split` findings per tier, produced by the decimator
merging vertices that disagreed. That is inherent to decimation, and they are warnings.

## Step 6 — regenerate the baked bounding boxes

The boxes the engine culls against are the **archetype's**, baked into `hidDescriptor`, not the
mesh's. Copying the donor's archetype gives you the donor's boxes, which do not describe your model:
the VSS moves the extent from the Dragunov's y −0.2556…0.9756 to y −0.3103…0.6315, putting the rear
of the weapon 5.5 cm outside the box it is culled against.

`tools/misc/weapon-swap/fix_bboxes.ps1` regenerates them from the pack's own `mesh.json`, so they
describe what is in the file rather than what the donor had.

:::warning[Match parts by name, never by index]
The archetype lists parts in its own order and the pack in its own. An index-based rewrite puts the
magazine's box on the frame.
:::

## Step 7 — the weapon on the ground

:::danger[The pickup is a different archetype, with its own part list]
`pickups.Weapons.<Weapon>_new` and its `.Dropped`, `.WeaponStorage` and `.Multi` variants carry
**their own baked part lists**. The Dart Rifle's names four parts, so a five-part weapon lying on
the ground has no barrel or suppressor at close range — and grows them back as you walk away,
because LOD1 and below fold `ACCESSORY02` into `FRAME`. That combination is the signature of this
bug and not of a LOD problem.
:::

Fix it the same way as the weapon archetype: **take the donor's entire pickup and change only its
identity and the model it draws.** That carries the five-part list, and the skeleton with an
`ACCESSORY02` bone to hang it on, across in one piece. The identity is again `Name`, `hidName` and
`disEntityId` from the archetype you are replacing.

:::warning[`archWeapon` is not cosmetic]
The pickup names the weapon it hands the player. Copy the Dragunov's pickup without repointing
`archWeapon` from `weapons.Primary.Dragunov` to your own, and picking up your weapon gives you a
Dragunov. Repoint `objGeometryPreload` and the `.glm` with it.
:::

Then run `fix_bboxes.ps1` over the pickups too — it handles one exactly as it does a weapon.

## Step 8 — the name and the icons

Neither lives in the archetype, and neither needs a config change — both are bound **by name**, so
you are replacing what an existing name points at.

The weapon-bazaar name is **not** `sDisplayName`. `engine\gamemodes\gamemodesconfig.xml` binds a
crate to a string id and an icon by name:

```xml
<Item category="weapons" subcategory="special" name="dart rifle crate"
      nameOasis="WEAPONBAZAAR_DART_RIFLECRATE_NAME"
      descriptionOasis="WEAPONBAZAAR_DART_RIFLECRATE_DESCRIPTION"
      availability="1" needsUnlock="1" cost="10" icon="hud_icon_sniperdart"/>
```

### The text

Strings live in `languages\<language>\oasisstrings.rml`. **Ten strings name one weapon**, spread
across five sections — the bazaar crate, both manuals, the item list, the challenge list, three
statistics entries, and a second copy of the crate name under `Tutorial`. Search by string id rather
than by value. Eleven languages ship the table and every one of them carries all ten, so a rename is
eleven fragments or it is not finished.

Do not translate the new name — a weapon's name is a proper noun and stays as it is. What moves is
the grammar around it in the four strings that are not the bare name: the three statistics entries
and the stealth-equipment advert. Some languages take the substitution unchanged ("Pfeilgewehr -
Kills" becomes "VSS Vintorez - Kills"); some inflect the old name into a case a foreign name cannot
take, so the case becomes a preposition instead — Polish "Zabici strzelbą na strzałki" becomes
"Zabici **z** VSS Vintorez", Russian "Выстрелов в голову дротиками" becomes "Выстрелов в голову
**из** VSS Vintorez".

A mod states its localization edits in **one `oasisstrings.fragment.xml` per language**, holding
only the strings it changes. That is the only shape a layer may stage — a whole-file override is
refused, because it would silently overwrite every other localization mod's work. Edit a decoded
copy, then let the tool write the diff:

```
jackall-cli rml decode oasisstrings.rml          # edit the strings you want
jackall-cli rml encode oasisstrings.xml
jackall-cli rml fragments oasisstrings.rml --base <retail>\oasisstrings.rml
```

```xml
<oasisstrings>
  <section name="Items">
    <string enum="dart_rifle" value="VSS Vintorez" />
  </section>
</oasisstrings>
```

For the VSS Vintorez that is **1,271 bytes in place of 946 KB**. The override unit is the individual
string, so a mod renaming a different weapon never meets yours — not even when the two strings sit
in the same section — and only two mods rewriting the *same* string conflict, which is reported
rather than swallowed.

:::warning[Diff against the copy the game loads]
`oasisstrings.rml` ships in the **retail patch**, not only in `common.dat`. Point `--base` at the
patch copy; diffing against `common.dat`'s would stage sections that only differ because the patch
changed them.

Two languages are not where you expect: the retail patch carries ten tables and no Japanese one,
`common.dat` carries ten and no Chinese one. Staging a fragment for a language the patch has no
table for still works — the build finds the ancestor in `common.dat` and the merged table is *added*
to `patch.dat`.
:::

### The icons

Icons belong to no model, so they do not travel in a `.fc2model` pack. They go through
`jackall-cli xbt extract` and `xbt build`, with the header the extract wrote. A weapon has two, and
only one of them is single-player:

| Texture | Size | Codec | Drawn by |
| --- | --- | --- | --- |
| `ui\textures\hud\icons_weapons\hud_icon_<name>.xbt` | 128×32 | DXT5 | the HUD indicator **and** the bazaar crate |
| `ui\textures\guns\gun_icon_<name>.xbt` | 256×64 | DXT1 | the multiplayer weapon select |

Neither has a `_mip0` companion, which makes them far simpler than a weapon's own textures.

:::danger[texconv tags every PNG it writes as linear]
Converting a DDS to PNG for editing, `texconv` stamps `gAMA = 100000` — a declaration that the file
is linear. The DDS carries no such tag, so a viewer that honours `gAMA` shows the PNG **noticeably
brighter than the DDS it came from** while the bytes are identical, and `-srgbo` does not suppress
it. An editor that honours it converts on load and again on save, and then the shift is real.

Strip `gAMA`, `cHRM`, `sRGB` and `iCCP` from the PNG before handing it to anyone. An untagged PNG is
read as sRGB everywhere. On a cutout icon, check the alpha survived the round trip too — flattened,
a DXT5 icon draws as a filled box rather than a silhouette.
:::

## Step 9 — the shot sound

:::caution[Replaced and verified on disk, not yet heard in game]
Unlike everything above, this half has not been confirmed by ear. The mechanism is RE-verified and
the files round-trip; treat the recipe as unproven until someone fires it.
:::

The archetype holds sound **ids**, not filenames, and there are two of them — one per listener:

```xml
<object type="Sounds">
  <value name="sndSingleBulletShot" type="String">0x004BF5EA</value>   <!-- first person -->
  <object type="ThirdPerson">
    <value name="sndSingleBulletShot" type="String">0x004BF5EB</value> <!-- third person -->
```

You replace what those ids reach; **the ids themselves never change**, which is what keeps this a
pure data swap with no registration work.

### Neither id necessarily names the bank holding the audio

A sound id resolves to `soundbinary\<id:08x>.spk`, but that bank may hold no audio at all.
`0x004BF5EA` is a **list event**: one record, no audio, and four trailing bytes naming its real
target, `0x004BF5E9`. `0x004BF5EB` is a leaf and holds its own. Same weapon, two different shapes —
so **always look before you edit**:

```
jackall-cli spk list soundbinary\004bf5ea.spk
  0x004bf5ea  Event list  plays 1 -> 0x004bf5e9      ← follow this
jackall-cli spk list soundbinary\004bf5eb.spk
  0x004bf5eb  Sound event   -> 0x004bf5f0
  0x004bf5f0  Audio params  -> audio 0x004bf5f2 - 44100 Hz
  0x004bf5f2  Audio  Mono - 44100 Hz - IMA-ADPCM - 4.8 KB   ← this is the record to import into
```

The full dispatch model — event types, why a list event fires *all* its children, and what actually
loads the child bank — is on the [`.spk` page](../file-formats/spk.md#binary-event-objects).

### First person is stereo, third person is mono

The first-person shot is a 2D clip played flat on the player; the third-person one is a 3D point
emitter. `spk import` warns on a mismatch but **does not re-mix**, so prepare one master per slot at
the rate and channel count the record already uses:

```
ffmpeg -i shot.wav -ac 2 -ar 44100 -c:a pcm_s16le fp.wav      # first person, stereo
ffmpeg -i shot.wav -ac 1 -ar 44100 -c:a pcm_s16le tp.wav      # third person, mono

jackall-cli spk import 004bf5e9.spk 0x004bf5f3 fp.wav
jackall-cli spk import 004bf5eb.spk 0x004bf5f2 tp.wav
```

`spk import` needs 16-bit PCM for an IMA-ADPCM record and encodes natively; it takes an already-Ogg
file verbatim for an Ogg-backed one. `spk extract` on the original tells you which you are dealing
with. Both files go in the layer at `mods/soundbinary/<id>.spk`.

### Rewrite the descriptor's length, because the importer doesn't

Each audio record has a sibling descriptor whose `word[2]` is that audio's byte length, exact in
every shipped record. Neither importer updates it, so a swap leaves the descriptor describing the
*old* clip. `spk list` flags the mismatch:

```
0x004bf5f3  Audio  Stereo - 44100 Hz - IMA-ADPCM - 16 KB  (!) descriptor declares 9,766 B
```

Whether the engine reads it as a playback-length gate is
[untested](../file-formats/spk.md#playback-length-shorter-ima-adpcm-replacements-decode-as-trailing-noise),
but it is the best candidate for the known trailing-noise symptom. Patch `word[2]` and `word[22]` to
the real length.

Both banks are overrides of paths the game already ships, so they need no registration, exactly like
a texture. That does **not** generalise to a new bank: a sound is requested by id against a registry
with no load-on-miss path, so an unlisted bank resolves to null and plays nothing.

:::note[The third-person file may be dead weight in a single-player mod]
The third-person shot only plays when something other than the player's own first-person view fires
the weapon — an NPC carrying it, or another player. Whether any single-player AI is ever issued the
weapon you replaced is worth checking before you ship that half.
:::

## Step 10 — check it in game

Buying a weapon behind an unlock is a slow way to look at a mesh. This launch option unlocks
everything available in the current map:

```
farcry2.exe -GameProfile_AllWeaponsUnlock 1
```

Buy or pick the weapon up **after** installing — one already in inventory keeps the archetype it was
acquired with. Then check the five things the whole job was aiming at:

1. **Does it sit in the hands** — both of them, in idle and while aiming.
2. **Does the magazine leave cleanly on reload** — this is `CLIP`.
3. **Does the bolt cycle** — `SLIDE`.
4. **Does the whole front of the gun come off when it breaks** — `ACCESSORY02`, the bone that
   travels furthest. This is also the cheapest way to confirm the archetype names that part at all.
5. **Does the scope aim** — the zoomed view is `SCOPE_HI` alone, so a missing tube or a missing
   reticle points at that part and nowhere else.

### When something is wrong

| What you see | What it means |
| --- | --- |
| Old stats, old name, nothing changed at all | the archetype edit went into a container the game does not read, or the weapon was already in inventory |
| New mesh, old behaviour | the weapon was already in inventory — re-acquire it |
| A whole section of the gun missing at every range | the archetype's baked part list does not name that part; copy the donor's archetype whole |
| Missing section on the dropped weapon only, back at distance | the [pickup archetypes](#step-7--the-weapon-on-the-ground) still carry the old part list |
| The gun tears itself apart on reload | geometry on the wrong bone — re-read the [motion table](#c-find-out-which-part-is-which) |
| Rear of the weapon disappears at some angles | the [baked boxes](#step-6--regenerate-the-baked-bounding-boxes) are still the donor's |
| Zoomed: the donor's scope, or no reticle | `SCOPE_HI` is still the donor's, or your reticle is on `FRAME` |
| Zoomed: no tube at all | you emptied the `SCOPE_HI` housing — `FRAME` is switched off while zoomed |
| Zoomed: a pane of glass in mid-air | lens and crosshair left at the donor's position while the tube moved |
| Zoomed: the sight picture fills the screen | the optic discs were scaled up, or `fIronsightFOV` came from the wrong weapon |
| The sight jumps when you zoom | the `FRAME` tube and the `SCOPE_HI` assembly are not in the same place |
| Confetti at distance | a coarse tier decimated below what the shells survive |
| A part draws black or invisible | the colour or `UVMap1` channel was left unfilled |
| Nothing changed after an export that looked right | you replaced an imported object instead of editing it, or transformed in Object Mode |
| The weapon locks up mid-reload | a clip at a path that is not registered in `depload` |

## Known constraints, collected

- **The archetype, not the mesh, decides what is drawn.** A part the archetype's baked descriptor
  does not name is never drawn, however complete the `.xbg` is.
- **The container is `worlds\<world>\generated\entitylibrary.fcb` — the suffix-less one — and it is
  per world.** Patch `world1` and `world2` both. `_full` is never read in single-player.
- **The hands are fixed** by `iAnimationValue`. Fit the gun to them — and the scope eyepiece too,
  because the aim clips were authored for the donor's.
- **Bone ids, not names**, bind clips to weapon parts. Changing animation set without changing
  skeleton silently animates the wrong parts.
- **`FX_Casing` ≠ `FX_CASING`.** Hashes are exact-case throughout the engine.
- **A part is one object per cluster**, each with its own material, and the cluster count and order
  change between LOD tiers. Identify clusters by material, never by index.
- **`SCOPE_HI` and `FRAME` are alternates, not overlay and base**, RE-verified in `ShowHiResScope`.
  Zooming hides every other part outright. A scoped weapon needs its optic built twice, and the two
  copies must sit in the same place.
- **A part that exists only at LOD0 folds into `FRAME` below it.** Ship it on its own part at every
  tier and it vanishes at distance.
- **The pickup archetypes carry their own part lists.** A part the weapon archetype draws is still
  absent from the weapon on the ground until you rebuild those too.
- **A decimated model cannot reach a hand-authored coarse budget.** A collapse leaves a sliver per
  disconnected shell, so floor the coarse tiers at the last one that holds together.
- **Budget a tier by the donor's part total, not one cluster's share of it.**
- **Fill every channel the donor's buffer declares**, not the ones your model has. An unsupplied
  channel is left stale rather than cleared, and nothing warns.
- **A seam is a duplicated vertex.** Merge only where position, normal and UV all agree.
- **Engine units are metres, but scale to the donor anyway.** Match its envelope and hand span, not
  your weapon's real-world spec — the hands are baked into the clips.
- **A part can be added but not removed**, and an added part exists only at the LOD it was added to.
- **Collision keeps the donor's shape** — `.hkx` is not parsed by any tool here.
- **`.Multi` is the multiplayer twin.** Single-player needs only the base archetype.
- **An already-owned weapon keeps its old archetype.** Re-acquire it after installing.
- **`fIronsightFOV` frames the optic**, and lives on `WeaponProperties`. Take it from whichever
  weapon's `SCOPE_HI` you are drawing.
- **The baked bounding boxes are the archetype's, not the mesh's.** Regenerate them from the pack
  you ship, matching parts **by name** — the two lists are in different orders.
- **Fragments must be UTF-8 with no BOM**, or `mod build` rejects them with a message that names no
  file.
- **A clean `mod lint` and a clean model check are not evidence.** Read the container and the mesh
  back out of the built patch, and render the mesh before installing it.
- **The mesh moved; its material and texture references did not.** Rewriting them retextures the
  donor as well — see [texturing a replaced weapon](./texturing-a-weapon.md).
- **A sound id is not a sound.** It may name a wrapper that only lists other banks. Run `spk list`
  and follow it before importing, and keep each slot's own channel count.

## What is left after the mesh

The art. A mesh built inside a donor's pack keeps that donor's material table, so retexturing means
taking a material the *replaced* weapon owned and moving your clusters onto it — and because
`SCOPE_HI` shares the body's material, that move has to **append** an entry rather than rewrite one.
The PBR-to-legacy conversion, finding a material you are allowed to own, and the specular settings
that decide whether the weapon reads matte or polished are all on [texturing a replaced
weapon](./texturing-a-weapon.md).

Three things nobody has needed yet, so they are unwritten rather than impossible: a second material
for a part of the body, a normal map where a weapon owns no third texture path, and the `.Multi`
pickup for multiplayer.
