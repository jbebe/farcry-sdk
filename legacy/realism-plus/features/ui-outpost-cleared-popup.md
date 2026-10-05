---
title: Outpost Cleared popup text
kind: component
bundle: gameplay
status: located
systems: [ui, missions]
match:
  - "onscreenpopup/onscreendata.xml#Objective[PGPUnlocked]"
  - "languages/*/oasisstrings.fragment.xml#SafeHouse/PGPUnlockedSwoosh"
exclude: []
requires: []
verified: diff
---

# "Outpost Cleared" popup text

The text of an "Outpost Cleared" popup for when an outpost's last guard dies. The installed mod
never shows it. Its outpost scripts are the preinstalled "Notification Off" set, and only the
optional "Notification On" customisation's scripts push it.

## How

- `languages/*/oasisstrings.fragment.xml`, new `SafeHouse/PGPUnlockedSwoosh` in all ten languages:
  "Outpost Cleared", "Avant-poste dégagé", "Außenposten besiegt" and so on.
- `onscreenpopup/onscreendata.xml` gains `Objective[PGPUnlocked]`, one `Popup` with `save` 0,
  `section` `SafeHouse`, `icon` `Grin`, `text` `PGPUnlockedSwoosh`, `soundEvent` `0x004f014c`,
  `soundType` 20.

This is the same pair `functional-outposts` in `legacy/scubrahs-patch` carries.

## Depends on

- Nothing in the analysed install. The 58 scripts of
  [`outposts-functional`](outposts-functional.md) are byte for byte the customisation's
  `45 Minute Timer (Preinstalled)/Notification Off (Preinstalled)` set, and none of them calls
  `PushNewObjective`. The `Notification On` sets of the optional `Outpost Respawn Timer & 'Outpost
  Cleared' Notification` customisation replace all 58. Each of those calls
  `CFCXObjectiveHudManager_GetInstance():PushNewObjective("SafeHouse", "Added",
  "PGPUnlockedSwoosh", -1, -1, 5)` when its outpost is cleared, and needs this text.

## Uncertain

- `PushNewObjective` takes the section and text directly, so the `onscreendata.xml`
  `Objective[PGPUnlocked]` entry looks unused even with the notification on. That is inferred, as
  in Scubrah's Patch.
