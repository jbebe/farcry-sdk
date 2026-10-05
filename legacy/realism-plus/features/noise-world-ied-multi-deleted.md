---
title: Multiplayer IED variants removed from the override library
kind: noise
status: located
systems: [weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/_layout.xml#**"
exclude: []
requires: []
verified: diff
---

# Multiplayer IED variants removed from the override library

The override library's layout drops three fragments the base game ships in it:
`weapons\Explosives\IED_Base\IED_Mine\Multi.xml`, `IED_MortarShell\Multi.xml` and
`IED_PipeBomb\Multi.xml`, the multiplayer variants of the three placed IEDs.

Nothing names them: the IED weapon, `weapons.Secondary.IED` and its `.Multi` variant alike, places
`weapons.Explosives.IED_Base.IED_Mine`, `IED_MortarShell` and `IED_PipeBomb`, not their `.Multi`
children, and no world library, sector or inventory pack refers to `IED_*.Multi`. So the deletion
does nothing in game (inference from those searches). It goes with the mod rewriting the IED
archetypes in the same library (`weapons/explosives/ied_base.xml`, on the weapons pages); why the
multiplayer children had to go is not stated.
