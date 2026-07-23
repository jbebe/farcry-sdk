---
sidebar_position: 8
sidebar_label: "Weapons"
format: md
---

:::info[From the Almost Complete Guide]
This page reproduces a section of ["An Almost Complete Guide to Far Cry 2 Modding"](./index.md) by Boggalog, verbatim, with attribution. See the [guide index](./index.md) for the full attribution note.
:::

# Weapons

## Base game weapons

The stats for the base weapons are controlled by the files “xx\_WeaponProperties.xml” and “xx\_Weapons.xml” within “entitylibrarypatchoverride.fcb” from \\patch\_unpack\\generated\\. I’ve included the complete files in there already but if you are starting from scratch they need to be copied in from “entitylibrary.fcb” in \\patch\_unpack\\worlds\\tmpla\\generated\\.

## DLC weapons

The stats for the DLC weapons have the same structure as the base game weapons, but in two different files. The DLC weapon files are “1\_DLC1Weapons.xml” and “3\_WeaponProperties.xml” within “entitylibrary.fcb” from \\patch\_unpack\\downloadcontent\\dlc1\\generated\\.

## Weapon Entry Titles

When editing weapons each .xml file contains entries that are specifically named for each weapon. Some weapons have multiple entries for different varieties. These are the different kinds:

Special.RPG7 \- The regular singleplayer version, available from the weapon armories.

Special.RPG7.Mikes\_Rusty \- The version available during the showdown at Mike’s Bar.

Special.RPG7.Persistent \- The version that can be found in the open world.

Special.RPG7.RPG7\_Merc \- The enemies don’t have three weapon slots like the player. This means that weapons that go in the special slot for the player have alternate versions that are primaries for the enemies to use. The enemies will drop the correct version for the player to use when killed but it’s still useful to edit these weapons too if you want to rebalance the enemies’ damage output or if you’ve changed something noticeable like rate of fire and want the enemies to be consistent.

Special.RPG7.Multi \- The multiplayer version.

All of these versions are relevant apart from the one for multiplayer. If you want your weapon edits to be fully consistent you should make the same changes for every version.

This is the complete list of entries so you can easily Ctrl-F and find them all:

Machete  
Modern machete \- HandToHand.Machete  
Homemade machete \- HandToHand.Machete\_HomeMade  
Primitive machete \- HandToHand.Machete\_Primitive

Pistols  
Makarov \- Secondary.Makarov  
Silenced Makarov \- Secondary.SilencedMakarov\_6P9  
Star .45 \- Secondary.Star45  
Eagle .50 \- Secondary.DesertEagle, Secondary.DesertEagle.Persistent

SMGs  
Mac-10 \- Secondary.MAC10, Secondary.MAC10.Mikes\_Rusty  
Uzi \- Secondary.Uzi

Shotguns  
Homeland 37 \- Primary.Ithaca  
Spas-12 \- Primary.SPAS12, Primary.SPAS12.Persistent  
USAS-12 \- Primary.USAS12, Primary.USAS12.Persistent

Assault Rifles  
G3KA4 \- Primary.G3KA4  
AK-47 \- Primary.AK47  
Gold AK-47 \- Primary.AK47.AK47\_Gold  
MP5 \- Primary.MP5, Primary.MP5.Mikes\_Rusty, Primary.MP5.Persistent  
FAL Paratrooper \- Primary.FNFAL, Primary.FNFAL.Persistent  
AR-16 \- Primary.M16, Primary.M16.Persistent

LMGs  
PKM \- Special.PKM, Special.PKM.Mikes\_Rusty, Special.PKM.PKM\_Merc  
M249 Saw \- Special.M249\_Saw, Special.M249\_Saw.Persistent, Special.M249\_Saw.M249\_Saw\_Merc

Sniper Rifles  
M1903 \- Special.M1903, Special.M1903.M1903\_Merc  
Dragunov \- Primary.Dragunov, Primary.Dragunov.Mikes\_Rusty, Primary.Dragunov.Persistent, Primary.Dragunov.Dragunov\_Merc  
AS50 \- Primary.AS50, Primary.AS50.Persistent  
Dart Rifle \- Special.Dart\_Rifle

Explosives/Flamethrower  
Flare Gun \- Special.Flare\_Gun, Special.Flare\_Gun.Flare\_Gun\_Merc  
IEDS \- Secondary.IED  
M79 \- Secondary.M79, Secondary.M79.Mikes\_Rusty  
RPG \- Special.RPG7, Special.RPG7.Mikes\_Rusty, Special.RPG7.Persistent, Special.RPG7.RPG7\_Merc  
Carl G \- Special.Carl\_Gustaf, Special.Carl\_Gustaf.Persistent, Special.Carl\_Gustaf.Carl\_Gustaf\_Merc  
Mortar \- Special.Mortar, Special.Mortar.Persistent, Special.Mortar.Mortar\_Merc  
Flamethrower \- Special.LPO50, Special.LPO50.Persistent

DLC Weapons  
Sawed off shotgun \- DLC1.SawedOffShotgun  
Silenced shotgun \- DLC1.SilencedShotgun  
Explosive crossbow \- DLC1.Crossbow

## Accuracy

### Regular accuracy

**Decoding required**  
Base game weapons \- xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 3\_WeaponProperties.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Accuracy is controlled by the “fAmplitude” stats in the “BulletSpread” section.

You can change the value for each different player state, which are different combinations of aiming, crouching and jumping.

```xml {2,6,10,14,18}
<object hash="1383D9F5" type="BulletSpread">
          <value name="fAmplitude" hash="3026125D" type="Float">3.5</value> <!-- type="BinHex" value="00006040" -->
          <value name="fFrequency" hash="7DF4325F" type="Float">35.0</value> <!-- type="BinHex" value="00000C42" -->
        </object>
        <object hash="33DBF44C" type="BulletSpread_IronSight">
          <value name="fAmplitude" hash="3026125D" type="Float">0.4</value> <!-- type="BinHex" value="CDCCCC3E" -->
          <value name="fFrequency" hash="7DF4325F" type="Float">35.0</value> <!-- type="BinHex" value="00000C42" -->
        </object>
        <object hash="A7EC6750" type="BulletSpreadCrouch">
          <value name="fAmplitude" hash="3026125D" type="Float">1.0</value> <!-- type="BinHex" value="0000803F" -->
          <value name="fFrequency" hash="7DF4325F" type="Float">35.0</value> <!-- type="BinHex" value="00000C42" -->
        </object>
        <object hash="21FDF772" type="BulletSpreadCrouch_IronSight">
          <value name="fAmplitude" hash="3026125D" type="Float">0.38</value> <!-- type="BinHex" value="5C8FC23E" -->
          <value name="fFrequency" hash="7DF4325F" type="Float">35.0</value> <!-- type="BinHex" value="00000C42" -->
        </object>
        <object hash="A071CB54" type="BulletSpreadJump">
          <value name="fAmplitude" hash="3026125D" type="Float">4.5</value> <!-- type="BinHex" value="00009040" -->
          <value name="fFrequency" hash="7DF4325F" type="Float">35.0</value> <!-- type="BinHex" value="00000C42" -->
        </object>
```

### Shotgun accuracy

**Decoding required**  
Base game weapons \- xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 3\_WeaponProperties.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Shotgun accuracy is controlled by the stats “fAngleYawBulletSpread”, “fAnglePitchBulletSpread”, “fSecondaryAngleYawBulletSpread” and “fSecondaryAnglePitchBulletSpread”, all of them in the “FireStrategyProperties” section.

These can be visualised as “fAngleYaw/PitchBulletSpread” controlling the initial accuracy of your shots, and “fSecondaryAngleYaw/PitchBulletSpread” controlling the accuracy of your shots after a few metres. The ratio between the two sets of values is important. For more accurate shotguns I found that a 1:2 ratio gives a good balance and edit the accuracy values from there.

Yaw is the horizontal accuracy and pitch is the vertical accuracy.

```xml {5,6,8,9}
<object type="FireStrategyProperties">
            <value name="bUseAngleSpread" type="Bool">True</value>
            <value name="iBulletsShot" type="UInt32">7</value>
            <value name="iBurstLength" type="UInt32">0</value>
            <value name="fAngleYawBulletSpread" type="Float">2</value>
            <value name="fAnglePitchBulletSpread" type="Float">2</value>
            <value name="bHasMuzzleLight" type="Bool">True</value>
            <value name="fSecondaryAngleYawBulletSpread" hash="6E2151FF" type="Float">4.0</value> <!-- type="BinHex" value="00008040" -->
            <value name="fSecondaryAnglePitchBulletSpread" hash="07F52A4D" type="Float">4.0</value> <!-- type="BinHex" value="00008040" -->
            <value hash="F8F5F0F8" type="String">Weapon.MetalShellMedium</value>
            <value name="matimpShellImpactFx" type="Hash">561ED150</value>
            <value hash="EB8DE264" type="String">Weapon.Bullet</value>
            <value name="matimpBulletImpactFx" type="Hash">792B8A0E</value>
            <value hash="74A94828" type="String">Weapon.Bullet</value>
            <value name="matimpSecondaryBulletImpactFx" type="Hash">792B8A0E</value>
            <object type="Network">
              <value name="strControllerNetobjectType" type="String"></value>
            </object>
```

### 

### Accuracy while moving

**Decoding required**  
Base game weapons \- xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 3\_WeaponProperties.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Accuracy while moving is controlled by the stat “fBulletSpread\_MovementModifier” in the “FirstPerson” section. This works as a proportional modifier, so any value higher than 1 will increase bullet spread while moving and a value of 1.25 will increase bullet spread by 25%.

```xml {2}
<object hash="1E97B101" type="FirstPerson">
        <value name="fBulletSpread_MovementModifier" hash="EA167604" type="Float">1.25</value> <!-- type="BinHex" value="0000A03F" -->
```

## Ammo Type

**Decoding required**  
Base game weapons \- xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 3\_WeaponProperties.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Ammo is controlled by the “ammoAmmoType” and “AB258E09” values in the “Ammo” section. I don’t know exactly how the two of these work but both need to be changed.

Here are the different values for the ammo types:

Pistol ammo  
“AB258E09” \= “6465736572746561676C6500”  
“ammoAmmotype” \= “6D6540FA”

SMG ammo  
“AB258E09” \= “736D6700”  
“ammoAmmotype” \= “AA73EE0A”

Shotgun ammo  
“AB258E09” \= “73686F7467756E00”  
“ammoAmmotype” \= “EEAE53E1”

Assault rifle ammo  
“AB258E09” \= “61737361756C747269666C6500”  
“ammoAmmotype” \= “BC6782FC”

LMG ammo  
“AB258E09” \= “6C6D6700”  
“ammoAmmotype” \= “BD090A47”

Sniper rifle ammo  
“AB258E09” \= “736E697065727269666C6500”  
“ammoAmmotype” \= “7D6BD5F2”

Dart rifle ammo  
“AB258E09” \= “646172747300”  
“ammoAmmotype” \= “FC2096BC”

MGL140 ammo  
“AB258E09” \= “6D676C31343000”  
“ammoAmmotype” \= “E710123D”

Rocket launcher/Explosive crossbow ammo  
“AB258E09” \= “726F636B657400”  
“ammoAmmotype” \= “CEB9BB1E”

M79 ammo  
“AB258E09” \= “6D373900”  
“ammoAmmotype” \= “704CA95D”

IED ammo  
“AB258E09” \= “69656400”  
“ammoAmmotype” \= “EA12131E”

Flare gun ammo  
“AB258E09” \= “666C61726500”  
“ammoAmmotype” \= “C86412FF”

Flamethrower ammo  
“AB258E09” \= “6675656C00”  
“ammoAmmotype” \= “31BD6FE9”

Mortar ammo  
“AB258E09” \= “6D6F7274617200”  
“ammoAmmotype” \= “4EE9BFD6”

```xml {2,3}
<object hash="4FBDD114" type="Ammo">
       <value hash="AB258E09" type="BinHex">6675656C00</value>
       <value name="ammoAmmoType" hash="5957C8C7" type="Hash">31BD6FE9</value> <!-- type="BinHex" value="E96FBD31" -->
```

## 

## Auto Reload

There are two ways of enabling and disabling auto reload. Editing the weapons involves changing more values but your change will be applied on any saved game. Editing the playable characters involves changing less values but you need to start a new game for your change to take effect.

### Option 1 \- Editing weapons

Base game weapons \- xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 3\_WeaponProperties.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Auto reload is controlled by the “bAutoReload” stat in the “CommonProperties” section. Enable or disable it by changing the value to “True” or “False”.

```xml {5}
<object type="CommonProperties">
       <value name="sName" type="String">ak47</value>
       <value name="sDisplayName" type="String">AK-47</value>
       <value name="fReloadTime" type="Float">0</value>
       <value name="bAutoReload" type="Bool">True</value>
       <value name="bIsSilent" type="Bool">False</value>
```

### Option 2 \- Editing playable characters

xx\_player.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated)

Each playable character has their own section of this file, these are the entry titles:

MainCharacter.PawnPlayer.Andre\_Hyppolite  
MainCharacter.PawnPlayer.Frank\_Bilders  
MainCharacter.PawnPlayer.Hakim\_Echebbi  
MainCharacter.PawnPlayer.Josip\_Idromeno  
MainCharacter.PawnPlayer.Marty\_Alencar  
MainCharacter.PawnPlayer.Paul\_Ferenc  
MainCharacter.PawnPlayer.Quarbani\_Singh  
MainCharacter.PawnPlayer.Warren\_Clyde  
MainCharacter.PawnPlayer.Xianyong\_Bai

Auto reload is controlled by the “bAutoReload” stat in the “Inventory” section. Enable or disable it by changing the value to “True” or “False”.

```xml {6}
<object type="Inventory">
       <value hash="8C965C28" type="String">player</value>
       <value name="packInventoryPack" type="Hash">98197A65</value>
       <value name="archGPSVehicleArchetype" type="String">gadgets.Equipped.Compass_Vehicle</value
       <value name="bUnlimitedAmmo" type="Bool">False</value>
       <value name="bAutoReload" type="Bool">True</value>
       <value name="bAutoDraw" type="Bool">True</value>
       <value hash="130CDED8" type="String">hand_hand</value>
       <value name="sInitialWeaponCategory" type="Hash">E97A284A</value>
</object>
```

## 

## Damage

Base game weapons \- xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 3\_WeaponProperties.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Damage is controlled by the “nLevel” stat in the “Stim\_ImpactDamage” section. Be careful to find the right section because there are lots of other “nLevel” stats.

```xml {8}
<object type="Stim_ImpactDamage">
        <value name="hidEventName" type="String">Stims</value>
        <value name="eventMask" type="UInt32">2</value>
        <value name="hidTargetEntityId" type="UInt64">18446744073709551615</value>
        <value hash="FC25E1F1" type="String">BulletImpact</value>
        <value name="sDetail" type="Hash">AB3FB98A</value>
        <value name="selType" type="UInt32">3</value>
        <value name="nLevel" type="UInt32">26</value>
        <value name="hidShowType" type="BinHex">01</value>
        <value name="hidShowRadius" type="BinHex">00</value>
        <value name="fPhysImpulse" type="Float">70</value>
```

## Explosives

**Decoding required**  
Base game explosives \- xx\_Weapons.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC explosive \- 1\_DLC1Weapons.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

There are two main explosion types that we might have some use in editing, regular explosions and fire explosions. Explosives in Far Cry 2 use different combinations of these, grenades use only regular explosions, molotovs use only fire explosions and all other explosives use both.

This is a full list of entry titles for the explosives:

Grenade \- Grenades.M67  
Molotov \- Grenades.Molotov

IED (Mine) \- Explosives.IED\_Base.IED\_Mine  
IED (Mortar shell) \- Explosives.IED\_Base.IED\_MortarShell  
IED (Pipe bomb) \- Explosives.IED\_Base.IED\_PipeBomb

M79 grenade \- Grenades.M79\_Grenade  
MGL140 grenade \- Grenades.MGL140\_Grenade  
RPG rocket \- Rockets.RPG7Rocket  
Carl G rocket \- Rockets.CarlGustafRocket  
Crossbow bolt \- DLC1.Arrow  
Mortar shell \- Explosives.MortarShell

All of these entries have an “ExplodeStims” section where we can edit the explosive properties. In this section there may be two or three subsections under the titles “Stim”. Each “Stim” section is a different element of the explosion. We can tell the different explosive elements apart with the “selType” value, where “4” is a regular explosion and “7” is a fire explosion.

The main stats we can change here are: “nLevel”, “fRadius” and “fPhysImpulse”.

“nLevel” controls the damage an explosion does. This doesn’t affect fire explosions as fire still does the same damage.

“fRadius” controls the size of an explosion.

“fPhysImpulse” controls the physics power of an explosion, so how much it will push all the objects around it. Fire explosions don’t have this stat.

This is an example regular explosion:

```xml {8,9,10,15}
<object type="Stim">
      <value name="selStimType" type="UInt32">2</value>
      <value name="hidEventName" type="String">Stims</value>
      <value name="eventMask" type="UInt32">2</value>
      <value name="hidTargetEntityId" type="UInt64">18446744073709551615</value>
      <value hash="FC25E1F1" type="String"></value>
      <value name="sDetail" type="Hash">FFFFFFFF</value>
      <value name="selType" type="UInt32">4</value>
      <value name="nLevel" type="UInt32">30</value>
      <value name="fRadius" type="Float">7</value>
      <value name="bFalloff" type="Bool">True</value>
      <value name="nFalloffMinLevel" type="UInt32">24</value>
      <value name="hidShowType" type="Bool">True</value> <!-- type="BinHex" value="01" -->
      <value name="hidShowRadius" type="Bool">True</value> <!-- type="BinHex" value="01" -->
      <value name="fPhysImpulse" type="Float">100</value>
```

This is an example fire explosion:

```xml {8,10}
<object type="Stim">
      <value name="selStimType" type="UInt32">0</value>
      <value name="hidEventName" type="String">Stims</value>
      <value name="eventMask" type="UInt32">2</value>
      <value name="hidTargetEntityId" type="UInt64">18446744073709551615</value>
      <value hash="FC25E1F1" type="String"></value>
      <value name="sDetail" type="Hash">FFFFFFFF</value>
      <value name="selType" type="UInt32">7</value>
      <value name="nLevel" type="UInt32">20</value>
      <value name="fRadius" type="Float">1</value>
      <value name="bFalloff" type="Bool">True</value>
      <value name="nFalloffMinLevel" type="UInt32">8</value>
      <value name="hidShowType" type="Bool">True</value> <!-- type="BinHex" value="01" -->
      <value name="hidShowRadius" type="Bool">True</value> <!-- type="BinHex" value="01" -->
```

## Fire Mode

There are two different stats that you can use to edit fire mode, one that controls overall full auto/single shot or “prepare shot” and another that controls how many shots a full auto weapon fires in a burst.

### Overall fire mode

Base game weapons \- xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 3\_WeaponProperties.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Overall fire mode is controlled by the “selFireRateMode” stat in the “FireRate” section.

These are the different values available: 0 \= Single shot, 1 \= Full auto, 2 \= Prepare shot. Prepare shot is used for weapons that have an animation play before they can fire again, like the M1903 sniper and Ithaca shotgun.

```xml {4}
<object type="FireRate">
       <value name="fBusyDuration" type="Float">0</value>
       <value name="iFireRate" type="Float">120</value>
       <value name="selFireRateMode" type="UInt32">1</value>
```

### Full auto fire modes

Base game weapons \- xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 3\_WeaponProperties.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Full auto fire modes are controlled through the “iBurstLength” stat in the “FireStrategyProperties” section.

These are some examples of different values: 0 \= Full auto, 1 \= Single shot, 3 \= Burst fire.

```xml {4}
 <object type="FireStrategyProperties">
      <value name="bUseAngleSpread" type="Bool">False</value>
      <value name="iBulletsShot" type="UInt32">1</value>
      <value name="iBurstLength" type="UInt32">3</value>
```

## Fire Rate

Base game weapons \- xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 3\_WeaponProperties.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Fire rate is controlled by the “iFireRate” stat in the “FireRate”section and is measured in rounds-per-minute.

```xml {3}
<object type="FireRate">
       <value name="fBusyDuration" type="Float">0</value>
       <value name="iFireRate" type="Float">600</value>
       <value name="selFireRateMode" type="UInt32">1</value>
```

## 

## Iron Sights

### Movement speed

**Decoding required**  
Base game weapons \- xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 3\_WeaponProperties.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Iron sight movement speed is controlled by the “fMoveSpeedFactor” stat in the “Ironsight” section. 

This works as a proportional modifier, so any value lower than 1 will decrease movement speed when aiming and a value of 0.5 will decrease movement speed by 50%.

```xml {2}
<object hash="BB04E184" type="IronSight">
       <value name="fMoveSpeedFactor" hash="7D725133" type="Float">0.5</value> <!-- type="BinHex" value="0000003F" -->
       <value name="bCanIronsight" hash="E49EEB82" type="Bool">True</value> <!-- type="BinHex" value="01" -->
       <value name="fLookSensitivityFactor" hash="DD39539B" type="Float">0.5</value> <!-- type="BinHex" value="0000003F" -->
       <value name="fIronsightFOV" hash="FB4ADD00" type="Float">1.1</value> <!-- type="BinHex" value="CDCC8C3F" →
```

### Zoom/FOV

**Decoding required**  
Base game weapons \- xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 3\_WeaponProperties.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Iron sight zoom/FOV is controlled by the “fIronsightFOV” stat in the “Ironsight” section.

Only change this for weapons without sights, as otherwise you’ll cause some bugs. 

A value of 1.309 means that the weapon won’t zoom when aiming. Decrease this value to increase zoom/FOV.

```xml {5}
<object hash="BB04E184" type="IronSight">
       <value name="fMoveSpeedFactor" hash="7D725133" type="Float">0.5</value> <!-- type="BinHex" value="0000003F" -->
       <value name="bCanIronsight" hash="E49EEB82" type="Bool">True</value> <!-- type="BinHex" value="01" -->
       <value name="fLookSensitivityFactor" hash="DD39539B" type="Float">0.5</value> <!-- type="BinHex" value="0000003F" -->
       <value name="fIronsightFOV" hash="FB4ADD00" type="Float">1.1</value> <!-- type="BinHex" value="CDCC8C3F" →
```

### Look sensitivity

**Decoding required**  
Base game weapons \- xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 3\_WeaponProperties.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Iron sight look sensitivity is controlled by the “fLookSensitivityFactor” stat in the “Ironsight” section.

This works as a proportional modifier, so any value lower than 1 will decrease look sensitivity when aiming and a value of 0.5 will decrease look sensitivity by 50%.

```xml {4}
<object hash="BB04E184" type="IronSight">
       <value name="fMoveSpeedFactor" hash="7D725133" type="Float">0.5</value> <!-- type="BinHex" value="0000003F" -->
       <value name="bCanIronsight" hash="E49EEB82" type="Bool">True</value> <!-- type="BinHex" value="01" -->
       <value name="fLookSensitivityFactor" hash="DD39539B" type="Float">0.5</value> <!-- type="BinHex" value="0000003F" -->
       <value name="fIronsightFOV" hash="FB4ADD00" type="Float">1.1</value> <!-- type="BinHex" value="CDCC8C3F" →
```

### Enable/disable

**Decoding required**  
Base game weapons \- xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 3\_WeaponProperties.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Enabling/disabling iron sights is controlled by the “bCanIronsight” stat in the “Ironsight”section.

This works by changing the value to either “True” or “False”.

I used this to enable an ironsight mode for the machetes to simulate creeping, I don’t see it being useful for anything else.

```xml {3}
<object hash="BB04E184" type="IronSight">
       <value name="fMoveSpeedFactor" hash="7D725133" type="Float">0.5</value> <!-- type="BinHex" value="0000003F" -->
       <value name="bCanIronsight" hash="E49EEB82" type="Bool">True</value> <!-- type="BinHex" value="01" -->
       <value name="fLookSensitivityFactor" hash="DD39539B" type="Float">0.5</value> <!-- type="BinHex" value="0000003F" -->
       <value name="fIronsightFOV" hash="FB4ADD00" type="Float">1.1</value> <!-- type="BinHex" value="CDCC8C3F" →
```

## Magazine Size

**Decoding required**  
Base game weapons \- xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 3\_WeaponProperties.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Magazine size is controlled by the “iAmmoInClip” stat in the “Ammo” section.

```xml {4}
<object hash="4FBDD114" type="Ammo">
      <value hash="AB258E09" type="BinHex">73686F7467756E00</value>
      <value name="ammoAmmoType" hash="5957C8C7" type="Hash">EEAE53E1</value> <!-- type="BinHex" value="E153AEEE" -->
      <value name="iAmmoInClip" hash="88596C97" type="Int32">9</value> <!-- type="BinHex" value="09000000" -->
      <value name="iMaxAmmoCasual" hash="2A0F1CC2" type="Int32">63</value> <!-- type="BinHex" value="3F000000" -->
      <value name="iMaxAmmoExperimented" hash="C7DA96EA" type="Int32">36</value> <!-- type="BinHex" value="24000000" -->
      <value name="iMaxAmmoHardcore" hash="EF3C58C3" type="Int32">36</value> <!-- type="BinHex" value="24000000" -->
      <value name="iMaxAmmoInfamous" hash="DE33B3EC" type="Int32">27</value> <!-- type="BinHex" value="1B000000" -->
      <value name="bUsesClips" hash="B72CF1A1" type="Bool">True</value> <!-- type="BinHex" value="01" -->
      <value name="bIsAmmoVisible" hash="6A9D69B4" type="Bool">False</value> <!-- type="BinHex" value="00" -->
</object>
```

## Max Ammo

**Decoding required**  
Base game weapons \- xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 3\_WeaponProperties.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Max ammo is controlled by the “iMaxAmmoCasual”, “iMaxAmmoExperimented”, “iMaxAmmoHardcore” and “iMaxAmmoInfamous” stats in the “Ammo” section. As you can see there are separate stats for the different difficulty levels.

```xml {5,6,7,8}
<object hash="4FBDD114" type="Ammo">
       <value hash="AB258E09" type="BinHex">73686F7467756E00</value>
       <value name="ammoAmmoType" hash="5957C8C7" type="Hash">EEAE53E1</value> <!-- type="BinHex" value="E153AEEE" -->
       <value name="iAmmoInClip" hash="88596C97" type="Int32">9</value> <!-- type="BinHex" value="09000000" -->
       <value name="iMaxAmmoCasual" hash="2A0F1CC2" type="Int32">63</value> <!-- type="BinHex" value="3F000000" -->
       <value name="iMaxAmmoExperimented" hash="C7DA96EA" type="Int32">36</value> <!-- type="BinHex" value="24000000" -->
       <value name="iMaxAmmoHardcore" hash="EF3C58C3" type="Int32">36</value> <!-- type="BinHex" value="24000000" -->
       <value name="iMaxAmmoInfamous" hash="DE33B3EC" type="Int32">27</value> <!-- type="BinHex" value="1B000000" -->
       <value name="bUsesClips" hash="B72CF1A1" type="Bool">True</value> <!-- type="BinHex" value="01" -->
       <value name="bIsAmmoVisible" hash="6A9D69B4" type="Bool">False</value> <!-- type="BinHex" value="00" -->
</object>
```

## Projectiles \- Rockets and Explosive Bolts

This includes RPG rockets, Carl G rockets and Crossbow bolts. The projectiles have their own entries separate from their respective weapons, these are their titles:

RPG rocket \- Rockets.RPG7Rocket  
Carl G rocket \- Rockets.CarlGustafRocket  
Crossbow bolt \- DLC1.Arrow

### Speed

Base game projectiles \- xx\_Weapons.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC projectile \- 1\_DLC1Weapons.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Projectile speed for the RPG rocket and crossbow bolt is controlled by the stat “fSpeed” in the “Fire” section.

```xml {2}
<object type="Fire">
       <value name="fSpeed" type="Float">50</value>
       <value name="fGravity" type="Float">-0.55</value>
       <value hash="3BB1654D" type="BinHex">776561706F6E732E776561706F6E732E7270675F726F636B65745F656A6563745F737461727400</value>
       <value name="psStartPS" type="Hash">7D5C57B7</value>
```

Projectile speed for the Carl G rocket is controlled by the “fImpulse” stat in the “Ignite” section.

```xml {2}
<object type="Ignite">
              <value name="fImpulse" type="Float">75</value>
              <value name="fTime" type="Float">0</value>
              <value hash="3BB1654D" type="BinHex">00</value>
```

### Gravity

Base game projectiles \- xx\_Weapons.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC projectile \- 1\_DLC1Weapons.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Projectile gravity is controlled by the “fTime” and “fGravity” stats in the “Fall” section. 

“fTime” controls the time until the projectile starts to drop.

“fGravity” controls the speed with which the projectile drops once that time has run out.

Note that with time being a factor in when the projectile starts to fall, your projectile speed will also influence how far away the projectile is when it starts to drop.

```xml {2,3}
<object type="Fall">
       <value name="fTime" type="Float">7</value>
       <value name="fGravity" type="Float">-9.8</value>
```

## Projectiles \- Grenades

### Speed

xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Some of the grenade launchers share the same projectile type, so if you want to edit the projectile speed of individual weapons we are going to edit the weapon entries instead. These are:

M79 \- Secondary.M79, Secondary.M79.Mikes\_Rusty  
MGL-140 \- Primary.MGL140, Primary.MGL140.Persistent  
MK19 \- MountedWeapons.MK19\_Mounted

The speed/power with which the grenades are fired is controlled by the “fInitialImpulse” stat within the “FireStrategyProperties” section.

```xml {3}
<value name="matimpSecondaryBulletImpactFx" type="Hash">FFFFFFFF</value>
<value name="archProjectileArchetype" type="String">weapons.Grenades.M79_Grenade.Multi</value>
<value name="fInitialImpulse" type="Float">600</value>
<value name="fMalfunctionImpulse" type="Float">100</value>
<value name="fMalfunctionDetonateAfterHit" type="Float">1</value>
```

## Range

### Regular effective range/max range

Base game weapons \- xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 3\_WeaponProperties.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Max range is controlled by the “fRange” stat, beyond this you won’t do any damage.

Effective range is controlled by the “x” and “y” stats in the “vectorEffectiveRange” and “vectorEffectiveRangeIS” sections. There are four numbers here but they should run into each other, so the second and third numbers should be the same. With this you can visualise these as the three numbers, as in the example below it’s 45\>60\>75. I don’t know exactly how these numbers impact the damage drop off so you’ll probably need to do some trial and error to get the results you want.

```xml {1,2,3,4,5,6,7,8}
<value name="fRange" type="Float">300</value>
            <value name="vectorEffectiveRange" type="Vector2">
              <x>45</x>
              <y>60</y>
            </value>
            <value name="vectorEffectiveRangeIS" type="Vector2">
              <x>60</x>
              <y>75</y>
            </value>
```

### Flamethrower range

xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Flamethrower range is controlled by the “fSize” stat in the “FlameMesh” section. 

I also suggest increasing the speed of the flame to compensate for the increased range. Flamethrower flame speed is controlled by the “fSpeed” stat in the “FlameMesh” section.

```xml {2,17}
<object type="FlameMesh">
       <value name="fSize" type="Float">10</value>
       <value name="fSplineTension" type="Float">0</value>
       <value name="fSplineContinuity" type="Float">0</value>
       <value name="fSplineBias" type="Float">0</value>
       <value name="fPSSpawnTime" type="Float">0.03</value>
       <value name="archSpawnTimeAngularSpeedRatioCurve" type="String">Curves.ShootingSystem.FlameThrowerEmissionVSAngularSpeed</value>
       <value name="fSegmentLength" type="Float">0.8</value>
       <value name="fRestitutionInterpolationDist" type="Float">2</value>
       <value name="fSizeGrowInterpolationDist" type="Float">10</value>
       <value name="fSizeShrinkInterpolationDist" type="Float">10</value>
       <value name="fGravityScalePlayerPitch" type="Float">5</value>
       <value name="fGravityInterpolationDist" type="Float">10</value>
       <value name="iRingNVertex" type="Float">1.401298E-44</value>
       <value name="fRingStartAngle" type="Float">0</value>
       <value name="fTeselation" type="Float">0.1</value>
       <value name="fSpeed" type="Float">15</value>
```

## Recoil

### Overall Recoil

**Decoding required**  
Base game weapons \- xx\_Weapons.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 1\_DLC1Weapons.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Recoil is controlled by the “fHorizontalRecoilPerShot” and “fVerticalRecoilPerShot” stats in the “ReliabilityLevelsData”. There are sections for the different reliability levels so the weapon can have increased recoil as it degrades.

```xml {3,4,9,10,15,16,21,22}
<object type="ReliabilityLevelsData">
      <object type="Failure">
         <value name="fHorizontalRecoilPerShot" type="Float">0.46</value>
         <value name="fVerticalRecoilPerShot" type="Float">1.7</value>
         <value name="fBulletDeviationMax" type="Float">0</value>
         <value name="fJamProbabilityPerReload" type="Float">0.08</value>
      </object>
      <object type="Low">
         <value name="fHorizontalRecoilPerShot" type="Float">0.44</value>
         <value name="fVerticalRecoilPerShot" type="Float">1.5</value>
         <value name="fBulletDeviationMax" type="Float">0</value>
         <value name="fJamProbabilityPerReload" type="Float">0.04</value>
      </object>
      <object type="Medium">
         <value name="fHorizontalRecoilPerShot" type="Float">0.42</value>
         <value name="fVerticalRecoilPerShot" type="Float">1.3</value>
         <value name="fBulletDeviationMax" type="Float">0</value>
         <value name="fJamProbabilityPerReload" type="Float">0.02</value>
      </object>
      <object type="High">
         <value name="fHorizontalRecoilPerShot" type="Float">0.4</value>
         <value name="fVerticalRecoilPerShot" type="Float">1.1</value>
         <value name="fBulletDeviationMax" type="Float">0</value>
         <value name="fJamProbabilityPerReload" type="Float">0</value>
      </object>
```

### Recoil recovery

**Decoding required**  
Base game weapons \- xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 3\_WeaponProperties.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Revoil recovery is controlled by the “iRecoilRecoveryLevel” stat in the “Recoil” section.

Every weapon apart from the PKM has a value of 1 for this, the PKM has a value of 2\. I recommend reducing this to 1\. There are other values to tweak in this section if you want to further mess with the recoil.

```xml {2}
<object hash="3B75D90F" type="Recoil">
       <value name="iRecoilRecoveryLevel" hash="158EFFD8" type="Int32">2</value> <!-- type="BinHex" value="02000000" -->
       <value name="fRecoilAchieveTime" hash="C0671E89" type="Float">0.08</value> <!-- type="BinHex" value="0AD7A33D" -->
       <value name="fRecoilAnimationWeight" hash="641C31C5" type="Float">1.0</value> <!-- type="BinHex" value="0000803F" -->
       <value name="fRecoilMax" hash="0788D065" type="Float">45.0</value> <!-- type="BinHex" value="00003442" -->
```

## Reliability

### Overall reliability

Base game weapons \- xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 3\_WeaponProperties.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Overall weapon reliability is controlled by the “iClipsForSelfDestruct” stat, measured in how many magazines you can fire before the weapon breaks.

```xml {1}
<value name="iClipsForSelfDestruct" type="UInt32">6</value>
```

### Likelihood to jam

**Decoding required**  
Base game weapons \- xx\_Weapons.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 1\_DLC1Weapons.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

A weapon’s likelihood to jam is controlled by the “fJamProbabilityPerReload” stat in the “ReliabilityLevelsData” section. You can set separate likelihoods for the different weapon degradation levels and this is a percentage chance out of 100 per time you reload the weapon.

```xml {6,12,18,24}
<object type="ReliabilityLevelsData">
      <object type="Failure">
         <value name="fHorizontalRecoilPerShot" type="Float">0.45</value>
         <value name="fVerticalRecoilPerShot" type="Float">9.5</value>
         <value name="fBulletDeviationMax" type="Float">0</value>
         <value name="fJamProbabilityPerReload" type="Float">0.12</value>
      </object>
      <object type="Low">
         <value name="fHorizontalRecoilPerShot" type="Float">0.4</value>
         <value name="fVerticalRecoilPerShot" type="Float">9</value>
         <value name="fBulletDeviationMax" type="Float">0</value>
         <value name="fJamProbabilityPerReload" type="Float">0.06</value>
      </object>
      <object type="Medium">
         <value name="fHorizontalRecoilPerShot" type="Float">0.35</value>
         <value name="fVerticalRecoilPerShot" type="Float">8.5</value>
         <value name="fBulletDeviationMax" type="Float">0</value>
         <value name="fJamProbabilityPerReload" type="Float">0.03</value>
      </object>
      <object type="High">
         <value name="fHorizontalRecoilPerShot" type="Float">0.3</value>
         <value name="fVerticalRecoilPerShot" type="Float">8</value>
         <value name="fBulletDeviationMax" type="Float">0</value>
         <value name="fJamProbabilityPerReload" type="Float">0</value>
      </object>
```

## Shotguns \- Number of pellets fired

Base game weapons \- xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 3\_WeaponProperties.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

The number of pellets fired by shotgun is controlled by the “iBulletsShot” stat in the “FireStrategyProperties” section.

The default value is 7, the only difference is the sawed-off shotgun having a default of 14 because it fires both barrels at once.

```xml {3}
<object type="FireStrategyProperties">
       <value name="bUseAngleSpread" type="Bool">True</value>
       <value name="iBulletsShot" type="UInt32">7</value>
       <value name="iBurstLength" type="UInt32">0</value>
```

## 

## Weapon Slot

### Regular slots

Base game weapons \- xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 3\_WeaponProperties.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Weapon slot is controlled by the “selCategory” stat in the “CommonProperties” section.

The value represents different weapon slots: 0 \= Machete, 1 \= Primary, 2 \= Secondary, 3 \= Special.

```xml {3}
<value name="bSingleHitHealthFailure" type="Bool">False</value>
<value name="fHealthFailureChanceModifier" type="Float">1</value>
<value name="selCategory" type="UInt32">1</value>
<value hash="E0FF29E0" type="String"></value>
```

### Extra/gadget slot

Base game weapons \- xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 3\_WeaponProperties.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

It is possible to add a weapon to an extra slot, sometimes referred to as the gadget slot. This is accessible by pressing the machete button twice. It is only possible to add one weapon to this slot, any more than that and it won’t work properly.

To assign a weapon to the extra slot you need to delete the whole line with the “selCategory” stat in the “CommonProperties” section.

```xml {3}
<value name="bSingleHitHealthFailure" type="Bool">False</value>
<value name="fHealthFailureChanceModifier" type="Float">1</value>
<value name="selCategory" type="UInt32">1</value>   < Delete this whole line
<value hash="E0FF29E0" type="String"></value>
```

It should look like this:

```xml
<value name="bSingleHitHealthFailure" type="Bool">False</value>
<value name="fHealthFailureChanceModifier" type="Float">1</value>
<value hash="E0FF29E0" type="String"></value>
```

## Guide \- Weapon Inspecting

Inputactionmapcommon.xml (\\patch\_unpack\\config\\)

To add weapon inspecting we’re going to create a new key binding that links to the “cyclebreaker” signal which causes the weapon idle animations.

Add your extra key bindings to the section under the title \- ActionMap name="common\_weapons"

You can customise the actual key binding in the “binding input” section and you can choose if you need to press or hold the relative button in the “action” section. My suggested controls for keyboard and gamepad are shown below.

Add this line to the keyboard section:

```xml {1}
<Binding input="kb:i" action="press" signal="cyclebreaker"/>
```

Add this line to the gamepad section:

```xml {1}
<Binding input="pad:x" action="hold" signal="cyclebreaker"/>
```

## Guide \- Weapon Holstering

There are two known ways of implementing weapon holstering. 

The first option is to disable auto draw and add a key binding linked to the built in holster method that would normally trigger when entering buildings. This is the way Far Cry 2: Redux does holstering. Doing this means that whenever your characters put their weapon away they won’t automatically get it back out. It is an extensive change as while your character won’t automatically draw their weapon after leaving a building or exiting a vehicle, they also won’t do so after throwing a grenade. This is a much simpler way of implementing holstering but has the drawbacks of requiring a new game to take effect and  your character maybe putting their weapon away when you may not want them to.

The second option is to create a new holstered state, add a key binding linked to the new state and then edit some weapon animations to fix visual bugs. This is the way I have done holstering with Far Cry 2: Vanilla+. Doing this means that regular gameplay is untouched and holstering can be triggered only when the player wants. This method does not require a new game to take effect but is much more complicated to set up. You must also unholster your weapons before entering buildings, so to enter a building while holstered you need to press the use button twice.

### Option 1 \- Disabling Auto Draw

Step 1: Disabling Auto Draw  
xx\_player.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated)

Each playable character has their own section of this file, these are the entry titles:

MainCharacter.PawnPlayer.Andre\_Hyppolite  
MainCharacter.PawnPlayer.Frank\_Bilders  
MainCharacter.PawnPlayer.Hakim\_Echebbi  
MainCharacter.PawnPlayer.Josip\_Idromeno  
MainCharacter.PawnPlayer.Marty\_Alencar  
MainCharacter.PawnPlayer.Paul\_Ferenc  
MainCharacter.PawnPlayer.Quarbani\_Singh  
MainCharacter.PawnPlayer.Warren\_Clyde  
MainCharacter.PawnPlayer.Xianyong\_Bai

Auto draw is controlled by the “bAutoDraw” stat in the “Inventory” section. Enable or disable it by changing the value to “True” or “False”.

```xml {7}
<object type="Inventory">
       <value hash="8C965C28" type="String">player</value>
       <value name="packInventoryPack" type="Hash">98197A65</value>
       <value name="archGPSVehicleArchetype" type="String">gadgets.Equipped.Compass_Vehicle</value
       <value name="bUnlimitedAmmo" type="Bool">False</value>
       <value name="bAutoReload" type="Bool">True</value>
       <value name="bAutoDraw" type="Bool">True</value>
       <value hash="130CDED8" type="String">hand_hand</value>
       <value name="sInitialWeaponCategory" type="Hash">E97A284A</value>
</object>
```

(Optional) Step 2: Adding auto-draw to particular actions  
It’s possible to add auto-draw to individual actions. This involves rerouting the animations so that rather than an action animation flowing straight into the idle animation it instead flows into the weapon draw animation and then the idle animation.

In practice this means replacing “Pawn Weapons/External States/Main Avatar/Common/xIdle” with “Pawn Weapons/Weapon Mechanics/States/Drawing” as the “Connection Target” within each animation. There can be variations on the Idle animation title depending on what file you’re in, but it will always contain “External States/Main Avatar/Common/xIdle”.

For example, the grenade throwing animation is below, with the full title “\<State FullName="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Throwing grenade" Type="CGOStateEquipment"\>".

```xml
<State FullName="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Throwing grenade" Type="CGOStateEquipment">
	<Parameter Name="groups" />
	<Parameter Name="duration" Value="0" />
	<Parameter Name="signalpriorities" />
	<Parameter Name="forceAnim" Value="0" />
	<Parameter Name="syncAnimDuration" Value="0" />
	<Parameter Name="animStateID" Value="0" />
	<Parameter Name="layerStateID" Value="Pawn_Generic_Throw_Layered" />
	<Parameter Name="gestureStateID" Value="0" />
	<Parameter Name="followTerrain" Value="0" />
	<Parameter Name="MoveLayer" Value="-1" />
	<Parameter Name="autosetDuration" Value="0" />
	<Parameter Name="syncWith" Value="0" />
	<Connection Target="::Pawn Weapons/External States/Main Avatar/Common/xIdle" />
	<Event Name="Throw" Type="CGOStateEventPawn" Start="25" End="25">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="1" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="11" />
		<Parameter Name="simpleEventID" Value="" />
	</Event>
	<Sink Name="abort" Start="0" End="100">
		<Connection Target="::Pawn Weapons/External States/Main Avatar/Common/xIdle" Signal="abort" />
	</Sink>
</State>
```

We are going to replace “Pawn Weapons/External States/Main Avatar/Common/xIdle” with “Pawn Weapons/Weapon Mechanics/States/Drawing” so that it looks like this:

```xml {14}
<State FullName="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Throwing grenade" Type="CGOStateEquipment">
	<Parameter Name="groups" />
	<Parameter Name="duration" Value="0" />
	<Parameter Name="signalpriorities" />
	<Parameter Name="forceAnim" Value="0" />
	<Parameter Name="syncAnimDuration" Value="0" />
	<Parameter Name="animStateID" Value="0" />
	<Parameter Name="layerStateID" Value="Pawn_Generic_Throw_Layered" />
	<Parameter Name="gestureStateID" Value="0" />
	<Parameter Name="followTerrain" Value="0" />
	<Parameter Name="MoveLayer" Value="-1" />
	<Parameter Name="autosetDuration" Value="0" />
	<Parameter Name="syncWith" Value="0" />
	<Connection Target="::Pawn Weapons/Weapon Mechanics/States/Drawing" />
	<Event Name="Throw" Type="CGOStateEventPawn" Start="25" End="25">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="1" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="11" />
		<Parameter Name="simpleEventID" Value="" />
	</Event>
	<Sink Name="abort" Start="0" End="100">
		<Connection Target="::Pawn Weapons/External States/Main Avatar/Common/xIdle" Signal="abort" />
	</Sink>
</State>
```

The weapon drawing animation as default flows into the idle animation so this is now complete for throwing grenades.

Below I will list the different animations that I have found that can have auto draw added to them. These will be shown with their titles and the animation file that contains them.

Weapons.gosm.xml (\\patch\_unpack\\scripts\\engine\\objects\\pawn\\statemachine\\)

Throwing grenades  
```xml
<State FullName="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Throwing grenade" Type="CGOStateEquipment">
```

Leaving a mounted weapon (This has a weird bug where you will be changed to the next weapon when leaving the weapon \- not recommended)  
```xml
<State FullName="::Pawn Weapons/Weapon Mechanics/States/Mounted weapons/GetOutMountedWeapon" Type="CGOStateAnim">
```

Healing.gosm.xml (\\patch\_unpack\\scripts\\game\\objects\\pawn\\statemachine\\)

Healing (with syrettes)  
```xml
<State FullName="::Healing/Healing/States/UseSyringe" Type="CGOStateAnim">
```

Healing (emergency healing)  
```xml
<State FullName="::Healing/Healing/States/Healing" Type="CGOStateAnim">
```

Waking up after collapsing in the desert  
```xml
<State FullName="::Healing/Desert/CollapseWakeUp" Type="CGOStateAnim">
```

Vehicles.gosm.xml (\\patch\_unpack\\scripts\\engine\\objects\\pawn\\statemachine\\)

Exiting vehicles  
```xml
<State FullName="::Vehicles/Vehicles/States/ExitingVehicle" Type="CGOStateExitVehicle">
```

Exiting vehicles in motion  
```xml
<State FullName="::Vehicles/Vehicles/States/JumpOutOfVehicle" Type="CGOStateExitVehicle">
```

Exiting a hang glider in the air  
```xml
<State FullName="::Vehicles/Vehicles/States/ParagliderFallout" Type="CGOStateAnim">
```

Repairing vehicles  
```xml
<State FullName="::Vehicles/Vehicles/States/RepairToIdle" Type="CGOStateAnim">
```

Interactions.gosm.xml (\\patch\_unpack\\scripts\\game\\objects\\pawn\\statemachine\\)

Exiting buildings  
```xml
<State FullName="::Interactions/Door/ExitWeaponSafeMode" Type="CGOStateAnim">
```

Collecting diamonds  
```xml
<State FullName="::Interactions/Diamonds/BriefcaseLookDiamond" Type="CGOStateAnim">
```

Collecting syrettes  
```xml
<State FullName="::Interactions/MedicStation/States/PickSyringeGrab" Type="CGOStateAnim">
```

Exiting ladders (top)  
```xml
<State FullName="::Interactions/Ladder/States/GetOutLadderTop" Type="CGOStateLadderTransition">
```

Exiting ladders (bottom)  
```xml
<State FullName="::Interactions/Ladder/States/GetOutLadderBottom" Type="CGOStateLadderTransition">
```

Climbing out of water  
```xml
<State FullName="::Interactions/GetOutWater/States/JumpOutWater" Type="CGOStateAnim">
```

(Optional) Step 3 \- Adding a key to trigger unholstering  
Weapons.gosm.xml (\\patch\_unpack\\scripts\\engine\\objects\\pawn\\statemachine\\)

To add a key that will trigger unholstering, first find the section with the title “\<Group FullName="::Pawn Weapons/Weapon Mechanics/States/AllowWeaponSwitch" Type="BaseGroup"\>”.

Within that section is a subsection that looks like this:

```xml
<Event Name="Try draw" Type="CGOStateEventInventory" Start="0" End="100" Signal="drawweapon">
	<Parameter Name="alwaysTrigger" Value="0" />
	<Parameter Name="triggerOnce" Value="0" />
	<Parameter Name="triggeredOnEnd" Value="0" />
	<Parameter Name="triggeredOnBegin" Value="0" />
	<Parameter Name="requestType" Value="5" />
	<Parameter Name="simpleEventID" Value="" />
</Event>
```

Copy and paste this section directly below the existing one. You can now edit the “signal” value to any of the signal values found in “inputactionmapcommon.xml” (\\patch\_unpack\\config\\).

For example, if you wanted to make pressing the fire button unholster your weapon, you would add the “startshooting” signal, so your section would look like this:

```xml {1}
<Event Name="Try draw" Type="CGOStateEventInventory" Start="0" End="100" Signal="startshooting">
	<Parameter Name="alwaysTrigger" Value="0" />
	<Parameter Name="triggerOnce" Value="0" />
	<Parameter Name="triggeredOnEnd" Value="0" />
	<Parameter Name="triggeredOnBegin" Value="0" />
	<Parameter Name="requestType" Value="5" />
	<Parameter Name="simpleEventID" Value="" />
</Event>
```

Step 4: Adding the key binding  
inputactionmapcommon.xml (\\patch\_unpack\\config\\)

To add weapon holstering we’re going to create a new key binding that links to the “holsterweapon” signal which normally holsters weapons when entering buildings etc.

Add your extra key bindings to the section under the title \- ActionMap name="common\_weapons"

You can customise the actual key binding in the “binding input” section and you can choose if you need to press or hold the relative button in the “action” section. My suggested controls for keyboard and gamepad are shown below.

Add this line to the keyboard section:

```xml {1}
<Binding input="kb:x" action="press" signal="holsterweapon"/>
```

Add this line to the gamepad section:

```xml {1}
<Binding input="pad:y" action="hold" signal="holsterweapon"/>
```

(Optional) Step 5: Making the new key binding rebindable

Adding the key binding to the default controls list  
defaultusercontrols.xml (\\patch\_unpack\\config\\)

To add the new key binding to the “Actions” in-game controls menu add the following line to the “CATEGORY\_ACTIONS” section:

```xml
<Control name="inspect" key1="kb:i" actionmap="common_inspect_remap" group="1" conflictmask="12"/>
```

The completed section should look like this:

```xml {5}
<Category name="CATEGORY_ACTIONS">
<Control name="fire" key1="mouse:lb" actionmap="common_shoot_remap" group="-1" conflictmask="-1"/>
<Control name="ironsight" key1="mouse:rb" actionmap="common_iron_remap" group="-1" conflictmask="-1"/>
<Control name="reload" key1="kb:r" actionmap="common_reload_remap" group="1" conflictmask="12"/>
<Control name="inspect" key1="kb:i" actionmap="common_inspect_remap" group="1" conflictmask="12"/>
<Control name="sprint" key1="kb:lshift" actionmap="common_move_remap" group="1" conflictmask="12"/>
        	<Control name="jump" key1="kb:space" actionmap="common_jump_remap" group="1" conflictmask="12"/>
        	<Control name="crouch" key1="kb:c" actionmap="common_crouch_remap" group="1" conflictmask="12"/>
     	<Control name="interact" key1="kb:e" actionmap="common_use_remap" group="1" conflictmask="12"/>		
</Category>
```

Link the changes to the default controls to the controls system  
Inputactionmapcommon.xml (\\patch\_unpack\\config\\)

To link our changes to the controls system add the following line below the “common\_weapons” title:

```xml
<Import actionmap="common_inspect_remap" optional=""/>
```

The completed section should look like this:

```xml {6}
<ActionMap name="common_weapons">
<Import actionmap="common_weapons_remap" optional=""/>
<Import actionmap="common_shoot_remap" optional=""/>
<Import actionmap="common_iron_remap" optional=""/>	
<Import actionmap="common_reload_remap" optional=""/>
<Import actionmap="common_inspect_remap" optional=""/>
```

Add a new control label  
\\patch\_pack\\languages\\ \- Each language has its own folder and “oasisstrings.rml” file.

Find the section with the title “\<section name="Actions"\>”.

Add the following line into this section, with the correct word for your language in the “value” section:

```xml
<string enum="inspect" value="Inspect" />
```

### 

### Option 2 \- New holstered state

Step 1: Linking the holstered state to the ‘Idle’ state  
main\_avater.gosm.xml (\\patch\_unpack\\scripts\\engine\\objects\\pawn\\statemachine\\)

Find the section of this file with the following title: \<State FullName="::Main Avatar/Common/Idle" Type="CGOStateAnim"\>

This is the idle state, so when not in a building or vehicle, just walking around like usual. We want holstering to be triggered from this state so we’re going to add that to it.

1. Find the section with the title “Main Avatar/Common/Idle" and add the following lines to the bottom of it:

```xml
<Sink Name="HolsterWeapons" Start="0" End="100">
		<Connection Target="::Pawn Weapons/Weapon Mechanics/States/HolsterWeapons" Signal="HolsterWeapons" />
</Sink>
```

Your completed section should look like this:

```xml {21,22,23}
<State FullName="::Main Avatar/Common/Idle" Type="CGOStateAnim">
<Parameter Name="groups">
		<Parameter Name="0" Value="Idle" />
		<Parameter Name="1" Value="can_ironsight" />
	</Parameter>
	<Parameter Name="duration" Value="0" />
	<Parameter Name="signalpriorities" />
	<Parameter Name="forceAnim" Value="0" />
	<Parameter Name="syncAnimDuration" Value="0" />
	<Parameter Name="animStateID" Value="Pawn_Generic_Movement" />
	<Parameter Name="layerStateID" Value="Pawn_Generic_Aim" />
	<Parameter Name="gestureStateID" Value="0" />
	<Parameter Name="followTerrain" Value="0" />
	<Parameter Name="MoveLayer" Value="-1" />
<Sink Name="Slide" Start="0" End="100">
		<Connection Target="::Main Avatar/Common/Slide/StartSliding" Signal="start_sliding" />
</Sink>
<Sink Name="random idle" Start="0" End="100">
		<Connection Target="::Main Avatar/Common/IdleCycleBreaker" Signal="cyclebreaker" />
</Sink>
	<Sink Name="HolsterWeapons" Start="0" End="100">
		<Connection Target="::Pawn Weapons/Weapon Mechanics/States/HolsterWeapons" Signal="HolsterWeapons" />
</Sink>
</State>
```

Step 2: Creating a new holstered state  
main\_avater.gosm.xml (\\patch\_unpack\\scripts\\engine\\objects\\pawn\\statemachine\\)

Paste the following directly below the idle section that we edited in the previous step. This is the holstered state which includes being able go from holstered to doing every other action like driving and healing etc:

```xml
<State FullName="::Main Avatar/Common/WeaponsHolsteredState" Type="CGOStateAnim">
	<Parameter Name="groups">
		<Parameter Name="0" Value="IdleCycleBreaker" />
		<Parameter Name="1" Value="can_ironsight" />
	</Parameter>
	<Parameter Name="duration" Value="0" />
	<Parameter Name="signalpriorities" />
	<Parameter Name="forceAnim" Value="0" />
	<Parameter Name="syncAnimDuration" Value="0" />
	<Parameter Name="animStateID" Value="Pawn_Generic_Movement" />
	<Parameter Name="layerStateID" Value="Pawn_Generic_Wait" />
	<Parameter Name="gestureStateID" Value="0" />
	<Parameter Name="followTerrain" Value="0" />
	<Parameter Name="MoveLayer" Value="-1" />
	<Connection Target="::Main Avatar/Common/Idle" />
		<Event Name="try heal" Type="CGOStateEventHeal" Start="0" End="100" Signal="heal">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="0" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="0" />
	</Event>
	<Sink Name="heal wound" Start="0" End="100">
		<Connection Target="::Healing/Healing/States/Entering Healing" Signal="heal_now" />
	</Sink>
	<Sink Name="apply syringe" Start="0" End="100">
		<Connection Target="::Healing/Healing/States/SyringeHolster" Signal="apply_syringe" />
	</Sink>
	<Event Name="Try Use" Type="CGOStateEventPawn" Start="0" End="100" Signal="use">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="0" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="1" />
		<Parameter Name="simpleEventID" Value="" />
	</Event>
	<Event Name="Try use mounted weapon" Type="CGOStateEventEquipment" Start="0" End="100" Signal="use_mounted_weapon">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="0" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="21" />
	</Event>
	<Sink Name="use mounted weapon" Start="0" End="100">
		<Connection Target="::Pawn Weapons/Weapon Mechanics/States/Mounted weapons/GetInMountedWeapon" Signal="force_use_mounted_weapon" />
	</Sink>
	<Event Name="entervehicle" Type="CGOStateEventVehicle" Start="0" End="100" Signal="entervehicle_new">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="0" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="6" />
	</Event>
	<Sink Name="enter vehicle" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/Holster" Signal="entervehicle_now" />
	</Sink>
	<Event Name="Try throw grenade" Type="CGOStateEventPawn" Start="0" End="100" Signal="throw_grenade">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="0" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="10" />
		<Parameter Name="simpleEventID" Value="" />
	</Event>
	<Sink Name="Throw grenade - player" Start="0" End="100">
		<Connection Target="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Lowering arms" Signal="throw_player" />
	</Sink>
	<Sink Name="Slide" Start="0" End="100">
		<Connection Target="::Main Avatar/Common/Slide/StartSliding" Signal="start_sliding" />
	</Sink>
	<Sink Name="pickup" Start="0" End="100">
		<Connection Target="::Interactions/Pickup/Grab" Signal="pickup_weapon" />
	</Sink>
	<Sink Name="pickup from pile" Start="0" End="100">
		<Connection Target="::Interactions/Pickup/GrabAndReload" Signal="pickup_and_reload" />
	</Sink>
	<Sink Name="pick syringe" Start="0" End="100">
		<Connection Target="::Interactions/MedicStation/States/PickSyringeHolster" Signal="pickup_syringe" />
	</Sink>
	<Sink Name="open briefcase" Start="0" End="100">
		<Connection Target="::Interactions/Diamonds/HolsterBriefcase" Signal="open_briefcase" />
	</Sink>
	<Sink Name="Get In Top" Start="0" End="100">
		<Connection Target="::Interactions/Ladder/States/GetInLadderTop" Signal="GetOnLadderTop" />
	</Sink>
	<Sink Name="Get In Bottom" Start="0" End="100">
		<Connection Target="::Interactions/Ladder/States/GetInLadderBottom" Signal="GetOnLadderBottom" />
	</Sink>
	<Sink Name="Get In Water" Start="0" End="100">
		<Connection Target="::Interactions/Ladder/States/GetInLadderWater" Signal="GetOnLadderWater" />
	</Sink>
	<Sink Name="abort breaker" Start="0" End="100">
		<Connection Target="::Main Avatar/Common/Idle" Signal="startshooting" />
	</Sink>
	<Sink Name="abort breaker" Start="0" End="100">
		<Connection Target="::Main Avatar/Common/Idle" Signal="use" />
	</Sink>
</State>
```

Step 3: Creating a new holstering animation system  
weapons.gosm.xml (\\patch\_unpack\\scripts\\engine\\objects\\pawn\\statemachine\\)

We are going to create a new holstering animation system which we can link to our new holstered state.

1. Find the section with the title “Pawn Weapons/Weapon Mechanics/States/Holstering”. Copy and paste this section so you have two identical sections one above the other. Make sure to maintain the same formatting.  
 


2. We’re going to make edits to one of these, it doesn’t matter which but I did the top one. Change the full title to this:  
     
```xml {1}
    <State FullName="::Pawn Weapons/Weapon Mechanics/States/HolsterWeapons" Type="CGOStateAnim">
```
     
     
3. Now find the following line:

```xml
<Connection Target="::Pawn Weapons/External States/Main Avatar/Common/xIdle" />
```

We are going to edit this to point to our holstered state:

```xml
<Connection Target="::Main Avatar/Common/WeaponsHolsteredState" />
```

Your completed section should look like this:

```xml {1,12}
<State FullName="::Pawn Weapons/Weapon Mechanics/States/HolsterWeapons" Type="CGOStateAnim">
<Parameter Name="groups" />
<Parameter Name="duration" Value="0" />
<Parameter Name="signalpriorities" />
<Parameter Name="forceAnim" Value="0" />
<Parameter Name="syncAnimDuration" Value="0" />
<Parameter Name="animStateID" Value="0" />
<Parameter Name="layerStateID" Value="Pawn_Generic_Holster" />
<Parameter Name="gestureStateID" Value="-2" />
<Parameter Name="followTerrain" Value="0" />
<Parameter Name="MoveLayer" Value="-1" />
<Connection Target="::Main Avatar/Common/WeaponsHolsteredState" />
<Event Name="Holster" Type="CGOStateEventInventory" Start="100" End="100">
	<Parameter Name="alwaysTrigger" Value="1" />
	<Parameter Name="triggerOnce" Value="1" />
	<Parameter Name="triggeredOnEnd" Value="1" />
	<Parameter Name="triggeredOnBegin" Value="0" />
	<Parameter Name="requestType" Value="4" />
	<Parameter Name="simpleEventID" Value="" />
</Event>
</State>
```

Step 4: Linking the holstered state to the weapons system  
weapons.gosm.xml (\\patch\_unpack\\scripts\\engine\\objects\\pawn\\statemachine\\)

We are going to add our holstered state to the game’s pre-existing systems that decide what actions can be performed within all of the game’s states.

This involves adding the following line throughout “weapons.gosm.xml”:

```xml
<StateRef Path="::Main Avatar/Common/WeaponsHolsteredState" />
```

	  
We are going to add it to the sections with the following titles:

```xml
<Group FullName="::Pawn Weapons/Weapon Mechanics/Allow Gadget Use" Type="BaseGroup">
```

```xml
<Group FullName="::Pawn Weapons/Weapon Mechanics/CanSprintGroup" Type="BaseGroup">
```

```xml
<Group FullName="::Pawn Weapons/Weapon Mechanics/States/AllowWeaponSwitch" Type="BaseGroup">
```

```xml
<Group FullName="::Pawn Weapons/Weapon Mechanics/States/MapCompass/AllowMapCompass" Type="BaseGroup">
```

Add our line to the lists directly underneath these titles. It’s the same for each and here is an example of what it should look like:

```xml {4}
<Group FullName="::Pawn Weapons/Weapon Mechanics/Allow Gadget Use" Type="BaseGroup">
	<StateRef Path="::Pawn Weapons/External States/Main Avatar/Common/xIdle" />
	<StateRef Path="::Pawn Weapons/External States/Main Avatar/Common/xIdleCycleBreaker" />
	<StateRef Path="::Main Avatar/Common/WeaponsHolsteredState" />
```

Step 5: Adding the key binding  
inputactionmapcommon.xml (\\patch\_unpack\\config\\)

To add weapon holstering we’re going to create a new key binding that links to the “HolsterWeapons” signal we created in step 2\.

Add your extra key bindings to the section under the title: \<ActionMap name="common\_weapons"\>

You can customise the actual key binding in the “binding input” section and you can choose if you need to press or hold the relative button in the “action” section. My suggested controls for keyboard and gamepad are shown below.

Add this line to the keyboard section:

```xml {1}
<Binding input="kb:x" action="press" signal="Holsterweapons"/>
```

Add this line to the gamepad section:

```xml {1}
<Binding input="pad:y" action="hold" signal="Holsterweapons"/>
```

(Optional) Step 6: Making the new key binding rebindable

Adding the key binding to the default controls list  
defaultusercontrols.xml (\\patch\_unpack\\config\\)

To add the new key binding to the “Actions” in-game controls menu add the following line to the “CATEGORY\_ACTIONS” section:

```xml
<Control name="holster" key1="kb:x" actionmap="common_holster_remap" group="1" conflictmask="12"/>
```

The completed section should look like this:

```xml {5}
<Category name="CATEGORY_ACTIONS">
<Control name="fire" key1="mouse:lb" actionmap="common_shoot_remap" group="-1" conflictmask="-1"/>
<Control name="ironsight" key1="mouse:rb" actionmap="common_iron_remap" group="-1" conflictmask="-1"/>
<Control name="reload" key1="kb:r" actionmap="common_reload_remap" group="1" conflictmask="12"/>
<Control name="holster" key1="kb:x" actionmap="common_holster_remap" group="1" conflictmask="12"/>
<Control name="sprint" key1="kb:lshift" actionmap="common_move_remap" group="1" conflictmask="12"/>
        	<Control name="jump" key1="kb:space" actionmap="common_jump_remap" group="1" conflictmask="12"/>
        	<Control name="crouch" key1="kb:c" actionmap="common_crouch_remap" group="1" conflictmask="12"/>
     	<Control name="interact" key1="kb:e" actionmap="common_use_remap" group="1" conflictmask="12"/>		
</Category>
```

Link the changes to the default controls to the controls system  
Inputactionmapcommon.xml (\\patch\_unpack\\config\\)

To link our changes to the controls system add the following line below the “common\_weapons” title:

```xml
<Import actionmap="common_holster_remap" optional=""/>
```

The completed section should look like this:

```xml {6}
<ActionMap name="common_weapons">
<Import actionmap="common_weapons_remap" optional=""/>
<Import actionmap="common_shoot_remap" optional=""/>
<Import actionmap="common_iron_remap" optional=""/>	
<Import actionmap="common_reload_remap" optional=""/>
<Import actionmap="common_holster_remap" optional=""/>
```

Add a new control label  
\\patch\_pack\\languages\\ \- Each language has its own folder and “oasisstrings.rml” file.

Find the section with the title “\<section name="Actions"\>”.

Add the following line into this section, with the correct word for your language in the “value” section:

```xml
<string enum="holster" value="Holster" />
```

#### 

Step 7: Editing weapon animations  
The steps we have already completed are all that is technically required for holstering to work. We are left with a bug though where once holstered 12 of the weapons will still show the players arms as though they are still holding the weapon. This section will fix this.

We are going to need the weapon animation files. These are found for base game weapons by unpacking “worlds.fat/.dat” and then within \\worlds\_unpack\\graphics\\characters\\\_common\\animations\\weapons\\. For DLC weapons unpack “entitylibrary.fat/.dat” from \\Far Cry 2\\Data\_Win32\\downloadcontent\\dlc1\\ and then they are found within \\entitylibrary\_unpack\\graphics\\characters\\\_common\\animations\\weapons\\dlc\\.

1. Get the MGL140 holstering animation file “1stge\_uppb\_holster\_+000fw\_prmgl\_i1.mab”. Open this in your hex editor.

2. Scroll down this file and replace the section I have highlighted in the image below with zeros. Make sure you zeros go into the section on the left.

Your completed file should look like this:

3. Save this file. We are now going to use this file to replace the holstering animations for the following weapons. This will leave the animations looking identical but minimise visual bugs from the holstering. Trust me, I spent a long time experimenting with replacing using different animations and replacing/deleting different parts of the hex.   
     
   These are the bugged weapons:  MP5, G3KA4, AK47, Ithaca, MGL140, M249 Saw, Dart Rifle, Mortar, Sawed off shotgun, Silenced Shotgun, Crossbow.  
     
     
   Within \\patch\_unpack\\graphics\\characters\\\_common\\animations\\weapons\\ create folders for each weapon type (Secondary, Primary, Special, DLC). They should look like this:  
     
   Each weapon has its own folder within these parent folders and you can copy the folder structure and names from where you found the original animations.  
     
   Paste our edited MGL140 animation file and copy it into each weapon folder. Then replace it’s name with the filename for each respective weapon’s original holster animation.   
 


4. You can stop here if you want but right now each weapon has the same sound when it is holstered. This sound is specified in a hex string near the bottom of each animation file. You need to open up each weapon’s original holstering animation using your hex editor and copy that string into the same position within each newly created file. I have highlighted the specific string that needs to be changed in the image below:   
   

Step 8: Editing the SPAS12 animation  
The SPAS12 animation is not fixed by replacing it with the MGL140 animation like the other weapons in the previous step. To fix this we are going to hex edit it’s holstering animation with the exact method we did to the MGL140 animation.

So, find the SPAS12 animation which is called “1stge\_uppb\_holster\_+000fw\_prspa\_i1.mab” and found within \\worlds\_unpack\\graphics\\characters\\\_common\\animations\\weapons\\primary\\franchi\_spas12\\. Paste it into your patch folder within the same folder structure.

Now, open the file with your hex editor. Scroll down this file and replace the section I have highlighted in the image below with zeros. Make sure you zeros go into the section on the left.

Your complete section should look like this:

## Guide \- Silent Machete Assassinations

Step 1: Making machete assassinations a guaranteed kill  
weapons.gosm.xml (patch\_unpack\\scripts\\engine\\objects\\pawn\\statemachine\\)

Find these sections:

```xml
<Sink Name="melee attack success" Start="0" End="100">
<Connection Target="::Pawn Weapons/Weapon Mechanics/States/Machete/MeleeAttackBegin" Signal="melee_attack_success" />
</Sink>
<Sink Name="melee attack miss" Start="0" End="100">
<Connection Target="::Pawn Weapons/Weapon Mechanics/States/Machete/MeleeAttackMiss" Signal="melee_attack_miss" />
</Sink>
```

To make successful stealth attacks a guaranteed kill we are going to change the animation to the two handed finishing move by editing the “Connection Target” to “MeleeAttackFinishDouble”. Your completed “melee attack success” section should look like this:

```xml {2}
<Sink Name="melee attack success" Start="0" End="100">
<Connection Target="::Pawn Weapons/Weapon Mechanics/States/Machete/MeleeAttackFinishDouble" Signal="melee_attack_success" />
</Sink>
```

You now have the choice to remove the chance for stealthed attacks to miss. There is a rare animation where the enemy will dodge your hit and this can be removed by editing the “melee attack miss” section to this:

```xml {2}
<Sink Name="melee attack miss" Start="0" End="100">
<Connection Target="::Pawn Weapons/Weapon Mechanics/States/Machete/MeleeAttackFinishDouble" Signal="melee_attack_success" />
</Sink>
```

Step 2: Making machete assassinations silent  
Find “3rdge\_uppb\_lethalslash\_+000fw\_hhmac\_i1.mab” by unpacking “worlds.fat/.dat” and then within \\graphics\\characters\\\_common\\animations\\weapons\\handtohand\\machete\\.

Copy this file into the following directory in your “patch\_unpack” folder: \\patch\_unpack\\graphics\\characters\\\_common\\animations\\weapons\\handtohand\\machete\\sync\_finishground\\  
Create these folders if they are not present.

Rename this file to “3rdge\_syns\_finishground\_+000fw\_nowep\_i1.mab”.

(Optional) Step 3: Disabling enemy screams  
By default the enemies will scream in pain or make dying sounds when you perform an assassination. This screaming can be disabled if it is weird for you that other enemies don’t react to the noise.

1. Copy “hmr.gosm.xml” from \\worlds\_unpack\\scripts\\game\\objects\\pawn\\statemachine\\ and paste it into the same address within your patch folder: \\patch\_unpack\\scripts\\game\\objects\\pawn\\statemachine\\. Create these folders if they are not present.

2. Find and delete this section:

```xml
<Event Name="DeathBark" Type="CGOStateEventBark" Start="30" End="31">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="1" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="0" />
		<Parameter Name="barkEvent" Value="14" />
	</Event>
```

(Optional) Step 4: Creeping  
**Decoding required**  
Base game weapons \- xx\_WeaponProperties.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC weapons \- 3\_WeaponProperties.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

This feature will make stealth easier when playing using mouse and keyboard because in the vanilla game stealth was designed around being able to walk slowly behind enemies using a controller. By enabling ‘creeping’ you will be able to hold the right mouse button while using the machete to walk at half speed.

There are three machete entries that need to be edited:

WeaponProperties.HandToHand.Machete  
WeaponProperties.HandToHand.Machete\_HomeMade  
WeaponProperties.HandToHand.Machete\_Primitive

	  
To create a creeping function we’re going to enable iron sights for the machetes and then edit the iron sights movement speed and FOV.

Find the “IronSight” section for each machete:

```xml
<object hash="BB04E184" type="IronSight">
       <value name="fMoveSpeedFactor" hash="7D725133" type="Float">1.0</value> <!-- type="BinHex" value="0000803F" -->
       <value name="bCanIronsight" hash="E49EEB82" type="Bool">False</value> <!-- type="BinHex" value="00" -->
       <value name="fLookSensitivityFactor" hash="DD39539B" type="Float">1.0</value> <!-- type="BinHex" value="0000803F" -->
       <value name="fIronsightFOV" hash="FB4ADD00" type="Float">1.308</value> <!-- type="BinHex" value="8B6CA73F" →
```

To enable iron sights change the “bCanIronSight” stat to “True”.

To decreased iron sight movement speed to 50% change the “fMoveSpeedFactor” stat to “0.5”.

To add a slight zoom when creeping, change the “fIronsightFOV” stat to “1.3”.

You can customise the movement speed and FOV settings to your choice, these are my chosen settings:

```xml {2,3,5}
<object hash="BB04E184" type="IronSight">
       <value name="fMoveSpeedFactor" hash="7D725133" type="Float">0.5</value> <!-- type="BinHex" value="0000003F" -->
       <value name="bCanIronsight" hash="E49EEB82" type="Bool">True</value> <!-- type="BinHex" value="01" -->
       <value name="fLookSensitivityFactor" hash="DD39539B" type="Float">1.0</value> <!-- type="BinHex" value="0000803F" -->
       <value name="fIronsightFOV" hash="FB4ADD00" type="Float">1.3</value> <!-- type="BinHex" value="8B6CA73F" -->
```

## Guide \- Throwing grenades from mounted weapons

This guide will cover enabling throwing grenades from mounted weapons. These are regular mounted weapons, not those on vehicles. This is a smooth addition, the animations all line up well. The only bug with this is that if you try to throw a grenade when you haven’t got any left you will leave the mounted weapon.

Step 1: Creating new grenade throwing entries  
weapons.gosm.xml (\\patch\_unpack\\scripts\\engine\\objects\\pawn\\statemachine\\)

We are going to copy and paste two sections of this file, with these titles:

```xml
<State FullName="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Lowering arms" Type="CGOStateAnim">
```

```xml
<State FullName="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Throwing grenade" Type="CGOStateEquipment">
```

These sections are next to each other, copy both of them and paste them directly below.

We are now going to rename both of these new sections so these are their titles:

```xml {1}
<State FullName="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Mounted Weapons Lowering arms" Type="CGOStateAnim">
```

```xml {1}
<State FullName="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Mounted Weapons Throwing grenade" Type="CGOStateEquipment">
```

Now we are going to change the “Connection Target” values of both sections.

The connection target of “Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Driving Lowering arms” is going to be “Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Mounted Weapons Throwing grenade”.

The connection target of “Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Mounted Weapons Throwing grenade” is going to be “Pawn Weapons/Weapon Mechanics/States/Mounted weapons/GetInMountedWeapon”.

We are also going to change the “abort” Connection Target value of “Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Driving Throwing grenade” to “Pawn Weapons/Weapon Mechanics/States/Mounted weapons/GetInMountedWeapon”.

Your complete sections should look like this:

```xml {1,12,38,51,61}
<State FullName="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Mounted Weapons Lowering arms" Type="CGOStateAnim">
	<Parameter Name="groups" />
	<Parameter Name="duration" Value="0" />
	<Parameter Name="signalpriorities" />
	<Parameter Name="forceAnim" Value="0" />
	<Parameter Name="syncAnimDuration" Value="0" />
	<Parameter Name="animStateID" Value="0" />
	<Parameter Name="layerStateID" Value="Pawn_Generic_Holster" />
	<Parameter Name="gestureStateID" Value="0" />
	<Parameter Name="followTerrain" Value="0" />
	<Parameter Name="MoveLayer" Value="-1" />
	<Connection Target="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Mounted Weapons Throwing grenade" />
	<Event Name="Select grenade" Type="CGOStateEventInventory" Start="100" End="100">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="1" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="14" />
		<Parameter Name="simpleEventID" Value="" />
	</Event>
	<Event Name="Backup" Type="CGOStateEventInventory" Start="95" End="95">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="1" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="16" />
		<Parameter Name="simpleEventID" Value="" />
	</Event>
	<Event Name="Net Throw event" Type="CGOStateEventPawn" Start="0" End="1">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="0" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="12" />
		<Parameter Name="simpleEventID" Value="event_net_throw_grenade" />
	</Event>
</State>
<State FullName="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Mounted Weapons Throwing grenade" Type="CGOStateEquipment">
	<Parameter Name="groups" />
	<Parameter Name="duration" Value="0" />
	<Parameter Name="signalpriorities" />
	<Parameter Name="forceAnim" Value="0" />
	<Parameter Name="syncAnimDuration" Value="0" />
	<Parameter Name="animStateID" Value="0" />
	<Parameter Name="layerStateID" Value="Pawn_Generic_Throw_Layered" />
	<Parameter Name="gestureStateID" Value="0" />
	<Parameter Name="followTerrain" Value="0" />
	<Parameter Name="MoveLayer" Value="-1" />
	<Parameter Name="autosetDuration" Value="0" />
	<Parameter Name="syncWith" Value="0" />
	<Connection Target="::Pawn Weapons/Weapon Mechanics/States/Mounted weapons/GetInMountedWeapon" />
	<Event Name="Throw" Type="CGOStateEventPawn" Start="25" End="25">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="1" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="11" />
		<Parameter Name="simpleEventID" Value="" />
	</Event>
	<Sink Name="abort" Start="0" End="100">
		<Connection Target="::Pawn Weapons/Weapon Mechanics/States/Mounted weapons/GetInMountedWeapon" Signal="abort" />
	</Sink>
</State>
```

Step 2: Editing the mounted weapon state  
weapons.gosm.xml (\\patch\_unpack\\scripts\\engine\\objects\\pawn\\statemachine\\)

Find the mounted weapon state with this title: 

```xml
<State FullName="::Pawn Weapons/Weapon Mechanics/States/Mounted weapons/UsingMountedWeapon" Type="CGOStateAnim">
```

We are going to add the following section to the bottom of the mounted weapon state:

```xml
<Sink Name="Throw grenade - Mounted Weapon" Start="0" End="100">
<Connection Target="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Mounted Weapons Lowering arms" Signal="mounted_weapons_throw_grenade" />
</Sink>
```

Your completed section should look like this:

```xml {16}
<State FullName="::Pawn Weapons/Weapon Mechanics/States/Mounted weapons/UsingMountedWeapon" Type="CGOStateAnim">
	<Parameter Name="groups" />
	<Parameter Name="duration" Value="0" />
	<Parameter Name="signalpriorities" />
	<Parameter Name="forceAnim" Value="0" />
	<Parameter Name="syncAnimDuration" Value="0" />
	<Parameter Name="animStateID" Value="Pawn_Generic_Mounted_BaseLayer" />
	<Parameter Name="layerStateID" Value="Pawn_Generic_Mounted" />
	<Parameter Name="gestureStateID" Value="Pawn_Generic_Mounted_Gesture" />
	<Parameter Name="followTerrain" Value="0" />
	<Parameter Name="MoveLayer" Value="-1" />
	<Sink Name="leave" Start="0" End="100">
		<Connection Target="::Pawn Weapons/Weapon Mechanics/States/Mounted weapons/GetOutMountedWeapon" Signal="leave_mounted_weapon" />
	</Sink>
	<Sink Name="Throw grenade - Mounted Weapon" Start="0" End="100">
		<Connection Target="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Mounted Weapons Lowering arms" Signal="mounted_weapons_throw_grenade" />
	</Sink>
</State>
```

Step 3: Adding the new grenade throwing entries into the mounted weapon systems  
weapons.gosm.xml (\\patch\_unpack\\scripts\\engine\\objects\\pawn\\statemachine\\)

We are now going to link our new grenade throwing entries into various vehicle systems that allow them to work.

This involves copying the following lines into various lists in this file:

```xml
<StateRef Path="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Mounted Weapons Lowering arms" />
<StateRef Path="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Mounted Weapons Throwing grenade" />
```
		

These lists have the following titles, you’ll see that there are already lists there. You simply need to copy the lines above into them:

```xml
<Group FullName="::Pawn Weapons/Weapon Mechanics/States/Mounted weapons/OnMountedWeapon" Type="BaseGroup">
<Group FullName="::Pawn Weapons/Weapon Mechanics/States/Mounted weapons/MountedWeaponBeautifier" Type="BaseGroup">
<Group FullName="::Pawn Weapons/Weapon Mechanics/States/Mounted weapons/MountedWeaponActionMap" Type="BaseGroup">
```

For example, here is one of the lists with the new lines added:  
    
```xml
<Group FullName="::Pawn Weapons/Weapon Mechanics/States/Mounted weapons/OnMountedWeapon" Type="BaseGroup">
	<StateRef Path="::Pawn Weapons/Weapon Mechanics/States/Mounted weapons/UsingMountedWeapon" />
	<StateRef Path="::Pawn Weapons/Weapon Mechanics/States/Mounted weapons/MountedFireBullets" />
	<StateRef Path="::Pawn Weapons/Weapon Mechanics/States/Mounted weapons/GetOutMountedWeapon" />
	<StateRef Path="::Pawn Weapons/Weapon Mechanics/States/Mounted weapons/AttachToMounted" />
	<StateRef Path="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Mounted Weapons Lowering arms" />
	<StateRef Path="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Mounted Weapons Throwing grenade" />
```

Step 4: Adding new controls  
inputactionmapcommon.xml (\\patch\_unpack\\config\\)

Find the section of this file with the title: \<ActionMap name="common\_using\_mounted\_weapon"\>

Copy and paste the following lines into this section, this includes a button to throw a grenade and also swap grenade type:

```xml
<Binding input="kb:q" action="press" signal="mounted_weapons_throw_grenade"/>
<Binding input="kb:f" action="press" signal="select_next_throw_gadget"/>
<Binding input="pad:right_shoulder" action="press" signal="mounted_weapons_throw_grenade"/>
<Binding input="pad:left" action="press" signal="select_next_throw_gadget"/>
```

Step 5: Make the new controls rebindable  
inputactionmapcommon.xml (\\patch\_unpack\\config\\)

Controls being rebindable is controlled by the “import actionmap” lines directly below the titles of each section of this file.

As we haven’t added any brand new controls we just need the following line into the “import actionmap” sections of the mounted weapon controls sections:

```xml
<import actionmap="common_grenade_remap" optional=""/>
```

Your completed passenger section should look like this:

```xml {4}
<ActionMap name="common_using_mounted_weapon">
	<import actionmap="common_heal_remap" optional=""/>
	<Import actionmap="common_changeseat_remap" optional=""/>
	<import actionmap="common_grenade_remap" optional=""/>
```

## Guide \- Alternate animations

This guide will cover different animations that can be swapped around to alter how the player carries their weapons. You can do this to individual weapons to meet your preference.

To swap around the animations you just need to replace the original animation with the new one, while making sure to give the new file the same filename as the original.

The animation files for all base game weapons can be found in worlds.dat/.fat (\\Far Cry 2\\Data\_Win32\\worlds\\), within \\worlds\_unpack\\graphics\\characters\\\_common\\animations\\weapons\\. The animation files for dlc weapons are found in dlc\_jungle.dat/.fat (\\Far Cry 2\\Data\_Win32\\downloadcontent\\dlc\_jungle\\), within \\dlc\_jungle\_unpack\\graphics\\weapons\\dlc\\.

In these folders you’ll see that each weapon has its own folder that contains all the animations for that weapon. The only weapons that are different are the desert eagle, ithaca, m1903 and machete, who’s walking animation files can be found in \\worlds\_unpack\\graphics\\characters\\\_common\\animations\\locomotion\\stand\\walk\\.

To replace the existing animation files you need to paste the new animation files into your “patch\_unpack” folder, making sure to maintain the existing folder structure. Each category of weapons has its own folder and within your “patch\_unpack” folder it should look like this:

Within each weapon’s folder you’ll find the same animations and below I will outline the more useful ones when it comes to swapping. You’ll see that most animations follow the same naming structure but there are some that don’t which I will highlight. I’ll also go through some suggested swaps but you could be creative here and swap any of the animations around.

Not moving (standing)  
aimcycle (e.g. 1stge\_uppb\_**aimcycle**\_+000fw\_se6p9.mab)  
aimingcycle (desert eagle, ithaca, silenced shotgun \- 1stge\_uppb\_**aimingcycle**\_+000fw\_sedea\_i1.mab)

Not moving (crouched)  
aimcyclecrh (e.g. 1stge\_uppb\_**aim2ironcrh**\_+000fw\_se6p9\_i1.mab)  
aimcrh (m1903 \- 1stge\_uppb\_**aimcrh**\_+000fw\_spm19\_i1.mab)  
aimcyclec (ak47 \- 1stge\_uppb\_**aimcyclec**\_+000fw\_prak4\_i1.mab)

Not moving (lowered weapon \- safe zone style)  
wsafeidle (e.g. 1stge\_uppb\_**wsafeidle**\_+000fw\_se6p9\_i1.mab)

Walking (standing)  
walk (e.g. 1stge\_uppb\_**walk**\_+000fw\_se6p9\_i1.mab)  
idle (machete, ied detonator \- 1stge\_uppb\_**idle**\_+000fw\_hhmac\_i1.mab)

Walking (crouched)  
walkcrh (e.g. 1stge\_uppb\_**walkcrh**\_+000fw\_se6p9\_i1)  
walkc (ak47 \- 1stge\_uppb\_**walkc**\_+000fw\_prak4\_i1.mab)

Walking (lowered weapon \- safe zone style)  
wsafewalk (e.g. 1stge\_uppb\_**wsafewalk**\_+000fw\_se6p9\_i1.mab)

Aiming (standing)  
aim2iron (e.g. 1stge\_uppb\_**aim2iron**\_+000fw\_se6p9\_i1.mab)  
regular2ironsight (desert eagle, ithaca, m1903 \- 1stge\_uppb\_**regular2ironsight**\_+000fw\_sedea\_i1.mab)

Aiming (crouched)  
aim2ironcrh (e.g. 1stge\_uppb\_**aim2ironcrh**\_+000fw\_se6p9\_i1.mab)

### Option 1 \- Carrying weapons lower

This option involves swapping the regular standing animations with the crouched animations, as the player holds their weapons lower when crouching. See below for comparisons, with the new animations on the right:

To do this we are going to swap the following animations:

Replace “Not moving (standing)” with “Not moving (crouched)”  
Replace “Walking (standing)” with “Walking (crouched)”  
Replace “Aiming (standing)” with “Aiming (crouched)”

### Option 2 \- Safe zone animations

This option involves using the two safe zone animations. Every weapon has two of these, one for moving and another for not moving. You can swap these around pretty freely but the example below shows the crouched standing still animations replaced on the right:

I would suggest the most common use of these animations are to replace the original crouched animations, which is what Redux does. You can replace the crouched standing still animation and the couched walking animation but some advice is that only changing one of these looks weird in motion when using a controller and walking slowly.

So, to swap the crouched animations we are going to swap the following animations:

Replace “Not moving (crouched)” with “Not moving (lowered weapon \- safe zone style)”  
Replace “Walking (crouched)” with “Walking (lowered weapon \- safe zone style)”

Another possibility here could be to lower weapons when walking standing up, which would involve swapping the following:

Replace “Walking (standing up)” with “Walking (lowered weapon \- safe zone style)”

