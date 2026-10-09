---
slug: /troubleshooting
sidebar_position: 2
title: Troubleshooting
description: What to do when a JackAll mod does nothing in Far Cry 2, a Deploy stops, an import is refused, or JackAll shows something unexpected
---

# Troubleshooting

Each problem says what causes it and what to do, with a link to the page that explains more.

## A mod does nothing in game

Go through these in order.

1. **It isn't in the game yet.** Is the mod ticked on the **Mods** tab, and did you click **Deploy
   mods** after the last change? Nothing reaches the game until you do.
2. **Another mod replaces the same file.** A whole file, such as a texture or a sound, is the lowest
   mod's in the list. See [How two mods combine](/jackall/managing-mods#how-two-mods-combine).
3. **The save remembers the old value.** A save keeps many of the values it was made with, so a
   weapon or other value change shows in a new game. To bring it into an old save, see
   [Purge](/jackall/saves#purge-let-a-save-pick-up-your-mods).
4. **The game reads another copy.** An archetype can be declared in more than one library, and an
   edit to the copy the game doesn't read is dead. Click **Check for dead edits** on the **Mods**
   tab. See [Which copy does the game read?](/jackall/archetypes)
5. **Only one world has it.** Each campaign world has its own copy of every weapon; act 2 reads
   world2's. See [Do the same in world2](/jackall/first-mod#5-do-the-same-in-world2).
6. **It needs FCSE.** A mod with a plugin only works when the game starts with `bin\FCSE.exe`. See
   [FCSE and plugins](/jackall/installing-mods#fcse-and-plugins).

Some changes do nothing however you deploy them:

- **A file the game never opens.** The Files tab shows those in *italics*. See
  [Files the game never reads](/jackall/finding-files#files-the-game-never-reads).
- **A grey `text_…` field.** It's a note the original editor kept; the game reads the hash next to
  it. See [Field types](/jackall/value-editor#field-types).
- **Text in one language only.** A fragment for English changes nothing in German. See
  [Other languages](/jackall/renaming#other-languages).
- **One copy of a HUD or menu.** The game ships each package once per language and screen shape.
  See [Every copy of the package](/jackall/hud-and-menus#every-copy-of-the-package).
- **A model part moved in Object Mode.** Move it in Edit Mode. See [Edit](/jackall/blender#4-edit).
- **An AI setting no one reads:** a `DistanceAccuracy_*` curve, five of the behaviour odds, or a
  brain parameter whose name is struck through. See [Tune the AI](/jackall/ai).

## Importing and deploying

**"Import mod" says the zip has no files this game recognises.** Either `mods` isn't the first
thing in the zip, because a folder is wrapped around it (see [Zip it](/jackall/sharing-a-mod#2-zip-it)),
or the mod ships its own `patch.dat` and `patch.fat` and goes in with **Import legacy mod** (see
[Import an old patch.dat mod](/jackall/legacy-import)). A zip with `FCSE.exe` in it is FCSE itself;
see [Install FCSE](/jackall/installing-mods#install-fcse).

**Import legacy mod staged far more than the mod changes.** The mod was built on another version of
the game than yours, such as Steam's when you have GOG, and the differences came along. Trim the
workspace to the mod's own files. See
[Keep only the mod's changes](/jackall/legacy-import#keep-only-the-mods-changes).

**Deploy mods stops and names a field.** Two mods set the same value differently. Untick one of
them and deploy again. The message's advice, to save your own version into the workspace, doesn't
clear it. See [When two mods conflict](/jackall/managing-mods#when-two-mods-conflict).

**Deploy failed for another reason.** The game is untouched. Close the game if it's running and try
again. See [Something went wrong](/jackall/installing-mods#something-went-wrong).

**A mod's plugin wasn't installed.** A file you copied by hand already sits where the plugin goes,
and JackAll leaves it alone. Delete it and deploy again. See
[Plugins](/jackall/managing-mods#plugins).

**You moved or deleted a mod's zip.** JackAll reads the zip where you imported it from. Remove the
mod from the list, put the zip somewhere it can stay, import it again and deploy.

**The Map tab's Deploy button does nothing.** It isn't working yet. Save on the Map tab, then click
**Deploy mods** on the **Mods** tab. See [Save](/jackall/map-editing#save).

## Checks against a clean game

**The first start says some files don't match a clean 1.03 install.** Something changed the game
before JackAll saw it. Close JackAll, verify the game's files in Steam or GOG Galaxy, and start it
again; JackAll treats what it finds the first time as the original game. See
[Show it your game](/jackall/installing-mods#2-show-it-your-game).

**After Revert to original, the GOG version says the files still don't match.** JackAll's reference
hashes come from the Steam version, and GOG ships a different `patch.dat`. If the game was clean
when JackAll first deployed, the original files are back. See
[Removing mods](/jackall/installing-mods#removing-mods).

## In JackAll

**Save stays grey in the value editor.** A field holds something it can't, and it's red. See
[When Save stays grey](/jackall/value-editor#when-save-stays-grey).

**Import XML is refused on the text table.** A mod states only the strings it changes, in a small
fragment file. See [Don't import the whole table](/jackall/renaming#3-dont-import-the-whole-table).

**A fragment is rejected with "Data at the root level is invalid".** The file starts with a UTF-8
byte order mark. Save it as plain UTF-8. See [Write the fragment](/jackall/renaming#4-write-the-fragment).

**A file you changed in the workspace with another program doesn't show.** JackAll doesn't watch
the folder. Click **Rescan mods**.

**The references lists say they're still indexing.** The first start builds the index in the
background, which takes a few minutes. See [The reference panel](/jackall/references#the-reference-panel).

**Revert refuses on a file.** The file comes from a mod, not from your workspace. Switch that mod off
on the Mods tab, or use **Mirror original** to undo just that file. See
[Revert](/jackall/export-replace-revert#revert).

**The Archetypes tab shows far fewer archetypes than the world has.** Two mods conflict. Untick one
and restart JackAll. See [When two mods conflict](/jackall/managing-mods#when-two-mods-conflict).

**The AI tab saved only one of the archetypes you ticked.** Change one archetype at a time, save,
and restart JackAll before the next. See [Soldiers](/jackall/ai#soldiers).

**The Domino viewer can't change a mission.** Its button says Editor, but it only reads; a mission
changes in its Lua, by hand. See [Read a mission](/jackall/missions).

**No Mod Configuration Menu in the game's Options.** The game was started without FCSE. Start it
with `bin\FCSE.exe`.

## On the command line

**`mod build` stops because the patch looks modded.** There's no vanilla backup yet and the current
patch doesn't look like the game's own. Verify the game's files first. See
[Build](/jackall/cli-mods#build).

**`mod build` succeeded but two mods conflicted.** The command line keeps the later layer's value and
prints a warning. See [Two mods that change the same value](/jackall/cli-mods#two-mods-that-change-the-same-value).

**`rml fragments` is an unknown command.** It can't be reached in this version. See
[Game data and text](/jackall/cli-formats#game-data-and-text).
