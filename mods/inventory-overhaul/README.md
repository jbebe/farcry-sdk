# Inventory Overhaul

Two guns of any class, and a notebook, opened with Tab and used in real time, for everything else
the player carries. The design is [the inventory design log](../../docs/docs/design/inventory.md);
this is an FCSE plugin, built in the log's steps.

| Step | What it does | Status |
|---|---|---|
| 1. Two guns | gun 1 on key 2, gun 2 on key 3, of any class; a third gun replaces the one in hand | works in game |
| 2. Items | guns stowed outside the two slots, at most two, and any other item by id and count; a test window in DevTools' overlay | works in game |
| 3. The notebook on Tab | a new Notebook control brings the map out with its notebook face up; the map's own mesh and paper stand in | works in game |
| 4. The Inventory page | the mockup's spread as a Magma page drawn onto the notebook's pages: Hands, Items with the diamonds, the belt with its counts, the wheel moving along them, and a post-it with the selected row's details and upgrades | seen in game, a long Items list included; a bought upgrade not seen yet. On the notebook's pages seen only with the notebook model from a test layer; with the map it lands on the map's sheet |
| 6, in part. Papers, Journal, Contacts | R turns the page; letters and photos and the Jackal tapes found, the reputation, and the buddies met, in the pause menu's words | seen in game in the demo range; the Journal's jobs are not read yet, and the papers are samples |

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
Fill gives the mockup's long Items list, two stowed guns and nine items, to see Items scroll, and the
other pages' test data; the game started with the environment variable `INVENTORY_OVERHAUL_FILL` set
does the same as it loads.
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

The pages are the mockup's spread built in Magma and added to the HUD (`hud.mgb`), and drawn onto
the notebook's pages in the world rather than over the screen. `build-ui.ps1` makes it from each
retail `hud.mgb`, in both aspects and every language, and writes the parts it changes as fragments
under `layer/mods/ui/localized`, which merge with other mods' HUD fragments, such as the flashlight's
icon, and its two marker textures under `layer/mods/ui/textures/io`:

| Area | What it holds |
|---|---|
| `a_io_notebook` | the sheet the notebook's pages show, laid untilted over the HUD's whole canvas, last on the HUD page |
| `a_io_page` | the spread and the index tabs, in the spread's own pixels, with the areas below, between two marker images |
| `a_io_inventory`, `a_io_papers`, `a_io_journal`, `a_io_contacts` | one page each |
| `a_io_portraits` | the buddies' portraits, hidden, for rows to copy |
| `a_io_postit` | the post-it with its tape, tilted on the page |

It is drawn the way the pause menu's paper pages are: the `notebook` texture, black Farcry2 ink with
no shadow, `common.mgb`'s post-it, tape, red line and brush stroke, cropped with the image's `OFFSET`
and `TILING`. The sheet is the spread with room above it for the index tabs, which stand up from the
right page's top edge, out of the fingers' way: the open page's pulled up and white, the others grey.
The guns' pictures are the shop's `gun_icon_*` textures, multiplied onto the paper, one image per gun
and row, all but one hidden; each condition word is its own text in its own colour the same way,
since a colour written into a text at runtime does not stay.

`src/engine/paper.cpp` puts it on the pages. Its first and last images, `mark_begin` and `mark_end`,
are clear textures of a size no other has. While the notebook is open, the Direct3D texture binds
between them go into a target of the screen's size, with alpha, instead of the screen. That target is
copied into a mip-mapped texture, which is bound wherever the area map is: the notebook's pages draw
with a material named `MAPTOP`, which `CGadgetMapStrategy::SetMapTexture` fills with the area map,
and cut themselves out by the sheet's alpha, so their outline is the notebook picture's. Magma draws
after the world, so the pages show the frame before's page. A frame that ends before the last marker
goes back to the screen, and a device reset releases both targets.

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
hidden. Using the selected row is still to come.

### The other pages

R, the map's `flipside`, turns the page while the notebook's face is up: Inventory, Papers, Journal,
Contacts, then Inventory again. The notebook takes the signal, as it does the wheel's, so the map
stays as it is. Each page keeps its own selection. Each is an area of its own in the spread
(`a_io_inventory`, `a_io_papers`, `a_io_journal`, `a_io_contacts`), one shown at a time, with its
index tab pulled out over the spread; the post-it is shared, and hidden on a page with no rows. Every
text the game has is the game's, in the player's language, through its string table
(`src/engine/story.cpp`):

- **Papers** (`src/papers_page.cpp`): on the left the letters and photos, the Items records the page
  knows by id, which leave the Inventory page's Items. For now four sample papers from the mockup,
  with their textures in `textures\common`. On the right the Jackal tapes found, from the mission
  manager, each with its title and the game's line about it.
- **Journal** (`src/journal_page.cpp`): the reputation level and its words, as the pause menu's
  Journal has them. Where the game keeps the jobs is not traced yet, so the right page says so.
- **Contacts** (`src/contacts_page.cpp`): the buddies met, from the buddies manager, those ready to
  pull the player out on the left and the rest on the right, each with a portrait, name and status,
  and on the post-it their nationality, age and the first line the pause menu's Buddies page has.
  A row's portrait is copied from a hidden image of the same package (`a_io_portraits`).

Fill in the test window, or `INVENTORY_OVERHAUL_FILL`, also gives the sample papers and, through the
game's own Lua in DevTools' console, meets Frank, Flora, Marty, Josip and Warren, makes Flora
rescue-ready, and finds five tapes. `INVENTORY_OVERHAUL_LUA` holds Lua lines, split by `|`, that the
console runs as the game loads, such as `AddGadget(SpawnWeapon("gadgets.Equipped.Notebook"))`.

## Testing

`build.ps1 -Install "<game>\bin"` builds the plugin and copies it into `bin\plugins\`. The layer goes
in with JackAll, alongside every other layer the install carries:

```
tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe mod build --game "C:\Games\Far Cry 2" --layer <each layer> --layer mods\inventory-overhaul\layer
```
