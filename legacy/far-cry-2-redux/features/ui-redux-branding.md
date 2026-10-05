---
title: Redux logo and credits
kind: component
bundle: ui
claims: []
status: located
systems: [ui]
match:
  - "ui/textures/common/logo_new_02.xbt"
  - "languages/english/oasisstrings.fragment.xml#{MainMenu/TEXT_LEGAL,Generic/HELP_WEBSITE,MultiErrors/*}"
exclude: []
requires: []
verified: diff
---

# Redux logo and credits

The title logo reads "Far Cry 2 Redux", and a few English texts carry the mod's name.

## How

- `ui/textures/common/logo_new_02.xbt`, the right half of the "FAR CRY 2" logo, 256x256, DXT5 ->
  DXT1: the registered-trademark sign under the "2" is replaced by "REDUX" painted in red.
- `languages/english/oasisstrings.fragment.xml`:
  - `MainMenu/TEXT_LEGAL`: the legal line drops "Ubisoft and" from the trademark sentence, the
    country clause and the Crytek credit, and ends "Redux mod by Hunter © 2019".
  - `Generic/HELP_WEBSITE` "Need Help? Visit www.farcrygame.com/help" -> "Questions or feedback?
    Visit www.moddb.com/mods/far-cry-2-redux".
  - `MultiErrors/E_SESSION_ERROR_GATOR_PROFANITY_CHECK_TIMEOUT` -> "R3DUX" and
    `MultiErrors/E_SESSION_ERROR_RENDEZVOUS_AUTHENTICATION_ACCESS_DENIED` -> "Far Cry 2 Redux by
    BigTinz", two online-play error messages.

## Uncertain

- Where the two multiplayer error strings show in single player, if anywhere, is not known; they may
  be signatures rather than visible text.
- The DXT1 logo has no alpha channel beyond 1-bit; whether its torn edge still blends as before is
  not checked.
