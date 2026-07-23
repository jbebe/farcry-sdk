---
sidebar_position: 9
sidebar_label: "Weapon/Upgrade Shop"
format: md
---

:::info[From the Almost Complete Guide]
This page reproduces a section of ["An Almost Complete Guide to Far Cry 2 Modding"](./index.md) by Boggalog, verbatim, with attribution. See the [guide index](./index.md) for the full attribution note.
:::

# Weapon/Upgrade shop

The weapon shop has separate entries for everything it sells and we can individually edit their price and the point at which they become available. It’s also possible to edit the order or the items in the weapon shop menu.

The weapon shop is edited within “gamemodesconfig.xml” (\\patch\_unpack\\engine\\gamemodes\\) and you can find the start of this section by searching for “\<\!--CATEGORY WEAPONS \--\>”. 

There are number of subcategories, under these titles:

```xml
<!--SUBCATEGORY PRIMARYWEAPONS -->
<!--SUBCATEGORY SECONDARYWEAPONS -->
<!--SUBCATEGORY SPECIALWEAPONS -->
<!--SUBCATEGORY EXPLOSIVES -->
<!--SUBCATEGORY PRIMARYWEAPONS -->
<!-- OPERATION MANUALS -->
<!-- MAINTENANCE AND REPAIRS MANUALS -->
<!--SUBCATEGORY SECONDARYWEAPONS -->
<!-- OPERATION MANUALS -->
<!-- MAINTENANCE AND REPAIRS MANUALS -->
<!--SUBCATEGORY SPECIALWEAPONS -->
	<!-- OPERATION MANUALS -->
	<!-- MAINTENANCE AND REPAIRS MANUALS -->
<!--SUBCATEGORY VEHICULE MANUALS -->
<!--SUBCATEGORY BANDOLIERS -->
<!--SUBCATEGORY CAMOUFLAGE -->
<!--SUBCATEGORY FIRST AID KITS ICONS NOT SET-->
<!--SUBCATEGORY WEAPON CRATES ICONS NOT SET-->
```

## Price

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

Weapon/upgrade shop prices are controlled by the “cost” stat of each shop entry. 

The minimum value for this is 1, you can’t set a cost as 0\.

```xml {1}
<Item category="weapons" subcategory="primary" name="ithaca crate"  nameOasis="WEAPONBAZAAR_ITHACA_CRATE_NAME" descriptionOasis="WEAPONBAZAAR_ITHACA_CRATE_DESCRIPTION"   availability="0" needsUnlock="0" cost="4"  layer="Missions/WeaponBazaar/Primary/Ithaca" unlockUpgrade="1" icon="hud_icon_ithaca"/>
```

## Availability

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

Weapon/upgrade availability is controlled by the “availability” stat of each shop entry.

There are three options:

0 \= Available during the tutorial, at the first visit to the shop.  
1 \= Available after the tutorial.  
2 \= Available at the start of act 2\.

```xml {1}
<Item category="weapons" subcategory="primary" name="ithaca crate"  nameOasis="WEAPONBAZAAR_ITHACA_CRATE_NAME" descriptionOasis="WEAPONBAZAAR_ITHACA_CRATE_DESCRIPTION"   availability="0" needsUnlock="0" cost="4"  layer="Missions/WeaponBazaar/Primary/Ithaca" unlockUpgrade="1" icon="hud_icon_ithaca"/>
```

## Order in shop menu

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

The order of the items in the weapon shop is controlled by the “name” value of each shop entry. This stat only controls the order of the menu so we can change the rest of each entry to redirect the entries to different weapons/upgrades. The order of the shop entries within this section of “gamemodesconfig.xml” is the order within the in-game menu.

**Note that changing the order of the weapon shop using this method will break the “Upgrades” section of the pause menu.**

Below is an example where the Makarov has replaced the ithaca as the top entry in the weapon shop:

```xml {1}
<Item category="weapons" subcategory="primary" name="ithaca crate"  nameOasis="WEAPONBAZAAR_MAKAROV_CRATE_NAME" descriptionOasis="WEAPONBAZAAR_MAKAROV_CRATE_DESCRIPTION" 	availability="0" needsUnlock="0" cost="4"  layer="Missions/WeaponBazaar/Secondary/Makarov" unlockUpgrade="1" icon="hud_icon_makarov"/>
```

# 

