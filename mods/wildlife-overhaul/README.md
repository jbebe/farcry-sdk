# Wildlife Overhaul

More varied and more dangerous wildlife, toward [attacking animals](../../docs/docs/design/attacking-animals.md):
new behaviours first, then new animals alongside the old ones. An FCSE plugin plus a data layer: a
`layer/` you install with `jackall-cli mod build`, which carries the plugin once `build.ps1` has
staged it.

| Animal | Behaviour | Status |
|---|---|---|
| Cape buffalo | charges the player instead of fleeing, gores on contact, then runs off | charge seen in game |
| | a gore knocks the player down; they get up, unless it was the killing blow | built, not seen in game |
| Hyena (new) | a new archetype on the goat as a stand-in, with zebra sight; retail animal brain, placed nowhere yet | built, not seen in game |

## The buffalo charge

`Animals.Quadrupeds.CapeBuffalo` gets its own brain, `wildlifeoverhaul_buffalo.ai.rml`: retail's
`animalbrain.ai.rml` renamed to `::WildlifeBuffalo/`, with one change to the alert plan. When the
buffalo is alarmed - shot at, hit, hearing gunfire or seeing the player within 25 m - and the player
is within 40 m, it charges:

1. it claims the world blackboard fact `WildlifeBuffaloCharging`, so only one buffalo charges at a
   time; the others stand their ground and retry every second;
2. it runs at a point 8 m beyond the player, so it is still at full speed when it arrives, merging a
   new path toward that point every second without stopping. The path keeps a 3 to 6 m turning radius,
   so a buffalo facing away wheels round instead of stalling, and the charge sets
   `DisableDynamicAvoidance` so the buffalo does not stop and sidestep the player;
3. within 3.5 m of its centre, before its own steering swerves round the player, it sends one Crush
   stim of level 26 to every pawn within 4.5 m, about 300 health on
   the player's damage curve;
4. on a hit, or after 12 s without one, it runs on to a point 12 m past the player, steering round
   them again;
5. it stands for 2.5 to 4 s looking at the player, the player's chance to act, releases the claim and
   charges again, so the fight goes on until one of them dies or the player is more than 40 m away.

If no path reaches the player, it stands still for 20 s and then runs away (a diagnostic). The claim
is also released when the buffalo dies or its alert is interrupted, and a waiting buffalo clears a
claim older than 30 s. Farther than 40 m, it flees as in retail. The scripted buffalo of the opening taxi ride
(`Animals.Scripted.CapeBuffalo`) is unchanged.

| File | What it is |
|---|---|
| `brains/buffalobrain.source.xml` | the brain's BlackBox.AI source, the file to edit |
| `layer/mods/scripts/game/newbrains/wildlifeoverhaul_buffalo.ai.rml` | the compiled brain |
| `layer/mods/worlds/world{1,2}/generated/entitylibrary.fcb/Animals/Quadrupeds/CapeBuffalo.xml` | the archetype, pointed at the new brain |

After editing the source, compile it into the layer:

```
tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe ai pack mods\wildlife-overhaul\brains\buffalobrain.source.xml -o mods\wildlife-overhaul\layer\mods\scripts\game\newbrains\wildlifeoverhaul_buffalo.ai.rml
```

## Knocked down

The gore's Crush stim carries the detail `wildlife_knockdown`, and the plugin, `WildlifeOverhaul.dll`,
knocks the player down when one lands. The stim does its damage as any other would; the plugin
watches the health it leaves (`CFCXCountersComponentPlayer::HandleDamageStim` and `SetHealth`), and on
the next frame runs `knockout.lua` in the game's own Lua with one of two endings:

| Ending | When | What it calls | What the game then does |
|---|---|---|---|
| `Faint()` | the player survives the gore | `SetScriptedDeath(1)`, a `PlayerDied` callback, two lethal Pierce stims | `scripted_death`: falls, holsters and fades to black; 2 s later the script plays `pawn_generic_desertgetup` (`SetupPlayerForAnchorAnim`, `PlayAnim`), which lifts the black screen, then `SetScriptedDeath(0)`; when the clip reports `FinishedPlayAnim`, `SetPlayerActionMap("default")` hands control back, as the GRIN missions do, and `WildlifeKnockdownRestore()` puts back the health the gore left |
| `Die()` | the gore would kill | `BlockBuddyRescue()`, the same stims, `UnblockBuddyRescue()` 1 s later | `no_rescue`: falls, game over (the load-save screen), death ragdoll |

A killing gore is held at 1 health so that `Die()`, not the engine, picks the ending. While the
player is down or getting up from a faint, a gore does nothing; after 30 s the plugin takes another
regardless.
The engine heals on waking; the restore replaces that heal, so a knockdown neither refills health
nor costs a syringe.

| File | What it is |
|---|---|
| `src/` | the plugin: `knockdown.cpp` the hooks, `engine/lua.cpp` the game's Lua |
| `layer/plugins/wildlife-overhaul/knockout.lua` | the endings, read by the plugin at load |

`build.ps1` builds the plugin and stages it into the layer. With the plugin loaded, DevTools' Lua tab
can run `WildlifeKnockout:Faint()` or `WildlifeKnockout:Die()` once a knockdown has defined them
(the game's Lua is 4.0: there is no `true`, so a flag argument cannot work).

## The hyena

A new animal, `Animals.DLC.Hyena`, beside the retail ones, built in steps from
[the hyena research](../../docs/docs/design/attacking-animals.md#the-hyena). Step 1 is the archetype
alone: a copy of `Animals.Quadrupeds.Goat`, still on the goat's mesh, rig and clips, with four
changes:

| Field | Goat | Hyena |
|---|---|---|
| `Name`, `hidName` | `Quadrupeds.Goat`, `Animals.Quadrupeds.Goat` | `DLC.Hyena`, `Animals.DLC.Hyena` |
| `disEntityId` | 6 | 5,744, above every library's ids (`tmpla`'s run to 5,743) |
| `CCharacterPhysComponent` `fMass` | 50 | 60 |
| `CAnimalAgent.selAnimalSpecies` | 5 (goat: 3 m sight) | 2 (zebra: 25 m sight) |

The fragment, `layer/mods/worlds/world{1,2}/generated/entitylibrary.fcb/Animals/DLC/Hyena.xml`,
matches no retail archetype, so JackAll appends it to the `Animals` group. Nothing places it yet;
spawn one from DevTools: on the Spawner tab press Datsun, copy the `SpawnEntityFromArchetype` line the
Lua tab shows, and run it again with `"Animals.DLC.Hyena"` in place of the Datsun's archetype.

## Testing

Run `build.ps1` first so the layer carries the plugin. The GOG install carries Vehicle Overhaul's
layer, so build it in alongside:

```
tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe mod build --game "C:\Games\Far Cry 2" --layer mods\vehicle-overhaul\layer --layer mods\wildlife-overhaul\layer
```

Buffalo stand in world 1 at about (1,318, 2,786), a herd of four, and (3,816, 2,322), two; and in
world 2 at Rail Xing, (3,944, 2,824). Fire a shot near one from closer than 40 m.
