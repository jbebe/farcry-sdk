---
sidebar_position: 1
---

# Cut and dormant content

What the retail game holds but never uses or keeps switched off, and how it differs from what was
shown before release. Two comparisons back every page in this section:

- **The retail game against itself.** Everything the code can do, held against everything the retail
  data turns on: AI tasks no brain uses, properties no archetype sets, settings no file writes,
  archetypes nothing places, animation states, effects, sounds and strings nothing reaches.
- **Retail against the September 2008 prototype.** The Xbox 360 build of 8 September 2008, about six
  weeks before release, ships its data with real file names, its shader source and its sound banks.
  Its differences from retail show what changed in the last weeks.

Cut missions, mission steps and voice lines are in [Cut and unused content](../lore/missions/cut-content.md);
whole shipped files the engine cannot reach are in [Asset reachability](../engine-internals/asset-reachability.md).

:::info[Verified via reverse engineering]
Code traced in `FarCry2_server`, the symbolized Linux dedicated server (the whole game but the
renderer), and in Steam `Dunia.dll` for the renderer and to confirm PC gates. Retail data is the
1.03 release, decoded with `jackall-cli`. The prototype is
`tools/third-party/Far Cry 2 Sep 8 2008 prototype`. Each page names its own sources; nothing in this
section was seen in game unless a page says so.
:::

## What the comparisons show

The content was fixed by September 2008. Retail did not thin the world: world 1 has 2,472,738
vegetation instances against the prototype's 2,472,754. No shader option, sound variation or
physics value was dropped. What separates retail from the pre-release demos is of four kinds:

- **Conservative constants in code.** A car breaks a bush only above 10 m/s, head on, and at most 10
  branches per hit. A bullet pushes a ragdoll at 0.2 to 0.4 of its impulse. These are fixed numbers,
  not settings.
- **Calls that nothing makes.** Storm winds tearing branches, squads splitting into fire teams, the
  armies' strength changing over the acts: the code is finished and has no caller.
- **Switches left at their off value.** Explosions light nothing, rockets never carry through thin
  walls, every soldier sprints at the same speed, the player cannot ride as a passenger. Each is one
  value in the data.
- **Features cut before the code was written.** Lightning, tree felling, predators, wounds by body
  part and surrender have no code at all.

## The pages

| Page | What it covers |
| --- | --- |
| [The prototype against retail](./prototype-comparison.md) | every difference between the September 2008 build and retail: settings, combat values, vegetation, sound |
| [Pre-release promises](./pre-release-claims.md) | what demos and previews showed or promised, and what the retail code holds of each |
| [Vegetation, fire and weather](./vegetation-fire-weather.md) | bushes and vehicles, branch breaking and regrowth, grass, storms, rain, fire |
| [Physics and explosions](./physics-and-explosions.md) | ragdolls, loose objects and debris, explosions, bullets, vehicles, breakable buildings |
| [AI and combat](./ai-and-combat.md) | squad roles, army strength, soldier classes, morale, weapon failure, stims and voice lines nothing uses |
| [Rendering](./rendering.md) | render features present but off, gated to a quality tier, or capped |
| [Settings and scripting](./settings-and-scripting.md) | game-mode and profile settings, Lua functions and Domino boxes no shipped script uses |
| [Unused assets](./unused-assets.md) | vehicles, characters, weapons, animations, effects, UI and sounds nothing places or plays |
