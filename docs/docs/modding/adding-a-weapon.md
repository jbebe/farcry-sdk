---
sidebar_position: 5
---

# Adding a new weapon

:::info[Measured against the shipped DLC, with the engine side RE-verified]
No community mod has ever added a new weapon — [the survey](./mods-survey.md) turns up only
rebalances, reskins and unlocks — and every other weapon page on this site edits an existing entry.
This page is built from two things instead: Ubisoft's own DLC1 weapon pack, decoded field by field
out of `tmp/gamefiles`, and the engine code that consumes it, traced through GhidraMCP against
`FarCry2_server`.

**The art half is solved and no longer needs this page.** A weapon whose mesh, materials and textures
were all authored here has been built, installed and played, and the twenty-odd one-off scripts that
took have since become [one file and one plugin](#geometry-materials-and-textures-one-file-one-plugin).
The archetype half — the seven `.fcb` records, `iAnimationValue`, `depload`, spawning a weapon that
did not previously exist — is still a procedure nobody has run end to end.
:::

## The reference implementation

`downloadcontent/dlc1/` is Ubisoft answering exactly this question. It adds three weapons — a
crossbow, a sawed-off shotgun and a silenced shotgun — plus a weapon crate, and every piece of it is
in the extracted archives. Read it before writing anything.

```
downloadcontent/dlc1/
├── entitylibrary/
│   ├── downloadcontent/dlc1/generated/
│   │   ├── entitylibrary.fcb                    109 KB — all the archetypes
│   │   ├── entitylibrary_depload.dat             10 KB — dependency graph
│   │   └── entitylibrary_deploadnewparticles.rml 883 KB — particle deps
│   ├── graphics/weapons/dlc/sawed_off_shotgun/
│   │   ├── dlc1_sawedoff_shotgun.xbg            506 KB — mesh, parts FRAME/CLIP/ACCESSORY/ACCESSORY2, LOD0–5
│   │   ├── dlc1_sawedoff_shotgun.hkx            2,880 B — Havok collision
│   │   ├── dlc1_sawedoff_shotgun_ref.skeleton     537 B — 5 bones incl. FX_FIRE
│   │   ├── sawed_off_shotgun_state0{1,2}.xbt          — degradation masks (+ _mip0 siblings)
│   │   └── bullet.xbg                           3,316 B — ejected shell
│   ├── graphics/_materials/*.xbm                       — 5 for this weapon, in the shared pool
│   ├── graphics/characters/_common/animations/weapons/dlc/sawedoff_shotgun/
│   │   └── *.mab                                       — 49 clips, weapon code `sesos`
│   ├── graphics/move/dlc1.bin + dlc1named.bin          — the MOVE animation-graph expansion
│   └── soundbinary/*.spk                               — 16 new banks (+10 reused from the base game)
└── dominos/domino/
    ├── system/dlc1weaponsspawn.lua                     — places the pickups in the campaign
    └── user/dlc/dlc1weapons.graph.lua                  — the Domino graph that calls it
```

Note the two folder names differ: the mesh lives under `sawed_off_shotgun`, the animations under
`sawedoff_shotgun`. Neither is derived from the other; both are spelled out in data.

## The archetype family

One weapon is **seven archetypes**, split across two libraries in the same `.fcb`. For the sawed-off:

```
WeaponProperties.DLC1.SawedOffShotgun            ← stats, names, sounds, particles, ballistics
WeaponProperties.DLC1.SawedOffShotgun.Multi
DLC1Weapons.DLC1.SawedOffShotgun                 ← the weapon entity: mesh, collision, rig, reliability
DLC1Weapons.DLC1.SawedOffShotgun.Multi
DLC1Weapons.DLC1.Pickup_SawedOffShotgun          ← the world pickup
DLC1Weapons.DLC1.Pickup_SawedOffShotgun.Dropped  ← what spawns when the player drops it
DLC1Weapons.DLC1.Pickup_SawedOffShotgun.Multi
```

plus projectiles (`DLC1Weapons.DLC1.Shotgun_Bullet`, `…01`, `…02`, `…03`, each with `.Multi`) and the
shared `DLC1Weapons.DLC1.WeaponCrate`.

Base-game weapons use the same shape with a `_Merc` variant added — the same weapon redefined into
the `Primary` slot, because mercs have no "special" inventory slot. See
[data-recipes](./data-recipes.md) for the full archetype-name table.

The library is loaded after the patch override and wins over it, via `CEntityLibraryManager::Override`;
matching is case-insensitive on the fully-qualified `hidName`, and **insertion replaces the whole
definition** rather than merging fields. See [entity instancing](../engine-internals/entity-instancing.md).

## The names that matter

Four strings do real work, and only one of them is the archetype path.

| String | Where | What it drives |
|---|---|---|
| `hidName` | every archetype | the library key; what `SpawnEntityFromArchetype` and `archPickupArchetype` reference |
| `CommonProperties.sName` | `WeaponProperties.*` | the **short code** — picks the HUD icon and the MP kill-message glyph |
| `CommonProperties.sDisplayName` | `WeaponProperties.*` | the on-screen name, a **literal string, not a localization ID** |
| `CSimpleAnimationComponent.sPartName` | the weapon entity | the **animation key** — what the MOVE graph and `depload` bind clips to |

For the sawed-off those are `WeaponProperties.DLC1.SawedOffShotgun`, `sawedoffshotgun`,
`S-O Shotgun`, and `dlc1_sawedoff_shotgun`.

**Localization is not involved.** `oasisstrings.xml`/`.rml` have zero hits for "sawed" or "crossbow"
in any of the eleven shipped languages. You only need string-table work if you want a weapon-bazaar
entry, which uses `WEAPONBAZAAR_<WEAPON>_{CRATE,OPERATION_MANUAL,REPAIR_MANUAL}_{NAME,DESCRIPTION}`.

## Every `Hash` field is CRC32 of its companion string

This is the single most useful thing to know when authoring weapon data by hand, and it dissolves
the long-standing "magazine capacity and fire mode are hash-only" complaint in
[gotchas](./gotchas.md).

Weapon records pair each `Hash` field with an adjacent string field whose *own* name hash is
unresolved, so it decodes as a bare `hash="…"` attribute:

```xml
<value hash="B171F78F" type="String">dlc1_particles.dlc1.pl_muzzleflash_sawedgun</value>
<value name="psMuzzleParticlesId" type="Hash">DC471B0B</value>
```

The `Hash` is **plain CRC-32/ISO-HDLC of the companion string, exact case, not normalized**. Verified
on nine independent pairs from `WeaponProperties.DLC1.SawedOffShotgun`, all nine exact:

| String | CRC32 | Field |
|---|---|---|
| `FX_FIRE` | `AF2676DA` | `MuzzleBone` |
| `FX_CASING` | `D431B68E` | `BulletCaseBone` |
| `graphics\gfx\weapons\bullettracer_d.xbt` | `5D831E10` | `texTexture` |
| `dlc1_particles.dlc1.pl_muzzleflash_sawedgun` | `DC471B0B` | `psMuzzleParticlesId` |
| `weapons.weapons.muzzleflash_ithaca` | `C9445137` | `psMuzzleParticlesId_3rd` |
| `bullet_impact.bullet_impact.b_imp_water` | `19CB9FE7` | `psBulletUnderwaterId` |
| `ironsightfx` | `ED63A53E` | `IronsightFX` |
| `deserteagle` | `6D6540FA` | `ammoAmmoType` |
| `m79` | `704CA95D` | `HolsterHandle` |

Two consequences. First, **when you author a new weapon you must write both halves** — the readable
string *and* its CRC32 — because the engine reads the hash. Second, these are the same CRC32 the
`.fat` index uses for file paths, so `graphics\gfx\weapons\bullettracer_d.xbt` → `5D831E10` is also
its archive key; adding new asset paths means appending them to `tools/JackAll/assets/fc2.hashlist`
and re-running `jackall-cli system hash archiveitems`.

Two of these are worth reading twice: the sawed-off's `ammoAmmoType` is CRC32 of **`deserteagle`**
(it shares the Desert Eagle's ammo pool), and its `HolsterHandle` is CRC32 of **`m79`** (it holsters
using the M79's handle). Ammo types and holster handles are named after whichever weapon defined
them first.

## `WeaponProperties` — the stat archetype

`CWeaponProperties` decodes with readable field names throughout. The blocks, as they appear in
`WeaponProperties.DLC1.SawedOffShotgun`:

| Block | Notable fields |
|---|---|
| `CommonProperties` | `sName`, `sDisplayName`, `selCategory`, `selWeaponClass`, `selFireStrategy`, `selReloadType`, `selJamType`, `selReticleType`, `crosshairMagmaAreaName`, `fRange`, `vectorEffectiveRange`, `vectorEffectiveRangeIS`, `iClipsForSelfDestruct`, `bIsBreakable`, `archPickupArchetype`, `HolsterHandle` |
| `FireRate` | `iFireRate` (RPM), `selFireRateMode`, `fBusyDuration` |
| `Ammo` | `ammoAmmoType`, `iAmmoInClip`, `iMaxAmmo{Casual,Experimented,Hardcore,Infamous}`, `bUsesClips`, `bIsAmmoVisible` |
| `Recoil` / `RecoilIK` | `iRecoilRecoveryLevel`, `fRecoilMax`, `fRecoilAchieveTime`, `fIKRecoilBurst*`, `vIKRecoilBurstOffset` |
| `IronSight` | `bCanIronsight`, `fIronsightFOV`, `fIronsightTransitionTime`, `fMoveSpeedFactor`, `fLookSensitivityFactor`, `archTransitionCurve`, `IronsightFX` |
| `FireStrategyProperties` | shotgun spread, burst length, bullets per shot |
| `Sounds` (+ `ThirdPerson`) | `sndSingleBulletShot`, `sndEmptyWeapon`, `sndPassByWizz`, `sndHitPlayerSound`, and their `sndtp*Type` ints |
| `Particles` | `psMuzzleParticlesId`, `psMuzzleParticlesId_3rd`, `MuzzleBone`, `BulletCaseBone`, `psBulletUnderwaterId`, `psShineParticleId` |
| `FirstPerson` | `fBulletSpread_MovementModifier`, `BulletSpread{,_IronSight,Crouch,CrouchIronSight,Jump}` each `{fAmplitude, fFrequency}` |
| `BulletTracer` | `texTexture`, `fSpeed`, `fLength`, `fWidth`, `iFrequency` |
| `WeaponStims` / `ImpactStims` / `MuzzleStims` / `VictimStims` | `Stim_ImpactDamage`, `Stim_ImpactDamageSecondary`, `selType`, `nLevel`, `fRadius`, `fPhysImpulse` |
| `RangeMultipliers` | per-difficulty `RangeMultiplier` arrays |

The `sel*` enums are **self-describing in the file** — each one is followed by its own value list, so
you never have to guess an index:

```
enumCategory      Hand To Hand, Primary, Secondary, Special
enumWeaponClass   Machete, Pistol, Assault, SMG, Shotgun, Sniper, LMG, RPG,
                  CarlGustav, Flamethrower, Mortar, IED
enumFireStrategy  Bullet, Melee, Flame, Mortar, Projectile, IED
enumFireRateMode  SingleShot, FullAuto, PrepareShot
enumReloadType    Magazine, Bullet, None
enumJamType       Jam, Malfunction, StrategyManaged
enumReticleType   Type1, Type2, Type3
```

The sawed-off is `selCategory=2` (Secondary), `selWeaponClass=4` (Shotgun), `selFireStrategy=0`
(Bullet), `iFireRate=200`, `iAmmoInClip=1`.

`crosshairMagmaAreaName` is a plain string naming a Magma area — the sawed-off reuses the Ithaca's
(`a_ithaca`). This is the actual crosshair control; `iAnimationValue` is not, despite the community
claim (see below).

Behaviour is composed, not hardcoded per weapon: `CWeaponFireStrategy` has one concrete subclass per
`selFireStrategy` value, each with a matching `*Properties` class, so **a new weapon is a new data
archetype instantiating existing C++ classes — no new engine code**. See
[architecture](../engine-internals/architecture.md).

## The weapon entity archetype

`DLC1Weapons.DLC1.SawedOffShotgun` is where the art binds:

- **`CFileDescriptorComponent`** — an embedded RML `hidDescriptor` that mirrors the build-time
  `.xml` sidecar (`graphics\weapons\dlc\sawed_off_shotgun\dlc1_sawedoff_shotgun.xml`). It declares
  each part with its bone name and bbox, the `.xbg` resource and its bbox, the **complete skeleton
  with inline bone positions and rotations**, and a `RigidPhysComponent` naming the `.hkx`.
- **`CGraphicComponent`** — one entry per part, each carrying `objModel` (CRC32 of the `.xbg` path),
  `hidMeshName`, `hidNodeName` (CRC32 of the part name) and `hidNodeNameLOD0` (CRC32 of
  `PART_LOD0`). Part names are hashed **exact-case**, which is why a replacement mesh must keep them
  byte-identical — see [xbm-xbg](../file-formats/xbm-xbg.md).
- **`CSimpleAnimationComponent`** — `fileSkeleton` plus `sPartName`, the animation key.
- **`CFCXWeapon`** — `iAnimationValue`, `ReliabilityLevelsData` (`Failure`/`Low`/`Medium`/`High`,
  each with `fHorizontalRecoilPerShot`, `fVerticalRecoilPerShot`, `fJamProbabilityPerReload`),
  `WeaponStatusSwitchValues`, `bUseHiResScope`.
- **`CCountersComponent`**, **`CWeaponNetworkComponent`**, **`CPersistComponent`**.

## The hard ceiling: `iAnimationValue`

`CFCXWeapon.iAnimationValue` is **an index into the `EquippedWeapon` enum in
[`movemgr.bin`](../file-formats/move.md)**, which has exactly 44 entries. Confirmed against shipped
data: `DLC1.Crossbow` = 41, `DLC1.SawedOffShotgun` = 42, `DLC1.SilencedShotgun` = 43.

That enum lives only in the base file's `CMoveValueContainer`. A DLC-style MOVE expansion contains
no value container at all, so **it cannot introduce a 45th value**, and `MSAnim::LoadMoves` rejects
any base file that does not declare exactly 105 channels. The three DLC weapon slots were reserved
in the base game before the DLC shipped — the `SawedOffShotgun` string is present even in v1.0
`Dunia.dll`.

So a new weapon's `iAnimationValue` must be one of 0–43. In practice that means one of:

1. **Reuse an index**, and name your model after the weapon that owns it — set `sPartName` to
   `dlc1_sawedoff_shotgun` and you inherit the entire DLC1 animation graph for free. Costs you that
   weapon.
2. **Commandeer a slot that nothing uses.** `Ratchet`, `Phone`, `Watch`, `MapCompass` and `Compass`
   are candidates; check what actually references them first.
3. **Replace `movemgr.bin`**, which needs a writer for a format that is only partly decoded. The
   base path comes from a config key (`files` / `Move File` in
   `common/config/defaultengineconfig.xml`), so repointing it is at least cheap to try.

:::note[Corrects an earlier community claim]
`iAnimationValue` is described in [data-recipes](./data-recipes.md) as "reportedly affects which
crosshair is used". That is wrong — it is the `EquippedWeapon` index. The crosshair comes from
`CommonProperties.crosshairMagmaAreaName`.
:::

## HUD icon and kill-message code

[gotchas](./gotchas.md) records weapon icons as effectively blocked: partly hardcoded in
`Dunia.dll`, with `hud.mgb` unworkable by any tool. Both halves are now out of date, and there is a
sanctioned escape hatch that needs no binary patching at all.

The icon is selected from `CommonProperties.sName`, hashed into `CFCXWeaponIconMap` (`0x08a68fd0`),
a compiled-in `hash_map<u32, int>`. Its key strings sit contiguously at `0x0a14496f` in
`FarCry2_server`:

```
sawedoffshotgun  silencedshotgun  dlc1  dlc2  dlc3  dlc4  dlc5  dlc6
```

`CMagmaFacade::GetHudIconMaterialName` (`0x09608540`) maps the resulting enum values `0x54`–`0x59`
to `hud_icon_dlc_01` … `hud_icon_dlc_06`, plus `gun_icon_dlc` at `0x5a`. Those six textures ship —
`common/ui/textures/hud/icons_weapons/hud_icon_dlc_0{1..6}.xbt`, 4,256 B each — and are already
declared in `hud.mgb` and `hud.mgb.desc`. `gamemodesconfig.xml` reserves the matching kill-message
glyphs and says so outright:

```xml
<Weapon name="dlc1" code="~D1" /> ... <Weapon name="dlc6" code="~D6" />
<!-- The weapons MUST be named dlc1, dlc2 etc. for the switch gizmo to work -->
```

**So: set `sName` to `dlc1`, re-skin `hud_icon_dlc_01.xbt`, done.** No `Dunia.dll` patch, no `.mgb`
edit, no sweep across the eighteen localized UI folders. Six free slots — and note this is a
different budget from `iAnimationValue`, which reserves nothing.

`.mgb` is in any case fully round-trippable now (`jackall-cli mgb decode/encode/verify`), so a
seventh icon is a normal edit rather than a wall; the `dlc1`…`dlc6` path is just cheaper.

## Placement

DLC weapons are placed entirely from Lua — no world-sector `.fcb` surgery. `dlc1weaponsspawn.lua` is
a plain readable Domino box keyed on a parent-entity id, with hardcoded coordinates for ten sites:

```lua
local crate           = "DLC1Weapons.DLC1.WeaponCrate";
local sawedOffShotgun = "DLC1Weapons.DLC1.Pickup_SawedOffShotgun";

if (self.ParentEntity == "2058084353816150950") then          -- W1B2
  SpawnEntityFromArchetype(crate, 1275.58, 3108.97, 28.7483, 0, 0, -72);
  SpawnEntityFromArchetype(sawedOffShotgun, 1275.84, 3108.57, 29.4285, -2.9236, 54.9809, 141.695);
```

The base game pre-wires four empty DLC weapon slots — `common/domino/user/dlc/dlc{1..4}weapons.graph.lua`
and `dlc{1..4}world.graph.lua` — so a mod can drop straight into one. Related engine Lua verbs are in
[the Lua API surface](../engine-internals/lua-api-surface.md): `SpawnWeapon`, `AddWeapon`,
`SelectWeapon`, `DrawWeapon`, `RefillWeaponAmmo`, `GetWeaponBazaar`.

Other acquisition routes, and what they cost:

| Route | File | Constraint |
|---|---|---|
| Weapon shop | `gamemodesconfig.xml` `<Item category="weapons">` | [guide/vehicles](./guide/vehicles.md) reports entries can only be **replaced**, not added — untested against JackAll's fragment appender |
| Enemy / buddy loadout | `gamemodesconfig.xml` `<InventoryPacks>` | [guide/patrols](./guide/patrols.md): new inventory packs **cannot** be created; reuse an existing pack |
| Player start loadout | `Inventory.packInventoryPack` on `MainCharacter.PawnPlayer.*` | captured in savegames — new game only |
| Armory / dropped pickups | `xx_pickups.xml` archetypes | three entries per weapon, as above |
| Map editor palette | `ingameeditor/object_inventory.xml` | pure curation, no allow-list in the loader — the DLC weapons are simply not listed |

## Dependencies

Each weapon needs entries in the world's `depload`. The readable reference is
`downloadcontent/dlc1/dlc1/worlds/mp_dlc01_resort/generated/mp_dlc01_resort_depload.xml`, which
carries a `CGeometryResource` → `CMaterialResource` → `CTextureResource` chain per mesh, and one
`CAnimationPackageResource` per weapon — named by `sPartName`, with 75 `CAnimationResource` children
for the sawed-off, each pulling in its own `.spk` and shell mesh.

**Trap:** the parents array is sorted by CRC32 and must be re-sorted after any insert. Getting it
wrong misbehaves animations *without crashing*, so it is easy to miss until playtesting. See
[depload](../file-formats/depload.md).

## Tooling

| Piece | Tool | Status |
|---|---|---|
| Archetypes, stats, pickups | `jackall-cli fcb decode` / `encode` | works; see caveat below |
| New archetype into an existing library | stage `mods/generated/entitylibrary.fcb/Weapons/<Name>.xml` in a layer | `FcbAssembler` appends unmatched fragment ids as new content — a tested path |
| Checking nothing shadows your archetype | `jackall-cli mod lint` | models the real override chain |
| Finding what references what | `jackall-cli xref build` then `xref to` / `xref from` | |
| Mesh, materials, textures, animation | `jackall-cli fc2model export` → Blender → `fc2model extract` | one decoded file; see [below](#geometry-materials-and-textures-one-file-one-plugin) |
| A HUD icon, or any texture outside a model | `jackall-cli xbt extract` → edit DDS → `xbt build` | header XML is required, not optional — every header byte must come from a real file. A model's own textures travel as PNG in the pack instead |
| Shop / menu UI | `jackall-cli mgb decode` / `encode` / `verify` | byte-exact round trip |
| Sounds | `jackall-cli spk list/extract/import`, `sbao build` | |
| Packaging | `jackall-cli mod build -g <game> -l <layer>`, `mod restore` to undo | there is no loose-file override; a mod is a rebuilt `patch.dat`/`patch.fat` — see [getting-started](./getting-started.md) and [vortex](./vortex.md) |

:::note[The DLC library decodes, but does not re-encode byte-exact]
[getting-started](./getting-started.md) records `downloadcontent/dlc1/…/entitylibrary.fcb` as
undecompilable by Gibbed's `ConvertBinary`. **JackAll decodes it fine**, into
`1_DLC1Weapons.xml`, `2_vehicle.xml` and `3_WeaponProperties.xml`.

Re-encoding is a different matter: the output is 322 KB against the original's 109 KB, because the
encoder expands the `0xFE` back-references the original uses for repeated values and objects rather
than re-emitting them (object count goes 945 → 4,550). The decode is nonetheless **semantically
lossless and stable** — `decode(encode(decode(x)))` produces XML byte-identical to `decode(x)`
across all three sub-files.

**The inflated file does load.** Scubrah's Patch overrides fragments inside this library, which
forces exactly that re-encode into `patch.dat`, and the DLC archetypes still resolve and spawn in a
running game.
:::

## Geometry, materials and textures: one file, one plugin

:::info[Verified end to end]
`tools/BlenderFC2/tests/blender_transplant.py` rebuilds a weapon from a donated mesh using nothing
but the Blender add-on and stock Blender — transplant, check, unwrap, export, apply — and reads the
written `.xbg` back at full part count with its own reload still posing it. Nothing in the run
reaches past the add-on into a file format.
:::

The art half used to mean roughly twenty one-off scripts, a trip outside the repo to convert a
texture, and a working knowledge of chunk padding, bone palettes, mip companions and the `Weapon`
shader's missing albedo slot. It is now one file and one plugin.

```
jackall-cli fc2model export graphics/weapons/dlc/sawed_off_shotgun/dlc1_sawedoff_shotgun.xbg \
    --game "C:\Games\Far Cry 2" --clips -o shotgun.fc2model
```

That collects the mesh, its materials, its textures, the rig beside it and every animation bank that
names it into one [`.fc2model`](../file-formats/fc2model.md) — a zip with **no Dunia format inside
it**: JSON and flat float arrays for the mesh, JSON for the materials, PNG for the textures. JackAll.App
does the same from **Export as .fc2model** on any `.xbg`.

Open it with **File ▸ Import ▸ Far Cry 2 Model Pack**, work, and export it back. Applying is
**Apply .fc2model** in the app, which stages the changed files into the workspace, or
`jackall-cli fc2model extract` for a folder to drop into a mod layer. Only what actually changed is
written — a texture travels as PNG, so re-encoding an untouched one would recompress it on every
save.

Four things the plugin now does that this page used to have to explain:

- **It tells you where geometry belongs.** *Measure motion* reports, per bone, the worst rotation and
  translation across every bank the pack carries. The table below is what it prints for the
  sawed-off; you no longer have to know it in advance.
- **It refuses an export that would silently lose your work** — a new object export would skip, a
  part dragged in object mode (positions are object-local, so the drag is discarded), a part left
  unwrapped, a vertex in no vertex group. Each says what to do instead.
- **It warns about channels the format does not carry** — metalness, roughness maps, emission — and
  about the `Weapon` shader's missing albedo slot, with the recipe below as the fix.
- **It shows what the format *does* carry**, so a specular or normal-map edit is visible rather than
  taken on trust.

Every rule is silent on the game's own models: retail is the definition of valid, so a rule that
fires on a shipped weapon is a wrong rule.

### What it still cannot do

- **Remove a part, or add a node or an LOD.** A part can now be added — select the mesh and use
  **Add as New Part**, and export appends it, leaving every part already there untouched (see
  [adding a part](../file-formats/xbm-xbg.md#adding-a-part-to-a-model-that-shipped-without-one)).
  Removing one, and adding a node or a whole LOD tier, still have no scene-to-document path.
  Reusing the donor's parts is still the easier road where it fits, since `CGraphicComponent` hashes
  part names exact-case and the MOVE graph and `.skeleton` bind to them: an added part draws, but
  nothing outside the mesh knows its name.
- **No split UVs, normals or colours** — the file stores all three per vertex, so a seam must be a
  duplicated vertex. The plugin now counts them for you instead of letting the first corner quietly
  win.
- **`.hkx` collision is untouched.** Reshape a weapon and it keeps the donor's collision shape.

## Replacing a weapon's art

:::info[Verified in a running game]
A donated mesh with its own textures and materials, built through `tools/BlenderFC2`, packaged with
`jackall-cli mod build` and played. Eight files, all overrides of existing paths — no new asset
paths, so no hashlist entry and no `depload` work.
:::

The replacement was the DLC1 sawed-off: `dlc1_sawedoff_shotgun.xbg` (all six LODs), its two state
`.xbt` textures with their `_mip0` companions, and the three `.xbm` materials it owns. The `.hkx`,
the `_ref.skeleton` and all 49 `.mab` clips were left alone.

**`patch.dat` overrides a file whose home is a DLC archive.** This is the load-bearing assumption
under any DLC-content mod and it holds: an override staged at `graphics\weapons\dlc\…` in
`patch.dat` is what the engine loads, even though the vanilla file lives in
`downloadcontent\dlc1\entitylibrary.dat`. Confirmed twice — once with a `.lua` whose home is
`downloadcontent\dlc1\dominos.dat`, once with the weapon mesh itself.

### Which part you put geometry in decides how it animates

The clips drive the donor's parts by name, so the cut is an animation decision, not just a material
one. **The plugin's *Measure motion* prints this for whatever pack is open** — the table below is the
sawed-off's, measured across all 49 of its clips as the largest departure from each bone's rest pose,
and it is here as the worked example rather than as something to memorise:

| Part | Turns | Moves |
| --- | --- | --- |
| `FRAME` | 0° | 0 m |
| `CLIP` | 177° | 1.01 m |
| `ACCESSORY` | 31° | 0.18 m |
| `ACCESSORY2` | 45° | 0 m |

`FRAME` never moves at all, so it is where the body belongs. `CLIP` is the break-action hinge — on
the sawed-off it carries the barrels, which swing fully open on reload. Anything mapped onto it will
swing with them, and anything left on `FRAME` will not.

A weapon whose parts do not line up with the donor's is still workable: a submesh you cannot fill
needs a marker triangle rather than a zero face count, because
[no shipped cluster draws nothing](../file-formats/xbm-xbg.md#authoring-ceilings).

### Giving a weapon its own colour

:::note[The whole job is on its own page]
[Texturing a replaced weapon](./texturing-a-weapon.md) covers this end to end — finding a material
you are allowed to own, the PBR conversion, and the specular settings that decide whether the weapon
reads matte or polished. What follows is the summary.
:::

The `Weapon` shader has **no albedo slot**. Colour comes from two shared, game-wide tiling maps
blended by a per-model mask, so a donated colour texture has nowhere to go and a mask can only say
*where*, never *what colour*. Rewriting the `.xbm` gives it somewhere:

- `DiffuseTexture1` → a texture the weapon already owns, with `DiffuseTiling1` set to `1,1` so it
  lands on the model's own UVs instead of tiling.
- `MaskTexture1` and `MaskTextureBroken` → a control map with **green at 0** so the second tiling
  layer never blends, and **blue at 1** so the tint weight is full.
- `DiffuseColorBase` and `DiffuseColor1`, and their `Clean`/`Broken` variants, set so no weapon
  condition re-tints the texture.

With blue held at 1, `DiffuseColor1` becomes a plain per-material multiplier. That is the right place
to put hue: the texture carries wear and detail, the material carries the colour, and one atlas can
serve a steel receiver and a wooden grip through two materials.

A weapon typically owns exactly two texture paths (its two damage-state masks), which is enough for
one albedo and one control map, and not enough for a normal map. The cheapest way to rebuild them is
to swap the PNGs inside a `.fc2model` pack and let the applier re-encode and split the pair; going
through `jackall-cli xbt extract` / `xbt build` works too but leaves the `_mip0` split to you.
**The replacement may change dimensions** — 512²/1024² was raised to 1024²/2048² and loaded fine, so
the `_mip0` relationship is "twice the base", not a fixed size.

### Four things that went wrong, and why

The first three are the same trap seen from three angles, and the plugin now names it: a material
driving **Metallic** raises `channel.metallic`, with the band below as the fix. The fourth it cannot
see, because it is about where you make the cut.

- **A physically based albedo is far too dark for this shader.** Doom's texture measured 0.05 to 0.12
  luma and up to 3.7 times as red as blue, because its metal gets brightness from a metalness map
  this shader has no equivalent for. Lit, it reads as black plastic with rust.
- **Fix it in the texture, not the material.** Reaching a metal level from 0.05 needs about 8x, and
  DXT1 gives red 5 bits — multiplying through `DiffuseColor1` afterwards bands it badly. Do it in
  float before quantisation.
- **A gamma lift is the wrong curve.** It compresses the top, so it burns pale areas to white under
  the game's sun while barely moving the darkest metal. Fit the albedo into a band with a floor and
  a ceiling instead; nothing can then blow out under any light.
- **Never split one visual surface across two clusters with different materials.** If the cut assigns
  triangles by count rather than by an edge loop, triangles at the same position land in different
  clusters and the boundary interleaves triangle by triangle. That is invisible while both clusters
  draw the same thing and reads as a row of saw teeth the moment their materials differ.

## Not solved

| Gap | Detail |
|---|---|
| **`.xbm` materials** | Solved, in JackAll. All 2,379 shipped materials round-trip byte-identically and rewritten ones load in game — see [xbm-xbg](../file-formats/xbm-xbg.md#the-xbm-body-and-writing-one-back). A pack carries them as JSON, so nothing outside JackAll parses one. |
| **`.hkx` collision** | Not parsed at all. Reuse the donor's — a reshaped weapon keeps its collision shape. |
| **`.mab` authoring** | Solved. Pose the rig and **Write Animation** puts the Action back into the bank, rewriting only the clip that fits this model and leaving the character's byte for byte. See [rewriting one clip](../file-formats/mab.md#rewriting-one-clip-and-leaving-the-rest-alone). |
| **Adding a part** | Solved. **Add as New Part** appends a Blender mesh to the model, and every shipped mesh takes one with its own parts unchanged, 3,133 of 3,133 — see [adding a part](../file-formats/xbm-xbg.md#adding-a-part-to-a-model-that-shipped-without-one). The part lives at the LOD you imported, and nothing outside the mesh binds to its name. |
| **Adding a node or an LOD** | The container [carries either](../file-formats/xbm-xbg.md#a-container-can-be-authored-not-just-edited), but nothing turns a Blender bone into a node or generates a new LOD tier. |
| **MOVE authoring** | Header, class-ID table, channel table and merge semantics are decoded; per-state record interiors are not. See [move](../file-formats/move.md). |
| **New `.spk` sound ids** | Only replacement of an existing record is documented; how a new id is minted is not. |
| **Missing `depload` entries** | Whether an absent asset fails to load, loads late, or is fine has never been tested. |

The vendored `tools/third-party/Dunia-Engine-XBG-Blender-Importer/` additionally claims `.xbm`, `.xbt`
and HKX+MOPP export, which would close the first two rows — but
[xbm-xbg](../file-formats/xbm-xbg.md) records its weapon XBG import as **confirmed broken, reproduced
on the AK-47 and a 1911**, so re-verify before relying on any of it.

## Checklist

1. Pick an `iAnimationValue` from 0–43 and set `sPartName` to match whatever owns that slot's
   animation set.
2. `fc2model export` the donor, reshape it in Blender, **Check**, and export the pack back. Its
   materials, textures and animation come with it. Keep every internal part and bone name
   byte-identical.
3. Clone the donor's `.hkx` under the new path — nothing parses it, so the collision shape is the
   donor's whatever the mesh became.
4. Append the new paths to `tools/JackAll/assets/fc2.hashlist`; run `jackall-cli system hash archiveitems`.
5. Author the seven archetypes as FCB fragments. Set `sName` to `dlc1` (or 2–6).
6. Write **both halves** of every hash-backed field: the string and its exact-case CRC32.
7. Re-skin `hud_icon_dlc_01.xbt` via `xbt extract` / `xbt build` — it belongs to no model, so it is
   not in the pack.
8. Add the resources to the world's `depload`, then re-sort the parents array by CRC32.
9. Spawn it from a Domino Lua box in one of the four pre-wired DLC slots.
10. `jackall-cli mod lint`, then `mod build`.
11. **Start a new game** — `Inventory` and `CPickupWeapon` are captured in savegames. Ballistics are
    archetype-only and read fresh at spawn, so those you can iterate on a live save.

There is no in-game dev console, so every iteration is a full repack and relaunch.
`tools/misc/modpatcher/` is a working loose-file proxy that would make a faster inner loop for
texture work, but its own notes warn that `LevelAsset_OpenStream` bypasses the hook — validate the
final build through `patch.dat` regardless.
