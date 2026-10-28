# Inventory Overhaul

Two guns of any class, and a notebook, opened with Tab and used in real time, for everything else
the player carries. The design is [the inventory design log](../../docs/docs/design/inventory.md);
this is an FCSE plugin, built in the log's steps.

| Step | What it does | Status |
|---|---|---|
| 1. Two guns | gun 1 on key 2, gun 2 on key 3, of any class; a third gun replaces the one in hand | works in game |
| 2. Items | guns stowed outside the two slots, at most two, and any other item by id and count; a test window in DevTools' overlay | works in game |
| 3. The notebook on Tab | a gadget of its own beside the map, given with it, on a new Notebook control: two bent pages held in both hands, the right one bending as the hands move | works in game |
| 4. The Inventory page | the mockup's spread as a Magma page drawn onto the notebook's pages: Weapons, Items with the diamonds, the belt with its counts, the wheel moving along them, and a post-it with the selected row's details and upgrades | seen in game, a long Items list included; a bought upgrade not seen yet |
| 6. Papers, Journal, Contacts | R turns the page; letters and photos, read whole with LMB, the Jackal and Predecessor tapes found, the reputation and the current job, and the buddies met, in the game's words; the pause menu without those pages or its job panel | seen in game in world 2; the papers are samples; reading a paper whole seen by self-test, the dummy note's sheets and photo not yet |
| 7. Papers in the world | the torn notebook page with the Jackal photo lies on a burnt-out truck's bed in world 1; picking it up puts it in Items, so Papers shows it | built, not yet seen in game |

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

The notebook is a gadget of its own, `gadgets.Equipped.Notebook`, in the spare gadget category
`unused1` beside the map (`src/notebook.cpp`). It is the map's archetype with the notebook's model,
its own arms and no GPS. The plugin gives it to the player once they carry the map, with the
inventory's `AddGadget`, looking again whenever the inventory changes, so a save from before the mod
gets one too. A gadget category needs a slot in `gamemodesconfig.xml`'s `GadgetCategories` to be
taken out; the plugin adds `unused1`'s to the weapons service's map after
`CFCXWeaponsService::LoadCategorySlots` fills it, so the file itself is left alone.

A new control, **Notebook** (Tab, rebindable in Options > Controls), sends `toggle_notebook`; the
plugin passes it on as the map key's `toggle_gadget` and remembers it came from Tab. The map key's
action, `CGOStateEventInventory::ToggleGadget`, then runs with the notebook's category in place of
the map's, so the notebook comes out, switches with the map and goes away exactly as the map does.
`CGadgetMapStrategy::OnEquip` and `OnUnequip`, which both gadgets run, say when the notebook is out.
The terminal hands each signal to its listeners in turn, the same signal to each, and the pause
service's listener comes before the player's; the plugin rewrites the signal there, at the
listener's entry, which DevTools hooks too (FCSE stacks the two).

`build-notebook.ps1` builds the gadget into the layer with the scripts in `notebook/`, each of which
says what it makes:

| File | What it is |
|---|---|
| `layer/mods/config/defaultusercontrols.xml/CATEGORY_MISC.xml` | the Notebook control, on Tab |
| `layer/mods/config/inputactionmapsingle.xml/weapons.xml` | Tab bound to `toggle_notebook` where the map key is bound |
| `layer/mods/languages/<language>/oasisstrings.fragment.xml` | the control's name in each language |
| `layer/mods/graphics/objects/equipment/notebook/` | the pages (`page.py`): two pages meeting at the spine, each a chain of four hinged strips, drawn with a copy of the map's `MAPTOP` material cut out by its alpha |
| `layer/mods/graphics/characters/_common/animations/equipment/equipped_gadget/*_eqnote*.mab` | the arms (`arms.py`): the map's banks on foot with the left hand moved off the pages and the right one holding the other edge in the idle's grip, mirrored |
| `layer/mods/graphics/move/movemgr.bin/`, `layer/mods/worlds/<world>/generated/<world>_depload.dat/` | MOVE weapon 9's (the map's) rules copied onto 44 and pointed at those banks, each registered where the map's is (`clips.py`) |
| `layer/mods/worlds/<world>/generated/entitylibrary.fcb/gadgets/Equipped/Notebook.xml` | the archetype in each world (`archetype.py`) |

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
| `a_io_postit` | the post-it, held by `common.mgb`'s paperclip, tilted at the right page's top |

It is drawn the way the pause menu's paper pages are: the `notebook` texture, black Farcry2 ink with
no shadow, `common.mgb`'s post-it, paperclip, red line and brush stroke, cropped with the image's `OFFSET`
and `TILING`. The sheet is the spread with room above it for the index tabs, which stand up from the
left page's top edge behind the paper, out of the fingers' way: the open page's pulled up and white,
the others grey. Everything is written in the one ink; the only colour is the red line under the
selected row, tinted as the pause pages tint it.
The post-it's gun pictures are the shop's `gun_icon_*` textures, multiplied onto the paper, one image
per gun, all but one hidden.

`src/engine/paper.cpp` puts it on the pages. Its first and last images, `mark_begin` and `mark_end`,
are clear textures of a size no other has. While the notebook is open, the Direct3D texture binds
between them go into a target of the screen's size, with alpha, instead of the screen; it is cleared to
a clear paper tone, so the filtered edges of the spread and its tabs fade into paper, not black. That
target is
copied into a mip-mapped texture, which is bound wherever the area map is: the notebook's pages draw
with a material named `MAPTOP`, which `CGadgetMapStrategy::SetMapTexture` fills with the area map,
and cut themselves out by the sheet's alpha, so their outline is the notebook picture's. Magma draws
after the world, so the pages show the frame before's page. A frame that ends before the last marker
goes back to the screen, and a device reset releases both targets.

`src/page.cpp` reads the game and writes into the elements the package exports as `IO_*`, through
the string assignment `CMagmaFacade::SetLabel` makes and `Element::SetVisible`
(`src/engine/magma.cpp`), and only again when what it shows changes:

- **Weapons:** guns 1 and 2 by their short name, marked with multiplayer's VIP letters A and B,
  then the machete. Their picture and condition are on the post-it.
- **Items:** stowed guns, other items with their count, and the player's diamonds; ten rows at a
  time, with how many more lie above and below beside the pause menu's arrow.
- **The belt:** syrettes (the ammo type `syringe`), malaria pills (the player's counters
  component), grenades and molotovs (the uses of the gadget of each category).
- **The post-it:** the selected row. A gun has its picture, full name, type (Pistol), condition,
  `<loaded> / <spare>` ammo (the spare rounds being the player's whole pool of its ammo, so a stowed
  gun's too), its upgrades and the multiplayer loadout's five ratings as brush strokes. The machete
  has its HUD icon, "Melee" and a note; the diamonds their HUD icon and a description under the name;
  a belt item its picture, count and the upgrades for it right under that.

The mouse wheel moves the selection along every list in reading order, the weapons, Items, then the
belt, underlining the row in red pencil; the notebook takes the wheel's `cycleweapon` signal while
it is out, so the gun in hand stays. It opens on the gun in hand, and keeps its place while
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

R, the map's `flipside`, turns the page while the notebook is out: Inventory, Papers, Journal,
Contacts, then Inventory again. The notebook takes the signal, as it does the wheel's, so it is not
turned over as the map would be. The turn is the pause menu's own: an instance of `common.mgb`'s
page flip area (`a_io_flip`, last on the spread, shown only while a page turns) at twice its speed,
its time set each frame without its keyframes' actions, since the area is the pause menu's too. Its
`SoundEvent`'s sound is played from the plugin instead, with the same id and type, and faded out to
end at half its length (see
[driving an area's timeline](../../docs/docs/magma-ui/engine-interop.md#driving-an-areas-timeline-from-code)).
Each page keeps its own selection. Each is an area of its own in the spread
(`a_io_inventory`, `a_io_papers`, `a_io_journal`, `a_io_contacts`), one shown at a time, with its
index tab pulled out over the spread; the post-it is shared, and hidden on a page with no rows. Every
text the game has is the game's, in the player's language, through its string table
(`src/engine/story.cpp`):

- **Papers** (`src/papers_page.cpp`): on the left the letters and photos, the Items records the page
  knows by id, which leave the Inventory page's Items. Four papers from the mockup, with their
  textures in `textures\common`: the torn notebook page is found in the world, the other three are
  still samples. Two dummies try the reader: a note three sheets long and a photo, a loading-screen
  picture shown from its six tiles. On the right the Jackal tapes found and then the Predecessor's,
  from the mission manager's two lists, each with its title and the game's line about it. The post-it
  shows a paper's opening; LMB (`startshooting`) lays the paper whole over the right page, on the
  post-it's paper turned upright (`a_io_reader`), the wheel turns a long letter's sheets, and LMB, Esc
  (`show_pausemenu`), R or Tab put it back.
- **Journal** (`src/journal_page.cpp`): on the left the reputation level and its words, as the pause
  menu's Journal has them; on the right the current job, its objectives and the Underground's, as the
  pause menu's main page showed them (`src/engine/jobs.cpp`): every objective marker that is on, its
  texts from `Objectives` with the buddies' and lieutenants' names filled in by the game's own
  `GetDescriptionWithBuddyName`. The markers are read at most once a second.

The pause menu (`src/pause_menu.cpp`) no longer has the pages the notebook took: while
`CFCXPauseMenuPage::Setup` runs, `CListMenuPage::AddButton` leaves out Journal, Buddies, Jackal Tapes
and Predecessor Tapes, known by their words in the player's language. Its main page's job panel is
hidden in the layer (`sp_pause_menus.mgb`, one area fragment per language).
- **Contacts** (`src/contacts_page.cpp`): the buddies met, from the buddies manager, all on the left
  page: the active buddies, the primary and the secondary (the manager's `+0x50` and `+0x54` point at
  their records), then the others, the living before the dead. Each row has a portrait, name and
  status, a dead buddy's in faded italic ink, and the post-it has their nationality, age and the
  first line the pause menu's Buddies page has. A row's portrait is copied from a hidden image of the
  same package (`a_io_portraits`); a faded row is a second pair of texts, shown in the normal one's
  place, since a text's colour cannot be changed at runtime.

Fill in the test window, or `INVENTORY_OVERHAUL_FILL`, also gives the sample papers and, through the
game's own Lua in DevTools' console, meets Frank, Flora, Marty, Josip and Warren, makes Frank the
primary buddy and Hakim the secondary (the game takes only a living one), kills Warren, and finds
five Jackal tapes and the Moroccan's. `INVENTORY_OVERHAUL_LUA` holds Lua lines, split by `|`, that the
console runs as the game loads, such as `AddGadget(SpawnWeapon("gadgets.Equipped.Map"))`.

A paper in the world is the Predecessor's envelope pickup drawn as the stock single sheet, which
`build-papers.ps1` writes into the layer (`notebook/papers.py`), with an instance in its sector: a
`CPickupMissionItem` whose archetype holds its Items id as the entity data
`InventoryOverhaul.Item`, saved at persist level 3 like a diamond case so it stays picked. After the
engine's `CPickupMissionItem::Pick`, `src/pickups.cpp` gives the player that item. The torn notebook
page lies on the bed of the burnt-out truck at about (1835, 2387) in world 1, by its open tailgate.

## Testing

`build.ps1 -Install "<game>\bin"` builds the plugin and copies it into `bin\plugins\`. The layer goes
in with JackAll, alongside every other layer the install carries:

```
tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe mod build --game "C:\Games\Far Cry 2" --layer <each layer> --layer mods\inventory-overhaul\layer
```
