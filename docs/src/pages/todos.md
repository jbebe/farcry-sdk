---
title: Todos
description: Open tasks across the repository, docs, JackAll, and mod work.
---

# Todos

A running task list across the whole project.

## Repository

*nothing*

## Docs

- [ ] Improve JackAll docs

## Reverse

- [ ] Reverse Far Cry 2 Xbox360 prototypes with XEX decompiler
- [ ] Reverse other similar Ubisoft titles:
  - https://hiddenpalace.org/Far_Cry_4_(Oct_25,_2014_prototype)
  - https://hiddenpalace.org/Tom_Clancy%27s_Splinter_Cell:_Chaos_Theory_(Jan_18,_2005_Multiplayer_prototype)
  - https://hiddenpalace.org/Tom_Clancy%27s_Rainbow_Six:_Lockdown_(Jan_16,_2006_demo)
  - https://hiddenpalace.org/Assassin%27s_Creed_(Feb_15,_2008_prototype)

## Tools/JackAll

- [ ] Review the Domino viewer because it needs a lot of improvements
  - Review code
  - Revamp the visual interface, find a good graph wpf package

## Tools/FCSE

- [ ] **Known bug: pressing Enter in a text field does nothing.** On the stock Options → Network
  page that commits the value and refreshes the page. The `ActionExecuterEditbox` on the element only
  *raises* an action on the `enter` trigger — committing is the page's response to it, not something
  the widget does — and FCSE's page never sees that action: the inherited handler
  (`CFCXBaseOptionPage::OnActionSignal`, `0x1087f1f0`) early-returns unless the dirty flag at
  `page+0x1B8` is set, and FCSE clears that flag every frame to suppress the "unsaved changes"
  prompt. The two needs conflict, so the fix is FCSE registering its own `IMagmaActionListener`
  rather than relying on the inherited one — and probably narrowing the dirty-flag clear at the same
  time. Values still save; only the Enter gesture is missing.
- [ ] **Known bug: no mouse cursor on the Mod Configuration page.**
- [ ] Two faults seen in `fcse.log` and not yet chased: `FCSE.exe+0x14ABB` (in FCSE's own code) and
  a recurring `Dunia.dll+0xAD4095` in magma's draw-collection walk.

## Tools/"dll plugins"

- [ ] Lua script support for plugins so that simple changes don't have to be compiled

## Tools/vortex-farcry2

## Mod

- [ ] Create our first mod because that was the original plan
