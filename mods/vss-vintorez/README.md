# VSS Vintorez

Replaces the single-player **Dart Rifle** with a VSS Vintorez: the VSS's mesh on the Dragunov's
skeleton and animation set, semi-automatic, ten-round magazine off the sniper ammo pool.

**Status: complete and confirmed in game** — mesh, LOD tiers, textures, a worn appearance that grimes
as the weapon degrades, the weapon on the ground, the muzzle socket, icons, name, and jam/break
behaviour. See [what is left](#what-is-left) and [deliberate, not missing](#deliberate-not-missing).

The procedure this mod was built by is written up in
[replacing an existing weapon](../../docs/docs/modding/replacing-a-weapon.md) and
[texturing a replaced weapon](../../docs/docs/modding/texturing-a-weapon.md). This README is the
mod; those pages are the method.

## What it changes

Twenty-one files. Most are overrides of paths that already exist. Two are at paths invented for this
mod, and they need different treatment:

- `vss_worn_c.xbt` loads from `patch.dat` with no hashlist and no `depload` entry — a texture reached
  through a material that is itself listed needs no registration.
- the reload clip does not. An animation at an unlisted path never plays, so it is registered under
  the `dragunov` animation package — the one `sPartName` names — by the two `_depload.dat` fragments
  below. See [depload](../../docs/docs/file-formats/depload.md#animations-are-not-like-textures).

```
mods/graphics/weapons/special/dart_rifle/dart_rifle.xbg     the VSS, all five LOD tiers
mods/graphics/weapons/special/dart_rifle/dart_rifle.hkx     the Dragunov's collision shape
mods/graphics/weapons/special/dart_rifle/dart_rifle_state_01.xbt   the albedo, + _mip0
mods/graphics/weapons/special/dart_rifle/dart_rifle_state_02.xbt   the control map, + _mip0
mods/graphics/weapons/special/dart_rifle/vss_worn_c.xbt     the worn control map, a new path
mods/graphics/_materials/FBOIVIN2-M-2007050148031384.xbm    DART_RIFLE_METAL, repointed
mods/ui/textures/hud/icons_weapons/hud_icon_sniperdart.xbt  the HUD and bazaar icon
mods/ui/textures/guns/gun_icon_sniperdart.xbt               the multiplayer weapon select
mods/languages/english/oasisstrings.rml                     the weapon's name, ten strings
mods/graphics/characters/.../vss_vintorez/...vssvi_i1.mab   the reload, at a new path
mods/graphics/move/movemgr.bin                              the MOVE graph, reload repointed
mods/worlds/world1/generated/entitylibrary.fcb/...          four archetype fragments
mods/worlds/world2/generated/entitylibrary.fcb/...          the same four, per world
mods/worlds/world1/generated/world1_depload.dat/dragunov.3882209901.xml   registers the clip
mods/worlds/world2/generated/world2_depload.dat/dragunov.3882209901.xml   the same, per world
```

The name and the icons are bound by name in `engine\gamemodes\gamemodesconfig.xml`, so both are
replacements of what a name points at rather than config edits. The bazaar name is **not**
`sDisplayName` — it is `nameOasis="WEAPONBAZAAR_DART_RIFLECRATE_NAME"`, resolved against
`oasisstrings.rml`.

The four entitylibrary fragments are the weapon, its stats, and the two pickups. **The pickups carry
their own part lists**, so until they were rebuilt from the Dragunov's the weapon on the ground had
no barrel at close range and grew one back at distance — LOD1 and below fold `ACCESSORY02` into
`FRAME`.

The two `depload` fragments each add one line: the reload clip's `CPathID` as a `CAnimationResource`
child of the `dragunov` package. Without them the clip is in `patch.dat` and referenced by
`movemgr.bin`, and still never plays — the weapon enters the reload state and stays there. The
package is `dragunov` because that is what this weapon's `sPartName` is; `dart_rifle` would be
accepted and do nothing.

The mesh was built inside the Dragunov's pack, so it inherited the Dragunov's material table. Its
body clusters are moved onto `DART_RIFLE_METAL` — a material nothing else uses, already in both
worlds' `depload`, and unreferenced once this mod replaces the mesh that was its only reader. The
material is **appended** to the mesh's list rather than swapped in place, because `SCOPE_HI` draws
144 triangles through the body's old material and has to keep doing so.

`weapons.Special.Dart_Rifle` is the Dragunov's entity archetype with the identity and the model path
changed — that carries the five-part list including `ACCESSORY02`, the Dragunov skeleton,
`sPartName`, `iAnimationValue` and `bUseHiResScope` across in one piece. The bounding boxes are then
regenerated from the shipped mesh.

`WeaponProperties.Special.Dart_Rifle` is the vanilla Dart Rifle with nineteen values changed:

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
| `fUnjamTime` | 4.4 | 2.5 |
| `nForcedFailureMin/MaxCausal` | 0 / 0 | 2 / 2 |
| `nForcedFailureMin/MaxExperimented` | 0 / 0 | 1 / 1 |
| `nForcedFailureMaxHardcore` | 0 | 1 |

The last four give it the **Dragunov's** reliability rather than the Dart Rifle's, which never fails
at all. `fJamProbabilityPerReload` — in `ReliabilityLevelsData` on the *weapon* archetype, per reload
and zero at full condition — and `iClipsForSelfDestruct` = 20 needed no change: the two weapons
already carry identical values for both.

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
- **No normal map.** Not for want of a texture path — minting one is proven to work, and the worn
  control map uses it. **No `Weapon`-shader material declares a `NormalTexture1` slot at all**, across
  all nine on the Dragunov, the Dart Rifle and the sawed-off, so whether the shader would sample one
  is unknown. Settling it means disassembling the `Weapon` pixel shader out of `shadersobj.fat`.
- **Only English is renamed.** Ten other languages ship `oasisstrings.rml` and still carry the same
  ten strings saying "Dart Rifle" in the bazaar, the challenge list and the statistics.
- **`pickups.Weapons.DartRifle_new.Multi.Dropped`** was skipped on the single-player rule, so a
  dropped VSS in multiplayer still has no barrel.

## Deliberate, not missing

- **Damage is the Dart Rifle's.** `Stim_ImpactDamage.nLevel` is 5 against the Dragunov's 25 and
  `fPhysImpulse` 10 against 60, so it kills a soft target but does little to a tough one — which is
  what makes it a stealth weapon rather than a battle rifle. It does **not** tranquilize: both
  hit-location severities are `Kill`, it fires `Bullet` rather than `Projectile`, and no dart is
  spawned. `MuzzleStims.fRadius` is 2.5 m against the Dragunov's 150, and that — not `bIsSilent` —
  is what carries the suppression.
- **The coarse LOD tiers cost more than the donor's.** LOD3 and LOD4 ship LOD2's budget, 1,428 and
  1,189 against 278 and 96, because this model is some forty disconnected shells and decimating below
  LOD2 turns them into slivers. Accepted; reaching the donor's numbers needs hand-authored tiers.
- **LOD0 is undecimated** at 20,390 triangles, about 1.6× the heaviest weapon Ubisoft shipped. The
  first-person viewmodel never leaves LOD0, so it is the one mesh whose detail is always on screen.

## Credit

This work is based on **"VSS «Винторез»"**
(https://sketchfab.com/3d-models/vss-b1ef04a89cd44300b082d952fea94957)
by **Zol4ik** (https://sketchfab.com/Zol4ik) licensed under
**CC-BY-4.0** (http://creativecommons.org/licenses/by/4.0/).
