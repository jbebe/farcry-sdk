---
title: English text rolled back to the 1.00 release
kind: component
bundle: strings
status: located
systems: [ui]
match:
  - "languages/english/oasisstrings.fragment.xml#**"
exclude:
  - "languages/english/oasisstrings.fragment.xml#InGameEditor*/**"
requires: []
verified: diff
---

# English text rolled back to the 1.00 release

Nine English strings outside the editor go back to the wording of the game's original release,
undoing corrections patch 1.03 made. Most visibly, the Fortunes Pack's welcome message reads
"PLACEHOLDER TEST DO NOT TRANSLATE". This is a side effect of how the mod's string table was built,
not a feature.

## How

The mod ships a whole `languages/english/oasisstrings.rml`. Outside the `InGameEditor*` sections it
is the base game's `common.dat` table, compiled from the plain-XML source that ships beside it
(`languages/english/oasisstrings.xml`), not the `patch.dat` table patch 1.03 replaced it with. The
nine strings where those two differ are the changes:

| String | Patch 1.03 | Mod (1.00 release) |
|---|---|---|
| `DLC1/DESC` | the Fortunes Pack install message | "PLACEHOLDER TEST DO NOT TRANSLATE" |
| `MultiMenu/LOBBY_SEQUENCE_WAITING_FOR_MORE_PLAYERS` | "Waiting for [PLAYER_COUNT] players to be ready…" | "Waiting for more players…" |
| `Objectives/A1LM03_02` | "…wants to meet me southeast of the fuel depot, with a proposition." | "…wants to meet me. He's got a proposition. He's southeast of the fuel depot." |
| `Objectives/A2SM06_APR_04`, `A2SM06_UFLL_04` | "I have to warn some civilian doctor…" | "I have warn some civilian doctor…" |
| `Tutorial/COMPUTER_ADVERT_02` | "…light machine guns…" | "…light machines guns…" |
| `PauseMenu/UPGRADES_SURVIVAL_TITLE` | "Survival" | "SURVIVAL" |
| `PauseMenu/REPUTATION_INFAMY_DESCRIPTION_0` | starts with a "REPUTATION LEVEL 0" line | the description alone |
| `Subtitles/4911264` | a double space after "called a truce." | a tab there, as in the XML source |

Every value matches the `common.dat` table, and the tab and the source match its XML, so none of
these is an edit of the author's. The same text is in the seven copies the mod puts in place of the
other languages (`strings-english-in-other-languages`).

### Also dropped, not representable here

The patch table has 25 strings the 1.00 table lacks, and the mod's whole table drops them: the
faction names `CharacterNames/APR` and `UFLL`, ten multiplayer menu labels (`Hardcore Mode`,
`Minimum Players`, `Respawn Time`, `Starting Rank`, `Quick Match`, `Filter`, `Match Info`,
`Approved for Ranked`, `Deaths`, a min/max players warning), three PS3 labels,
`MainMenu/OPTIONS_DISPLAY_WIDESCREEN`, eight subtitles (short Spanish and French lines, two
Liberation Radio broadcasts) and the editor label `InGameEditor_Objects/Paraglider_Multi_Intel`.
A string fragment cannot remove a string, so these show up nowhere in the changes and a pick keeps
them. In the mod as shipped they are missing in every language it replaces.

## Uncertain

- What the game shows for a missing string (the key, or nothing) is not checked.
- Why the table was built from `common.dat` is not known; most likely the editor mod it is based on
  (Janne252's) was made that way and this mod kept it.
