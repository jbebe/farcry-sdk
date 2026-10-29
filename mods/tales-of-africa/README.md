# Tales of Africa

A missions mod, working title: visual storytelling, and small missions or riddles the player finds and
solves for a weapon or diamonds. Data only so far. Its papers are read in
[Inventory Overhaul](../inventory-overhaul/README.md)'s notebook, which knows them by
[convention](../inventory-overhaul/README.md#other-mods-papers), so it needs that mod.

## What the layer holds

A note, a photo and a cassette in world 1, the first papers the convention was seen working with
(2026-10-29):

| Items id | Where | Pickup | Picture or sound |
|---|---|---|---|
| `Note.Torn_notebook_page` | on the boombox on a beer barrel, (2604.02, 2108.09, 21.35) | the Predecessor's envelope drawn as the stock single sheet | `textures\common\jackal_photo` |
| `Photo.Cliff_village` | on the round table beside it, (2606.95, 2106.80, 20.51) | the envelope drawn as the hotel room's polaroid | `textures\loading\03_02`, cropped 3:2 |
| `Tape.Test_cassette` | on the same table, (2606.75, 2107.00, 20.51) | the Predecessor's tape pickup, a dictaphone | a minute of the game's own music, `0x004E1C52` |

Each is saved at persist level 3, like a diamond case, so it stays picked.

| Part | Files |
|---|---|
| Pickups | `layer/mods/worlds/world1/generated/entitylibrary.fcb/OA_MissionPickups/` and `layer/mods/levels/`, written by `build-papers.ps1` (`papers.py`) from the retail entity library under `tmp\` |
| Words | the `Papers` section of `layer/mods/languages/<language>/oasisstrings.fragment.xml` |
| Pictures | `layer/mods/ui/localized/<aspect>/<language>/ui/hud.mgb/`: the images in `a_io_paper_pictures`, their materials and exports |

## Testing

The layer goes in after Inventory Overhaul's:

```
tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe mod build --game "C:\Games\Far Cry 2" --layer <each layer> --layer mods\inventory-overhaul\layer --layer mods\tales-of-africa\layer
```
