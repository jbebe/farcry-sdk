# Wildlife Overhaul

More varied and more dangerous wildlife, toward [attacking animals](../../docs/docs/design/attacking-animals.md):
new behaviours first, then new animals alongside the old ones. Data only so far: a `layer/` you
install with `jackall-cli mod build`.

| Animal | Behaviour | Status |
|---|---|---|
| Cape buffalo | charges the player instead of fleeing, gores on contact, then runs off | built, not seen in game |

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

## Prototype: knocked down

`prototype/knockout.lua` knocks the player down through the game's own death handling, with two
endings. It is a script, not part of the layer, and wires only retail pieces:

| Ending | What it calls | What the game then does |
|---|---|---|
| wakes up | `SetScriptedDeath(1)`, a `PlayerDied` callback, two lethal Pierce stims | `scripted_death`: falls, holsters and fades to black; 2 s later the script plays `pawn_generic_desertgetup` (`SetupPlayerForAnchorAnim`, `PlayAnim`), which lifts the black screen and heals, then `SetScriptedDeath(0)` |
| stays down | `BlockBuddyRescue()`, the same stims, `UnblockBuddyRescue()` 1 s later | `no_rescue`: falls, game over (the load-save screen), death ragdoll |

To try it, paste the one line in `prototype/knockout.oneline.lua` into DevTools' Lua tab and Run, then
run `WildlifeKnockout:Faint()` to wake up or `WildlifeKnockout:Die()` to stay down (the game's Lua is 4.0:
there is no `true`, so a flag argument cannot work). DevTools' invincibility must be
off. The one-liner is the source file's lines joined by spaces, so regenerate it after an edit.

## Testing

The GOG install carries Vehicle Overhaul's layer, so build it in alongside:

```
tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe mod build --game "C:\Games\Far Cry 2" --layer mods\vehicle-overhaul\layer --layer mods\wildlife-overhaul\layer
```

Buffalo stand in world 1 at about (1,318, 2,786), a herd of four, and (3,816, 2,322), two; and in
world 2 at Rail Xing, (3,944, 2,824). Fire a shot near one from closer than 40 m.
