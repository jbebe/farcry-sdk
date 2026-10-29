---
sidebar_position: 6
---

# Inventory

**Status:** planned 2026-10-26. Nothing is built or seen in game, and none of the engine findings
below has been tested. The planned mod is `mods/inventory-overhaul`: an FCSE plugin plus a data
layer. Marked **(proposed)** are choices not yet agreed.

## The goal

- **The player stays in the game.** The inventory is a notebook pulled out with Tab and held in both
  hands the way the map is held, while the world keeps running. A pause screen or slowed time would
  make the player feel "they paused it for him".
- **Two guns, any kind.** Gun 1 and gun 2, whatever their class, the same model in both if the player
  has two of them.
- **Simple.** Lists, at most a table with a few columns, never a grid. Hold things, use what can be
  used, drop what can be dropped.
- **Every item shows a picture**, or a placeholder until it has one, and its details: a gun's wear,
  magazine, ammo.
- **Room to grow.** Fetch missions, letters and photos now; diamonds, food, pills and ammo as items
  later, once decided.

## Decisions

- **Tab, in real time, held like the map.** Tab is free in single player: only multiplayer's
  scoreboard and the console's autocomplete use it.
- **Two gun slots, any class.** The machete stays on 1, gun 1 and gun 2 are 2 and 3. Grenades and
  molotovs stay gadgets.
- **Picking up a gun** fills an empty slot. With both full it replaces the gun in hand, which is
  stowed in Items if it has room and falls at the feet if not **(proposed)**.
- **Items** carries what is not in a slot: at most two more guns, so four guns in all, and everything
  else without a limit. No weight and no carrying capacity.
- **One box, shared** by every safe house and the armory.
- **Bought guns are a catalogue:** always in the box, fresh, as many copies as wanted. A gun the player
  stores is kept as it is, with its wear.
- **No numbers about the player in the notebook.** A page of statistics pulls the player out of the
  game as a pause screen does, so Statistics and Upgrades stay in the pause menu, with Resume, Save,
  Load, Options, Legend and Quit. Jackal Tapes, Predecessor Tapes, Buddies, Journal and the malaria
  panel move into the notebook; malaria becomes the pills' card.
- **Handwriting only on the paper being read** (2026-10-28, narrowed 2026-10-29). A note's text and
  a photo's caption, on the half page the reader shows, are in Caveat, a handwriting font. Everything
  else, the Journal's job and objectives and the post-it included, keeps the game's Farcry2 ink.
- **No key legend on the notebook** (2026-10-29). A strip naming the keys as bound was built and
  dropped as not needed.

## What the engine gives us

- **The map is the template.** `gadgets.Equipped.Map` is a `CGadget` with `bUseBothHands`, hand
  animation set 9 and the map strategy (`CGadgetMapStrategy`). The paper is a mesh (`map.xbg`) with
  four corner bones, and its markers are small 3D objects placed between them. While it is out, the
  `mapcompass` input set keeps movement and look and adds left mouse, right mouse (monocular) and R.
- **The monocular is a gadget of its own.** The map names it in `archMonocularArchetype`:
  `gadgets.Equipped.Monocular`, the binoculars strategy, a zoom field of view of 0.2. Right mouse
  sends `draw_monocular` on press and `holster_monocular` on release. The notebook can name the same
  monocular.
- **R turns the map over by animation.** `CGadgetMapStrategy::FlipSide` (server `0x0890fc40`) sends
  `flipside` to the player's state machine. A page turn can be that signal.
- **Gadget slots have spares.** The gadget category and strategy enums both carry `Unused1` and
  `Unused2`. A gadget is taken out only when its category has a slot in `gamemodesconfig.xml`'s
  `GadgetCategories` (the map's is 1, the others' 0); without one it is carried but never drawn.
  `CFCXWeaponsService::LoadCategorySlots` (Steam `0x107692F0`) reads them into a `std::map` at
  `+0x13C`. The map key's action, `CGOStateEventInventory::ToggleGadget` (Steam `0x10CE2440`), takes
  the map's category from one global, `0x10FCD628`, which nothing else on that path reads.
- **The engine keeps one gun per category.** `CInventoryViewPawn::GetWeaponFromCategory` and
  `DropWeaponFromCategory` (server `0x08feb1a0`, `0x08ff8730`) walk the inventory list for the gun whose
  category matches (weapon vtable `+0x98`). The number key for each category is data:
  `WeaponCategories` in `gamemodesconfig.xml`, read by `CFCXWeaponsService::LoadCategorySlots`.
  Multiplayer's own copy already puts primary and special on one key.
- **A pickup replaces the gun of its own category.** `CPickupWeapon::PrePickup` (server `0x090ed740`)
  finds the player's gun in the pickup's category (the pickup's `+0xBC`), selects it, moves its loaded
  rounds into the spare pool and drops it. Between two of the same model, the one taken keeps the
  better condition. Spare ammo is one pool per ammo type (`CInventoryViewPawn::AddAmmo`), not per gun.
- **The box already exists.** Safe-house weapon crates are `CMagicCrate`, which hands out a bought gun
  of its category ([interactive objects](../modding/interactive-objects.md)). Buying at the armory
  switches on one layer per weapon (`layer="Missions/WeaponBazaar/..."`).
- **Quest items already exist.** Tapes and courier pouches are `CPickupMissionItem`, and mission
  graphs hear the pickup.
- **Pictures and ratings for every gun.** The shop names each gun's HUD icon (`hud_icon_*`) and a
  class label (`WEAPONBAZAAR_*_CRATE_DESCRIPTION`: "Assault", "R-Launcher"). Multiplayer's loadout gives
  each gun an icon (`gun_icon_*`) and five 1–5 ratings: damage, range, accuracy, reliability, fire rate.
- **Upgrades are the shop's plans** (`<Plan>` in `gamemodesconfig.xml`). Every gun but the IED has an
  operation manual (accuracy +20 %, recoil −20 %) and a repair manual (wear −20 %, unjam time −35 %).
  Nine bandoliers each raise the ammo of a group of guns, and two of them also of grenades or
  molotovs. The Medium and Large Kit add a syrette each. The Camo Suit is the Stealth Upgrade. The
  game draws upgrades with three icons: `ui_icon_manualupgrade`, `ui_icon_repairupgrade` and
  `ui_icon_ammoupgrade`.
- **A gun's state is saved on it.** `CFCXWeapon` keeps `AmmoInClip`, `MaxReliability`, `JamCounter`
  and `RememberedAmmoOverflow` ([savegame](../file-formats/savegame.md)).
- **A plugin can save on the player.** FCSE entity data on the player's entity is always saved
  ([persistence](../modding/persistence.md)). A plugin cannot read Domino's `Globals`.
- **Proven in this repo:** a new screen bound by FCSE (the Mods page), a new rebindable control
  (Flashlight), and picking out one held mesh's draws: Aiming Overhaul drops the scope tube's and
  lays its own eyepiece over the finished frame.

## The notebook

### Pages

Each page has an index tab along the left page's top, clear of the fingers, standing up from behind
the paper; the open page's is pulled up and white. The tab says where the player is, so a page has
no title. R turns the page, using the map's own flip.

| Page | Left page | Right page, below the post-it |
|---|---|---|
| Inventory | Weapons, Items | Belt |
| Papers | letters and photos | the Jackal and Predecessor tapes |
| Journal | reputation | current jobs |
| Contacts | the active buddies (primary, secondary), then the others, living first, the dead faded | nothing below the post-it |
| Box (only at a crate) | bought guns, stored things | Items |

### Lists, and a post-it for the highlighted row

Lists fill the left page and the right page below the post-it. The post-it, held at the right page's
top by a paperclip, shows the highlighted row's picture and details.

```
 ┌──────────────────────────────┬──────────────────────────────┐
 │ WEAPONS                      │ ┌───────────────────────║──┐ │
 │ Ⓐ AK-47                      │ │ [picture]  Carl G        │ │
 │ Ⓑ Makarov                    │ │ R-Launcher               │ │
 │   Machete                    │ │ condition worn  dmg ●●●●●│ │
 │ ITEMS                        │ │ ammo 1 / 4      rng ●●●●○│ │
 │ Carl G                       │ └──────────────────────────┘ │
 │ ‾‾‾‾‾‾                       │ BELT                         │
 │ Courier pouch                │ Syrettes           ×3        │
 │ 37 diamonds                  │ Malaria pills      ×14       │
 └──────────────────────────────┴──────────────────────────────┘
```

### Long lists scroll

Each list shows a fixed number of rows and scrolls with the highlight; there is no scrollbar.
When rows are hidden, a pencilled "▲ 3 more" or "▼ 5 more" sits at the list's edge. Only the list
moves: section headings stay, and Weapons never scrolls. On the Inventory page Items shows 10 rows; on
the Box page the catalogue shows 7 and the stored list 3.

The catalogue grows long: the shop sells 28 guns (12 primary, 8 secondary, 7 special, and IEDs),
and they are listed in the shop's order, so a gun is always in the same part of the list.

### What a row shows

| List | A row shows |
|---|---|
| Weapons | the slot's letter, A or B as multiplayer's VIP marks, and the gun's name; its picture and condition are on the post-it |
| Items | the name, nothing more |
| Belt | the picture, the name, the count |

**A gun's ammo is one line, "Ammo 30 / 120":** the rounds in the gun, then every remaining round
the player carries for it. The words "loaded" and "spare" are never shown.

**A gun's condition is only ever a word,** in the same ink as the rest: *new*, *used*, *worn*,
*failing*. Never a bar or a number, so nobody can tell exactly when a gun will jam or blow up. The
page uses no colours of its own.

### What the post-it shows

Every post-it reads the same way, whatever the kind: the picture, then the name, then the details.

| Kind | Picture | Details |
|---|---|---|
| Gun | its picture | class, where it is, condition word, ammo, upgrades, the five ratings |
| Quest item | placeholder until drawn | who it is for, which job |
| Diamonds | the diamond icon | count |
| Letter | the letter itself | where it was found, its text |
| Photo | the photo itself | caption |
| Tape | the tape icon | number, date, length, a line from it |
| Syrette, pills, grenade, molotov | their HUD icons | count, upgrades (not pills) |
| Camo Suit, once bought | placeholder until drawn | the shop's Stealth Upgrade |

### Upgrades

The post-it shows an item's upgrades as the game's three upgrade icons, inked when bought and faint
when not, so the level reads at a glance.

| Item | Upgrades |
|---|---|
| A gun | operation manual (accuracy), repair manual (reliability), its bandolier (ammo); the IED has only the bandolier |
| Syrettes | Medium Kit, Large Kit |
| M67 grenades | Grenadier Webbing |
| Molotovs | Pyrotechnic Satchel |

Which bandolier covers which gun, from the shop's plans:

| Bandolier | Guns |
|---|---|
| Pistol Belt | Makarov, Star .45, Eagle.50, 6P9 |
| Light Assault Webbing | MAC-10, Uzi |
| Shotgun Bandolier | Homeland 37, SPAS-12, USAS-12 |
| Assault Webbing | MP-5, FAL, G3-KA4, AK-47, AR-16 |
| Marksmans Bandolier | M1903, Dragunov, AS50, Dart Rifle |
| Rocketeer Satchel | RPG-7, Carl G, Type 63 Mortar |
| Grenadier Webbing | M-79, IEDs, MGL-140, and M67 grenades |
| Pyrotechnic Satchel | Flare Pistol, LPO-50, and molotovs |
| Gunner Pack | PKM, M-249 |

### What the player can do with it

| Kind | Use | Drop |
|---|---|---|
| Gun in a slot | — (2 and 3 select it, as now) | falls at the feet as a pickup, with its wear and ammo |
| Gun in Items | equip to gun 1 or 2, swapping with the gun there **(proposed)** | as above |
| Diamonds | — | no: the row is the player's own diamond count |
| Syrette | heal, as the heal key does | no |
| Letter, photo | read: it lies over the whole right page on the post-it's paper turned upright, the post-it hidden; a photo keeps its own colours; the wheel turns a long letter's sheets; LMB or Esc puts it back (built 2026-10-28) | no |
| Tape | play or stop, while the player walks on | no |
| Quest item | — | no, until its job ends |
| Other item | — | thrown away, one at a time |
| Pills, grenades, molotovs | — | no |

A gun swapped out of a slot is stowed in Items if Items holds fewer than two guns, and falls at the
player's feet if not.

### Items

At most two guns, any kind, so the player carries four in all: two in hand, two stowed. Everything
else has no limit and no weight, so a third spare gun is the only thing Items ever refuses.

### The box

Using a crate pulls the notebook out at the Box page:

- **Catalogue:** every bought gun, fresh, any number of times. Take one into gun 1, gun 2 or Items.
- **Stored:** what the player put in, each kept with its wear and ammo. No limit.

On the Inventory page, while the player is at a crate, a gun or quest item gets a **Store** action.

### Controls (proposed)

| Input | Does |
|---|---|
| Tab | take the notebook out or put it away |
| Mouse wheel, ↑ ↓ | move along the list |
| R | turn the page |
| Left mouse | use or equip the highlighted item |
| 2, 3 on a gun | put it in gun 1, gun 2 |
| X | drop the selected row; with the notebook away, the gun in hand (built 2026-10-28) |
| Right mouse, held | the monocular, as with the map: the notebook comes back on release |

Movement and look stay live, as with the map. The gamepad mirrors the map's: d-pad to move, X to turn.

## Under the hood

- **Two guns:** every gun keeps its real class, because the box, scripts and the HUD read it. Each
  carried gun holds its slot, 1 or 2, as entity data. Keys 2 and 3 draw gun 1 and gun 2 whatever
  their class, `AddWeapon` drops the gun in hand when two are carried, and while the player picks
  something up the engine finds no carried gun of its class to replace. Two of the same model share
  an item id, so the later one gets an id of its own.
- **A stowed gun** is a record (archetype, wear, loaded rounds) in Items, on the player's entity, and
  the gun itself leaves the game. Taking it out makes a new gun of that archetype and puts its wear
  and rounds back. Kept in the inventory, a stowed gun would be reached by weapon cycling, the HUD
  and the crates, which all walk it.
- **Item definitions** live in a data file (id, kind, name and text keys, picture, stack).
  The plugin holds two lists, Items and the box, both saved on the player's entity.
- **The notebook is a gadget of its own** beside the map, in the spare category `unused1`: the map's
  archetype with the notebook's model and arms, given to the player once they carry the map. A
  Notebook control (Tab) runs the map key's own action with the notebook's category in the map's
  place, so it comes out, switches with the map and goes away exactly as the map does. Its category's
  slot is added to the weapons service at runtime, which leaves `gamemodesconfig.xml` to other mods.
- **The page is Magma**, built the way the pause menu's paper pages are, so it looks like the game's
  own: the `notebook` texture, Farcry2 ink, `common.mgb`'s post-it, paperclip and red line, with the
  index tabs along the top, clear of the fingers. The handwriting is a font of the game's own kind,
  Caveat drawn at weight 600 and 40 px by JackAll's `mft build` and added to every `fonts.mgb` and
  UI dependency list as fragments (seen in game, 2026-10-28). It is drawn onto the notebook's pages, where it is
  lit by the world and moves with the hands, since a flat overlay reads as a HUD. The draws between
  the page's first and last image go into a texture instead of the screen, and the pages draw with a
  material named `MAPTOP`, which shows that texture in place of the area map, cut out to the notebook
  picture's own outline. The notebook in hand is a model of its own: two pages meeting at the spine,
  10° apart, each a chain of hinged strips, so the right page bends as the hands come together and
  apart. Its arms are the map's banks on foot, remade in Blender: the left hand moved 5 cm off the
  pages' edge along them and 0.5 cm toward the eye, the notebook's own track baked again so it stays
  put. The right hand keeps one grip on the right page's edge strip: the idle's left hand mirrored
  across the pages' centre line, lifted 3 cm up the screen, 2 cm further out and 1.5 mm toward the
  eye, so no finger crosses the page. Half the left hand's own movement mirrored across the chest, at
  most 3 mm, bends the right page's hinges, and the hand turns with the strip it holds. Where the right
  hand would be over 48.5 cm from its shoulder, as when walking, the notebook comes toward that
  shoulder instead, so each arm reaches with its elbow and shoulder and the collarbones keep the map's
  pose. Running, the notebook is lowered and the right arm is the map's own, swinging against the
  left; sliding, it is the left one mirrored across the chest.

## Steps

Each step ends with one check in game before the next.

| Step | What | Check in game |
|---|---|---|
| 1 | Two-gun rule, no UI | carry two AK-47s; a third gun swaps with the one in hand |
| 2 | Item core and Items; a test window in DevTools' overlay to stow, take, give and discard | a stowed Carl G survives a save and reload |
| 3 | The notebook in hand on Tab, the map's mesh standing in | walk, look and get shot with it out |
| 4 | The Inventory page in Magma, as an overlay, then on the paper | pictures, wear and ammo match the guns |
| 5 | The notebook model, page turns on the flip | |
| 6 | The pause pages moved in: Papers (sample papers, the Jackal and Predecessor tapes found), Journal (reputation and the current job) and Contacts (the buddies met), each read from the game, and R turning the page. The pause menu loses those entries and its main page's job panel; the story synopses the pause menu's Journal had are dropped | the pause menu keeps only its eight entries (seen, 2026-10-28) |
| 7 | Papers and memories: the torn notebook page with the Jackal photo clipped to it, where a boombox stands on a beer barrel, picked up into Items | read one, walking (seen in game, 2026-10-29) |
| 8 | The box at the crates | bring a found Carl G home and take it out again |
| 9 | One fetch mission | |
| later | Diamonds, food, pills, ammo as items | |

## Open

- **The arrow keys:** the mockup moves along the lists with ↑ and ↓ as well as the wheel, but
  the game binds them to walking beside W and S, so for now only the wheel moves the selection.
- **Real papers:** the torn notebook page is found in the world; the Papers page's other three are
  still samples the test window's Fill gives.
- **What still selects by class:** the weapon crate draws the gun it hands out by its class, and a
  pickup's switch prompt shows the carried gun of the pickup's class. Step 1 shows how far it shows.
- **How a mission asks "does the player carry X"**, since the plugin cannot read `Globals`. Candidates:
  a layer's state, or a handler in `MissionTools.lua`.
