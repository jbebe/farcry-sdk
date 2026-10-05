---
title: Infamous Redux as the default difficulty
kind: component
bundle: gameplay
claims: []
status: located
systems: [ui]
match:
  - "engine/settings/defaultgameconfig.xml#@DifficultyLevel"
  - "languages/english/oasisstrings.fragment.xml#MessagesBoxListDifficulty/MBOXLISTDIFFICULTY_INFAMOUS"
exclude: []
requires: []
verified: diff
---

# Infamous Redux as the default difficulty

A new game offers the hardest difficulty first, and in English that difficulty is called "Infamous
Redux". Not a line of the readme.

## How

- `engine/settings/defaultgameconfig.xml`, root `DifficultyLevel` 1 -> 3, the profile's default
  difficulty (Normal -> Infamous, by the order of the four levels).
- `languages/english/oasisstrings.fragment.xml`, `MessagesBoxListDifficulty/MBOXLISTDIFFICULTY_INFAMOUS`
  "Infamous" -> "Infamous Redux".

## Uncertain

- That 3 is Infamous (levels numbered 0 to 3 from Easy) is inferred, not traced.
- The label suggests the mod's balance is meant for Infamous; nothing in the settings ties the
  mod's other changes to that level.
