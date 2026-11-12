---
sidebar_position: 26
---

# Muzzle flash

:::info[Verified via reverse engineering]
Traced in `FarCry2_server` (symbols); the addresses are the server's. The light's loader is also in
Steam `Dunia.dll`, reading the same names. Not yet seen in a running game.
:::

A shot shows two things: a light, the same for every gun, and a particle system the gun names.

## The light

Every game mode runs one `CMuzzleFlashManager` service, with its properties in
`engine\gamemodes\gamemodesconfig.xml`'s `DefaultMuzzleFlashManager` block, read by
`CMuzzleFlashManager::LoadFromXML_Legacy` (`0x091B79F0`):

| Attribute | Offset | Retail |
|---|---|---|
| `clrColor` | `+0x68` | `1,1,1` |
| `fIntensity` | `+0x74` | 1 |
| `fRadius` | `+0x78` | 2 |
| `fLifeSpan` | `+0x7C` | 0.06 |
| `fTimeFlushHistory` | `+0x80` | 20 |
| `iMaxHistory` | `+0x84` | 20 |

`CWeaponFireBulletStrategy::ApplyDelayBullet` (`0x08F5A3E0`) asks for the light after
`SpawnBulletCasing` and `SpawnMuzzleFlash`, when the byte at `+0x1B1` of the fire strategy's
properties is set. That byte is `bHasMuzzleLight` by its values **(inferred)**: it is off on the
MP5SD, the silenced Makarov and the Dart Rifle only. The call is
`RequestMuzzleFlash(EntityId, muzzle bone, 0, 0, 0, zero vector, false)` (`0x091B8270`): the three
zeros are the request's radius, lifespan and intensity, at `+0x1C`, `+0x20` and `+0x24` of the
request, and the zero vector its colour.

`CMuzzleFlashManager::Update` (`0x091B8470`) drives one scene light at the muzzle bone of the
shooter's equipped weapon. A zero in the request takes the manager's own value, so every gun's light
is the block's: no weapon data changes its size, brightness or colour. The radius is the radius times
`sin(pi * (1 - t / lifespan))`, so it peaks halfway through the light's life. A newer request replaces
an older one: one light at a time, the latest shot's. Explosions call the other overload
(`0x091B80C0`, from `SpawnExplosionFX` and `SpawnRemainFX`) with a position and values of their own.

Whether the light falls on the ground is `TerrainAffectedByMuzzleFlash` in
`engine\settings\defaultrenderconfig.xml`, read by `CMuzzleFlashManager::UpdateAffectTerrain`; retail
sets it only on the highest terrain quality.

## The flash

A weapon's `Particles` object names its flash: `psMuzzleParticlesId` in first person,
`psMuzzleParticlesId_3rd` as anyone else sees it, and `MuzzleBone`, `FX_FIRE` on every gun. Each is a
particle system's [resource id](../file-formats/depload.md#the-particle-library), the `CPathID` of its
name. `CWeaponFireBulletStrategy::SpawnMuzzleFlash` (`0x08F50190`) starts the system at the muzzle
bone's world matrix.

Every world's library carries the same flashes and the DLC's its shotguns', and the weapons use all
of them: no unused one ships.

| First person | Third person | Guns |
|---|---|---|
| `pl_muzzleflash_ak47` | `muzzleflash_ak47` | AK-47, FAL, G3, AR-16, and the USAS-12 in first person |
| `muzzleflash_ak47` | none | the M2 carried |
| `pl_muzzleflash_base_gun` | `muzzleflash_base_gun` | Makarov, Star .45, Uzi, MAC-10 |
| `pl_muzzleflash_desert_eagle` | `muzzleflash_desert_eagle` | Desert Eagle |
| `pl_muzzleflash_sniper_riffle` | `muzzleflash_sniper_riffle` | Dragunov, M1903, MP5SD, Dart Rifle: smoke only in first person |
| `pl_muzzleflash_as50` | `muzzleflash_as50` | AS50 |
| `pl_muzzleflash_ithaca` | `muzzleflash_ithaca` | Ithaca, SPAS-12, the USAS-12 and sawed-off as others see them |
| `pl_muzzleflash_m249saw` | `muzzleflash_m249saw`, `muzzleflash_m249saw_mounted` | M249, mounted M249 |
| `pl_muzzleflash_pkm` | `muzzleflash_pkm` | PKM |
| `muzzleflash_m249saw_mounted` | `muzzleflash_m249saw_mounted` | mounted M2 |
| `muzzleflash_grenade_launcher` | `muzzleflash_grenade_launcher` | MGL-140 |
| `dlc1_particles.dlc1.pl_muzzleflash_sawedgun`, `pl_muzzleflash_silentshotgun` | | the DLC's shotguns |

The names but the DLC's start `weapons.weapons.`. Every flash draws one texture,
`graphics\gfx\weapons\gfx_muzzle_flash_co.xbt`: 256 by 128, two flame shapes played as
`FrameCount="2"`. What tells the flashes apart is their emitters. In an emitter drawing that texture:

- **Size** is `InitSizeKF`, from 0.05 m for each prong of a star to 0.8 m for the Desert Eagle as
  others see it, and `PosOffset` places it.
- **Brightness** is `HDRMul`, 1.5 on the first-person flashes and left out on the third-person ones,
  and `InitAlphaKF`.
- **Length** is `InitLifeTimeKF`, 0.01 to 0.05 s, with `EmitDuration` and `FramePerSecond` over the
  two frames.
- **Shape** is how many emitters there are and where: the M249's and PKM's flashes are four or five
  small ones set around the muzzle as a star, the AS50's two thrown to the sides.

Every retail system has a depload entry listing its emitters, and every emitter one listing its
textures; a weapon's own depload resource lists a flash only where a cutscene shares it. A system
added to a library gets the same two kinds of entries. The DLC's library loads in every world, a
world's only in its own. Aiming Overhaul's per-gun flashes are made this way.
