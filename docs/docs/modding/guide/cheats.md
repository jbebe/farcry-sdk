---
sidebar_position: 22
sidebar_label: "Testing Conditions/Cheats"
format: md
---

:::info[From the Almost Complete Guide]
This page reproduces a section of ["An Almost Complete Guide to Far Cry 2 Modding"](./index.md) by Boggalog, verbatim, with attribution. See the [guide index](./index.md) for the full attribution note.
:::

# Testing conditions/Cheats

These are conditions that can be useful for testing the effect of your changes.

## God mode

### Option 1: Add a launch property

Right click your Far Cry 2 shortcut and press properties, either in Steam if using that or the actual shortcut itself.

In Steam you can add “-GameProfile\_GodMode 1” as a launch option, for regular windows add it at the end of the “Target” section.

\-GameProfile\_GodMode 1

### Option 2: Edit the game files

defaultgameconfig.xml (\\patch\_unpack\\engine\\settings\\)

God mode is controlled by the “GodMode” stat. Change value to 1 to enable, 0 to disable.

GodMode \= "**0**"

## 

## Unlimited Ammo

### Option 1: Add a launch property

Right click your Far Cry 2 shortcut and press properties, either in Steam if using that or the actual shortcut itself.

In Steam you can add “-GameProfile\_UnlimitedAmmo 1” as a launch option, for regular windows add it at the end of the “Target” section.

\-GameProfile\_UnlimitedAmmo 1

### Option 2: Edit the game files

defaultgameconfig.xml (\\patch\_unpack\\engine\\settings\\)

Unlimited ammo is controlled by the “UnlimitedAmmo” stat. Change value to 1 to enable, 0 to disable.

UnlimitedAmmo \= "**0**"

## Unlimited weapon reliability

### Option 1: Add a launch property

Right click your Far Cry 2 shortcut and press properties, either in Steam if using that or the actual shortcut itself.

In Steam you can add “-GameProfile\_UnlimitedReliability 1” as a launch option, for regular windows add it at the end of the “Target” section.

\-GameProfile\_UnlimitedReliability 1

### Option 2: Edit the game files

defaultgameconfig.xml (\\patch\_unpack\\engine\\settings\\)

We can add a line to the top section of this file that says: SkipIntroMovies="1"

Change value to 1 to enable, 0 to disable.

Sensitivity\_x \= "1.0"  
Sensitivity\_y \= "1.0"  
Sensitivity \= "0.9"  
**UnlimitedReliability \= "1"**  
Invert\_x \= "0"

## Unlock all weapons

Right click your Far Cry 2 shortcut and press properties, either in Steam if using that or the actual shortcut itself.

In Steam you can add “-GameProfile\_AllWeaponsUnlock 1” as a launch option, for regular windows add it at the end of the “Target” section.

This will only unlock the weapons that are available in the current map, so if you do this in map 1 you won’t gain access to those that unlock in map 2\.

\-GameProfile\_AllWeaponsUnlock 1

## AI ignoring the player

### Option 1: Add a launch property

Right click your Far Cry 2 shortcut and press properties, either in Steam if using that or the actual shortcut itself.

In Steam you can add “-GameProfile\_IgnorePlayer 1” as a launch option, for regular windows add it at the end of the “Target” section.

\-GameProfile\_IgnorePlayer 1

### Option 2: Edit the game files

defaultgameconfig.xml (\\patch\_unpack\\engine\\settings\\)

The AI ignoring the player is controlled by the “IgnorePlayer” stat. Change value to 1 to enable, 0 to disable.

IgnorePlayer \= "**0**"
