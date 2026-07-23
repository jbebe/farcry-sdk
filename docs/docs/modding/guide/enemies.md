---
sidebar_position: 11
sidebar_label: "Enemies"
format: md
---

:::info[From the Almost Complete Guide]
This page reproduces a section of ["An Almost Complete Guide to Far Cry 2 Modding"](./index.md) by Boggalog, verbatim, with attribution. See the [guide index](./index.md) for the full attribution note.
:::

# Enemies

## Enemy Weapons

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

Enemy weapons are controlled within the “\<InventoryPacks\>” section of this file.

There are different weapon packs here for the different enemy types, these are their titles:

```xml
<Pack name="assault">
<Pack name="shotgun">
<Pack name="RocketLauncher">
<Pack name="Mortar">
<Pack name="sniper">
```

Below each of these titles you’ll see lists of weapons. Each enemy has two lists, one each to control their secondary and primary weapons. Some enemy types have a section for special weapons rather than primary weapons but it functions the same.

Each of these lists is made up of individual weapons and difficulty levels, these are the individual components:

SecondaryWeapon/PrimaryWeapon/SpecialWeapon \- These depend on the class of weapon you’re adding. “SecondaryWeapon” is always for secondaries but “PrimaryWeapon” or “SpecialWeapon” will depend on the enemy type, just follow what’s already there.

difficulty=”xx” \- There are 28 difficulty levels (0-27). You can remove this part if you want to make the same weapon set for every difficulty level. I don’t know exactly how these apply to gameplay, whether it’s based on geography, infamy level or position in the story. I’ve done a test before by setting levels 26 and 27 to only flamethrowers, driving around the map everyone had regular weapons but once I went into the Heart of Darkness for the final stages of the game everyone had the flamethrowers. I’ve always imagined the transition from map 1 to map 2 happening around difficulty level 14\.

probability=”xx” \- You can set individual probabilities for each weapon within the difficulty levels. The probabilities for each difficulty level need to add up to 1\. You can have as many weapons as you want with different probabilities or a single weapon with a probability of 1\. 

archetype=”xx” \- This is the name for each weapon. You can find these names within the xx\_weaponproperties.xml file from entitylibrarypatchoverride.fcb. Don’t forget that weapons that are specials for the player have separate versions for enemies that are primaries. These are normally marked by “\_Merc” in the title but for the flamethrower you can use the multiplayer version marked with “\_Multi” as that’s already a primary.

This is an example section from Vanilla+:

```xml
<PrimaryWeapon difficulty="24" probability="0.05" archetype="weapons.Primary.G3KA4" />
<PrimaryWeapon difficulty="24" probability="0.15" archetype="weapons.Primary.AK47" />
<PrimaryWeapon difficulty="24" probability="0.33" archetype="weapons.Primary.FNFAL" />
<PrimaryWeapon difficulty="24" probability="0.31" archetype="weapons.Primary.M16" />
<PrimaryWeapon difficulty="24" probability="0.05" archetype="weapons.Special.PKM.PKM_Merc" />
<PrimaryWeapon difficulty="24" probability="0.10" archetype="weapons.Special.M249_Saw.M249_Saw_Merc" />
<PrimaryWeapon difficulty="24" probability="0.01" archetype="weapons.Primary.AK47.AK47_Gold" />
```

Changing all of this is lots of work and if you make a single mistake in the format of a line your file can stop packing properly and you won’t know where you’ve gone wrong. I suggest planning it all out first. To make this easier I split the difficulties into sections (0-5, 6-10, 11-15, 16-20, 21-25, 26-27) and I made lists of it all in pencil so I could edit it and know what I want before working in the actual file.

## Ammo drops

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

The amount of ammo dropped by enemies is controlled by the “ClipMultiplierForPickup” line of this file.

Each difficulty has a separate value, where you can specify what proportion of a single magazine each enemy will drop.

```xml {1}
<ClipMultiplierForPickup Casual="2" Experimented="1" Hardcore="0.5" Infamous="0.25"/>
```

## Grenade drops

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

The chance for enemies to drop grenades is controlled by the “ChanceToDropGrenade” line of this file.

Each difficulty has a separate value, where you can specify the proportional probability of an enemy to drop a grenade.

```xml {1}
<ChanceToDropGrenade Casual="1" Experimented="0.5" Hardcore="0.33" Infamous="0.25"/>
```

## Stealth \- Enemy perception

xx\_enemy\_archetypes.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

The easiest way to make changes to the stealth system is to edit enemy perception. All of these changes are done in the file “xx\_enemy\_archetypes.xml”, where each enemy has a separate entry. The entry titles are listed below, you can see there are separate entries for the different enemy types, ethnicities, and factions. In addition to the normal enemies in the red/blue factions there are entries for the spec-ops enemies encountered in a single mission early in the game and also assassination targets.

Blue\_Faction.Assault\_Caucasian  
Blue\_Faction.Assault\_Nubian  
Blue\_Faction.CarlGustaf\_Caucasian  
Blue\_Faction.CarlGustaf\_Nubian  
Blue\_Faction.LightMachineGunner\_Caucasian  
Blue\_Faction.LightMachineGunner\_Nubian  
Blue\_Faction.MortarMan\_Caucasian  
Blue\_Faction.MortarMan\_Nubian  
Blue\_Faction.RocketMan\_Caucasian  
Blue\_Faction.RocketMan\_Nubian  
Blue\_Faction.ShotgunMan\_Caucasian  
Blue\_Faction.ShotgunMan\_Nubian  
Blue\_Faction.Sniper\_Caucasian  
Blue\_Faction.Sniper\_Nubian

Red\_Faction.Assault\_Caucasian  
Red\_Faction.Assault\_Nubian  
Red\_Faction.CarlGustaf\_Caucasian  
Red\_Faction.CarlGustaf\_Nubian  
Red\_Faction.LightMachineGunner\_Caucasian  
Red\_Faction.LightMachineGunner\_Nubian  
Red\_Faction.MortarMan\_Caucasian  
Red\_Faction.MortarMan\_Nubian  
Red\_Faction.RocketMan\_Caucasian  
Red\_Faction.RocketMan\_Nubian  
Red\_Faction.ShotgunMan\_Caucasian  
Red\_Faction.ShotgunMan\_Nubian  
Red\_Faction.Sniper\_Caucasian  
Red\_Faction.Sniper\_Nubian

Special.SpecOps\_Assault  
Special.SpecOps\_Shotgun

Missions.Assassination\_Target

### 

### Perception pre-combat

xx\_enemy\_archetypes.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

This is the main stat that I suggest editing to improve stealth, I personally found a value of 0.6 to be a good balance for the enemies to not spot you immediately while also not being blind.

Enemy perception pre-combat is controlled by the “fPreCombatMultiplier” stat in the “SensorySystem” section.

This works as a proportional modifier, so any value lower than 1 will decrease enemy perception pre-combat and a value of 0.5 will decrease enemy perception pre-combat by 50%.

```xml {4}
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

### Perception at night

xx\_enemy\_archetypes.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Enemy perception at night is controlled by the “fNightTimeMultiplier” stat in the “SensorySystem” section.

This works as a proportional modifier, so any value lower than 1 will decrease enemy perception at night and a value of 0.5 will decrease enemy perception at night by 50%.

```xml {8}
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

### Overall perception

xx\_enemy\_archetypes.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Overall enemy perception is controlled by separate stats for each location type. There are stats for focussed vision and peripheral visions, and you can edit the length and angle of each of these. You can make these out yourself in the example below.

**I do not recommend changing these settings, decreasing overall vision can make enemies behave strangely in combat.**

```xml
<object type="DesertFOV">
        <object type="FocusFOV">
            <value name="fLength" type="Float">60</value>
            <value name="fAngle" type="Float">60</value>
        </object>
        <object type="PeripheralFOV">
            <value name="fLength" type="Float">40</value>
            <value name="fAngle" type="Float">120</value>
         </object>
</object>
<object type="SavannahFOV">
         <object type="FocusFOV">
             <value name="fLength" type="Float">40</value>
             <value name="fAngle" type="Float">60</value>
         </object>
         <object type="PeripheralFOV">
             <value name="fLength" type="Float">30</value>
             <value name="fAngle" type="Float">120</value>
         </object>
</object>
<object type="JungleFOV">
        <object type="FocusFOV">
             <value name="fLength" type="Float">30</value>
             <value name="fAngle" type="Float">60</value>
        </object>
        <object type="PeripheralFOV">
             <value name="fLength" type="Float">20</value>
             <value name="fAngle" type="Float">120</value>
        </object>
</object>
```
              

## AI Behaviours

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

There are a number of enemy behaviours that can have their likelihoods individually customised. 

These are all within the “AdaptativeBehavior” section.

You can set percentage chances for the same 28 difficulty levels that the enemy weapon system is based on. I don’t know exactly how these apply to gameplay, whether it’s based on geography, infamy level or position in the story. I’ve done a test before by setting levels 26 and 27 to only flamethrowers, driving around the map everyone had regular weapons but once I went into the Heart of Darkness for the final stages of the game everyone had the flamethrowers. I’ve always imagined the transition from map 1 to map 2 happening around difficulty level 14\.

The labels for these behaviours are mostly vague, so my descriptions of what they mean may well be wrong. There are also behaviours I haven’t described that I don’t fully understand. Please someone test them and figure them out\!

```xml
<AdaptativeBehavior>
	<Item behavior="Grenade" ...
	<Item behavior="GrenadeAndBuilding" ...
	<Item behavior="ChaseWithVehicle" ...
	<Item behavior="ReachSniperWithVehicle" ...
	<Item behavior="MountedWeapon" ...
	<Item behavior="ShootFlare" ...
	<Item behavior="ShootInterestingObject" ...
	<Item behavior="RescueVictim" ...
	<Item behavior="RangeWeapon" ...
	<Item behavior="VehicleChaseLevel2" ...
	<Item behavior="VehicleChaseLevel3" ...
	<Item behavior="LongRangeVehicle" ...
</AdaptativeBehavior>
```

### Combat

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

There are four behaviours that apply to combat:

“MountedWeapon” for using mounted weapons.  
“ShootFlare” for calling reinforcements.  
“RescueVictim” for rescuing their injured friends.  
“RangeWeapon” for ranging mortars with a smoke shell before firing an explosive shell.

### Grenade throwing

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

There are two behaviours that apply to grenade throwing:

“Grenade” for regular throws.  
“GrenadeAndBuilding” for throwing grenades into buildings.

### Vehicle use

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

There are two behaviours that apply to vehicle use:

“ChaseWithVehicle” for chasing the player when they drive through checkpoints.  
“ReachSniperWithVehicle” for using a vehicle to close distance when they are attacked from far away.

## Ethnicity

xx\_enemy\_archetypes.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

By default, each enemy type for each faction has both a white and black variation. It is possible to change this, so every enemy type either overall or of a given faction is the same ethnicity.

Enemy ethnicity is controlled by the “CGraphicKitComponent” section of each enemy type. You can overwrite this section with either ethnicity that you want the enemy to be.

This is that section for a white enemy:

```xml
<object type="CGraphicKitComponent">
          <value name="hidHasAliasName" type="Bool">False</value>
          <value name="bRadomize" type="Bool">True</value>
          <object type="Tags">
            <object type="SpecializationTag">
              <value hash="9B35862A" type="String">caucasian</value>
              <value name="sTag" type="Hash">E3A43C0B</value>
            </object>
            <object type="SpecializationTag">
              <value hash="9B35862A" type="String"></value>
              <value name="sTag" type="Hash">FFFFFFFF</value>
            </object>
          </object>
          <object type="PartOverwrite">
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String">vgault-P-2008041562096342</value>
              <value name="PartID" type="Hash">1F4B6AA3</value>
              <value name="TextureIndex" type="UInt32">4</value>
              <value name="ColorIndex" type="UInt32">0</value>
            </object>
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String">ycloutier-P-2007072458454117</value>
              <value name="PartID" type="Hash">AE49CAFF</value>
              <value name="TextureIndex" type="UInt32">12</value>
              <value name="ColorIndex" type="UInt32">4294967295</value>
            </object>
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String">vgault-P-2008031064983593</value>
              <value name="PartID" type="Hash">F4A2B576</value>
              <value name="TextureIndex" type="UInt32">0</value>
              <value name="ColorIndex" type="UInt32">4294967295</value>
            </object>
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String">ycloutier-P-2007070548404363</value>
              <value name="PartID" type="Hash">F22507DF</value>
              <value name="TextureIndex" type="UInt32">3</value>
              <value name="ColorIndex" type="UInt32">1</value>
            </object>
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String">vgault-P-2008031435106617</value>
              <value name="PartID" type="Hash">61D6287C</value>
              <value name="TextureIndex" type="UInt32">0</value>
              <value name="ColorIndex" type="UInt32">4294967295</value>
            </object>
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String">ycloutier-P-2007072941899753</value>
              <value name="PartID" type="Hash">21E0E0ED</value>
              <value name="TextureIndex" type="UInt32">4294967295</value>
              <value name="ColorIndex" type="UInt32">4294967295</value>
            </object>
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String">ycloutier-P-2007072957240043</value>
              <value name="PartID" type="Hash">B6CD5AD0</value>
              <value name="TextureIndex" type="UInt32">4294967295</value>
              <value name="ColorIndex" type="UInt32">4294967295</value>
            </object>
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String">vgault-P-2008020655242910</value>
              <value name="PartID" type="Hash">CE4D2C24</value>
              <value name="TextureIndex" type="UInt32">4294967295</value>
              <value name="ColorIndex" type="UInt32">4294967295</value>
            </object>
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String">ycloutier-P-2007070330379177</value>
              <value name="PartID" type="Hash">F8A82C68</value>
              <value name="TextureIndex" type="UInt32">4294967295</value>
              <value name="ColorIndex" type="UInt32">4294967295</value>
            </object>
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String">vgault-P-2008060252847888</value>
              <value name="PartID" type="Hash">AE41BA3C</value>
              <value name="TextureIndex" type="UInt32">4294967295</value>
              <value name="ColorIndex" type="UInt32">4294967295</value>
            </object>
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String">vgault-P-2008042337976794</value>
              <value name="PartID" type="Hash">5E4515E4</value>
              <value name="TextureIndex" type="UInt32">4294967295</value>
              <value name="ColorIndex" type="UInt32">4294967295</value>
            </object>
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String"></value>
              <value name="PartID" type="Hash">FFFFFFFF</value>
              <value name="TextureIndex" type="UInt32">4294967295</value>
              <value name="ColorIndex" type="UInt32">4294967295</value>
            </object>
          </object>
        </object>
```

This is that section for a black enemy:

```xml
<object type="CGraphicKitComponent">
          <value name="hidHasAliasName" type="Bool">False</value>
          <value name="bRadomize" type="Bool">True</value>
          <object type="Tags">
            <object type="SpecializationTag">
              <value hash="9B35862A" type="String">nubian</value>
              <value name="sTag" type="Hash">B2CB79A8</value>
            </object>
            <object type="SpecializationTag">
              <value hash="9B35862A" type="String"></value>
              <value name="sTag" type="Hash">FFFFFFFF</value>
            </object>
          </object>
          <object type="PartOverwrite">
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String">htrandafir-P-2008060635626686</value>
              <value name="PartID" type="Hash">3E52333F</value>
              <value name="TextureIndex" type="UInt32">4294967295</value>
              <value name="ColorIndex" type="UInt32">4294967295</value>
            </object>
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String">ycloutier-P-2007072362488160</value>
              <value name="PartID" type="Hash">83B487F6</value>
              <value name="TextureIndex" type="UInt32">0</value>
              <value name="ColorIndex" type="UInt32">4294967295</value>
            </object>
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String">vgault-P-2008031137328325</value>
              <value name="PartID" type="Hash">A8E10946</value>
              <value name="TextureIndex" type="UInt32">0</value>
              <value name="ColorIndex" type="UInt32">4294967295</value>
            </object>
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String">ycloutier-P-2007070548986148</value>
              <value name="PartID" type="Hash">72A2F82F</value>
              <value name="TextureIndex" type="UInt32">2</value>
              <value name="ColorIndex" type="UInt32">3</value>
            </object>
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String">vgault-P-2008031435157790</value>
              <value name="PartID" type="Hash">46F5B5F5</value>
              <value name="TextureIndex" type="UInt32">0</value>
              <value name="ColorIndex" type="UInt32">4294967295</value>
            </object>
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String">ycloutier-P-2007072941899753</value>
              <value name="PartID" type="Hash">21E0E0ED</value>
              <value name="TextureIndex" type="UInt32">4294967295</value>
              <value name="ColorIndex" type="UInt32">4294967295</value>
            </object>
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String">ycloutier-P-2007072954271880</value>
              <value name="PartID" type="Hash">439612F6</value>
              <value name="TextureIndex" type="UInt32">4294967295</value>
              <value name="ColorIndex" type="UInt32">4294967295</value>
            </object>
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String">vgault-P-2008020655242910</value>
              <value name="PartID" type="Hash">CE4D2C24</value>
              <value name="TextureIndex" type="UInt32">4294967295</value>
              <value name="ColorIndex" type="UInt32">4294967295</value>
            </object>
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String">ycloutier-P-2007070330379177</value>
              <value name="PartID" type="Hash">F8A82C68</value>
              <value name="TextureIndex" type="UInt32">4294967295</value>
              <value name="ColorIndex" type="UInt32">4294967295</value>
            </object>
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String">vgault-P-2008060252847888</value>
              <value name="PartID" type="Hash">AE41BA3C</value>
              <value name="TextureIndex" type="UInt32">4294967295</value>
              <value name="ColorIndex" type="UInt32">4294967295</value>
            </object>
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String">vgault-P-2008060685370512</value>
              <value name="PartID" type="Hash">2470B3C1</value>
              <value name="TextureIndex" type="UInt32">4294967295</value>
              <value name="ColorIndex" type="UInt32">4294967295</value>
            </object>
            <object type="ActivePartOverwrite">
              <value hash="CE56B704" type="String"></value>
              <value name="PartID" type="Hash">FFFFFFFF</value>
              <value name="TextureIndex" type="UInt32">4294967295</value>
              <value name="ColorIndex" type="UInt32">4294967295</value>
            </object>
          </object>
        </object>
```

## Reinforcements

### Enemy type

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

The enemy types of reinforcements are controlled by the middle section of the first two values in the “ReinforcementArchetypes” section

You can swap the part that in the example below says “Assault” with another for a different enemy type.

It can be swapped with these values:

Assault  
ShotgunMan  
Sniper  
RocketMan  
MortarMan

```xml {2,3}
<ReinforcementArchetypes>
	<Archetype name="enemy_archetypes.Red_Faction.Assault_Caucasian" type="redmerc" />
	<Archetype name="enemy_archetypes.Red_Faction.Assault_Caucasian" type="BlueMerc" />
	<Archetype name="vehicle.Land.Rover" type="vehicle" />				
</ReinforcementArchetypes>
```

### Enemy ethnicity

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

The ethnicities of enemies within reinforcements are controlled by the last section of the first two values in the “ReinforcementArchetypes” section

The part that in the example below says “Caucasian”, you can keep it as that for the enemy to be white, or change it to “Nubian” for the enemy to be black. 

```xml {2,3}
<ReinforcementArchetypes>
	<Archetype name="enemy_archetypes.Red_Faction.Assault_Caucasian" type="redmerc" />
	<Archetype name="enemy_archetypes.Red_Faction.Assault_Caucasian" type="BlueMerc" />
	<Archetype name="vehicle.Land.Rover" type="vehicle" />				
</ReinforcementArchetypes>
```

### Vehicle type

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

The vehicles that reinforcements use are controlled by the last value in the “ReinforcementArchetypes” section

You can swap the part that in the example below says “vehicle.Land.Rover” with another for a different vehicle.

```xml {4}
<ReinforcementArchetypes>
	<Archetype name="enemy_archetypes.Red_Faction.Assault_Caucasian" type="redmerc" />
	<Archetype name="enemy_archetypes.Red_Faction.Assault_Caucasian" type="BlueMerc" />
	<Archetype name="vehicle.Land.Rover" type="vehicle" />				
</ReinforcementArchetypes>
```

It can be swapped with these values:

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

