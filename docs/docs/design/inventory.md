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
  `Unused2`.
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

Each page has an index label on the notebook's right edge, written vertically. The labels sit
tucked behind the pages; the open one is drawn over the notebook. The label says where the player
is, so a page has no title. R turns the page, using the map's own flip.

| Page | Left page | Right page, upper half |
|---|---|---|
| Inventory | Hands, Items | Belt |
| Papers | letters and photos | the Jackal and Predecessor tapes |
| Journal | reputation | current jobs |
| Contacts | buddies who will pull the player out | the other buddies |
| Box (only at a crate) | bought guns, stored things | Items |

### Lists, and a post-it for the highlighted row

Lists fill the left page and the upper half of the right page. A post-it stuck on the lower half of
the right page shows the highlighted row's picture and details.

```
 ┌──────────────────────────────┬──────────────────────────────┐╮
 │ HANDS                        │ BELT                         ││ I
 │ ① [AK-47 picture]  AK-47 used│  Syrettes          ×3        ││ n
 │ ② [Makarov]      Makarov new │  Malaria pills     ×14       ││ v
 │   Machete                    │  M67 grenades      ×3        │╯
 │ ITEMS                        │ ┌──────────────────────────┐ │╮
 │ ▸ Carl G                     │ │ [picture]  Carl G        │ ││ P
 │   Courier pouch              │ │ R-Launcher · stowed      │ ││ a
 │   37 diamonds                │ │ condition worn  dmg ●●●●●│ ││ p
 │                              │ │ ammo 1 / 4      rng ●●●●○│ │╯
 └──────────────────────────────┴─┴──────────────────────────┴─┘
```

### Long lists scroll

Each list shows a fixed number of rows and scrolls with the highlight; there is no scrollbar.
When rows are hidden, a pencilled "▲ 3 more" or "▼ 5 more" sits at the list's edge. Only the list
moves: section headings stay, and Hands never scrolls. On the Inventory page Items shows 8 rows; on
the Box page the catalogue shows 7 and the stored list 3.

The catalogue grows long: the shop sells 28 guns (12 primary, 8 secondary, 7 special, and IEDs),
and they are listed in the shop's order, so a gun is always in the same part of the list.

### What a row shows

| List | A row shows |
|---|---|
| Hands | the slot's number, the gun's picture, its name, its condition as a coloured word |
| Items | the name, nothing more |
| Belt | the picture, the name, the count |

**A gun's ammo is one line, "Ammo 30 / 120":** the rounds in the gun, then every remaining round
the player carries for it. The words "loaded" and "spare" are never shown.

**A gun's condition is only ever a word,** coloured: *new* green, *used* olive, *worn* orange,
*failing* red. Never a bar or a number, so nobody can tell exactly when a gun will jam or blow up.

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
| Letter, photo | read: it lies over the right page | no |
| Tape | play or stop, while the player walks on | no |
| Quest item | — | no, until its job ends |
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
| X | drop |
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
- **The page** is drawn by the plugin into a texture, swapped in when the notebook's paper is drawn,
  so it is lit by the world and moves with the hands. Picking out the draw is proven; swapping its
  texture is new. A flat overlay would read as a HUD.

## Steps

Each step ends with one check in game before the next.

| Step | What | Check in game |
|---|---|---|
| 1 | Two-gun rule, no UI | carry two AK-47s; a third gun swaps with the one in hand |
| 2 | Item core and Items; a test window in DevTools' overlay to stow, take, give and discard | a stowed Carl G survives a save and reload |
| 3 | The notebook in hand on Tab, the map's mesh standing in | walk, look and get shot with it out |
| 4 | The Inventory page drawn onto the paper | pictures, wear and ammo match the guns |
| 5 | The notebook model, page turns on the flip | |
| 6 | The pause pages moved in | the pause menu keeps only its eight entries |
| 7 | Papers and memories: a letter and a photo in the world | read one, walking |
| 8 | The box at the crates | bring a found Carl G home and take it out again |
| 9 | One fetch mission | |
| later | Diamonds, food, pills, ammo as items | |

## Open

- **A second held gadget.** Whether the engine takes a notebook beside the map, in an `Unused` slot.
  If not, the notebook can be the back of the map. Step 3 answers it.
- **Swapping the paper's texture** at the notebook's draw. Step 4 answers it.
- **What still selects by class:** the weapon crate draws the gun it hands out by its class, and a
  pickup's switch prompt shows the carried gun of the pickup's class. Step 1 shows how far it shows.
- **How a mission asks "does the player carry X"**, since the plugin cannot read `Globals`. Candidates:
  a layer's state, or a handler in `MissionTools.lua`.
