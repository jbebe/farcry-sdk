# Changelog

Notable changes to Inventory Overhaul, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.1.1] - 2026-11-21

### Fixed
- **A gun can be picked up with a jammed gun in hand.** The game refused the pickup until the jam
  was cleared, which cost the swap in a fight; the jammed gun is now dropped for the new one.

## [1.1.0] - 2026-11-17

### Added
- **Malaria on the pills' post-it,** in the words the pause menu's Journal had for how sick you are.
- **The camo suit in Items** once you have bought it.
- **The notebook in the armory** and the other rooms where you carry no gun.

### Changed
- **Where a gun you pick up goes:** into gun A when both slots are empty, into the empty slot when
  one is, and with both full in place of the gun in hand, or gun A when neither is in hand. The
  same holds for a gun taken from the armory's wall.
- **Needs FCSE 1.5.0.** Built for its plugin API 10; an older FCSE leaves this mod unloaded and says
  so in `fcse.log`.
- **Gun A on key 3, gun B on key 2,** as the game has the primary and the secondary; a save from
  before the mod puts the primary in A.
- **The notebook first opens on gun A**, whichever gun is in hand.

### Fixed
- **The mouse wheel reaches both guns:** it goes through the machete, gun A and gun B, where it
  reached only one of two guns of the same class.
- **The HUD's swap icon shows the gun a pickup replaces:** the gun in hand, or gun A when neither is
  in hand, and nothing while a slot is free. It showed the carried gun of the pickup's class, or
  nothing when you carried none of that class.
- **The notebook's pages are drawn on its paper under dgVoodoo and DLSS injectors too,** where they
  lay flat over the screen.
- **Every gun has its picture and name on the post-it:** guns bought at the armory, such as the
  flamethrower, and Mike's rusty ones showed the mosquito and a word like "Persistent". The sawed-off
  shotgun, the silenced shotgun and the crossbow have theirs too.
- **A dropped gun falls onto its side** instead of landing upright on its edge.
- **A gun's condition follows its wear:** new, used, worn and failing, where every gun read "new"
  until it was about to break.
- **The cassette's sounds no longer share ids with Vehicle Overhaul's,** which could crash the game
  with both installed.
- **A gun dropped from Items lands once,** where it could land twice, or land and stay in a slot.

## [1.0.0] - 2026-11-01

### Added
- **Two guns of any class.** Gun A on key 2 and gun B on key 3, whatever their class. A gun you pick
  up with two carried takes the place of the one in hand, and key 4 draws nothing.
- **A notebook on Tab**, held in both hands while the game runs on, on foot. The wheel moves along
  its rows, R turns the page, the left mouse button uses a row and X drops it.
- **Items.** Stow a gun with a click and keep up to four guns in all, slots included; each keeps its
  wear and its loaded rounds. Click a stowed gun for a small menu that puts it in slot A or B.
- **Items page:** your guns, your items, your diamonds and the belt, with a post-it for the selected
  row showing its picture, condition, ammo, upgrades and ratings. Clicking the syrettes heals.
- **Docs, Missions and Buddies pages,** in place of the pause menu's Journal, Buddies, Jackal Tapes
  and Predecessor Tapes: tapes play and stop with a click while you keep moving, and the current job,
  your reputation and the buddies you have met are in the game's own words.
- **X drops the gun in hand** at any time, on a new Drop control.
- **Other mods can add notes, photos, cassettes and items** to the notebook by following a naming
  convention. For now the Docs page lists a note, a photo and a cassette as dummy content, for later
  support of new mission types; saves keep nothing of them.
