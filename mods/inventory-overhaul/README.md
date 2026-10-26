# Inventory Overhaul

Two guns of any class, and a notebook, opened with Tab and used in real time, for everything else
the player carries. The design is [the inventory design log](../../docs/docs/design/inventory.md);
this is an FCSE plugin, built in the log's steps.

| Step | What it does | Status |
|---|---|---|
| 1. Two guns | gun 1 on key 2, gun 2 on key 3, of any class; a third gun replaces the one in hand | built, not seen in game |

## Two guns

The engine keeps its weapon classes, so the box, the scripts and the HUD still see a primary, a
secondary and a special. What changes is the player's inventory (`InventoryOverhaul.dll`,
`src/two_guns.cpp`):

| Engine call | What the plugin does |
|---|---|
| `AddWeapon` | for a gun picked up with two carried, drops the one in hand first (else the one last drawn); gives every new gun a free slot, kept on it as the entity data `InventoryOverhaul.Slot`. Loading a save adds its guns here too, and drops none |
| the state machine's select-class actions, `SelectWeapon` | key 2 draws gun 1 and key 3 gun 2, whatever their class; key 4 draws nothing; the machete is unchanged. Other callers of `SelectWeapon`, such as the weapon crate, still select by class |
| `GetWeaponFromCategory`, inside the player's `CPickupWeapon::PrePickup` and `Pick` | finds no gun, so a pickup replaces nothing of its own class and `AddWeapon` makes room; the gun picked up is then drawn |

Two guns of the same model share an item id, by which the engine draws them, so the later one is
given an id of its own. Every decision is logged to `bin\fcse.log`.

When the player spawns from a save (`CPlayer::CompleteDoSpawnPlayer`), every gun without a slot
takes the key its class had in the game where it is free: the secondary's 2 (gun 1), the primary's
3 (gun 2), else the free one. A save from before the mod carries up to three guns; the one left
without a slot, the special-class gun, is dropped at the player's feet, and if it was in hand gun 1
is drawn. A gun given in play some other way than a pickup, such as from a weapon crate, with two
carried is left without a slot until the next load. A scripted draw of the secondary class, if the
game has one, goes through the same action as key 2 and draws gun 1.

## Testing

`build.ps1 -Install "<game>\bin"` builds the plugin and copies it into `bin\plugins\`.
