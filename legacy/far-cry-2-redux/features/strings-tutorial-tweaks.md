---
title: Tutorial text edits
kind: component
bundle: ui
claims: []
status: located
systems: [ui]
match:
  - "languages/english/oasisstrings.fragment.xml#Tutorial/{TU33_MESSAGE,TU33_MESSAGE_WIN32,TU64_MESSAGE,TU67A_MESSAGE_1,TU67B_MESSAGE}"
exclude: []
requires: []
verified: diff
---

# Tutorial text edits

Five English tutorial messages are reworded. Not a line of the readme.

## How

`languages/english/oasisstrings.fragment.xml`, `Tutorial`:

- `TU33_MESSAGE` and `TU33_MESSAGE_WIN32`: "you can press {sprint} to sprint" -> "hold".
- `TU64_MESSAGE`: "You should talk to both of your Buddies before you leave." -> "Talk to both of
  your Buddies before you leave."
- `TU67A_MESSAGE_1`, the message on leaving the opening area: "free to explore the world" -> "free
  to explore"; the safehouse and diamond lines put their icon (`~SH`, `~DI`) after the text instead
  of before; the guard-post line "~GPGuard Posts can be scouted for Intel." becomes "Scouted
  outposts will be marked on your map." (see `ui-map-icons`, where unscouted guard posts lose their
  map icon).
- `TU67B_MESSAGE`: the Jackal briefing's last line "Find him and kill him." -> "Find him!".

## Uncertain

- Whether "hold to sprint" reflects a control change or only corrects the base wording is not
  checked; no sprint binding is in this page's domain.
