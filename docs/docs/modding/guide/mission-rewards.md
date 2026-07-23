---
sidebar_position: 15
sidebar_label: "Mission/Exploration Rewards"
format: md
---

:::info[From the Almost Complete Guide]
This page reproduces a section of ["An Almost Complete Guide to Far Cry 2 Modding"](./index.md) by Boggalog, verbatim, with attribution. See the [guide index](./index.md) for the full attribution note.
:::

# Mission/exploration rewards

## Story missions

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

Story mission rewards are controlled by the “diamondreward” stats within the “\<StoryMission\>” and “\<LibraryMission\>” sections.

Each mission has its own line where you can edit the individual rewards.

```xml {1}
<Item act="1" id="A1SM02" name="FoolsErrand"               diamondreward="20" buddyunlock="0" infbonus="5" achievement="killcaptain" />
```

## Assassination missions

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

Assassination mission rewards are controlled by the “AssassinationRewardWorld1” and “AssassinationRewardWorld2” stats.

The rewards for each map can be edited individually.

AssassinationRewardWorld1="**10**" AssassinationRewardWorld2="**15**"

## Convoy missions

convoymissions.unlockweapons.lua (\\patch\_unpack\\domino\\user\\sidemissions\\)

The weapons rewarded by each convoy mission can be swapped out within this file. 

By swapping the names below, the displayed names of each weapon in the pop-up reward screen will also be changed automatically. You can also edit the weapon images shown in the pop-up reward with the “Convoy mission reward images” section of this guide.

There are no separate sections for each mission, you just search for the weapon and swap it with another. Each weapon is listed with its name from the weapon/upgrade shop section of “gamemodesconfig.xml”, here is a complete list:

Makarov \- makarov crate  
Silenced Makarov \- 6p9 crate  
Star 45 \- star45 crate  
Eagle 50 \- de crate

Mac 10 \- mac10 crate  
Uzi \- uzi crate

G3KA4 \- g3ka4 crate  
AK47 \- ak47 crate  
FNFAL \- fnfal crate  
M16 \- m16 crate  
MP5 \- mp5 crate

Homeland 37 \- ithaca crate  
SPAS 12 \- spas12 crate  
USAS 12 \- usas12 crate

M1903 \- m1903 crate  
Dragunov \- dragunov crate  
AS50 \- as50 crate  
Dart rifle \- dart rifle crate

PKM \- pkm crate  
M249 \- m249 crate

Flare gun \- flare crate  
Flamethrower \- lpo50 crate

M79 \- m79 crate  
IEDs \- ied crate  
RPG \- rpg7 crate  
Carl G \- carlgustaf crate  
Mortar \- mortar crate  
MGL140 \- mgl40 crate

For reference these are the default mission rewards:

## Diamond briefcases

**Decoding required**  
xx\_OA\_MissionPickups.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

There are four different briefcase entries:

The briefcase from the tutorial \- MissionPickups.DiamondBriefcase\_TUTORIAL\_ONLY  
Briefcases that by default contain one diamond \- MissionPickups.DiamondBriefcase\_LVL1  
Briefcases that by default contain two diamonds \- MissionPickups.DiamondBriefcase\_LVL2  
Briefcases that by default contain three diamonds \- MissionPickups.DiamondBriefcase\_LVL3

The number of diamonds in each briefcase is controlled by the “nDiamonds” stat within each entry.

```xml {1}
<value name="nDiamonds" hash="8ACA6BE0" type="BinHex">03</value>
```

