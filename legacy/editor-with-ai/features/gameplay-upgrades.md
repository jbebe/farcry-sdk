---
title: Weapon manuals and ammo upgrades reworked
kind: component
bundle: gameplay
status: located
systems: [economy, weapons, player]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/BonusService/**"
exclude: []
requires: []
verified: diff
---

# Weapon manuals and ammo upgrades reworked

The upgrades bought at the arms dealer do different things. Operations manuals give big damage and
recoil boosts, repair manuals almost nothing, and ammo-carrying upgrades much less.

## How

`engine/gamemodes/gamemodesconfig.xml`, `BonusService`: 304 values across its `Plan`s. The plan
types are described in [data recipes](../../../docs/docs/modding/data-recipes.md#economy--progression).

- **Operations manuals**, 30 `*_oper_manual` plans: `damage` 0 → 25 %, `sticky` 0 → 5 %, `recoil`
  20 → 95 %. `accuracy` is unchanged.
- **Challenge bonuses**, 26 `*_challenge_bonus` plans: `accuracy` and `recoil` 10 → 0 %, `sticky`
  0 → 25 %.
- **Repair manuals**, 30 `*_repair_manual` plans: `degradation` −20 → −5 % and `unjamtime`
  −35 → −5 %. Five plans have no `unjamtime`.
- **Vehicle manuals** (`quad`, `rover`, `swampboat`): `degradation` −50 → −5 %, `repairtime`
  −50 → −75 %.
- **Ammo-carrying upgrades:** every value in the bandolier, webbing and satchel plans is cut, most
  to between a fifth and a half. `assault_webbing` and `light_assault_webbing` go from +150/+60 to
  +30. `pistol_belt` goes from +40/+16 to +8. `shotgun_bandolier` from +60/+24 to +12.
  `marksmans_bandolier` from +40/+20 to +10. `gunner_pack` from +500/+200 to +100.
  `grenadier_webbing` and `rocketeer_satchel` drop to +1 to +4 a slot. `pyrotechnic_satchel` drops
  to +1 to +3, and to +100 where it gave +500/+200.

## Uncertain

- What `sticky` does is unresolved in the docs. The large `recoil` percent is presumably a recoil
  reduction, but its sign and scale have not been checked.
