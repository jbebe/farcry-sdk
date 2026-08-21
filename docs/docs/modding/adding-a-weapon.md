---
sidebar_position: 5
---

# Adding a new weapon

:::info[Measured against the shipped DLC, with the engine side RE-verified]
No community mod has ever added a new weapon — [the survey](./mods-survey.md) turns up only
rebalances, reskins and unlocks — and every other weapon page on this site edits an existing entry.
This page is built from two things instead: Ubisoft's own DLC1 weapon pack, decoded field by field
out of `tmp/gamefiles`, and the engine code that consumes it, traced through GhidraMCP against
`FarCry2_server`. Nothing here has been round-tripped into a running game yet, so treat the
*procedure* as untested even though each individual fact is measured.
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
| HUD icon and textures | `jackall-cli xbt extract` → edit DDS → `xbt build` | header XML is required, not optional — every header byte must come from a real file |
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
across all three sub-files. Whether the game loads the inflated file is untested.
:::

## Geometry: edit a donor, don't build from scratch

`tools/BlenderFC2` exports meshes — **File ▸ Export ▸ Far Cry 2 Mesh (.xbg)** — and the shape of that
exporter happens to match this job exactly. It *edits* the container rather than rebuilding it: nodes,
materials, bone palettes, un-imported LODs and every opaque chunk survive untouched, and an untouched
export returns the source bytes through Blender for the AK-47, a 37-part vehicle and a skinned
character. A part's topology can change freely — the tangent frame is regenerated from the UVs, and
`tests/blender_export.py` subdivides a part from 805 to 2,637 vertices. Bounding spheres and boxes are
refitted whenever geometry moves, which matters because culling reads them.

Its limits line up with the archetype work above rather than fighting it:

- **Cannot add or remove parts, nodes or LODs.** Your weapon inherits the donor's part list — which is
  what you want anyway, since `CGraphicComponent` hashes part names exact-case and the MOVE graph and
  `.skeleton` bind to them.
- **No split UVs, normals or colours** — the file stores all three per vertex, so a seam must be a
  duplicated vertex.
- **Cannot outgrow the source's quantisation.** Positions are int16 against the file's own `PMCP`
  scale, so a much larger model than the one being replaced is refused rather than silently wrapped.

So the practical route is: pick a donor weapon with a part layout you can live with, reshape its parts,
and keep every name byte-identical.

## Not solved

| Gap | Detail |
|---|---|
| **`.xbm` materials** | No writer anywhere in-repo; both readers are parse-only. Reuse a donor's `.xbm` when the shader matches (`Weapon` for a gun body, `Generic` for shells). |
| **`.hkx` collision** | Not parsed at all. Reuse the donor's. |
| **`.mab` authoring** | Clips now decode fully — the sparse eight-frame keyframe groups are solved and **File ▸ Import ▸ Far Cry 2 Animation (.mab)** loads one onto an armature as an Action. There is still no *export* path, so a new clip cannot be authored. See [mab](../file-formats/mab.md). |
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
2. Clone the donor's `.xbg`, `.hkx`, `_ref.skeleton` and state textures under new paths, keeping every
   internal part and bone name byte-identical. Reshape the mesh in Blender if you want your own
   silhouette.
3. Append the new paths to `tools/JackAll/assets/fc2.hashlist`; run `jackall-cli system hash archiveitems`.
4. Author the seven archetypes as FCB fragments. Set `sName` to `dlc1` (or 2–6).
5. Write **both halves** of every hash-backed field: the string and its exact-case CRC32.
6. Re-skin `hud_icon_dlc_01.xbt` and the two state masks via `xbt extract` / `xbt build`.
7. Add the resources to the world's `depload`, then re-sort the parents array by CRC32.
8. Spawn it from a Domino Lua box in one of the four pre-wired DLC slots.
9. `jackall-cli mod lint`, then `mod build`.
10. **Start a new game** — `Inventory` and `CPickupWeapon` are captured in savegames. Ballistics are
    archetype-only and read fresh at spawn, so those you can iterate on a live save.

There is no in-game dev console, so every iteration is a full repack and relaunch.
`tools/misc/modpatcher/` is a working loose-file proxy that would make a faster inner loop for
texture work, but its own notes warn that `LevelAsset_OpenStream` bypasses the hook — validate the
final build through `patch.dat` regardless.
