---
title: More vehicles, wrecks and train pieces in the editor palette
kind: component
bundle: editor-content
status: located
systems: [world, vehicles, ui]
match:
  - "ingameeditor/object_inventory.xml#Directory[1]/**"
  - "languages/english/oasisstrings.fragment.xml#InGameEditor_Objects/{dlc1_vehicle1,dlc1_vehicle2,dlc1_vehicle3,dlc1_vehicle4,Razor19,Razor20,Razor21,Razor22,Razor41,razor57,razor58,razor59,razor60,razor69,razor70,razor71,razor72,razor88,razor89,razor90,razor91}"
  - "languages/english/oasisstrings.xml#section[InGameEditor_Objects]/string[{dlc1_vehicle1,dlc1_vehicle2,dlc1_vehicle3,dlc1_vehicle4,Razor19,Razor20,Razor21,Razor22,Razor41,razor57,razor58,razor59,razor60,razor69,razor70,razor71,razor72,razor88,razor89,razor90,razor91}]{,@value}"
exclude: []
requires: [editor-razor-library]
verified: diff
---

# More vehicles, wrecks and train pieces in the editor palette

The palette's Vehicles folder grows from 95 to 227 entries: drivable campaign and multiplayer
vehicles the vanilla palette leaves out, the Fortunes Pack vehicles, painted variants, wrecks,
wagons and train track pieces.

## How

`ingameeditor/object_inventory.xml`, top-level `Directory_Vehicles` (`Directory[1]`). A second
"Drivable" folder is inserted before "Non Drivable and Wrecks", so the comparison pairs the vanilla
folders with the mod's one place off. The 337 changes (renamed folders, rewritten and added entries,
dropped `Pivot`s) only make sense applied together.

What the folder offers afterwards:

- **Drivable**: `vehicle.Land.Datsun.Multi`, `.ScriptedDatsun`, `JeepLiberty.Multi` and `.VIP`,
  `JeepWrangler`, the `Rover.Multi` family with M2, M249 and MK19 mounts, `BigTruck` and its
  scripted, faction and tanker variants, `FishingBoat` and `SwampBoat` with their mounted variants,
  and `vehicle.Air.Paraglider.Paraglider_Lv1`/`_Lv5`.
- **Fortunes Pack**: `vehicle.Land.DLC_Vehicle1_DLC1` and `.Multi`, and `DLC_Vehicle2_DLC1`, `.Multi`
  and `.Multi_MK19_Mounted`, under the ids `dlc1_vehicle1`..`5`.
- **From the custom library** ([`editor-razor-library`](editor-razor-library.md)), 17 entries: the
  painted `vehicle.Land.Datsun.Color1`..`4`, `vehicle.Land.Buggy.Color1`..`4` and
  `RaZoR_Vehicles.Quad1.Color1`..`4`, the static `Custom.RaZoR_Object.MagicBus`, and the
  `CarWreck1`/`2` and `TruckWreck1`/`2` wrecks.
- **Non drivable**: `vehicle.Land.Datsun.BrokenDatsun`, `Rover.BrokenRover`,
  `BigTruck.A2LM09_NitrousTruck`, two `vehicle.Wreck.*_BK`, carts, tyres and wreck parts, most of
  them `IGE_Archetypes.AutoGen.*`.
- **Trains and Tracks**: wagons and cabooses (`IGE_Archetypes.AutoGen.Train*`), 12
  `IGE_Archetypes.AutoPrefab.INFRA_TRAIN_Track_*` pieces, seven `Tracks_*` prefabs (straights,
  curves and switches) and `props.World2_RailBridge.BigTruck`.

Except the 17 custom ones, every archetype is in the template world's vanilla library, and every
prefab is in its `tmpla.managers.fcb`. The custom entries use `RazorN`/`razorN` ids. The strings
under those ids in the `InGameEditor_Objects` table give them their labels: "White car wreck",
"Pink car wreck", "Truck wreck 01"/"02", "Bus (L)", "Datsun 1".."4", "Buggy 1".."4" and "Quad
Bike 01".."04". `dlc1_vehicle1`..`4` all read "DLC_object 21", a copy-paste slip. Those 21
strings, in `oasisstrings.fragment.xml` and in `oasisstrings.xml`, are this page's too.

## Depends on

- [`editor-razor-library`](editor-razor-library.md) for the 17 custom entries, which spawn nothing
  without it.
- The Fortunes Pack DLC for the `DLC_Vehicle*` entries.

## Uncertain

- Of the folder's 227 entries, 94 have no `ObjectCost` and 21 cost 1, where the vanilla drivable
  vehicles cost 36 to 297. A map can hold far more of them than the budget meant. Whether an entry
  without a cost counts as free is not checked.
