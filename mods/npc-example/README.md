# NPC example

Two test layers, each placing one NPC in world 1 with nothing but data. Neither has been run in the
game. The method and what each part means are in
[adding an NPC](../../docs/docs/modding/adding-an-npc.md).

| Layer | Places | Where |
|---|---|---|
| `layer/` | a neutral civilian, `buddies.Civilians.Male_Civilian`, standing | `w1_b_2`, sector 3944, at (1563.5, 3196.2, 16.4) |
| `layer-merc/` | a soldier, `enemy_archetypes.Blue_Faction.Assault_Caucasian` with `selArmy` 1, at a guard post | `w1_b_2`, sector 4027, at (1771.4, 3251.4, 27.5) |

Each fragment is a retail instance copied with a new id, name and position: the civilian from
`Civilians.Male_Civilian_0` (sector 3944) and the soldier from `Blue_Faction.Assault_Caucasian_41`
(sector 4027). A fragment whose id is not in the sector is added to its `main` layer, so both NPCs
are always present.

## Testing

The GOG install carries Vehicle Overhaul's layer, so build it in alongside:

```
tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe mod build --game "C:\Games\Far Cry 2" --layer mods\vehicle-overhaul\layer --layer mods\npc-example\layer
```

Add `--layer mods\npc-example\layer-merc` for the soldier. To go back, build Vehicle Overhaul alone:

```
tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe mod build --game "C:\Games\Far Cry 2" --layer mods\vehicle-overhaul\layer
```
