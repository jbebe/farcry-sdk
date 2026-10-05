---
title: Multiplayer archetypes the mod's library differs in
kind: noise
systems: [weapons, player]
match:
  - "generated/entitylibrarypatchoverride.fcb/{weaponproperties,weapons,pickups,gadgets,pile_archetypes}/**/multi{,_apr,_ufll}.xml#**"
  - "generated/entitylibrarypatchoverride.fcb/pickups/**/multi/*.xml#**"
  - "generated/entitylibrarypatchoverride.fcb/player/maincharacter/pawnplayernetwork*.xml"
  - "generated/entitylibrarypatchoverride.fcb/player/maincharacter/pawnplayernetworkkit/**"
  - "generated/entitylibrarypatchoverride.fcb/gadgets/equipped/compassmulti.xml"
  - "generated/entitylibrarypatchoverride.fcb/curves/stimeffectcurves/crushdamagemp.xml#**"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/{LoadOutService,MPCountersService,GameSettings}/**"
exclude:
  - "**#**/{bAutoReload,fIronsightFOV,fIronsightTransitionTime,iBurstLength,iAmmoInClip,fAimSeekerAngularSpeed}"
  - "**#**/{hidDescriptor,WeaponStatusSwitchValues}/{hidDescriptor,WeaponStatusSwitchValues,rml}"
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/special/lpo50/multi.xml#Entity/Components/**"
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/handtohand/machete/multi.xml#**/Stim_ImpactDamage/nLevel"
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/primary/m16/multi.xml#Entity/Components/**"
requires: []
verified: diff
---

# Multiplayer archetypes the mod's library differs in

The base game's override library is mostly multiplayer archetypes (`.Multi`, the network player).
The mod's copy of that library differs from Steam's in many of them in ways no single-player game
sees. All its library differences but the whole new units show up value for value in Realism Plus's
library too
([`noise-weapons-multiplayer`](../../realism-plus/features/noise-weapons-multiplayer.md)), and the
new network-player units are the same archetypes at slightly different sizes, so they come from a
library export both mods started from, not from either author.

## How

- **Renumbered ids.** `Entity/disEntityId` of the multiplayer weapon, weapon-property and pickup
  archetypes shifts by 4 or 5 (for example `pickups.Weapons.AK47_new.Multi` `4673` -> `4669`): the
  library was re-exported with entries added before them. A pick keeps the base game's ids.
- **A dropped field.** `CGraphicComponent/bIntelHackGliderOn` (`False`) is missing from 64 multiplayer
  pickup and grenade archetypes.
- **Multiplayer values.** `RangeMultipliers` (range bands and multipliers per difficulty, some bands
  removed), first-person spread (`BulletSpread*`, `fBulletSpread_MovementModifier`,
  `fBulletSpread_MinimumSpreadPercentage`) and impact and victim stims on the multiplayer rifles,
  SMGs, pistols, shotguns, snipers and machine guns; the multiplayer IED's three explosives.
- **Network player and compass.** New whole archetypes `player.MainCharacter.PawnPlayerNetwork`,
  `PawnPlayerNetworkKit` and its class kits (38 units), and `gadgets.Equipped.CompassMulti`.
- **`Curves.StimEffectCurves.CrushDamageMP`** reduced from 12 knots to a straight line to its old end
  point.
- **`gamemodesconfig.xml`.** `LoadOutService/Equipment` attribute bars of the multiplayer FAL (`3` ->
  `3.5`), flamethrower (`1` -> `1.3`) and MGL140 (`3` -> `3.5`); `MPCountersService/PlayerSickness/Health`
  `ValueRegenDelay` `5` -> `6`, `ValueBurnDamageMax` `130` -> `150`; the `Adversarial` game setting
  `weather_type`'s `STORMY` value `100` -> `90`.

## Uncertain

- Exceptions are on component pages because this mod uses those archetypes in single player or edits
  them on purpose: `LPO50.Multi` (`weapons-enemy-flamethrower`), `M16.Multi`'s values
  (`weapons-enemy-loadouts`), and the author's blanket edits that reach the `.Multi` copies too
  (`weapons-no-auto-reload`, `weapons-ironsight-fov`, `weapons-scope-fov`, `weapons-ironsight-speed`,
  `weapons-m16-full-auto`, `weapons-magazine-sizes`, `weapons-machetes`).
- The seven `gamemodesconfig.xml` multiplayer values are not in Realism Plus and could be the
  author's own; they only reach multiplayer either way.
