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
   new path toward that point every second without stopping;
3. within 2.5 m, it sends one Crush stim of level 26 to every pawn within 3 m, about 300 health on
   the player's damage curve;
4. after the hit or 12 s it releases the claim and runs away as retail does. If no path reaches the
   player, it stands still for 20 s first (a diagnostic).

The claim is also released when the buffalo dies or its alert is interrupted, and a waiting buffalo
clears a claim older than 15 s. Farther than 40 m, it flees as in retail. The scripted buffalo of the opening taxi ride
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

## Testing

The GOG install carries Vehicle Overhaul's layer, so build it in alongside:

```
tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe mod build --game "C:\Games\Far Cry 2" --layer mods\vehicle-overhaul\layer --layer mods\wildlife-overhaul\layer
```

Buffalo stand in world 1 at about (1,318, 2,786), a herd of four, and (3,816, 2,322), two; and in
world 2 at Rail Xing, (3,944, 2,824). Fire a shot near one from closer than 40 m.
