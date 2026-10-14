# New world

Two test worlds the game does not ship, built from `tmpla`, the editor's flat template world. Neither
has been loaded in the game. The method, the file checklist and the plan are in
[building a new world](../../docs/docs/modding/building-a-new-world.md).

| World | What it is |
|---|---|
| `newworld_clone` | `tmpla` under a new name, 8 × 8 sectors, nothing else changed |
| `newworld_tile` | one 64 m sector of flat ground, the spawn point lifted onto it |

`build-layer.ps1` writes `layer\` from the extracted retail `worlds` archive in
`tmp\gamefiles\worlds\worlds`. That copies about 29 MB of retail files, so the layer is not
committed.

## Testing

The GOG install carries Vehicle Overhaul's layer, so build it in alongside:

```
.\mods\new-world\build-layer.ps1
tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe mod build --game "C:\Games\Far Cry 2" --layer mods\vehicle-overhaul\layer --layer mods\new-world\layer
```

Then load a world 1 save, press `~`, and travel, one launch per line:

```
#GameChangeWorld("world2", "2051908267268899174")
#GameChangeWorld("tmpla", "2054037628277228842")
#GameChangeWorld("newworld_clone", "2054037628277228842")
#GameChangeWorld("newworld_tile", "2054037628277228842")
```

What each run should show is in [the test sheet](../../docs/docs/modding/building-a-new-world.md#the-test-sheet).
To go back, build Vehicle Overhaul alone:

```
tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe mod build --game "C:\Games\Far Cry 2" --layer mods\vehicle-overhaul\layer
```
