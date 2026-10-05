---
title: Homemade and primitive machetes, by registry file
kind: component
bundle: dlc-unlocks
status: no-artifact
systems: [weapons]
match: []
exclude: []
requires: []
existing: mods/UFCP — src/fixes/bonus_content.cpp (ApplyMachetesUnlock), both builds
verified: re
---

# Homemade and primitive machetes, by registry file

The two bonus machetes are unlocked by an optional registry file, not by anything in the game files,
so nothing in the analysis carries this.

## How

`2. DLC Machetes/Install DLC Machetes.reg` sets three DWORDs under
`HKEY_CURRENT_USER\Software\Ubisoft\Far Cry 2`: `PartnerKey1`, `PartnerKey2` and `MachetesKey`, all
`1`. `Uninstall DLC Machetes.reg` deletes them. These are the values the GOG engine's bonus gates
read. `MachetesKey` makes `IsMachetesUnlocked` (GOG `0x100489A0`) answer yes, which shows the game's
own Options → Game → Machete Type row. The `PartnerKey` values unlock the predecessor tapes, which
the mod's `Dunia.dll` already forces ([`dlc-predecessor-tapes`](dlc-predecessor-tapes.md)).

The registry file only works because the mod ships the GOG `Dunia.dll`. Steam's own engine asks the
privileges service and ignores these values. Scubrah's Patch NOPs the machete gate in code instead
([`machete-type-option`](../../scubrahs-patch/features/machete-type-option.md)).
