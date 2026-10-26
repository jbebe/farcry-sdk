# Inventory Overhaul

Two guns of any class, and a notebook, opened with Tab and used in real time, for everything else
the player carries. The design is [the inventory design log](../../docs/docs/design/inventory.md);
this is an FCSE plugin, built in the log's steps.

| Step | What it does | Status |
|---|---|---|
| 1. Two guns | gun 1 on key 2, gun 2 on key 3, of any class; a third gun replaces the one in hand | works in game |
| 2. Items | guns stowed outside the two slots, at most two, and any other item by id and count; a test window in DevTools' overlay | built, not seen in game |

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

A loading save adds its guns before the save's own data on them comes back, and that data replaces
any slot given meanwhile. So guns added any way but a pickup are placed later, the first time
`CInventoryViewPawn::GetEquippedWeapon` is asked for the player's gun, which the HUD does every
frame; a number key that finds no gun in its slot places them too. Every gun without a slot then
takes the key its class had in the game where it is free: the secondary's 2 (gun 1), the primary's
3 (gun 2), else the free one. A save from before the mod carries up to three guns; the one left
without a slot, the special-class gun, is dropped at the player's feet, and if it was in hand gun 1
is drawn. A weapon crate's gun taken with two carried is placed the same way, so the gun left over
is dropped. A scripted draw of the secondary class, if the game has one, goes through the same
action as key 2 and draws gun 1.

## Items

Items is what the player carries outside the two slots (`src/items.cpp`). It is one string of
entity data on the player's entity, `InventoryOverhaul.Items`, so it is saved with the game: a line
per record, `gun <archetype> <wear> <rounds>` or `item <id> <count>`.

A stowed gun leaves the game and becomes a record. Stowing is the engine's own drop
(`DropWeapon`) with its `CanSpawnPickup` question answered no, so nothing lands on the ground.
Taking it out makes a new gun of that archetype with the inventory's `AddWeaponByName`, then puts
back its wear and loaded rounds; with two guns carried, the one in hand takes its place in Items.
The gun in hand keeps its magazine in `AmmoInClip`, a holstered one in `RememberedAmmoInClip`,
which it loads when drawn; a gun taken out is given the latter, as a gun made from a pickup is. With
nothing left in hand, the gun is drawn with the immediate select a pickup uses.

The player's diamonds are a row of Items too, read straight from the wallet (`CEconomyComponent`,
`DiamondCount`); they cannot be dropped.
The engine adds no gun of a model already carried, which it tells by item id, so the carried
guns of that model have their ids turned aside for the call. Spare ammo is one pool per ammo type
and stays with the player, and upgrades are the player's, not the gun's, so nothing else is lost.

Until the notebook exists, a window in DevTools' overlay (Home) shows both slots and Items, with
Stow, Take, Give and Discard, and +1 on the diamonds, which runs `#Game:AddDiamonds(1)`. Its buttons
are carried out on the game thread, the next time the HUD asks for the player's gun.

## Testing

`build.ps1 -Install "<game>\bin"` builds the plugin and copies it into `bin\plugins\`.
