# VSS Vintorez

Replaces the single-player **Dart Rifle** with a VSS Vintorez: the VSS's mesh on the Dragunov's
skeleton and animation set, semi-automatic, ten-round magazine off the sniper ammo pool.

**Status: geometry and textures done, playing and confirmed in game.** See
[what is left](#what-is-left) for the parts that are not art.

The procedure this mod was built by is written up in
[replacing an existing weapon](../../docs/docs/modding/replacing-a-weapon.md) and
[texturing a replaced weapon](../../docs/docs/modding/texturing-a-weapon.md). This README is the
mod; those pages are the method.

## What it changes

Eleven files, all overrides of paths that already exist.

```
mods/graphics/weapons/special/dart_rifle/dart_rifle.xbg     the VSS, all five LOD tiers
mods/graphics/weapons/special/dart_rifle/dart_rifle.hkx     the Dragunov's collision shape
mods/graphics/weapons/special/dart_rifle/dart_rifle_state_01.xbt   the albedo, + _mip0
mods/graphics/weapons/special/dart_rifle/dart_rifle_state_02.xbt   the control map, + _mip0
mods/graphics/_materials/FBOIVIN2-M-2007050148031384.xbm    DART_RIFLE_METAL, repointed
mods/worlds/world1/generated/entitylibrary.fcb/...          two archetype fragments
mods/worlds/world2/generated/entitylibrary.fcb/...          the same two, per world
```

The mesh was built inside the Dragunov's pack, so it inherited the Dragunov's material table. Its
body clusters are moved onto `DART_RIFLE_METAL` — a material nothing else uses, already in both
worlds' `depload`, and unreferenced once this mod replaces the mesh that was its only reader. The
material is **appended** to the mesh's list rather than swapped in place, because `SCOPE_HI` draws
144 triangles through the body's old material and has to keep doing so.

`weapons.Special.Dart_Rifle` is the Dragunov's entity archetype with the identity and the model path
changed — that carries the five-part list including `ACCESSORY02`, the Dragunov skeleton,
`sPartName`, `iAnimationValue` and `bUseHiResScope` across in one piece. The bounding boxes are then
regenerated from the shipped mesh.

`WeaponProperties.Special.Dart_Rifle` is the vanilla Dart Rifle with thirteen values changed:

| field | from | to |
| --- | --- | --- |
| `sDisplayName` | `Dart` | `VSS Vintorez` |
| `selFireRateMode` | 2 PrepareShot | 0 SingleShot |
| `iFireRate` | 120 | 85 |
| ammo string + `ammoAmmoType` | `darts` / `FC2096BC` | `sniperrifle` / `7D6BD5F2` |
| `iAmmoInClip` | 1 | 10 |
| `iMaxAmmo{Casual,Experimented,Hardcore,Infamous}` | 9/4/3/2 | 40/20/20/10 |
| `BulletCaseBone` + twin | `FX_CASING` / `D431B68E` | `FX_Casing` / `2365743E` |
| `fIronsightFOV` | 0.3 | 0.28 |

It keeps `bIsSilent = True`, `selCategory = 3` (Special slot), `sName`, and `archPickupArchetype`.

## Install

```
jackall-cli mod build   --game "C:\Games\Far Cry 2" --layer mods\vss-vintorez\layer
jackall-cli mod restore --game "C:\Games\Far Cry 2"      # undo
```

**Buy the weapon again after installing.** A weapon already in inventory keeps the archetype it was
acquired with, so a save that already holds a Dart Rifle shows the new model on the old behaviour.

## Building it

**Only the layer is checked in.** The build machinery that produced it — the transplant and bake
scripts, the donor pack, the source model, the reference renders — stays out of the repo: the
scripts are hardcoded to this weapon and this machine's paths, the donor pack is regenerable in one
command, and the source model is third-party.

What transfers is the method, and it is written up in full:

- [replacing an existing weapon](../../docs/docs/modding/replacing-a-weapon.md) — the archetype and
  the mesh, including which part each piece of geometry belongs on and why `SCOPE_HI` is a complete
  sight picture rather than an eyepiece.
- [texturing a replaced weapon](../../docs/docs/modding/texturing-a-weapon.md) — the PBR conversion,
  finding a material this weapon is allowed to own, and the specular settings that decide whether it
  reads matte or polished.

The one rule worth repeating here, because every scope defect in this build's history was a legal
file that passed every numeric gate: **render it and compare against the donor before installing.**
The donor works in game, so any difference between the two images in the same view is your bug.

## What is left

- **One material for the whole body.** The stock takes the same specular response as the steel,
  separated only by the control map's red rather than by a material of its own. Splitting it needs
  the transplant re-run, not new textures.
- **No normal map.** The weapon owns two texture paths and both are spent, on the albedo and the
  control map. The source's 4096² normal map has nowhere to go without minting a new asset path.
- **The pickup archetypes.** `pickups.Weapons.DartRifle_new*` keep their own four-part lists, so the
  weapon on the ground loses its barrel at close range and regains it at distance — the part list is
  per archetype, and coarser LOD tiers fold `ACCESSORY02` into `FRAME`.
- **Lethality.** It still tranquilizes: `WeaponStims` / `ImpactStims` are the Dart Rifle's, and the
  projectile is still `dart.xbg`.
- **Icons.** `sName` is unchanged, so the HUD and bazaar icons are still the dart syringe.
- **`FX_FIRE`** is at the Dragunov's muzzle, which is further forward than the VSS's.

## Credit

This work is based on **"VSS «Винторез»"**
(https://sketchfab.com/3d-models/vss-b1ef04a89cd44300b082d952fea94957)
by **Zol4ik** (https://sketchfab.com/Zol4ik) licensed under
**CC-BY-4.0** (http://creativecommons.org/licenses/by/4.0/).
