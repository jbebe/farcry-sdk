# Inventory Overhaul

Two guns of any class, and a notebook, opened with Tab and used in real time, for everything else
the player carries. The design is [the inventory design log](../../docs/docs/design/inventory.md);
this is an FCSE plugin, built in the log's steps.

| Step | What it does | Status |
|---|---|---|
| 1. Two guns | gun 1 on key 2, gun 2 on key 3, of any class; a third gun replaces the one in hand | works in game |
| 2. Items | guns stowed outside the two slots, at most two, and any other item by id and count; a test window in DevTools' overlay | works in game |
| 3. The notebook on Tab | a new Notebook control brings the map out with its notebook face up; the map's own mesh and paper stand in | works in game |
| 4. The Inventory page | the mockup's spread as a Magma page over the HUD: Hands, Items with the diamonds, the belt with its counts, the wheel moving along them, and a post-it with the selected row's details and upgrades | seen in game, a long Items list included; a bought upgrade not seen yet |

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

Until the notebook can use its rows, a window in DevTools' overlay (Home) shows both slots and
Items, with Stow, Take, Give and Discard, and +1 on the diamonds, which runs `#Game:AddDiamonds(1)`.
Fill gives the mockup's long Items list, two stowed guns and nine items, to see Items scroll; the
game started with the environment variable `INVENTORY_OVERHAUL_FILL` set does the same as it loads.
Its buttons are carried out on the game thread, the next time the HUD asks for the player's gun. An
item's id has no spaces, so the page shows its underscores as spaces.

## The notebook

The notebook is the map held in hand with its other face up (`src/notebook.cpp`). A new control,
**Notebook** (Tab, rebindable in Options > Controls), sends `toggle_notebook`; the plugin passes it
on as the map key's `toggle_gadget`, so the map comes out exactly as it does on its own key, and
remembers which face is up. While it is out, Tab and the map key turn it to their own face, and put
it away when their face is already up. `CGadgetMapStrategy::OnEquip` and `OnUnequip` say when the
map is really out. The terminal hands each signal to its listeners in turn, the same signal to
each, and the pause service's listener comes before the player's; the plugin rewrites the signal
there, at the listener's entry, which DevTools hooks too (FCSE stacks the two). A signal that only
turns the map over becomes one no listener knows, so the map stays out.

| File | What it is |
|---|---|
| `layer/mods/config/defaultusercontrols.xml/CATEGORY_MISC.xml` | the Notebook control, on Tab |
| `layer/mods/config/inputactionmapsingle.xml/weapons.xml` | Tab bound to `toggle_notebook` where the map key is bound |
| `layer/mods/languages/<language>/oasisstrings.fragment.xml` | the control's name in each language |

The notebook works on foot only for now: the driving controls are left as they are.

### The Inventory page

The page is the mockup's spread built in Magma and added to the HUD (`hud.mgb`), drawn over the
screen while the notebook's face is up. It is a 2D overlay for now; drawing it onto the map's paper
comes after. `build-ui.ps1` makes it from each retail `hud.mgb`, in both aspects and every language,
and writes the parts it changes as fragments under `layer/mods/ui/localized`, which merge with other
mods' HUD fragments, such as the flashlight's icon:

| Area | What it holds |
|---|---|
| `a_io_notebook` | the page's place on the HUD page, last there so it draws over the rest |
| `a_io_page` | the spread, the index tabs, Hands, Items and the belt, in the spread's own pixels |
| `a_io_postit` | the post-it with its tape, tilted on the page |

It is drawn the way the pause menu's paper pages are: the `notebook` texture, black Farcry2 ink with
no shadow, `common.mgb`'s post-it, tape, red line and brush stroke, cropped with the image's
`OFFSET` and `TILING`. Its size keeps the pause menu's notebook proportions, shrunk to the mockup's
size and place on screen. The guns' pictures are the shop's `gun_icon_*` textures, multiplied onto
the paper, one image per gun and row, all but one hidden; each condition word is its own text in its
own colour the same way, since a colour written into a text at runtime does not stay.

`src/page.cpp` reads the game and writes into the elements the package exports as `IO_*`, through
the string assignment `CMagmaFacade::SetLabel` makes and `Element::SetVisible`
(`src/engine/magma.cpp`), and only again when what it shows changes:

- **Hands:** guns 1 and 2 with their picture, short name and condition as a coloured word (new,
  used, worn, failing), then the machete.
- **Items:** stowed guns, other items with their count, and the player's diamonds; eight rows at a
  time, with how many more lie above and below.
- **The belt:** syrettes (the ammo type `syringe`), malaria pills (the player's counters
  component), grenades and molotovs (the uses of the gadget of each category).
- **The post-it:** the selected row. A gun has its picture, full name, class, slot, condition,
  `<loaded> / <spare>` ammo (the spare rounds being the player's whole pool of its ammo, so a stowed
  gun's too), its upgrades and the multiplayer loadout's five ratings as brush strokes. The machete,
  an empty slot, an item, the diamonds and the belt's items each have their own picture, count and
  notes.

The mouse wheel moves the selection along every list in reading order, the hands, Items, then the
belt, underlining the row in red pencil; the notebook takes the wheel's `cycleweapon` signal while
its face is up, so the gun in hand stays. It opens on the gun in hand, and keeps its place while
the game runs.

A gun's upgrades are the game's three icons, faint until bought: its operation manual, its repair
manual and the bandolier for its ammo. The syrettes have the two first aid manuals, and the grenades
and molotovs their bandoliers. Bought is a name in the wallet's list, as the shop names its items
(`ak47 repair manual`, `assault webbing`).

Names, classes and ratings are the shop's and the loadout's, matched by the archetype's last part;
a gun the table does not know is named from its archetype. The page shows at the end of each frame
(`GameThread::EachFrame`) and only while the notebook is open, so a HUD loaded meanwhile starts
hidden. Using the selected row, and the other tabs' pages, are still to come.

## Testing

`build.ps1 -Install "<game>\bin"` builds the plugin and copies it into `bin\plugins\`. The layer goes
in with JackAll, alongside every other layer the install carries:

```
tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe mod build --game "C:\Games\Far Cry 2" --layer <each layer> --layer mods\inventory-overhaul\layer
```
