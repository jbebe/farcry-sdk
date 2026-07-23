---
sidebar_position: 5
sidebar_label: "Getting Started (Editing the Base Game & DLC)"
format: md
---

:::info[From the Almost Complete Guide]
This page reproduces a section of ["An Almost Complete Guide to Far Cry 2 Modding"](./index.md) by Boggalog, verbatim, with attribution. See the [guide index](./index.md) for the full attribution note.
:::

# Getting Started

For Far Cry 2 modding we use the built in patch system. This allows the relatively small patch files to be easily shared online and as the patch system is already designed to overwrite the base game using those files it’s ideal for our purpose.

So, the first thing to do is unpack the patch.fat/.dat files. If you are starting from scratch these can be found in the “Data\_Win32” folder within your Far Cry 2 folder. Otherwise you can use patch.fat/.dat files from any mod or those that accompany this guide.

With no extra steps at this point you can make edits to the graphics settings, textures, in-game text, mission scripting, controls and some gameplay settings. The next steps will open the rest of the game files so you can make overall edits, edits specific to each game map (North/South) and also edit the DLC.

## Editing the base game

### Making overall edits (across both maps)

1. Unpack “entitylibrarypatchoverride.fcb” found in \\patch\_unpack\\generated\\. This is where we will be copying our files into.  
2. Unpack “entitylibrary.fcb” found in \\patch\_unpack\\worlds\\tmpla\\generated\\. This contains all of the .xml files for both game maps.  
3. Copy files you want to edit from the “entitylibrary” folder, into the “entitylibrarypatchoverride” folder, according to the instructions for .fcb files above.

### Making edits specific to each map

The files for world 1 and 2 are included in the files accompanying this guide, otherwise they are found in the same file structure below within worlds.fat/.dat.

1. Each map has it’s own “entitylibrary.fcb” file. For map 1 (North) it is found in \\patch\_unpack\\worlds\\world1\\generated\\ and for map 2 (South) it is found within \\patch\_unpack\\worlds\\world2\\generated\\. You can unpack the “entitylibrary.fcb” files for whatever map you are editing.  
2. Once you have unpacked the file you will see it contains files with the same names as those in “entitylibrarypatchoverride.fcb”. Here’s how this works: the files in the individual map files are overwritten by the files in “entitylibrarypatchoverride.fcb”, so if you want to make a change to an individual map you can edit the file within each “entitylibrary.fcb” but then you need to make sure that same file isn’t within “entitylibrarypatchoverride.fcb”.

## Editing the DLC

The files for this are already included in the files accompanying this guide, otherwise here is how to find them:

1. Unpack “entitylibrary.fat/.dat” from your Far Cry 2 folder, within \\Far Cry 2\\Data\_Win32\\downloadcontent\\dlc1\\.  
2. Open your unpacked file and find “entitylibrary.fcb” within \\downloadcontent\\dlc1\\generated\\. Copy this file into your patch file, using the same file structure.

This “entitylibrary.fcb” file contains the files that allow us to edit the stats for the DLC vehicles and weapons. That will probably be everything you need but if you want to edit more you can also find the DLC texture/animation/sound files within the “entitylibrary.fat/dat” you unpacked.

# 

