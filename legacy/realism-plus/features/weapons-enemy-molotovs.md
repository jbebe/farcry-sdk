---
title: Shotgunners, mortarmen and snipers throw Molotovs
kind: component
bundle: gameplay
status: located
systems: [ai, weapons]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponsService/Properties/InventoryPacks/Pack[*]/Gadget@archetype"
exclude: []
requires: []
verified: diff
---

# Shotgunners, mortarmen and snipers throw Molotovs

Three enemy classes carry Molotov cocktails instead of frag grenades.

## How

`engine/gamemodes/gamemodesconfig.xml`, `WeaponsService/Properties/InventoryPacks`: the `Gadget` of
`Pack[shotgun]`, `Pack[Mortar]` and `Pack[sniper]` goes from `gadgets.Grenades.M67` to
`gadgets.Grenades.Molotov`; its `count` stays `3`.

## Depends on

Nothing. Scubrah's Patch gives the shotgunners Molotovs too
([`ai-shotgunners-molotovs`](../../scubrahs-patch/features/ai-shotgunners-molotovs.md)).

## Uncertain

- Whether the AI's grenade logic throws a Molotov as often as a frag is not checked.
