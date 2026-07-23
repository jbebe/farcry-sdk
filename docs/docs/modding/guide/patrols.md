---
sidebar_position: 12
sidebar_label: "Patrols"
format: md
---

:::info[From the Almost Complete Guide]
This page reproduces a section of ["An Almost Complete Guide to Far Cry 2 Modding"](./index.md) by Boggalog, verbatim, with attribution. See the [guide index](./index.md) for the full attribution note.
:::

# Patrols

Editing the patrols works by swapping out details of those in the default game, and there are two ways we can do this. We can use a file that combines the patrols of maps 1 and 2 into one list and make overall edits, or we can use seperate files for each map and edit them separately. 

This works because most patrols are featured in both maps. So, for example, both maps have a patrol with the title “Patrols.Rover.M249\_Mounted”. If you use the method that combines the maps, whatever you edit “Patrols.Rover.M249\_Mounted” to will be the same in both. If you seperate them you can make that patrol different in each map.

I would suggest editing them separately for maximum variety and it’s maybe the one time that doing separate edits for each map seems worthwhile. Instructions for doing separate map edits can be found under the title “Making edits specific to each map” in the “Editing the base game” section above.

When editing both maps together you make edits to “xx\_GhostPatrols.xml” within “entitylibrarypatchoverride.fcb” (\\patch\_unpack\\generated\\).

When editing the maps separately there are two individual files:

To edit map 1 patrols you make edits to “10\_GhostPatrols.xml” within “entitylibrary.fcb” (\\patch\_unpack\\worlds\\world1\\generated\\).

To edit map 2 patrols you make edits to “10\_GhostPatrols.xml” within “entitylibrary.fcb” (\\patch\_unpack\\worlds\\world2\\generated\\).

## Entry titles

These are the entry titles for both methods, along with their respective vehicles:

| Both maps combined |  |
| ----- | ----- |
| Patrol title | Vehicle |
| Convoy.AssassinationTarget | vehicle.Land.JeepLiberty |
| Convoy.ConvoyTarget | vehicle.Land.BigTruck |
| Convoy.EscortVehicle | vehicle.Land.Rover |
| MissionSpecific.CopKiller | vehicle.Land.JeepLiberty |
| Patrols.Datsun | vehicle.Land.Datsun |
| Patrols.JeepLiberty | vehicle.Land.JeepLiberty |
| Patrols.JeepWrangler | vehicle.Land.JeepWrangler |
| Patrols.Rover | vehicle.Land.Rover |
| Patrols.Rover.M249\_Mounted | vehicle.Land.Rover.M249\_Mounted |
| Patrols.Rover.M2\_Mounted | vehicle.Land.Rover.M2\_Mounted |
| Patrols.Rover.MK19\_Mounted | vehicle.Land.Rover.MK19\_Mounted |
| Patrols.SwampBoat | vehicle.Sea.SwampBoat |
| Patrols.SwampBoat.M249\_Mounted | vehicle.Sea.SwampBoat.M249\_Mounted |
| Patrols.SwampBoat.M2\_Mounted | vehicle.Sea.SwampBoat.M2\_Mounted |
| Patrols.SwampBoat.MK19\_Mounted | vehicle.Sea.SwampBoat.MK19\_Mounted |
| Patrols.FishingBoat | vehicle.Sea.FishingBoat |
| Patrols.FishingBoat.M249\_Mounted | vehicle.Sea.FishingBoat.M249\_Mounted |
| Patrols.FishingBoat.M2\_Mounted | vehicle.Sea.FishingBoat.M2\_Mounted |
| Patrols.FishingBoat.MK19\_Mounted | vehicle.Sea.FishingBoat.MK19\_Mounted |

| Separate maps |  |
| ----- | ----- |
| Map 1 |  |
| Patrol title | Vehicle |
| Convoy.AssassinationTarget | vehicle.Land.JeepLiberty |
| Convoy.ConvoyTarget | vehicle.Land.BigTruck |
| Convoy.EscortVehicle | vehicle.Land.Rover |
| MissionSpecific.CopKiller | vehicle.Land.JeepLiberty |
| Patrols.Datsun | vehicle.Land.Datsun |
| Patrols.JeepWrangler | vehicle.Land.JeepWrangler |
| Patrols.Rover | vehicle.Land.Rover |
| Patrols.Rover.M249\_Mounted | vehicle.Land.Rover.M249\_Mounted |
| Patrols.SwampBoat | vehicle.Sea.SwampBoat |
| Patrols.SwampBoat.M249\_Mounted | vehicle.Sea.SwampBoat.M249\_Mounted |
|  |  |
| Map 2 |  |
| Patrol title | Vehicle |
| Convoy.AssassinationTarget | vehicle.Land.JeepLiberty |
| Convoy.ConvoyTarget | vehicle.Land.BigTruck |
| Convoy.EscortVehicle | vehicle.Land.Rover |
| Patrols.Datsun | vehicle.Land.Datsun |
| Patrols.JeepLiberty | vehicle.Land.JeepLiberty |
| Patrols.JeepWrangler | vehicle.Land.JeepWrangler |
| Patrols.Rover | vehicle.Land.Rover |
| Patrols.Rover.M249\_Mounted | vehicle.Land.Rover.M249\_Mounted |
| Patrols.Rover.M2\_Mounted | vehicle.Land.Rover.M2\_Mounted |
| Patrols.Rover.MK19\_Mounted | vehicle.Land.Rover.MK19\_Mounted |
| Patrols.SwampBoat.M249\_Mounted | vehicle.Sea.SwampBoat.M249\_Mounted |
| Patrols.SwampBoat.M2\_Mounted | vehicle.Sea.SwampBoat.M2\_Mounted |
| Patrols.SwampBoat.MK19\_Mounted | vehicle.Sea.SwampBoat.MK19\_Mounted |
| Patrols.FishingBoat.M249\_Mounted | vehicle.Sea.FishingBoat.M249\_Mounted |

## Vehicle type

**Decoding required**  
Combined maps  
xx\_GhostPatrols.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Separate maps  
Map 1: 10\_GhostPatrols.xml \< entitylibrary.fcb (\\patch\_unpack\\worlds\\world1\\generated\\)  
Map 2: 10\_GhostPatrols.xml \< entitylibrary.fcb (\\patch\_unpack\\worlds\\world2\\generated\\)

The vehicles used by each patrol are controlled by the “archVehicle” value in the “Ghost” section.

```xml {2}
<object hash="C292BFA5" type="Ghost">
<value name="archVehicle" hash="27D0A9BA" type="String">vehicle.Land.Datsun</value>
<value name="entPathToFollow" hash="5C91004B" type="Int64">-1</value> <!-- type="BinHex" value="FFFFFFFFFFFFFFFF" -->
<value name="vectorBBoxMin" hash="BC35D67A" type="Vector3">
```

You can replace the existing values with those below:

| Big truck | vehicle.Land.BigTruck |
| :---- | :---- |
|  | vehicle.Land.BigTruck.Tanker |
| Dune buggy | vehicle.Land.Buggy |
| Car | vehicle.Land.Datsun |
| ATV | vehicle.Land.DLC\_Vehicle1\_DLC1 |
| Jeep Liberty | vehicle.Land.JeepLiberty |
|  | vehicle.Land.JeepLiberty.VIP |
| Jeep Wrangler | vehicle.Land.JeepWrangler |
| Assault truck | vehicle.Land.Rover.M249\_Mounted |
|  | vehicle.Land.Rover.M2\_Mounted |
|  | vehicle.Land.Rover.MK19\_Mounted |
| Utility truck/Unimog | vehicle.Land.DLC\_Vehicle2\_DLC1 (singleplayer version, with grey paint and mounted M2) |
|  | vehicle.Land.DLC\_Vehicle2\_DLC1.Multi\_M249\_Mounted |
|  | vehicle.Land.DLC\_Vehicle2\_DLC1.Multi\_M2\_Mounted |
|  | vehicle.Land.DLC\_Vehicle2\_DLC1.Multi\_MK19\_Mounted |
| Fishing boat | vehicle.Sea.FishingBoat.M249\_Mounted |
|  | vehicle.Sea.FishingBoat.M2\_Mounted |
|  | vehicle.Sea.FishingBoat.MK19\_Mounted |
| Swamp boat | vehicle.Sea.SwampBoat.M249\_Mounted |
|  | vehicle.Sea.SwampBoat.M2\_Mounted |
|  | vehicle.Sea.SwampBoat.MK19\_Mounted |

## Faction (Enemy infighting)

**Decoding required**  
Combined maps  
xx\_GhostPatrols.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Separate maps  
Map 1: 10\_GhostPatrols.xml \< entitylibrary.fcb (\\patch\_unpack\\worlds\\world1\\generated\\)  
Map 2: 10\_GhostPatrols.xml \< entitylibrary.fcb (\\patch\_unpack\\worlds\\world2\\generated\\)

The faction of the patrols is controlled by the start of the “archPassenger” values in the “Passengers” section.

By default the drivers/gunners belong to the blue faction and their values begin with “enemy\_archetypes.Blue\_”. If you change this section to the red faction, so “enemy\_archetypes.Red\_”, then the patrol will attack camps that they drive through.

Don’t forget to change all of the passengers to the same faction, or as soon as the patrol spawns they will all get out of the vehicle and shoot each other.

```xml {3}
<object hash="AC0A8D5A" type="Passengers">
          <object hash="B91E6A7E" type="Passenger">
            <value name="archPassenger" hash="4071905F" type="String">enemy_archetypes.Blue_Faction.Assault_Caucasian</value>
          </object>
          <object hash="B91E6A7E" type="Passenger">
            <value name="archPassenger" hash="4071905F" type="String"></value> <!-- type="BinHex" value="00" -->
          </object>
```

## Enemy type

**Decoding required**  
Combined maps  
xx\_GhostPatrols.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Separate maps  
Map 1: 10\_GhostPatrols.xml \< entitylibrary.fcb (\\patch\_unpack\\worlds\\world1\\generated\\)  
Map 2: 10\_GhostPatrols.xml \< entitylibrary.fcb (\\patch\_unpack\\worlds\\world2\\generated\\)

The enemy types within the patrols are controlled by the middle of the “archPassenger” values in the “Passengers” section.

You can swap the part that in the example below says “Assault” with another for a different enemy type.

It can be swapped with these values:

Assault  
ShotgunMan  
Sniper  
RocketMan  
MortarMan

```xml {3}
<object hash="AC0A8D5A" type="Passengers">
          <object hash="B91E6A7E" type="Passenger">
            <value name="archPassenger" hash="4071905F" type="String">enemy_archetypes.Blue_Faction.Assault_Caucasian</value>
          </object>
          <object hash="B91E6A7E" type="Passenger">
            <value name="archPassenger" hash="4071905F" type="String"></value> <!-- type="BinHex" value="00" -->
          </object>
```

## Enemy ethnicity

**Decoding required**  
Combined maps  
xx\_GhostPatrols.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Separate maps  
Map 1: 10\_GhostPatrols.xml \< entitylibrary.fcb (\\patch\_unpack\\worlds\\world1\\generated\\)  
Map 2: 10\_GhostPatrols.xml \< entitylibrary.fcb (\\patch\_unpack\\worlds\\world2\\generated\\)

The ethnicities of the enemies within the patrols are controlled by the end of the “archPassenger” values in the “Passengers” section.

The part that in the example below says “Caucasian”, you can keep it as that for the enemy to be white, or change it to “Nubian” for the enemy to be black. 

```xml {3}
<object hash="AC0A8D5A" type="Passengers">
          <object hash="B91E6A7E" type="Passenger">
            <value name="archPassenger" hash="4071905F" type="String">enemy_archetypes.Blue_Faction.Assault_Caucasian</value>
          </object>
          <object hash="B91E6A7E" type="Passenger">
            <value name="archPassenger" hash="4071905F" type="String"></value> <!-- type="BinHex" value="00" -->
          </object>
```

## 

## Guide \- How to create new driver/gunner enemy types

This guide will cover creating new enemy types. The only way I know how to insert new enemy types is into patrols, so that is why this is under the title of driver/gunner titles. If you can find a way of inserting enemies into the rest of the game you can use the enemy types created here.

Step 1: Creating a new enemy type  
xx\_enemy\_archetypes.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

The first step here is to copy an existing enemy type. There are slight visual differences between enemy types (assault, shotgun, sniper etc) and you also need to choose what ethnicity you want your new enemy type to be.

Once you’ve decided this, copy the entire entry for that enemy type and paste it at the top of this file.

The first edit we are going to do is changing the entry title. There are two values for this under the “Name” and “hidName” values, shown in the example below:

```xml {2,4}
<object hash="256A1FF9">
    <value name="Name" type="String">Blue_Faction.Assault_Caucasian</value>
    <object type="Entity">
      <value name="hidName" type="String">enemy_archetypes.Blue_Faction.Assault_Caucasian</value>
      <value name="disEntityId" type="UInt64">263</value>
```

You can change these to anything you like but you will need these values later on so make them something simple, like I have done in the example below:

```xml {2,4}
<object hash="256A1FF9">
    <value name="Name" type="String">Enemy_Driver</value>
    <object type="Entity">
      <value name="hidName" type="String">enemy_archetypes.Enemy_Driver</value>
      <value name="disEntityId" type="UInt64">263</value>
```

The main way you can customise your new enemy type is what weapons they use. We can’t create new inventory packs, I will explain why shortly, so we need to reuse an existing one. The least used inventory pack is the Carl Gustaf one. Only one enemy entry uses it and we can easily swap them to the generic rocket launcher inventory pack.

The first step to doing this is editing our new enemy type. Within your new entry search for “Inventory”. You will find a section like the one below:

```xml {2,3}
<object type="Inventory">
       <value hash="8C965C28" type="String">assault</value>
       <value name="packInventoryPack" type="Hash">CCE9D60C</value>
       <value name="archGPSVehicleArchetype" type="String"></value> <!-- type="BinHex" value="00" -->
       <value name="bUnlimitedAmmo" type="Bool">True</value>
       <value name="bAutoReload" type="Bool">False</value>
       <value name="bAutoDraw" type="Bool">False</value>
       <value hash="130CDED8" type="String"></value>
       <value name="sInitialWeaponCategory" type="Hash">FFFFFFFF</value>
</object>
```

There are two values that control the inventory packs and I have highlighted them in the example above. We can’t create new “packInventoryPack” values, and that is why we must reuse existing packs.

To change this to the Carl Gustaf pack we are going to change the “8C965C28” value to “CarlGustav” and the “packInventoryPack” value to “B3E1E534”, as shown in the example below:

```xml {2,3}
<object type="Inventory">
       <value hash="8C965C28" type="String">CarlGustav</value>
       <value name="packInventoryPack" type="Hash">B3E1E534</value>
       <value name="archGPSVehicleArchetype" type="String"></value> <!-- type="BinHex" value="00" -->
       <value name="bUnlimitedAmmo" type="Bool">True</value>
       <value name="bAutoReload" type="Bool">False</value>
       <value name="bAutoDraw" type="Bool">False</value>
       <value hash="130CDED8" type="String"></value>
       <value name="sInitialWeaponCategory" type="Hash">FFFFFFFF</value>
</object>
```

Of course we also need to redirect the existing Carl Gustav enemy so search for “CarlGustav'' and you will find there is a single other entry. Change that section to the following:

```xml {2,3}
<object type="Inventory">
       <value hash="8C965C28" type="String">RocketLauncher</value>
       <value name="packInventoryPack" type="Hash">6F2D03DF</value>
       <value name="archGPSVehicleArchetype" type="String"></value> <!-- type="BinHex" value="00" -->
       <value name="bUnlimitedAmmo" type="Bool">True</value>
       <value name="bAutoReload" type="Bool">False</value>
       <value name="bAutoDraw" type="Bool">False</value>
       <value hash="130CDED8" type="String"></value>
       <value name="sInitialWeaponCategory" type="Hash">FFFFFFFF</value>
</object>
```

Step 2: Editing the Carl Gustaf inventory pack  
gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

In this file search for “\<Pack name="CarlGustav"\>” and you will find the right section.

The steps for editing this are the same as the other enemy inventory packs, instructions for which can be found [here](#enemy-weapons). You will see that by default the Carl Gustaf section is fairly empty, so you will have to build it mostly from scratch.

Step 3: Adding the new enemy types into the patrols  
**Decoding required**  
Combined maps  
xx\_GhostPatrols.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Separate maps  
Map 1: 10\_GhostPatrols.xml \< entitylibrary.fcb (\\patch\_unpack\\worlds\\world1\\generated\\)  
Map 2: 10\_GhostPatrols.xml \< entitylibrary.fcb (\\patch\_unpack\\worlds\\world2\\generated\\)

The steps for this are largely the same as adding other enemy types into the patrols, instructions for which can be found [here](#enemy-type-1). The only thing that’s different is that you use the “hidName” values you used in “xx\_enemy\_archetypes.xml”. In my example above the value would be “enemy\_archetypes.Enemy\_Driver”.

## Guide \- How to create a friendly faction

This guide will cover creating new friendly npcs that can then be inserted into the patrols to simulate a friendly faction.

Step 1: Creating friendly npcs  
**Decoding required**  
xx\_buddies.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Our first step to creating new npcs is choosing what we want them to look like. This file contains unique buddies that all look the same and non-unique buddies whose appearance is randomly generated. For our purpose the non-unique buddies are obviously ideal, so these are your choices:

Civilians.Female\_Civilian\_NoDress  
Civilians.Female\_Civilian\_WithDress  
Civilians.Male\_Civilian

It might also be possible to edit the unique buddies so parts of them are randomly generated, but you’ll have to figure that out. You could use the enemy entries from “xx\_enemy\_archetypes.xml” but then you have a problem that your new npcs aren’t visually distinct.

For this guide we’ll be creating female npcs using the existing “Civilians.Female\_Civilian\_WithDress” entry. The first step is copy this whole section and paste it at the top of the file. We are going to edit the entry titles of this, by default it looks like this:

```xml {2,4}
<object hash="256A1FF9">
    <value name="Name" type="String">Civilians.Female_Civilian_WithDress</value>
    <object type="Entity">
      <value name="hidName" type="String">buddies.Civilians.Female_Civilian_WithDress</value>
      <value name="disEntityId" type="UInt64">75</value>
```

We are going to edit the “Name” and “hidName” values, it can be anything you want but we need these later on so make it something simple. It should look something like the example below:

```xml {2,4}
<object hash="256A1FF9">
    <value name="Name" type="String">Friendlyfighter_Female</value>
    <object type="Entity">
      <value name="hidName" type="String">buddies.Friendlyfighter_Female</value>
      <value name="disEntityId" type="UInt64">75</value>
```

Now, there are a few sections of this entry that we need to change so this npc has the brains of a regular enemy but won’t attack like a buddy. I’ve found the sections we are going to use for this already, so I’ll just tell you what sections to replace with what.

1. “Inventory”

	This section controls the npc’s weapons. By default it looks like this:

```xml
	<object type="Inventory">
            <value hash="8C965C28" type="String"></value>
            <value name="packInventoryPack" type="Hash">FFFFFFFF</value>
            <value name="archGPSVehicleArchetype" type="String"></value> <!-- type="BinHex" value="00" -->
            <value name="bUnlimitedAmmo" type="Bool">False</value>
            <value name="bAutoReload" type="Bool">False</value>
            <value name="bAutoDraw" type="Bool">False</value>
            <value hash="130CDED8" type="String"></value>
            <value name="sInitialWeaponCategory" type="Hash">FFFFFFFF</value>
          </object>
```

You need to choose what weapons you want your npcs to have. You can give them an existing enemy intentory pack or create a new one. 

If you want to create a new one follow the instructions in the [How to create new driver/gunner enemy types](#guide---how-to-create-new-driver/gunner-enemy-types) guide. 

To give them an existing inventory pack then you need to edit the “8C965C28” and “packInventoryPack” values. For the different inventory packs they are as follows:

Assault: “8C965C28” \= “assault” / “packInventoryPack” \= “CCE9D60C”

Shotgun: “8C965C28” \= “shotgun” / “packInventoryPack” \= “EEAE53E1”

Sniper: “8C965C28” \= “sniper” / “packInventoryPack” \= “AE2848CB”

Rocket Launcher: “8C965C28” \= “RocketLauncher” / “packInventoryPack” \= “6F2D03DF”

Mortar: “8C965C28” \= “Mortar” / “packInventoryPack” \= “4945BAE0”

We also need to change the “bUnlimitedAmmo” value to “True”.

A finished section would look something like this:

```xml
<object type="Inventory">
       <value hash="8C965C28" type="String">assault</value>
       <value name="packInventoryPack" type="Hash">CCE9D60C</value>
       <value name="archGPSVehicleArchetype" type="String"></value> <!-- type="BinHex" value="00" -->
       <value name="bUnlimitedAmmo" type="Bool">True</value>
       <value name="bAutoReload" type="Bool">False</value>
       <value name="bAutoDraw" type="Bool">False</value>
       <value hash="130CDED8" type="String"></value>
       <value name="sInitialWeaponCategory" type="Hash">FFFFFFFF</value>
</object>
```

2. “CAgent” and “CGameAgent”

	These sections control the npc’s ai and movement. By default they look like this:

```xml
	<object type="CAgent">
              <value hash="24B313D8" type="String">::SpecialCharacter/BrainSpecialCharacter</value>
              <value name="Brain" type="BinHex">3F6EB6E6</value>
              <value hash="071B548C" type="String">scripts\game\newbrains\specialcharacter.ai.rml</value>
              <value name="aiwsBrainWorkspace" type="Hash">47207CF3</value>
              <object type="PersonalityComponent">
                <value hash="2B928622" type="String">CHumanPersonality</value>
                <value name="Type" type="Hash">8A702F75</value>
              </object>
            </object>
            <object type="CGameAgent">
              <value name="bIsScripted" type="Bool">True</value>
              <value name="fAccelerationsSlow" type="Float">0.75</value>
              <value name="fAccelerationsNormal" type="Float">1</value>
              <value name="fAccelerationsFast" type="Float">1.25</value>
              <value name="fDecelerationsSlow" type="Float">-1</value>
              <value name="fDecelerationsNormal" type="Float">-1.25</value>
              <value name="fDecelerationsFast" type="Float">-1.5</value>
              <value name="fSpeedsBabyStep" type="Float">0.5</value>
              <value name="fSpeedsWalk" type="Float">1</value>
              <value name="fSpeedsJog" type="Float">3</value>
              <value name="fSpeedsRun" type="Float">4</value>
              <value name="fSpeedsSprint" type="Float">5</value>
              <value name="fVariationBabyStep" type="Float">0</value>
              <value name="fVariationWalk" type="Float">0</value>
              <value name="fVariationJog" type="Float">0</value>
              <value name="fVariationRun" type="Float">0</value>
              <value name="fVariationSprint" type="Float">0</value>
            </object>
```

	We are going to replace these sections with this:

```xml
	<object type="CAgent">
              <value hash="24B313D8" type="String">::MercBrain/MercBrain</value>
              <value name="Brain" type="BinHex">01B6506D</value>
              <value hash="071B548C" type="String">scripts\game\newbrains\mercbrain.ai.rml</value>
              <value name="aiwsBrainWorkspace" type="Hash">1251B9DA</value>
              <object type="PersonalityComponent">
                <value hash="2B928622" type="String">CHumanPersonality</value>
                <value name="Type" type="Hash">8A702F75</value>
              </object>
            </object>
            <object type="CGameAgent">
              <value name="bIsScripted" type="Bool">False</value>
              <value name="fAccelerationsSlow" type="Float">2</value>
              <value name="fAccelerationsNormal" type="Float">3</value>
              <value name="fAccelerationsFast" type="Float">4</value>
              <value name="fDecelerationsSlow" type="Float">-2</value>
              <value name="fDecelerationsNormal" type="Float">-3</value>
              <value name="fDecelerationsFast" type="Float">-3.5</value>
              <value name="fSpeedsBabyStep" type="Float">0.5</value>
              <value name="fSpeedsWalk" type="Float">1</value>
              <value name="fSpeedsJog" type="Float">3</value>
              <value name="fSpeedsRun" type="Float">4</value>
              <value name="fSpeedsSprint" type="Float">5</value>
              <value name="fVariationBabyStep" type="Float">0</value>
              <value name="fVariationWalk" type="Float">0.2</value>
              <value name="fVariationJog" type="Float">0</value>
              <value name="fVariationRun" type="Float">0</value>
              <value name="fVariationSprint" type="Float">0</value>
            </object>
```

3. “selArmy”

	  
	The value controls if the npc is hostile towards the player. We are going to change this to “2”, it should look like this:

```xml {1}
	<value name="selArmy" type="UInt32">2</value>
```

4. “ShootingSystem”

	  
	This section controls the npc’s shooting ai. By default it looks like this:

```xml
	<object type="ShootingSystem">
                <value name="archGroupNumberCurve" type="String">Curves.ShootingSystem.GroupNumber</value>
                <value name="fMissWidth" type="Float">3</value>
                <value name="fMissHeight" type="Float">0.5</value>
                <value name="fTimerToMissTarget" type="Float">0.2</value>
                <value name="fPointBlankDistance" type="Float">3</value>
                <value name="fTimerToPointBlank" type="Float">0.5</value>
                <object type="ShooterStatus">
                  <value name="fStandingFactor" type="Float">1</value>
                  <value name="fCrouchingFactor" type="Float">1.2</value>
                  <value name="fMoveSpeedBabyStepFactor" type="Float">1</value>
                  <value name="fMoveSpeedWalkFactor" type="Float">0.95</value>
                  <value name="fMoveSpeedJogFactor" type="Float">0.8</value>
                  <value name="fMoveSpeedRunFactor" type="Float">0.7</value>
                  <value name="fMoveSpeedSprintFactor" type="Float">0.6</value>
                  <value name="fDrivingFactor" type="Float">0.1</value>
                  <value name="fSwimmingFactor" type="Float">0.1</value>
                  <value name="fIronsightFactor" type="Float">1</value>
                  <value name="uiMaxHitPerSecondFactor" type="UInt32">5</value>
                </object>
                <object type="TargetStatus">
                  <value name="fStandingFactor" type="Float">1</value>
                  <value name="fCrouchingFactor" type="Float">0.8</value>
                  <value name="fMoveSpeedBabyStepFactor" type="Float">1</value>
                  <value name="fMoveSpeedWalkFactor" type="Float">0.95</value>
                  <value name="fMoveSpeedJogFactor" type="Float">0.8</value>
                  <value name="fMoveSpeedRunFactor" type="Float">0.7</value>
                  <value name="fMoveSpeedSprintFactor" type="Float">0.6</value>
                  <value name="fDrivingFactor" type="Float">0.1</value>
                  <value name="fSwimmingFactor" type="Float">0.1</value>
                  <value name="fIronsightFactor" type="Float">1</value>
                  <value name="uiMaxHitPerSecondFactor" type="UInt32">5</value>
                </object>
              </object>
```

5. “SensorySystem”

	This section controls the npc’s ability to see. By default it looks like this:

```xml
	<object type="SensorySystem">
                <object type="FOVParameters">
                  <object type="FOVMultipliers">
                    <value name="fPreCombatMultiplier" type="Float">4</value>
                    <value name="fCombatMultiplier" type="Float">4</value>
                    <value name="fPostCombatMultiplier" type="Float">4</value>
                    <value name="fPlayerInVehicleMultiplier" type="Float">2</value>
                    <value name="fNightTimeMultiplier" type="Float">0.5</value>
                    <value name="fSniperLengthMultiplier" type="Float">6</value>
                    <value name="fSniperAngleMultiplier" type="Float">0.15</value>
                  </object>
```

	We are going to change it to this:

```xml
	<object type="SensorySystem">
                <object type="FOVParameters">
                  <object type="FOVMultipliers">
                    <value name="fPreCombatMultiplier" type="Float">0.75</value>
                    <value name="fCombatMultiplier" type="Float">1</value>
                    <value name="fPostCombatMultiplier" type="Float">1.25</value>
                    <value name="fPlayerInVehicleMultiplier" type="Float">2</value>
                    <value name="fNightTimeMultiplier" type="Float">0.5</value>
                    <value name="fSniperLengthMultiplier" type="Float">6</value>
                    <value name="fSniperAngleMultiplier" type="Float">0.15</value>
                  </object>
```

6. “CFCXCountersComponentAI”

	This section controls who can damage the npc. By default it looks like this:

```xml
<object type="CFCXCountersComponentAI">
      		<value name="hidHasAliasName" type="Bool">False</value>
      		<value name="archStimEffectTable" type="String">tables.StimEffectTables.NPCDefault</value>
     		<value name="bIsInvincibleExceptToPlayer" hash="3DED5A88" type="Bool">True</value> <!-- type="BinHex" value="01" -->
      		<value name="bIsInvincibleToAI" hash="C729E709" type="Bool">True</value> <!-- type="BinHex" value="01" -->
      		<value name="bIsInvincibleToPlayer" hash="0E37A34A" type="Bool">True</value> <!-- type="BinHex" value="01" -->
```

	  
You can customise these how you like, the value names are self-explanatory. We are going to replace it with the standard enemy version of this section that looks like this:

```xml
<object type="CFCXCountersComponentAI">
          <value name="hidHasAliasName" type="Bool">False</value>
          <value name="archStimEffectTable" type="String">tables.StimEffectTables.NPCDefault</value>
          <value name="bIsInvincibleExceptToPlayer" hash="3DED5A88" type="Bool">False</value> <!-- type="BinHex" value="01" -->
          <value name="bIsInvincibleToAI" hash="C729E709" type="Bool">False</value> <!-- type="BinHex" value="01" -->
          <value name="bIsInvincibleToPlayer" hash="0E37A34A" type="Bool">False</value> <!-- type="BinHex" value="01" -->
```

Step 2: Adding the new npcs into the patrols  
**Decoding required**  
Combined maps  
xx\_GhostPatrols.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Separate maps  
Map 1: 10\_GhostPatrols.xml \< entitylibrary.fcb (\\patch\_unpack\\worlds\\world1\\generated\\)  
Map 2: 10\_GhostPatrols.xml \< entitylibrary.fcb (\\patch\_unpack\\worlds\\world2\\generated\\)

The steps for this are largely the same as adding other enemy types into the patrols, instructions for which can be found [here](#enemy-type-1). The only thing that’s different is that you use the “hidName” values you used in “xx\_buddies.xml”. In my example above the value would be “buddies.Friendlyfighter\_Female”.

# 

