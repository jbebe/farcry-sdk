---
sidebar_position: 20
---

# Miscellaneous edits

## Timescale

defaultgameconfig.xml (\patch_unpack\engine\settings\\)

Timescale is controlled by the “TimeScale” stat near the top of this file.

The default value is 6, which means that time is sped up 6x so that an in-game 24 hours takes 4 hours, and an in-game hour takes 10 minutes.

TimeScale = "**6**"

## Disabling intro videos

### Option 1: Add a launch property

Right click your Far Cry 2 shortcut and press properties, either in Steam if using that or the actual shortcut itself.

In Steam you can add “-GameProfile_SkipIntroMovies 1” as a launch option, for regular windows add it at the end of the “Target” section.

\-GameProfile_SkipIntroMovies 1

### Option 2: Edit the game files

defaultgameconfig.xml (\patch_unpack\engine\settings\\)

We can add a line to the top section of this file that says: SkipIntroMovies="1"

Change value to 1 to enable, 0 to disable.

Sensitivity_x = "1.0"

Sensitivity_y = "1.0"

Sensitivity = "0.9"

```xml
SkipIntroMovies="1"
```

Invert_x = "0"

## Guide - Using the ‘Black Mamba’ buddy rescue mission

This guide will cover how to swap in the Black Mamba buddy rescue mission. It will replace one of the existing world 2 buddy rescue missions. Check out a video of it [here](https://www.youtube.com/watch?v=6iqWFqSTGOo).

**Step 1: Getting the world 2 scripting file**

Unpack common.dat/.fat and find “master_world2.world2.lua” in \common_unpack\domino\user\\. Paste this into your patch file with the same folder structure \patch_unpack\domino\user\\.

**Step 2: Editing in the new mission**

master_world2.world2.lua (\patch_unpack\domino\user)

At the top of this file you can see a list of missions in world 2. There are two buddy rescue missions, shown with the following lua files:

Domino/User/A2BU06_OldBrewery.A2BU06_Mission.lua

Domino/User/A2BU07_DogonRing.A2BU07_Mission.lua

You can replace either one of these, Hunter has suggested replacing the Dogon Ring mission as it is more buggy.

To replace the buddy mission we are going to replace the referenced file with the following one:

Domino/User/A2BU05_Blackmamba.A2BU05_Mission.lua

If you have replaced the Dogon Ring mission then you complete section should look like this:

"Domino/User/A2BU06_OldBrewery.A2BU06_Mission.lua",

"**Domino/User/A2BU05_Blackmamba.A2BU05_Mission.lua**",

"Domino/User/A2LM07_RadioArmageddon.A2LM07_Mission.lua",

We now need to replace all the other references to the old file in pretty much the same way. I’m going to keep describing this as if you are replacing the Dogon Ring mission.

There is one other reference to “Domino/User/A2BU07_DogonRing.A2BU07_Mission.lua” which needs to be replaced with "Domino/User/A2BU05_Blackmamba.A2BU05_Mission.lua".

There are also five references to the mission code “A2BU07” that need to be replaced with “A2BU05”.
