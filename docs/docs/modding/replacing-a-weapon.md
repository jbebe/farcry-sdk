---
sidebar_position: 6
---

# Replacing an existing weapon

:::tip[Built, played, and shipped]
The worked example — a **VSS Vintorez replacing the Dart Rifle** — is in the game and working:
correct model, correct animation set, semi-automatic, ten rounds, and a scope that aims. The mod is
`mods/vss-vintorez` in this repo, with its build scripts and reference renders beside it.

Everything below is measured or traced, and says which. Where a step is specific to that weapon pair
it says so; the reasoning is written to transfer to any other pair. This page is the mesh and the
archetype; the art it then wears is [texturing a replaced weapon](./texturing-a-weapon.md).
:::

[Adding a new weapon](./adding-a-weapon.md) covers standing up a weapon that did not previously
exist. This page is the cheaper and far more common job: taking a weapon the game already ships and
making it something else. Nothing here needs a free `iAnimationValue` slot, a `movemgr.bin` writer,
or a new `depload` entry — the donor already has all of that.

## The job, in two halves

A weapon replacement is two pieces of work that fail in completely different ways, and the single
most expensive mistake in this project was testing them together. **Do the archetype half first and
prove it with the donor's own unmodified mesh.** When the weapon you are replacing behaves exactly
like the donor, every field is right, and the mesh becomes a pure geometry problem with no wiring
left to doubt.

### The `.fcb` half — the archetype

What the engine reads to decide what this weapon *is*. Two archetypes, staged as fragments into the
world's entity library.

1. Find the container the game actually reads. It is
   [`worlds\<world>\generated\entitylibrary.fcb`](#which-entity-library-the-game-actually-reads),
   the suffix-less one, and it is **per world** — patch `world1` and `world2` both.
2. `weapons.<slot>.<Weapon>` — take the **donor's entire entity archetype** and change only its
   identity and the model path. That carries the part list, the baked descriptor, the skeleton,
   `sPartName`, `iAnimationValue` and `bUseHiResScope` across in one piece, so no field can be
   forgotten. See [the archetype names the parts to draw](#the-archetype-names-the-parts-to-draw-not-the-mesh).
3. `WeaponProperties.<slot>.<Weapon>` — edit the [stats](#stats-what-actually-changes) field by
   field. Write **both halves** of every hash-backed field, the string and its exact-case CRC32.
4. Regenerate the [baked bounding boxes](#the-archetype-names-the-parts-to-draw-not-the-mesh) once
   the mesh exists.
5. `mod build`, then [read the archetype back out of the built patch](#verify-the-edit-landed-by-reading-it-back-out-of-the-patch).

### The Blender half — the mesh

What that archetype then draws. One `.fc2model` pack in, one out.

1. `fc2model export` the donor **`--clips`**. Take the cost; the clips are what tell you which bone
   is which.
2. [Read the motion table](#step-2--find-out-which-part-is-which). The bone that never moves is the
   body; a bone that swings is a moving part. This is not guessable from the mesh.
3. [Count clusters, not parts](#step-3--count-the-donors-clusters-not-its-parts) — a part is one
   object per cluster and each carries its own material.
4. [Identify your own pieces by looking](#step-4--identify-your-own-pieces-by-looking), then
   [replace the geometry inside the imported objects](#step-5--the-rule-that-governs-everything-else).
   Never delete an imported object and add your own.
5. [Fit the gun to the hands](#step-6--align-to-the-hands-not-to-the-origin), and the scope to
   `SCOPE_HI`.
6. [Fill every channel the donor's buffer declares](#step-7--materials-channels-and-seams).
7. [**Check**](#step-8--check-before-you-export), then **look at it** — see below.
8. Repeat for [LOD 1–4](#step-10--the-other-lods).

### The rule that would have saved this project the most time

**Render it before you install it, and compare against the donor.**

Every scope defect in this project's history — five of them — was a *legal file that passed every
numeric gate with zero findings*. The validator checks what the format allows; it has nothing to say
about geometry in the wrong part, correct geometry in the wrong place, or an optic the wrong size on
screen. The donor works in game, so **any difference between your render and the donor's, in the same
view, is your bug**.

Three views are worth rendering for any pack: a flat-shaded side view, a lit one, and the sight
picture from where the aim bank puts the eye. Two things the script that does it had to get right,
and both are easy to get wrong in your own:

- **The sight view needs a material engine.** The reticle is an alpha-tested quad — a flat-shaded
  render shows grey and the reticle is simply not there.
- **`SCOPE_HI` isolation must set `hide_render`, not `hide_set`.** The latter is viewport-only, so a
  background render still draws the whole gun and hands you a convincing picture of the wrong thing.

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
Counts below come from decoding `worlds/world1/generated/entitylibrary.fcb` with
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
one with another teaches scope *preservation*: your mesh must supply a complete `SCOPE_HI` sight
picture, because that part is what `bUseHiResScope` draws while the player is zoomed — and while it
is drawn, nothing else on the weapon is.

Adding a scope to a weapon that has none is **not currently achievable with the supported
toolchain**. It needs a new `SCOPE_HI` part, and `tools/BlenderFC2` 0.1.0 can add a part only to a
single LOD — the scope would vanish as soon as the weapon dropped to LOD1. Do not plan a section
around it.

## Stats: what actually changes

The values below are the ones that make a bolt-action tranquilizer rifle behave like a semi-automatic
suppressed marksman rifle. Every one lives on `WeaponProperties.Special.Dart_Rifle`, in **each
world's** library (see [which library](#which-entity-library-the-game-actually-reads)); the `.Multi`
twin is multiplayer and does not need them.

| Field | Dart Rifle ships | VSS wants | Why |
| --- | --- | --- | --- |
| `selFireRateMode` | `2` (PrepareShot) | `0` (SingleShot) | 2 is the bolt-cycle mode; the VSS is gas-operated semi-auto |
| `iAmmoInClip` | `1` | `10` | VSS box magazine |
| `iFireRate` | `120` | `85` | RPM. Take the **donor's** measured value, not a guess — an earlier round picked 200 out of the air |
| `iMaxAmmo{Casual,…}` | `9/4/3/2` | `40/20/20/10` | otherwise a 10-round magazine has a 9-round reserve |
| ammo string + `ammoAmmoType` | `darts` / `FC2096BC` | `sniperrifle` / `7D6BD5F2` | **both halves**, or it still draws from the dart pool |
| `BulletCaseBone` + twin | `FX_CASING` / `D431B68E` | `FX_Casing` / `2365743E` | the new rig spells it differently, and hashes are exact-case |
| **`fIronsightFOV`** | `0.3` | **`0.28`** | the zoom FOV — see below |
| `sDisplayName` | `Dart` | `VSS Vintorez` | literal string, **not** a localization id |
| `bIsSilent` | `True` | `True` | already correct |
| `selWeaponClass` | `5` (Sniper) | `5` | already correct |
| `selCategory` | `3` (Special) | `3` | already correct |

:::warning[Inherit the donor's `SCOPE_HI`, inherit its `fIronsightFOV` with it]
**`fIronsightFOV` is the zoom field of view, and it lives on `WeaponProperties` — not on the mesh.**
It is the angle the zoomed view is framed at, so it decides how much of the screen the `SCOPE_HI`
assembly fills.

Take a donor's optic while keeping the replaced weapon's field and the optic comes out the wrong size
on screen: the Dart Rifle's `0.3` against the Dragunov's `0.28` left the assembly visibly small, with
empty screen all round it — "the scope is floating in the void". The eye position and `SCOPE_HI` were
both already correct; only the frame around them was wrong.

The rule is simple: **whichever weapon's `SCOPE_HI` you are drawing, take that weapon's
`fIronsightFOV` too.** They are one setting in two files.
:::

The `sel*` enums are self-describing in the file — each is followed by its own value list — so none
of these indices has to be guessed. See [adding-a-weapon](./adding-a-weapon.md#weaponproperties--the-stat-archetype).

Beyond the table, making the weapon lethal rather than tranquilizing means rewriting the
`WeaponStims` / `ImpactStims` blocks and repointing the projectile off `dart.xbg` (a visible dart)
onto the rifle bullet and tracer. That is the largest single chunk of data work in this build.

### Which entity library the game actually reads

:::danger[Get this wrong and nothing else on this page matters]
An install ships **many** entity libraries, and most of them contain the weapon archetypes without
being read. Three separate rounds of this build died here — first on
`patch\worlds\tmpla\generated\entitylibrary.fcb`, then on `worlds\world2\...\entitylibrary.fcb`, then
on `entitylibrary_full.fcb` in both campaign worlds. Every archetype edit any of them made was dead.
:::

**The answer, measured in a running game:**

```
worlds\world1\generated\entitylibrary.fcb     act 1, Leboa-Sako
worlds\world2\generated\entitylibrary.fcb     act 2, Bowa-Seko
```

The suffix-less library, and **per world** — patch both.

#### How that was settled, and why it is worth copying

`CXGame::LoadArchetypes` picks the base library on a flag whose meaning is not known:

```
if (flag at +0xC4 == 0)  load  \entitylibrary.fcb          1,419 archetypes
else                     load  \entitylibrary_full.fcb     5,566, a strict superset
                         load  generated\EntityLibraryPatchOverride.fcb
                         then a loop over further libraries (DLC)
```

The base is either/or, never both. Reasoning about which branch a campaign client takes produced the
wrong answer twice, so the question was turned into an experiment instead: **the same edit was staged
into all six candidate containers at once, each carrying a different magazine size.** One launch, and
the number on the HUD names the winner.

```
worlds\tmpla\generated\entitylibrary.fcb        21
worlds\tmpla\generated\entitylibrary_full.fcb   22
worlds\world1\generated\entitylibrary.fcb       23
worlds\world1\generated\entitylibrary_full.fcb  24
worlds\world2\generated\entitylibrary.fcb       25   <- observed, playing act 2
worlds\world2\generated\entitylibrary_full.fcb  26
```

The technique generalises to any "which file is live" question, and it is far cheaper than the
alternatives: one launch instead of a bisection, and it cannot be fooled by a plausible-sounding
inference. Pick a value that is visible without ambiguity — a magazine size or an ammo count, not a
name that some other subsystem might supply.

:::note[Two claims this disproves]
**`entitylibrary_full.fcb` is not the campaign's library**, despite being the client-only one. It is
absent from the dedicated server binary, which shows it is not a *server* library; it does not follow
that it is what the campaign reads, and it is not.

**`EntityLibraryPatchOverride.fcb` does not exist in every edition.** It is absent from the GOG
Fortune's Edition, whose `patch.fat` holds 215 entries with exactly one entity library. Its name is
in the hashlist, so a present file would have been resolved — it is genuinely not there. Check your
install before blaming it for a dead edit.
:::

:::warning[Ubisoft's own patch is a red herring here]
The retail patch's only entity-library entries are `worlds\tmpla\generated\entitylibrary.fcb` and
`worlds\tmpla\generated\tmpla_depload.dat`. That looks like strong evidence that `tmpla` is the
campaign world. **It is not** — `tmpla` carried magazine 21 in the experiment above and was never
seen. Whatever `tmpla` is for, the campaign does not read it.
:::

:::warning[A clean `mod lint` is not evidence an edit is live]
`jackall-cli mod lint` reported *"No dead archetype edits — every edited archetype is the copy the
game reads"* for containers that turned out never to be read at all. It models the override chain
*within* the containers you name, and is silent about whether the game opens them. Read the archetype
back out of the built `patch.dat`, then confirm it in game.
:::

:::note[An already-owned weapon keeps its old archetype]
Loading a save where the weapon was already in inventory gives you the **new mesh on the old
archetype** — a Dragunov model that still behaves like a Dart Rifle. Buy or pick the weapon up again
after installing and it binds correctly. Worth knowing before you conclude a build failed.
:::
### How to actually apply an archetype edit

Decode the library, edit, and stage **one fragment per archetype**:

```
jackall-cli fcb decode  worlds\world1\generated\entitylibrary.fcb
```

That writes an index plus ~42 group files (`41_WeaponProperties.xml`, `42_weapons.xml`, …). Edit the
archetype inside its group file — and **scope the edit to the `Entity` node whose `hidName` matches**
rather than to a line range, because neighbouring archetypes carry the same field names a few lines
away and a window will silently catch the wrong gun.

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

`sed -i` and shell redirection do not add one. If you are unsure, look:
`head -c 3 fragment.xml | xxd -p` gives `efbbbf` for a BOM and `202020` for the leading spaces of a
clean fragment.
:::

:::note[`fcb encode` will not take the decoded form back]
`decode` splits an entity library into group files; `encode` refuses multi-file XML. So there is no
round trip through the whole container, and fragments are the route. Applying them still re-encodes
the container, which inflates it — `patch.dat` went 9.9 → 49.7 MB once both worlds were covered. See
`docs/design/fcb-deep-fragments.md`.
:::

### Verify the edit landed, by reading it back out of the patch

Nothing in the build output tells you an archetype edit is live. Extract the container the game will
load from the built archive, decode it, and look:

```
jackall-cli mod build --game "C:\Games\Far Cry 2" --layer mylayer
jackall-cli archive extract "C:\Games\Far Cry 2\Data_Win32\patch.fat" ^
    --names --filter entitylibrary -o check
jackall-cli fcb decode check\worlds\world1\generated\entitylibrary.fcb
```

Then grep the decoded group file for the value you set. Expect `mod build` to report your file as
**added** rather than overridden — `world2` is not in the vanilla patch at all, and `patch.dat` wins
over `worlds.dat` at load time, so adding an entry there is what a replacement looks like.

**A display-name change is the cheapest possible canary.** Set `sDisplayName` to something
unmistakable before touching anything else. If the HUD does not show it, the container is wrong and
no other symptom you observe means anything.

### The archetype names the parts to draw, not the mesh

:::danger[The most expensive assumption in this whole job]
**The engine does not draw whatever the `.xbg` contains.** The `weapons.*` archetype carries a baked
description of the model, and only what *it* names is drawn.
:::

Two parallel lists, both inside the archetype:

1. A **resource entry per part** — `hidIndex`, `objModel` (CRC32 of the `.xbg` path), `hidMeshName`,
   `hidNodeName` (CRC32 of the part name) and `hidNodeNameLOD0` (CRC32 of `<PART>_LOD0`).
2. A **baked `GraphicComponent`** inside `CFileDescriptorComponent.hidDescriptor`, an Rml blob: one
   `<object index meshName boneName bboxMin bboxMax>` per part, a `<resource fileName bbox>`, a
   `RigidPhysComponent` naming the `.hkx`, and **a full `<skeleton>` copy with every bone's position
   and rotation**.

Swap the mesh and all of that still describes the weapon that used to be there:

| | Dart Rifle, after the three animation fields were changed | Dragunov |
| --- | --- | --- |
| `fileSkeleton` / `sPartName` / `iAnimationValue` | correct | same |
| baked objects | FRAME, CLIP, SLIDE, SCOPE_HI | CLIP, FRAME, SCOPE_HI, SLIDE, **ACCESSORY02** |
| baked skeleton | **the Dart Rifle's own bones**, incl. `FX_CASING` and `Dart_Rifle_DART` | the Dragunov's |

So `ACCESSORY02` was **never drawn** — the suppressor and handguard were missing in every build, and
no amount of mesh work could have fixed it. And `SCOPE_HI`, though listed, carried the Dart Rifle's
bone position and a 3 cm bounding box instead of the Dragunov's, which is why the zoomed view never
worked.

**So a mesh swap is an archetype edit too.** Regenerate the boxes from the pack you are actually
shipping — `tools/misc/weapon-swap/fix_bboxes.ps1` does it from the pack's own `mesh.json`, so they
describe what is in the file rather than what the donor had. **Match parts by name, never by index**:
the archetype lists them in its own order and the pack in its own, so an index-based rewrite puts the
magazine's box on the frame.

It matters in both directions. Replacing the Dragunov with a VSS moved the model's extent from
y −0.2556…0.9756 to y −0.3103…0.6315 — 5.5 cm further back, so the rear of the weapon sat outside the
box it is culled against.

Two details worth writing down:

- CRC32s you will need beyond the ones already in the file: `ACCESSORY02` = `D8D2CBA5`,
  `ACCESSORY02_LOD0` = `BACEEF7A`. Validate any CRC32 routine against hashes already present
  (`FRAME`, `FRAME_LOD0`, `CLIP`, `SLIDE`, `SCOPE_HI`, `SCOPE_HI_LOD0`) before trusting it.
- **`hidResourceCount` is 4 on both weapons** despite the Dragunov having five parts. It counts
  resource *files*, not parts. Do not touch it.

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
| `FRAME` | receiver, stock, trigger group, **and the external scope tube** | it never moves |
| `ACCESSORY02` | barrel, handguard, suppressor, front sight | it is the break-off piece |
| `CLIP` | magazine only | it swings and drops on reload |
| `SLIDE` | bolt / charging handle | it cycles on every shot |
| `SCOPE_HI` | a **complete, self-contained sight picture** — see below | it is drawn *instead of* the rest while zoomed |

:::warning[`ACCESSORY02` is not an optional extra]
The guide's donor tables used to list four parts, because the Dart Rifle has four. **The Dragunov has
five**, and the fifth carries the forward 56% of the rifle — barrel, handguard and front sight, at
model y 0.286…0.976 against a `FRAME` that stops at 0.302.

Its motion table entry is the largest of any bone on the weapon: **175.1° / 2.406 m**, worst in the
`break` bank. It is the piece that flies away when a degraded weapon comes apart. So a donor's part
list is something to *read off the pack*, never something to assume from another weapon.
:::

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

Read this off **your own donor**, every time. The Dragunov, the geometry donor in this build, is a
different shape again: five parts, of which `FRAME` alone carries five clusters and `SCOPE_HI`
another five — [the table below](#scope_hi-is-drawn-instead-of-the-rest-of-the-gun-not-on-top-of-it)
lists them with their model-space extents.

### `SCOPE_HI` is drawn *instead of* the rest of the gun, not on top of it

:::danger[This corrects what earlier versions of this page said, and it is the costliest mistake in the whole job]
Meshes on **`SCOPE_HI`** are what you see **while zoomed**. Meshes on **`FRAME`** are what you see
**while not zoomed**. The two are **mutually exclusive** — the engine swaps between them rather than
compositing one over the other.

This page previously taught the opposite: that `SCOPE_HI` was a small eyepiece overlay laid on top of
a still-visible `FRAME`, so your whole scope should stay on `FRAME` uncut. That is wrong, and it is
the root cause of four consecutive failed scope builds.
:::

:::info[RE-verified: while zoomed, `SCOPE_HI` is the only part drawn]
`CFCXWeapon::ShowHiResScope(bool)` (`0x088d6cf0` in `FarCry2_server`) walks every object in the
weapon's `CGraphicComponent` and sets each one's visibility from a single comparison:

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

So zooming hides `FRAME`, `CLIP`, `SLIDE` and `ACCESSORY02` outright, and un-zooming hides
`SCOPE_HI`. There is no compositing and no depth trick — it is one boolean per part, driven by an
exact-case name hash. The guard is `bUseHiResScope`: with it `False` the whole loop is skipped and
`SCOPE_HI` never shows at all.

Two consequences worth stating plainly:

- **Your `SCOPE_HI` must be a complete sight picture**, because nothing else is on screen with it.
- **Your `FRAME` scope must sit exactly where `SCOPE_HI` does**, because the swap happens at the
  eye's position and any offset between the two reads as the sight jumping when the player zooms.
:::

The Dragunov's LOD0 cluster table settles it. Part positions are stored in each part's *own* space,
so node translations have to be applied before two parts can be compared; in model space:

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

Three facts fall out, and together they prove `SCOPE_HI` is **self-sufficient**:

1. **It carries its own tube.** `c2` is a 1,008-triangle housing 8.8 cm long. It is not a slice of
   anything — it is a complete assembly occupying the same volume as `FRAME`'s scope.
2. **The crosshair material appears on no `FRAME` cluster.** The reticle exists only under
   `SCOPE_HI`.
3. **The wood material appears on no `SCOPE_HI` cluster.** You never see the stock while zoomed.

A part that has to supply its own tube, its own lens *and* its own reticle is a part that gets drawn
with nothing else beside it.

Read off the same numbers, the sight picture is: a tube (`c2`) spanning y −0.076…0.012, a flat ring
at y −0.043 (`c4`), and at the far end a stack of coplanar discs at y ≈ 0.013 — lens (`c0`), brushed
ring (`c3`), crosshair (`c1`). Their centre sits at z ≈ 0.126 against the aim camera's 0.130, so they
are on the aim axis.

**What this means for your model:**

- **Build the optic twice.** An external scope on `FRAME`, seen unzoomed, and a separate
  self-contained sight picture on `SCOPE_HI` — tube, lens and reticle — seen zoomed.
- **Leave the shipped lens and crosshair discs exactly as they are** — position, size and material.
  They are calibrated to where the aim clips put the eye, not to the tube around them. Scale them up
  to fill a wider eyepiece and you scale the angle they subtend: the sight picture swallows the whole
  screen. A lens smaller than the tube it sits in is not a defect; real scopes recess theirs.
- **The housing is the only part that is yours to replace** — `c2`, and arguably the ring `c4`.

Each of these was learned by shipping the opposite:

| Attempt | In Blender | In game |
| --- | --- | --- |
| Whole scope kept on `FRAME`, `SCOPE_HI` left as the donor's | looks right, 0 findings | donor's housing engulfs your tube; no usable reticle |
| Eyepiece carved out of `FRAME` into `SCOPE_HI` | looks right, 0 findings | back of the scope missing, spray of triangles |
| Optic left at the donor's position while the tube moved | looks right, 0 findings | pane of glass hanging in mid-air |
| Optic scaled up to fill the wider eyepiece | looks right, 0 findings | sight picture fills the screen when zoomed |
| `SCOPE_HI` housing emptied, lens and crosshair kept | looks right, 0 findings | no tube at all — `FRAME` is switched off while zoomed |

Every one of those exported clean, because none of them is a malformed file. The validator checks
what the *format* allows, and geometry in the wrong part is legal. Which is the reason for the next
section.

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

`tools/BlenderFC2` does all three for you: **Far Cry 2 ▸ Sight picture** isolates `SCOPE_HI` and
views it from the player's eye, deriving the camera from the aim bank's `Camera` participant. Use it
before every build. Rendering the `SCOPE_HI` objects on their own is worth doing too — a
1,008-triangle housing hiding inside a tube is invisible in a wide shot.

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

**The scope is a fourth anchor, and it is two jobs, not one.** Under the
[alternates rule](#scope_hi-is-drawn-instead-of-the-rest-of-the-gun-not-on-top-of-it) your tube on
`FRAME` and the sight picture on `SCOPE_HI` are never on screen together, so they are aligned for
different reasons.

- **The `FRAME` tube** only has to read correctly on the gun, unzoomed. Still put its rear opening
  near the donor's optic — on the VSS it was 13.4 cm too far back *and* 2.3 cm too high, which looks
  wrong from the shoulder — and check it has not sunk into the receiver on the way down.
- **The `SCOPE_HI` assembly is the sight picture**, and it stays exactly where the donor put it. Only
  its housing (the 1,008-triangle tube, and arguably the ring) is yours to replace, and your
  replacement has to occupy that same volume.

**Do not resize the optics to match your tube.** It is the obvious next move and it is wrong. The
crosshair and lens are sized for **where the aim clips put the eye**, not for the diameter of the
tube around them. Scale them up to fill a wider eyepiece and you scale up the angle they subtend:
the sight picture swallows the entire screen when you zoom. The VSS's eyepiece is 6.3 cm across
against the Dart Rifle's 3.2 cm, and closing that gap by doubling the optic made it unusable.

A lens smaller than the tube it sits in is not a defect — real scopes recess theirs.

**Never move the lens or the crosshair.** They are tuned to a camera you cannot see; every attempt to
improve them made things worse, in a way that only showed up in game.

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

Five things are worth checking, and they are the five the geometry work was aiming at:

1. **Does it sit in the hands** — both of them, in idle and while aiming.
2. **Does the magazine leave cleanly on reload** — this is `CLIP`.
3. **Does the bolt cycle** — `SLIDE`.
4. **Does the whole front of the gun come off when it breaks** — `ACCESSORY02`, the bone that travels
   furthest. This is also the cheapest way to confirm the archetype names that part at all.
5. **Does the scope aim** — the zoomed view is `SCOPE_HI` alone, so a missing tube or a missing
   reticle points at that part and nowhere else.

The world pickup and the third-person model draw from the same `.xbg`, but they are **separate
archetypes** with their own baked part lists (`pickups.Weapons.<Weapon>_new` and its `.Dropped`,
`.WeaponStorage` and `.Multi` variants). A part you added to the weapon archetype will not appear on
the ground until you add it there too.

### Step 10 — the other LODs

You imported LOD 0. The weapon has five tiers, and LOD depth is **per part** — the Dart Rifle carries
`FRAME_LOD0..4`, `CLIP_LOD0..3`, `SLIDE_LOD0..3`, `SCOPE_HI_LOD0`. Coarser tiers drop the smaller
parts. On the Dragunov, `FRAME` has five tiers and `CLIP` four, while **`SLIDE`, `SCOPE_HI` and
`ACCESSORY02` exist only at LOD0**.

:::danger[A part that stops at LOD0 does not stop existing — it folds into `FRAME`]
The donor does not simply drop the bolt and the barrel at distance. It **merges their geometry into
`FRAME`** from LOD1 down, so the gun still has a full silhouette; only the ability to animate those
pieces goes away.

Author your LOD1–4 the way the donor did and the weapon looks right at every range. Keep the
suppressor on `ACCESSORY02` at every tier instead, and it is simply absent below LOD0 — which is a
defect you will only ever see by backing away from a dropped weapon, long after you have decided the
build works.
:::

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

:::warning[Every offline gate passed, and the build was still wrong]
The VSS geometry below is transplanted across all five LOD tiers. **LOD0 passes Check with zero
findings**, and the pack round-trips: exported, re-imported, bounds identical.

It is also a **four-part** split made before `ACCESSORY02` was known about and before the `SCOPE_HI`
rule was corrected, so in game it had no suppressor and no working zoom. Keep it as a triangle-budget
reference, not as a part layout to copy.
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

The art, which is [its own page](./texturing-a-weapon.md) — the PBR-to-legacy conversion, and the
fact that the mesh you just moved is still drawing through **the donor's materials**, which the donor
is still drawing through too.

One consequence belongs here rather than there, because it is a property of the transplant: a mesh
built inside a donor's pack keeps that donor's material table, so retexturing it means taking a
material the *replaced* weapon owned and moving your clusters onto it. `SCOPE_HI` shares the body's
material, so that move has to **append** an entry rather than rewrite one.

## Known constraints, collected

Things that are settled, so nobody re-derives them:

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
- **The baked bounding boxes are the archetype's, not the mesh's.** Regenerate them from the pack you
  ship, matching parts **by name** — the two lists are in different orders.
- **Fragments must be UTF-8 with no BOM**, or `mod build` rejects them with a message that names no
  file.
- **Nothing numeric catches a part in the wrong place.** Render it and compare against the donor.
- **The mesh moved; its material and texture references did not.** Rewriting them retextures the
  donor as well — see [texturing a replaced weapon](./texturing-a-weapon.md).
## Answered by the finished build

- **`depload` needed no edit.** `sPartName = dragunov` alone was sufficient; the animation package
  resolves without touching `CAnimationPackageResource`. That holds because the donor already ships
  in the same worlds — a donor from a world your weapon does not appear in may not be so forgiving.
- **The Dragunov's magazine-drop trajectory reads fine** with a VSS magazine modelled into it.

## Open questions

- Whether to keep `fileSkeleton` pointed at the donor's rig or author one with matching bone order.
  The donor's rig puts `FX_FIRE` at the **Dragunov's** muzzle, which is 34 cm further forward than the
  VSS's, so the muzzle flash is out in front of the suppressor.
- The pickup archetypes (`pickups.Weapons.<Weapon>_new` and its `.Dropped`, `.WeaponStorage` and
  `.Multi` variants) carry their **own** part lists, so a part you add to the weapon archetype does
  not appear on the ground until you add it there too. Untouched here.
- Where the weapon-bazaar name comes from. It is **not** `sDisplayName` — that was set to an
  unmistakable canary and the bazaar still read the vanilla name. Likely `WEAPONBAZAAR_*_NAME` in
  `oasisstrings`. Unverified.
