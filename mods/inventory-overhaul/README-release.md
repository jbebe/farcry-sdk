# Inventory Overhaul

Two guns of any class, and a notebook for everything else you carry. You hold the notebook in both
hands while the game runs on, the way you hold the map, so looking through your things in a firefight
is your own risk.

- **Two guns.** Gun A is on key 2 and gun B on key 3, whatever their class. A gun you pick up with two
  carried takes the place of the one in hand. Key 4 draws nothing, and the machete is unchanged.
- **Items.** Stow a gun and it keeps its wear and its loaded rounds. You can have four guns in all,
  slots included. Spare ammo and upgrades stay yours, as they always were.
- **The notebook** comes with the map. It works on foot.
- **Items page:** your guns, your items, your diamonds and the belt. A post-it shows the selected
  row's picture, condition, ammo, upgrades and ratings.
- **Docs page:** the Jackal's and the Predecessor's tapes you have found, which play while you keep
  moving, and any notes and photos other mods add. For now it also lists an example note, photo and
  cassette, to show what those look like.
- **Missions page:** your reputation and your current job, with its objectives and the Underground's.
- **Buddies page:** the buddies you have met, with your active buddies first and the missing and
  the dead faded.
- **The pause menu** loses Resume, Journal, Buddies, Jackal Tapes and Predecessor Tapes, which the
  notebook and Esc now cover. Upgrades stays.

## Controls

| Key | What it does |
|---|---|
| Tab | Take the notebook out or put it away. Rebindable in Options > Controls |
| Mouse wheel | Move along the rows, or turn a long note's sheets |
| R | Turn the page |
| Left mouse button | Use the selected row: stow gun A or B, heal on the syrettes, play or stop a tape, read a note |
| Left mouse button on a stowed gun | Open a small menu; the wheel moves in it, a click puts the gun in slot A or B |
| Right mouse button or Esc | Close that menu, or put back the note you are reading |
| X | Drop the gun in hand, or with the notebook out the selected gun or one of the selected item. Rebindable |
| 2, 3 | Draw gun A, gun B |

## Requirements

- **FCSE 1.4.0 or later**, on Far Cry 2 1.03 (Steam or GOG).
- **JackAll 1.7.0 or the Vortex extension 0.6.0, or later**, to install it. Older versions can't merge
  the handwriting font this mod adds to the game's font list.

## Installing

This archive is a JackAll layer: `plugins\` is the FCSE plugin, and `mods\` holds the notebook's model
and animations, its HUD pages, the new controls and their names.

- **Vortex** — with the Far Cry 2 extension installed, drop the zip in and enable it.
- **JackAll** — add the zip in the app, or from the command line:

```
jackall-cli mod build   --game "C:\Games\Far Cry 2" --layer inventory-overhaul.zip
jackall-cli mod restore --game "C:\Games\Far Cry 2"
```

`mod restore` is the uninstall, and removes the plugin too. Whatever is in Items exists only while
this mod is installed. Loading a save made with the mod after removing it has not been tested, so
keep a save from before you installed it.

A save from before the mod works. If it carries three guns, the special-class gun is dropped at your
feet when it loads.

## Language

The game's own text appears in your language: tapes, buddies, your reputation and your job. The
notebook's own labels and the guns' names are in English in every language for now.

## Compatibility

- **Other mods' HUD changes** merge with this one when they ship them as fragments. A mod that
  replaces the whole `hud.mgb` will not work alongside it.
- **Weapon crates** still draw the gun they hand out by its class, and a pickup's prompt shows the
  carried gun of the pickup's class.

## For modders

Notes, photos, cassettes and items come from other mods: a pickup names its id in FCSE entity data,
and the rest follows from that id by convention. See
[Other mods' papers](https://github.com/jbebe/farcry-sdk/blob/main/mods/inventory-overhaul/README.md#other-mods-papers).
