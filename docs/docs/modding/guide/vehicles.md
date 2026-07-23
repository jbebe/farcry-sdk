---
sidebar_position: 14
sidebar_label: "Vehicles"
format: md
---

:::info[From the Almost Complete Guide]
This page reproduces a section of ["An Almost Complete Guide to Far Cry 2 Modding"](./index.md) by Boggalog, verbatim, with attribution. See the [guide index](./index.md) for the full attribution note.
:::

# Vehicles

## Base game vehicles

The stats for the base vehicles are controlled by the file “xx\_vehicle.xml” within “entitylibrarypatchoverride.fcb” from \\patch\_unpack\\generated\\. Each vehicle variation has a separate entry and I’ve listed all the entry titles below. I’ve tried to include every drivable vehicle that appears in the singleplayer game but I’m not 100% about all the paraglider and big truck entries, maybe I’ve included some that aren’t actually drivable.

| Hang glider | Air.ParagliderIntel |
| :---- | :---- |
|  | Air.Paraglider |
|  | Air.Paraglider.Paraglider\_Lv1 |
|  | Air.Paraglider.Paraglider\_Lv2 |
|  | Air.Paraglider.Paraglider\_Lv3 |
|  | Air.Paraglider.Paraglider\_Lv4 |
|  | Air.Paraglider.Paraglider\_Lv5 |
| Big truck | Land.BigTruck |
|  | Land.BigTruck.A2LM09\_NitrousTruck |
|  | Land.BigTruck.Tanker |
|  | Land.BigTruck\_Tanker |
| Dune buggy | Land.Buggy |
| Car | Land.Datsun |
| Jeep Liberty | Land.JeepLiberty |
|  | Land.JeepLiberty.VIP |
| Jeep Wrangler | Land.JeepWrangler |
| Assault truck | Land.Rover |
|  | Land.Rover.M249\_Mounted |
|  | Land.Rover.M2\_Mounted |
|  | Land.Rover.MK19\_Mounted |
| Fishing boat | Sea.FishingBoat |
|  | Sea.FishingBoat.M249\_Mounted |
|  | Sea.FishingBoat.M2\_Mounted |
|  | Sea.FishingBoat.MK19\_Mounted |
| Swamp boat | Sea.SwampBoat |
|  | Sea.SwampBoat.M249\_Mounted |
|  | Sea.SwampBoat.M2\_Mounted |
|  | Sea.SwampBoat.MK19\_Mounted |

## DLC vehicles

The stats for the DLC vehicles have the same structure as the base game vehicles, but in a different file. The DLC vehicle file is “2\_vehicles.xml” within “entitylibrary.fcb” from \\patch\_unpack\\downloadcontent\\dlc1\\generated\\.

| ATV | Land.DLC\_Vehicle1\_DLC1 |
| :---- | :---- |
| Unimog/Utility truck | Land.DLC\_Vehicle2\_DLC1 |

## Weight

**Decoding required**  
Base game vehicles \- xx\_vehicle.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC vehicles \- 2\_vehicle.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Vehicle weight is controlled by the “fMass” stat in the “WheeledParams” section.

You can reduce this to increase vehicle speed and responsiveness. Reducing it too much can break the steering so I recommend only reducing it by a maximum of 15%.

```xml {2}
<object hash="279B86EC" type="WheeledParams">
       <value name="fMass" hash="3D255EB4" type="Float">1000.0</value> <!-- type="BinHex" value="00007A44" -->
```

## Land vehicle speed

Land vehicle speed is controlled with a variety of stats, we can increase the top speed and then the ability to reach that top speed with engine power and geering stats. I have increased them all by the same proportion previously but you can experiment with it all.

### Top speed

**Decoding required**  
Base game vehicles \- xx\_vehicle.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC vehicles \- 2\_vehicle.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Land vehicle top speed is controlled by the “fGearBoxTopSpeed” stat in the “WheeledParams” section.

```xml {5}
<object hash="279B86EC" type="WheeledParams">
       <value name="fMass" hash="3D255EB4" type="Float">1600.0</value> <!-- type="BinHex" value="0000C844" -->
       <value name="fEnginePower" hash="0CF4A9FC" type="Float">95.0</value> <!-- type="BinHex" value="0000BE42" -->
       <value name="fExtraClimbEnginePower" hash="0585E2C2" type="Float">400.0</value> <!-- type="BinHex" value="0000C843" -->
       <value name="fGearBoxTopSpeed" hash="8E3D52A5" type="Float">31.0</value> <!-- type="BinHex" value="0000F841" -->
```

### Engine power

**Decoding required**  
Base game vehicles \- xx\_vehicle.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC vehicles \- 2\_vehicle.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Land vehicle engine power is controlled by two stats, “fEnginePower” and “fExtraClimbEnginePower” in the “WheeledParams” section.

I don’t know exactly what “fExtraClimbEnginePower” does but I think we can infer it helps climbing hills.

```xml {3,4}
<object hash="279B86EC" type="WheeledParams">
       <value name="fMass" hash="3D255EB4" type="Float">1600.0</value> <!-- type="BinHex" value="0000C844" -->
       <value name="fEnginePower" hash="0CF4A9FC" type="Float">95.0</value> <!-- type="BinHex" value="0000BE42" -->
       <value name="fExtraClimbEnginePower" hash="0585E2C2" type="Float">400.0</value> <!-- type="BinHex" value="0000C843" -->
       <value name="fGearBoxTopSpeed" hash="8E3D52A5" type="Float">31.0</value> <!-- type="BinHex" value="0000F841" -->
```

### Gearing

**Decoding required**  
Base game vehicles \- xx\_vehicle.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC vehicles \- 2\_vehicle.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Land vehicle gearing is controlled by the stats in the “GearEmulation” section. I don’t know exactly how these work but you can increase the stats for better acceleration and to make it easier to reach top speed. Every land vehicle is controlled by three gears, no matter how many are listed elsewhere. 

Editing these isn’t too bad but it’s a bit complicated to describe. 

The first thing to notice is that each gear overlaps, the max speed of one gear is faster than the minimum speed of the next. Make sure your gears overlap the same when you’re done\!

To increase the gearing we’re going to increase the “fMaxSpeed” stat of “Gear0” and both the “fMinSpeed” and “fMaxSpeed” stats of “Gear1” and “Gear2”. I recommend increasing all these stats by the same proportion, unless you actually know what you’re doing with vehicle gears.

```xml {4,9,10,15,16}
<object hash="4D76B715" type="GearEmulation">
       <object hash="52CCFEBA" type="Gear0">
            <value name="fMinSpeed" hash="5FFD7A4F" type="Float">0.0</value> <!-- type="BinHex" value="00000000" -->
            <value name="fMaxSpeed" hash="B99DD5AE" type="Float">4.0</value> <!-- type="BinHex" value="00008040" -->
            <value name="fMinRPM" hash="54F9B0B8" type="Float">800.0</value> <!-- type="BinHex" value="00004844" -->
            <value name="fMaxRPM" hash="11FBF33A" type="Float">8000.0</value> <!-- type="BinHex" value="0000FA45" -->
       </object>
       <object hash="25CBCE2C" type="Gear1">
            <value name="fMinSpeed" hash="5FFD7A4F" type="Float">3.8</value> <!-- type="BinHex" value="33337340" -->
            <value name="fMaxSpeed" hash="B99DD5AE" type="Float">9.5</value> <!-- type="BinHex" value="00001841" -->
            <value name="fMinRPM" hash="54F9B0B8" type="Float">2500.0</value> <!-- type="BinHex" value="00401C45" -->
            <value name="fMaxRPM" hash="11FBF33A" type="Float">9000.0</value> <!-- type="BinHex" value="00A00C46" -->
        </object>
        <object hash="BCC29F96" type="Gear2">
            <value name="fMinSpeed" hash="5FFD7A4F" type="Float">9.3000002</value> <!-- type="BinHex" value="CDCC1441" -->
            <value name="fMaxSpeed" hash="B99DD5AE" type="Float">15.0</value> <!-- type="BinHex" value="00007041" -->
            <value name="fMinRPM" hash="54F9B0B8" type="Float">3500.0</value> <!-- type="BinHex" value="00C05A45" -->
            <value name="fMaxRPM" hash="11FBF33A" type="Float">11000.0</value> <!-- type="BinHex" value="00E02B46" -->
        </object>
</object>
```

## Boats

### Engine power

**Decoding required**  
Base game vehicles \- xx\_vehicle.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC vehicles \- 2\_vehicle.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Boat engine power is controlled by two stats, “fForwardEnginePower” and “fReverseEnginePower” in the “BoatParams” section.

```xml {1,2}
<value name="fForwardEnginePower" hash="B835E52E" type="Float">3.5</value> <!-- type="BinHex" value="00006040" -->
<value name="fReverseEnginePower" hash="CB275F6D" type="Float">5.0</value> <!-- type="BinHex" value="0000A040" -->
<value name="fEngineBrakingPower" hash="D330AADC" type="Float">4.0</value> <!-- type="BinHex" value="00008040" -->
```

### Braking power

**Decoding required**  
Base game vehicles \- xx\_vehicle.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC vehicles \- 2\_vehicle.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Boat braking power is controlled by the “fEngineBrakingPower” stat in the “BoatParams” section.

```xml {3}
<value name="fForwardEnginePower" hash="B835E52E" type="Float">3.5</value> <!-- type="BinHex" value="00006040" -->
<value name="fReverseEnginePower" hash="CB275F6D" type="Float">5.0</value> <!-- type="BinHex" value="0000A040" -->
<value name="fEngineBrakingPower" hash="D330AADC" type="Float">4.0</value> <!-- type="BinHex" value="00008040" →
```

## Collision damage

**Decoding required**  
Base game vehicles \- xx\_vehicle.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC vehicles \- 2\_vehicle.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Collision damage is controlled by the “nMaxStimCollisionLevel” stat in the “CVehicleWheeledPhysComponent” section. This isn’t a direct modifier for collision damage, it controls the maximum damage that can be received from a single collision.

This must be a whole number, any value with a decimal point (e.g. 20.5) will stop the files from repacking.

```xml {7}
<object type="CVehicleWheeledPhysComponent">
       <value name="hidHasAliasName" type="Bool">False</value>
       <value hash="527E7674" type="String">graphics\vehicles\land\bigtruck_tanker\bigtruck_tanker.hkx</value>
       <value name="hidResourceId" type="Hash">3416FF86</value>
       <value name="hidNewCollision" hash="65D43FE4" type="Bool">True</value> <!-- type="BinHex" value="01" -->
       <value name="sndtpSoundType" hash="8FE662AD" type="Int32">13</value> <!-- type="BinHex" value="0D000000" -->
       <value name="nMaxStimCollisionLevel" hash="EFD4B11F" type="UInt32">20</value> <!-- type="BinHex" value="1C000000" -->
```

## Max look angle

Base game vehicles \- xx\_vehicle.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
DLC vehicles \- 2\_vehicle.xml \< entitylibrary.fcb (\\patch\_unpack\\downloadcontent\\dlc1\\generated\\)

Max look angle is controlled by the “vehicleMaxLookAngle” stat in the “CVehicle” section.

Horizontal max look angle is controlled by the z axis stat. All the vehicles have identical values except the swamp boats which have their max look angle reduced.

```xml {4}
<value name="vehicleMaxLookAngle" hash="58992FEC" type="Vector3">
       <x>30.0</x>
       <y>0.0</y>
       <z>169.9999847</z>
</value> <!-- type="BinHex" value="0000F04100000000FFFF2943" -->
```

## 

## Upgrades

Vehicle upgrades are controlled by the “gamemodesconfig.xml” file from \\patch\_unpack\\engine\\gamemodes\\.

Editing the upgrades is the only way to increase a vehicle’s health and repair speed.

Each upgrade has two identifying values, a name and object. These are all of them that are used in the singleplayer game:

| Vehicle | Upgrade Name | Upgrade object |
| :---- | :---- | :---- |
| Assault truck | rover\_vehicle\_manual | rover |
| Dune buggy | buggy\_vehicle\_manual | buggy |
| Swamp boat | swampboat\_vehicle\_manual | swampboat |
| Fishing boat | fishingboat\_vehicle\_manual | fishingboat |
| Jeep Liberty | jeep\_liberty\_vehicle\_manual | jeep\_liberty |
| Jeep Wrangler | jeep\_wrangler\_vehicle\_manual | jeep\_wrangler |
| Big truck | bigtruck\_vehicle\_manual | bigtruck |
| Car | datsun\_vehicle\_manual | datsun |

### Health

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

It is possible to increase a vehicle’s health by increasing the “degradation” stat of its upgrade. This stat works as a percentage reduction of all damage.

```xml {2}
<Plan name="rover_vehicle_manual" object="rover">
	<bonus attr="degradation" value="-50" type="percent"/>
	<bonus attr="repairtime" value="-50" type="percent"/>
</Plan>
```

### Repair speed

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

Vehicle repair speed is controlled by the “repairtime” stat of its upgrade.This stat works as a percentage reduction of the base repair time.

```xml {3}
<Plan name="rover_vehicle_manual" object="rover">
	<bonus attr="degradation" value="-50" type="percent"/>
	<bonus attr="repairtime" value="-50" type="percent"/>
</Plan>
```

## Bug fix \- Hang gliders falling out of the sky when shot

**Decoding required**  
xx\_vehicle.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

In the vanilla game the hang glider has a bad tendency to fall immediately out of the sky or get pushed into doing loop-de-loops when shot. We’re going to fix that so you will still feel getting shot but only fall out of the sky under very heavy fire.

To do this we’re going to increase the hang glider’s weight with the “fMass” stat in the “ParagliderParams” section. 

I’ve done a lot of testing and the best value I found for this is “2420”. Much heavier and getting shot makes no difference, much lighter and the problem isn't fixed.

The same change can be applied to all of the different singleplayer hang glider entries:

Air.ParagliderIntel  
Air.Paraglider  
Air.Paraglider.Paraglider\_Lv1  
Air.Paraglider.Paraglider\_Lv2  
Air.Paraglider.Paraglider\_Lv3  
Air.Paraglider.Paraglider\_Lv4  
Air.Paraglider.Paraglider\_Lv5

```xml {2}
<object hash="F766D2D8" type="ParagliderParams">
      <value name="fMass" hash="3D255EB4" type="Float">300.0</value> <!-- type="BinHex" value="00009643" →
```

## Bug fix \- Hang gliders bouncing on water

**Decoding required**  
xx\_vehicle.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

In the vanilla game the hang glider bounces on water like a cat that doesn’t want to get wet. We’re going to fix that so it is buoyant and will settle on the water surface. You can even climb onto it\!

The same changes can be applied to all of the different singleplayer hang glider entries:

Air.ParagliderIntel  
Air.Paraglider  
Air.Paraglider.Paraglider\_Lv1  
Air.Paraglider.Paraglider\_Lv2  
Air.Paraglider.Paraglider\_Lv3  
Air.Paraglider.Paraglider\_Lv4  
Air.Paraglider.Paraglider\_Lv5

Step 1: Increase discarded weight

The hang glider’s weight once discarded is controlled by the “fDiscardedMass” stat in the “CVehicleParagliderPhysComponent” section.

I’ve done a lot of testing and the best value I found for this is “825”. This had a good balance of being buoyant while reacting somewhat realistically to hitting the water and the player standing on it.

```xml {1}
<value name="fDiscardedMass" hash="331194A1" type="Float">75.0</value> <!-- type="BinHex" value="00009642" -->
```

Step 2: Increase maximum depth in water

The hang glider’s maximum depth in water is controlled by the “fUnderWaterMaxDepth” stat in the “CVehicle” section.

I recommend changing this to “1.5”.

```xml {1}
<value name="fUnderWaterMaxDepth" hash="1C83382E" type="Float">-1.0</value> <!-- type="BinHex" value="000080BF" -->
```

## Bug fix \- Seeing the edges of the players arms when using hang gliders

**Decoding required**  
xx\_vehicle.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

With the default fov in hang gliders, you can see the edges of the player’s arms because they don’t actually connect to the body.

Fov is controlled by the “fFOVAngle” stat in the “FOV” section.

The default value is 90, with a value of 81 you can no longer see the arm edges.

The same changes can be applied to all of the different singleplayer hang glider entries:

Air.ParagliderIntel  
Air.Paraglider  
Air.Paraglider.Paraglider\_Lv1  
Air.Paraglider.Paraglider\_Lv2  
Air.Paraglider.Paraglider\_Lv3  
Air.Paraglider.Paraglider\_Lv4  
Air.Paraglider.Paraglider\_Lv5

```xml {3}
<object hash="7EBF8F6B" type="FOV">
            <value name="archFOVCurveName" hash="7444EF78" type="String"></value> <!-- type="BinHex" value="00" -->
            <value name="fFOVAngle" hash="49745480" type="Float">81.0</value> <!-- type="BinHex" value="0000B442" -->
<value name="fFOVTransitionTime" hash="C7BBDB88" type="Float">0.2</value> <!-- type="BinHex" value="CDCC4C3E" -->
</object>
```

## 

## Bug fix \- Silent big truck engine

**Decoding required**  
xx\_vehicle.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

In the vanilla game the big truck barely makes any engine noise. We’re going to make this louder.

There are a number of big truck entries that need this fix applied to and we may as well apply it to all of them, including those that aren’t drivable by the player. This is the list of entries:

Land.BigTruck  
Land.BigTruck.A2LM09\_NitrousTruck  
Land.BigTruck.ScriptedBigTruck  
Land.BigTruck.Tanker  
Land.BigTruck\_Tanker

The fix is going to involve editing the sound values in the “Sound” section for each big truck entry. This is the list of changes, with a completed section beneath that you can copy and paste into your file:

sndPlayEngineIdleLoop \- 0x0045CD73 → 0x004EE930  
sndStopEngineIdleLoop \- 0x0045CD7A → 0x004EE933  
sndEngineLoop \- 0x0045CD74 → 0x004EE931  
sndEngineIgnition \- 0x0045CD79 → 0x004EE932  
sndTurnOffEngine \- 0x0045CD7A → 0x004EE933  
sndFrameLoop \- 0x004B8893 → 0x004EE940  
sndThrustPedal \- 0x0045CD71 → 0x004EE92F

```xml
<object type="Sound">
            <value name="sndPlayEngineIdleLoop" hash="EB7BAA7B" type="String">0x004EE930</value> <!-- type="BinHex" value="3078303034454539333000" -->
            <value name="sndStopEngineIdleLoop" hash="A64C403E" type="String">0x004EE933</value> <!-- type="BinHex" value="3078303034454539333300" -->
            <value name="sndEngineLoop" hash="4213C1F9" type="String">0x004EE931</value> <!-- type="BinHex" value="3078303034454539333100" -->
            <value name="sndExtraTorqueEngineLoop" hash="BAF04771" type="String">0xFFFFFFFF</value> <!-- type="BinHex" value="3078464646464646464600" -->
            <value name="sndEngineIgnition" hash="85ED816E" type="String">0x004EE932</value> <!-- type="BinHex" value="3078303034454539333200" -->
            <value name="sndTurnOffEngine" hash="9D9CC7C5" type="String">0x004EE933</value> <!-- type="BinHex" value="3078303034454539333300" -->
            <value name="sndFrameLoop" hash="5902AE1B" type="String">0x004EE940</value> <!-- type="BinHex" value="3078303034454539343000" -->
            <value name="sndGearShift_New" hash="8841202C" type="String">0xFFFFFFFF</value> <!-- type="BinHex" value="3078464646464646464600" -->
            <value name="sndGearShift_MinorDamage" hash="17F3423F" type="String">0xFFFFFFFF</value> <!-- type="BinHex" value="3078464646464646464600" -->
            <value name="sndGearShift_MajorDamage" hash="59C853C7" type="String">0xFFFFFFFF</value> <!-- type="BinHex" value="3078464646464646464600" -->
            <value name="sndThrustPedal" hash="A6A2A9B7" type="String">0x004EE92F</value> <!-- type="BinHex" value="3078303034454539324600" -->
```

## Guide \- DLC vehicle colour variety

By default in singleplayer the ATV is always blue and the Utility truck/Unimog is always grey. The multiplayer versions of these vehicles have a variety of colours, so this guide will cover transferring those colours to the singleplayer vehicles.

Step 1: Find the multiplayer DLC vehicle colour files

Go to your Far Cry 2 folder and copy “entitylibrary.fat/.dat” from \\Far Cry 2\\Data\_Win32\\downloadcontent\\dlc1\\.

Paste these files alongside your modding tools and unpack them.

There are three files you need to take from \\entitylibrary\_unpack\\graphics\\\_materials\\:

sdore2-m-2008101549108296.xbm (Utility truck/Unimog file 1\)  
sdore2-m-2008101549131546.xbm (Utility truck/Unimog file 2\)  
sdore2-m-2008101652084625.xbm (ATV)

Copy and paste these files into your patch at \\patch\_unpack\\graphics\\\_materials\\.

Step 2: Renaming the files

Rename the files to the following:

sdore2-m-2008101549108296.xbm → sdore2-m-2008081267040340.xbm

sdore2-m-2008101549131546.xbm → sdore2-m-2008081958233898.xbm

sdore2-m-2008101652084625.xbm → sdore2-m-2008100649183233.xbm

## Guide \- DLC vehicle upgrades

It is possible to add new vehicles upgrades for the ATV and unimog. The new upgrades work as intended except the repair animations don’t speed up like with the regular repair upgrades. The repair time still upgrades correctly though, so it’s only a visual difference. Adding the upgrades to the weapon shop also means replacing two existing ones, as we can’t add new entries. You can replace any of them but this guide will cover replacing the big truck and fishing boat upgrades, because these are the least used vehicles.

Step 1: Creating new upgrades  
gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

To create new upgrades we are going to redirect the big truck and fishing boat upgrades to the unimog and ATV. To do this we are going to replace the “object” values for the existing upgrades. The big truck object will be “unimog” and the fishing boat object will be “quad”. Your completed sections should look like this:

```xml {1}
<Plan name="bigtruck_vehicle_manual" object="unimog">
<bonus attr="degradation" value="-50" type="percent"/>
	<bonus attr="repairtime" value="-50" type="percent"/>
</Plan>
```

```xml {1}
<Plan name="fishingboat_vehicle_manual" object="quad">
	<bonus attr="degradation" value="-50" type="percent"/>
	<bonus attr="repairtime" value="-50" type="percent"/>
</Plan>
```

Step 2: Changing the upgrade names  
\\patch\_pack\\languages\\ \- Each language has its own folder and “oasisstrings.rml” file.

We are going to change the names of the big truck and fishing boat upgrades to match their new targets.

The entries are for “bigtruck” and “fishingboat” within the “Items” section. My completed section looks like this, and you can translate for each other language file:

```xml {1,2}
<string enum="fishingboat" value="ATV" />
<string enum="bigtruck" value="Utility Truck" />
```

## Guide \- Look back key

Looking back is already possible by pressing both mouse buttons but we can also create an equivalent function on a single, rebindable keyboard button.

Step 1: Create a new look back function  
inputactionmapcommon.xml (\\patch\_unpack\\config\\)

We are going to recreate the existing look back function but create it with a new binding.

Copy the section below into the “common\_in\_vehicle” section:

```xml
<CompoundInput name="look_pov" device="kb">
	<Binding input="v" axis="1" invert="1"/>
	<Binding input="" axis="1"/>
</CompoundInput>
<Binding input="kb:look_pov" action="press" signal="look_pov"/>
<Binding input="kb:look_pov" action="release" signal="look_pov"/>
```

(Optional) Step 2: Make the new binding rebindable

Adding the key binding to the default controls list  
defaultusercontrols.xml (\\patch\_unpack\\config\\)

To add the new key binding to the “Vehicles” in-game controls menu add the following line to the “CATEGORY\_VEHICLES” section:

```xml
<Control name="lookback" key1="kb:v" actionmap="common_lookback_remap" group="2" conflictmask="12"/>
```

The completed section should look like this:

```xml {6}
<Category name="CATEGORY_VEHICLES">
<Control name="accelerator" key1="kb:w" key2="kb:up" actionmap="common_driving_remap" group="2" conflictmask="12"/>
<Control name="reverse" key1="kb:s" key2="kb:down" actionmap="common_driving_remap" group="2" conflictmask="12"/>
<Control name="steerleft" key1="kb:a" key2="kb:left" actionmap="common_driving_remap" group="2" conflictmask="12"/>
<Control name="steerright" key1="kb:d" key2="kb:right" actionmap="common_driving_remap" group="2" conflictmask="12"/>
<Control name="lookback" key1="kb:v" actionmap="common_lookback_remap" group="2" conflictmask="12"/>
<Control name="toggle_headlights"  key1="kb:g" actionmap="common_driving_remap" group="2" conflictmask="12"/>
    	<Control name="hand_brake"  key1="kb:space" actionmap="common_driving_remap" group="2" conflictmask="12"/>
    	<Control name="change_seat" key1="kb:c" actionmap="common_changeseat_remap" group="2" conflictmask="12"/>
    	<Control name="exitvehicle" key1="kb:e" actionmap="common_exitvehicle_remap" group="2" conflictmask="12"/>	
</Category>
```

Link the changes to the default controls to the controls system  
Inputactionmapcommon.xml (\\patch\_unpack\\config\\)

To link our changes to the controls system add the following line below the “common\_in\_vehicle” title:

```xml
<Import actionmap="common_lookback_remap" optional=""/>
```

The completed section should look like this:

```xml {6}
<ActionMap name="common_in_vehicle">
<Import actionmap="common_in_vehicle_remap" optional=""/>
<import actionmap="common_heal_remap" optional=""/>
<Import actionmap="common_changeseat_remap" optional=""/>
<Import actionmap="common_exitvehicle_remap" optional=""/>
<Import actionmap="common_lookback_remap" optional=""/>
```

Add a new control label  
\\patch\_pack\\languages\\ \- Each language has its own folder and “oasisstrings.rml” file.

Find the section with the title “\<section name="Actions"\>”.

Add the following line into this section, with the correct words for your language in the “value” section:

```xml
<string enum="lookback" value="Look Back" />
```

## Guide \- Throwing grenades while driving

This guide will cover how to enable the use of grenades while driving. This is a buggy feature. The animation when throwing a grenade isn’t smooth and snaps the players view back and forward in a janky motion. The grenades also can’t break the windshield, so regular grenades will bounce back into the car and molotovs will immediately ignite unless the glass is broken. This will also happen if you hit the frame of the car.

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
<State FullName="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Driver Lowering arms" Type="CGOStateAnim">
```

```xml {1}
<State FullName="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Driver Throwing grenade" Type="CGOStateEquipment">
```

Now we are going to change the “Connection Target” values of both sections.

The connection target of “Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Driver Lowering arms” is going to be “Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Driver Throwing grenade”.

The connection target of “Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Driver Throwing grenade” is going to be “Vehicles/Vehicles/States/Driving”.

We are also going to change the “abort” Connection Target value of “Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Driver Throwing grenade” to “Vehicles/Vehicles/States/Driving”.

Your complete sections should look like this:

```xml {1,12,38,51,61}
<State FullName="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Driver Lowering arms" Type="CGOStateAnim">
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
	<Connection Target="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Driver Throwing grenade" />
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
<State FullName="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Driver Throwing grenade" Type="CGOStateEquipment">
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
	<Connection Target="::Vehicles/Vehicles/States/Driving" />
	<Event Name="Throw" Type="CGOStateEventPawn" Start="25" End="25">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="1" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="11" />
		<Parameter Name="simpleEventID" Value="" />
	</Event>
	<Sink Name="abort" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/Driving" Signal="abort" />
	</Sink>
</State>
```

Step 2: Editing the driving state  
vehicles.gosm.xml (\\patch\_unpack\\scripts\\engine\\objects\\pawn\\statemachine\\)

Find the driving state with this title: \<State FullName="::Vehicles/Vehicles/States/Driving" Type="CGOStateAnim"\>

We are going to add the following section to the bottom of the driving state:

```xml
<Sink Name="Throw grenade - Driving" Start="0" End="100">
	<Connection Target="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Driver Lowering arms" Signal="driver_throw_grenade" />
</Sink>
```

Your completed section should look like this:

```xml {27,28,29}
<State FullName="::Vehicles/Vehicles/States/Driving" Type="CGOStateAnim">
	<Parameter Name="groups" />
	<Parameter Name="duration" Value="0" />
	<Parameter Name="signalpriorities" />
	<Parameter Name="forceAnim" Value="1" />
	<Parameter Name="syncAnimDuration" Value="0" />
	<Parameter Name="animStateID" Value="Pawn_Generic_DrivingVehicule" />
	<Parameter Name="layerStateID" Value="0" />
	<Parameter Name="gestureStateID" Value="0" />
	<Parameter Name="followTerrain" Value="0" />
	<Parameter Name="MoveLayer" Value="-1" />
	<Event Name="toggle gadget" Type="CGOStateEventInventory" Start="0" End="100" Signal="toggle_gadget">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="0" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="32" />
		<Parameter Name="simpleEventID" Value="" />
	</Event>
	<Event Name="try heal" Type="CGOStateEventHeal" Start="0" End="100" Signal="heal">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="0" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="0" />
	</Event>
	<Sink Name="Throw grenade - player" Start="0" End="100">
		<Connection Target="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Driver Lowering arms" Signal="driver_throw_grenade" />
	</Sink>
	<Sink Name="Use Gadget" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/DrawGadget" Signal="switch" />
	</Sink>
	<Sink Name="heal" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/SyringeDriving" Signal="apply_syringe" />
	</Sink>
	<Sink Name="take pill" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/PillDriving" Signal="take_malaria_pill" />
	</Sink>
</State>
```

Step 3: Adding the new grenade throwing entries into the vehicle systems  
vehicles.gosm.xml (\\patch\_unpack\\scripts\\engine\\objects\\pawn\\statemachine\\)

We are now going to link our new grenade throwing entries into various vehicle systems that allow them to work.

This involves copying the following lines into various lists in this file:

```xml
<StateRef Path="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Driver Lowering arms" />
<StateRef Path="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Driver Throwing grenade" />
```
		

These lists have the following titles, you’ll see that there are already lists there. You simply need to copy the lines above into them:

```xml
<Group FullName="::Vehicles/Vehicles/AttachedToVehicle" Type="BaseGroup">
<Group FullName="::Vehicles/Vehicles/VehicleBackups" Type="BaseGroup">
<Group FullName="::Vehicles/Vehicles/SittingGroup" Type="BaseGroup">
<Group FullName="::Vehicles/Vehicles/VehicleNoNearZ" Type="BaseGroup">
<Group FullName="::Vehicles/Vehicles/DriverActionMap" Type="BaseGroup">
```

For example, here is one of the lists with the new lines added:  
    
```xml {14,15}
<Group FullName="::Vehicles/Vehicles/DriverActionMap" Type="BaseGroup">
	<StateRef Path="::Vehicles/Vehicles/States/Driving" />
	<StateRef Path="::Vehicles/Vehicles/States/DrawGadget" />
	<StateRef Path="::Vehicles/Vehicles/States/UseGadget" />
	<StateRef Path="::Vehicles/Vehicles/States/PillDriving" />
	<StateRef Path="::Vehicles/Vehicles/States/HolsterSwitchGadget" />
	<StateRef Path="::Vehicles/Vehicles/States/SyringeDriving" />
	<StateRef Path="::Vehicles/Vehicles/States/FlipMapVehicle" />
	<StateRef Path="::Vehicles/Vehicles/States/HolsterGadget" />
	<StateRef Path="::Vehicles/Vehicles/States/HolsterGadgetSyringe" />
	<StateRef Path="::Vehicles/Vehicles/States/HolsterGadgetPill" />
	<StateRef Path="::Vehicles/Vehicles/States/HolsterGadgetExitVehicle" />
	<StateRef Path="::Vehicles/Vehicles/States/HoslterGadgetChangeSeat" />
	<StateRef Path="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Driver Lowering arms" />
	<StateRef Path="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Driver Throwing grenade" />
```

Step 4: Adding new controls  
inputactionmapcommon.xml (\\patch\_unpack\\config\\)

Find the section of this file with the title: \<ActionMap name="common\_driving"\>

Copy and paste the following lines into this section, this includes a button to throw a grenade and also swap grenade type:

```xml
<Binding input="kb:q" action="press" signal="driver_throw_grenade"/>
<Binding input="kb:f" action="press" signal="select_next_throw_gadget"/>
<Binding input="pad:right_shoulder" action="press" signal="driver_throw_grenade"/>
<Binding input="pad:left" action="press" signal="select_next_throw_gadget"/>
```

These changes also require another modification to the controller preset, the headlights need to be moved, I suggest to up on the dpad: 

To do this find this line:

```xml
<Binding input="pad:right_shoulder" action="press" signal="toggle_headlights"/>
```

And change it to this:

```xml
<Binding input="pad:up" action="press" signal="toggle_headlights"/>
```

Also find the section with the title: \<ActionMap name="common\_passenger"\>

Copy and paste the following lines into this section:

```xml
<Binding input="kb:q" action="press" signal="driver_throw_grenade"/>
<Binding input="kb:f" action="press" signal="select_next_throw_gadget"/>
<Binding input="pad:right_shoulder" action="press" signal="driver_throw_grenade"/>
<Binding input="pad:left" action="press" signal="select_next_throw_gadget"/>
```

You can also add grenade throwing when using a hand glider. It’s pretty buggy as your view is snapped to looking straight down when throwing a grenade, but to do so find the section this this title:

```xml
<ActionMap name="common_paragliderdriving">
```

Add these lines:

```xml
<Binding input="kb:f" action="press" signal="select_next_throw_gadget"/>
<Binding input="kb:q" action="press" signal="driver_throw_grenade"/>
```

```xml
<Binding input="pad:left" action="press" signal="select_next_throw_gadget"/>
<Binding input="pad:right_shoulder" action="press" signal="driver_throw_grenade"/>
```

Step 5: Make the new controls rebindable  
inputactionmapcommon.xml (\\patch\_unpack\\config\\)

Controls being rebindable is controlled by the “import actionmap” lines directly below the titles of each section of this file.

As we haven’t added any brand new controls we just need the following line into the “import actionmap” sections of the driver and passenger controls sections:

```xml
<import actionmap="common_grenade_remap" optional=""/>
```

For example, your completed passenger section should look like this:

```xml {8}
<ActionMap name="common_passenger">
<import actionmap="common_heal_remap" optional=""/>
	<Import actionmap="common_shoot_remap" optional=""/>
	<Import actionmap="common_iron_remap" optional=""/>
	<Import actionmap="common_reload_remap" optional=""/>
	<Import actionmap="common_changeseat_remap" optional=""/>
	<Import actionmap="common_exitvehicle_remap" optional=""/>
	<import actionmap="common_grenade_remap" optional=""/>
```

## Guide \- Using weapons while driving

This guide will cover how to enable using secondary weapons while driving. This is a buggy feature. The animations don’t line up properly so there is a good amount of the player view jolting around or snapping forward, particularly in boats. The player’s arm also shows some pretty weird behaviour, especially the left arm while shooting and using any weapon that uses two arms.

Making this work is going to involve converting the passenger state to a new state where we can shoot weapons, so here we go.

Step 1: Editing the driver state  
vehicles.gosm.xml (\\patch\_unpack\\scripts\\engine\\objects\\pawn\\statemachine\\)

The driver state has this title:

```xml
<State FullName="::Vehicles/Vehicles/States/Driving" Type="CGOStateAnim">
```

We are going to add the following to this section, which will make it so you can draw your weapon by shooting or selecting your secondary:

```xml
<Event Name="Draw Weapon" Type="CGOStateEventInventory" Start="0" End="100" Signal="select_secondary_weapon">
	<Parameter Name="alwaysTrigger" Value="0" />
	<Parameter Name="triggerOnce" Value="0" />
	<Parameter Name="triggeredOnEnd" Value="0" />
	<Parameter Name="triggeredOnBegin" Value="0" />
	<Parameter Name="requestType" Value="11" />
	<Parameter Name="simpleEventID" Value="" />
</Event>
<Sink Name="Draw Weapon" Start="0" End="100">
	<Connection Target="::Vehicles/Vehicles/States/PassengerDrawWeapon" Signal="select_secondary_weapon" />
</Sink>
<Sink Name="Draw Weapon" Start="0" End="100">
	<Connection Target="::Vehicles/Vehicles/States/PassengerDrawWeapon" Signal="startshooting" />
</Sink>
```

Your finished section should look like this:

```xml {27,28,29,30,31,32,33,34,35,36,37,38,39,40}
<State FullName="::Vehicles/Vehicles/States/Driving" Type="CGOStateAnim">
<Parameter Name="groups" />
	<Parameter Name="duration" Value="0" />
	<Parameter Name="signalpriorities" />
	<Parameter Name="forceAnim" Value="1" />
	<Parameter Name="syncAnimDuration" Value="0" />
	<Parameter Name="animStateID" Value="Pawn_Generic_DrivingVehicule" />
	<Parameter Name="layerStateID" Value="0" />
	<Parameter Name="gestureStateID" Value="0" />
	<Parameter Name="followTerrain" Value="0" />
	<Parameter Name="MoveLayer" Value="-1" />
	<Event Name="toggle gadget" Type="CGOStateEventInventory" Start="0" End="100" Signal="toggle_gadget">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="0" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="32" />
		<Parameter Name="simpleEventID" Value="" />
	</Event>
	<Event Name="try heal" Type="CGOStateEventHeal" Start="0" End="100" Signal="heal">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="0" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="0" />
	</Event>
	<Event Name="Draw Weapon" Type="CGOStateEventInventory" Start="0" End="100" Signal="select_secondary_weapon">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="0" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="11" />
		<Parameter Name="simpleEventID" Value="" />
	</Event>
	<Sink Name="Draw Weapon" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/PassengerDrawWeapon" Signal="select_secondary_weapon" />
	</Sink>
	<Sink Name="Draw Weapon" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/PassengerDrawWeapon" Signal="startshooting" />
	</Sink>
	<Sink Name="Use Gadget" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/DrawGadget" Signal="switch" />
	</Sink>
	<Sink Name="heal" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/SyringeDriving" Signal="apply_syringe" />
	</Sink>
	<Sink Name="take pill" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/PillDriving" Signal="take_malaria_pill" />
	</Sink>
</State>
```

Step 2: Editing the passenger state  
vehicles.gosm.xml (\\patch\_unpack\\scripts\\engine\\objects\\pawn\\statemachine\\)

The passenger state has this title:

```xml
<State FullName="::Vehicles/Vehicles/States/Passenger" Type="CGOStateAnim">
```

There are lots of changes required here, not only editing the passenger state itself but also adding extra scripts so you can holster your weapon and  redraw it automatically when healing. Also included is a line that will allow this state to throw grenades, as long as you have followed the steps in that guide. It’s easiest if you select the entire passenger state section and paste the following over it:

```xml
<State FullName="::Vehicles/Vehicles/States/Passenger" Type="CGOStateAnim">
	<Parameter Name="groups" />
	<Parameter Name="duration" Value="0" />
	<Parameter Name="signalpriorities" />
	<Parameter Name="forceAnim" Value="1" />
	<Parameter Name="syncAnimDuration" Value="0" />
	<Parameter Name="animStateID" Value="Pawn_Generic_DrivingVehicule" />
	<Parameter Name="layerStateID" Value="0" />
	<Parameter Name="gestureStateID" Value="0" />
	<Parameter Name="followTerrain" Value="0" />
	<Parameter Name="MoveLayer" Value="-1" />
	<Event Name="vehicle beautifier" Type="CGOStateEventBeautifier" Start="0" End="100">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="1" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="action" Value="1" />
		<Parameter Name="context" Value="vehiclepassenger" />
	</Event>
	<Event Name="try heal" Type="CGOStateEventHeal" Start="0" End="100" Signal="heal">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="0" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="0" />
	</Event>
	<Event Name="Try Auto Draw" Type="CGOStateEventInventory" Start="0" End="1">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="1" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="1" />
		<Parameter Name="requestType" Value="38" />
		<Parameter Name="simpleEventID" Value="" />
	</Event>
	<Event Name="Try Start Shooting" Type="CGOStateEventEquipment" Start="0" End="100" Signal="startshooting">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="0" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="3" />
	</Event>
	<Event Name="Pull Trigger" Type="CGOStateEventPawn" Start="0" End="100" Signal="pull_trigger">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="0" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="12" />
		<Parameter Name="simpleEventID" Value="PullTrigger" />
	</Event>
	<Event Name="Try Reloading" Type="CGOStateEventEquipment" Start="0" End="100" Signal="reload">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="0" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="4" />
	</Event>
	<Event Name="Check equipment mode" Type="CGOStateEventInventory" Start="0" End="100">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="0" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="25" />
		<Parameter Name="simpleEventID" Value="" />
	</Event>
	<Event Name="toggle gadget" Type="CGOStateEventInventory" Start="0" End="100" Signal="toggle_gadget">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="0" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="32" />
		<Parameter Name="simpleEventID" Value="" />
	</Event>
	<Event Name="try exit" Type="CGOStateEventVehicle" Start="0" End="100" Signal="exitvehicle">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="0" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="11" />
	</Event>
	<Sink Name="switch gadget" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/HolsterSwitchGadget" Signal="switch" />
	</Sink>		
	<Sink Name="apply syringe" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/PassengerHolsterGadgetSyringe" Signal="apply_syringe" />
	</Sink>
	<Sink Name="malaria pill" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/HolsterGadgetPill" Signal="take_malaria_pill" />
	</Sink>
	<Sink Name="Fire Bullets" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/PassengerFireBullets" Signal="fire_bullet" />
	</Sink>
	<Sink Name="Exit Vehicle" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/PassengerExitHolster" Signal="exitvehicle_now" />
	</Sink>
	<Sink Name="Change Seat" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/HoslterGadgetChangeSeat" Signal="change_seat_now" />
	</Sink>
	<Sink Name="Sink1" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/PassengerUseIED" Signal="useied" />
	</Sink>
	<Sink Name="Holster Weapon" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/DriverHolsterWeapon" Signal="HolsterWeapons" />
	</Sink>
	<Sink Name="Holster Weapon" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/DriverHolsterWeapon" Signal="select_secondary_weapon" />
	</Sink>
	<Sink Name="Throw grenade - Driving" Start="0" End="100">
		<Connection Target="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Driver Lowering arms" Signal="driver_throw_grenade" />
	</Sink>
</State>
<State FullName="::Vehicles/Vehicles/States/DriverHolsterWeapon" Type="CGOStateAnim">
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
	<Connection Target="::Vehicles/Vehicles/States/Driving" />
	<Event Name="Holster" Type="CGOStateEventInventory" Start="100" End="100">
		<Parameter Name="alwaysTrigger" Value="1" />
		<Parameter Name="triggerOnce" Value="1" />
		<Parameter Name="triggeredOnEnd" Value="1" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="4" />
		<Parameter Name="simpleEventID" Value="" />
	</Event>
</State>
<State FullName="::Vehicles/Vehicles/States/PassengerHolsterGadgetSyringe" Type="CGOStateAnim">
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
	<Connection Target="::Vehicles/Vehicles/States/PassengerSyringeDriving" />
	<Event Name="holster" Type="CGOStateEventInventory" Start="99" End="100">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="1" />
		<Parameter Name="triggeredOnEnd" Value="1" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="4" />
		<Parameter Name="simpleEventID" Value="" />
	</Event>
</State>
<State FullName="::Vehicles/Vehicles/States/PassengerSyringeDriving" Type="CGOStateAnim">
	<Parameter Name="groups" />
	<Parameter Name="duration" Value="0" />
	<Parameter Name="signalpriorities" />
	<Parameter Name="forceAnim" Value="0" />
	<Parameter Name="syncAnimDuration" Value="0" />
	<Parameter Name="animStateID" Value="0" />
	<Parameter Name="layerStateID" Value="pawn_generic_healsyringe" />
	<Parameter Name="gestureStateID" Value="0" />
	<Parameter Name="followTerrain" Value="0" />
	<Parameter Name="MoveLayer" Value="-1" />
	<Connection Target="::Vehicles/Vehicles/States/PassengerDrawWeapon" />
	<Event Name="finish" Type="CGOStateEventHeal" Start="99" End="100">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="1" />
		<Parameter Name="triggeredOnEnd" Value="1" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="1" />
	</Event>
	<Event Name="cinematic input" Type="CGOStateEventInput" Start="0" End="1">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="1" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="1" />
		<Parameter Name="requestType" Value="2" />
		<Parameter Name="actionMapName" Value="cinematic" />
	</Event>
	<Event Name="pop input" Type="CGOStateEventInput" Start="99" End="100">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="1" />
		<Parameter Name="triggeredOnEnd" Value="1" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="5" />
		<Parameter Name="actionMapName" Value="cinematic" />
	</Event>
</State>
```

Step 3: Editing the ‘UseGadget’ state  
vehicles.gosm.xml (\\patch\_unpack\\scripts\\engine\\objects\\pawn\\statemachine\\)

The ‘UseGadget’ state is used when the player is using the map or phone. We are going to add the ability to draw your weapon and add a script that will holster the gadget before drawing.

The ‘UseGadget’ state has this title:

```xml
<State FullName="::Vehicles/Vehicles/States/UseGadget" Type="CGOStateAnim">
```

Add the following to this section:

```xml
<Event Name="Draw Weapon" Type="CGOStateEventInventory" Start="0" End="100" Signal="select_secondary_weapon">
	<Parameter Name="alwaysTrigger" Value="0" />
	<Parameter Name="triggerOnce" Value="0" />
	<Parameter Name="triggeredOnEnd" Value="0" />
	<Parameter Name="triggeredOnBegin" Value="0" />
	<Parameter Name="requestType" Value="11" />
	<Parameter Name="simpleEventID" Value="" />
</Event>
<Sink Name="Draw Weapon" Start="0" End="100">
	<Connection Target="::Vehicles/Vehicles/States/HolsterToDrawGadget" Signal="select_secondary_weapon" />
</Sink>		
```

Your complete section should look like this:

```xml {34,35,36,37,38,39,40,41,42,43,44}
<State FullName="::Vehicles/Vehicles/States/UseGadget" Type="CGOStateAnim">
	<Parameter Name="groups" />
	<Parameter Name="duration" Value="0" />
	<Parameter Name="signalpriorities" />
	<Parameter Name="forceAnim" Value="0" />
	<Parameter Name="syncAnimDuration" Value="0" />
	<Parameter Name="animStateID" Value="0" />
	<Parameter Name="layerStateID" Value="Pawn_Generic_Aim" />
	<Parameter Name="gestureStateID" Value="0" />
	<Parameter Name="followTerrain" Value="0" />
	<Parameter Name="MoveLayer" Value="-1" />
	<Event Name="try exit" Type="CGOStateEventVehicle" Start="0" End="100" Signal="exitvehicle">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="0" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="11" />
	</Event>
	<Event Name="toggle gadget" Type="CGOStateEventInventory" Start="0" End="100" Signal="toggle_gadget">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="0" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="32" />
		<Parameter Name="simpleEventID" Value="" />
	</Event>
	<Event Name="try heal" Type="CGOStateEventHeal" Start="0" End="100" Signal="heal">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="0" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="0" />
	</Event>		
	<Event Name="Draw Weapon" Type="CGOStateEventInventory" Start="0" End="100" Signal="select_secondary_weapon">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="0" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="11" />
		<Parameter Name="simpleEventID" Value="" />
	</Event>
	<Sink Name="Draw Weapon" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/HolsterToDrawGadget" Signal="select_secondary_weapon" />
	</Sink>		
	<Sink Name="switch gadget" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/HolsterSwitchGadget" Signal="switch" />
	</Sink>
	<Sink Name="flip map" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/FlipMapVehicle" Signal="flipside" />
	</Sink>
	<Sink Name="exit vehicle" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/HolsterGadgetExitVehicle" Signal="exitvehicle_now" />
	</Sink>
	<Sink Name="holster gadget" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/HolsterGadget" Signal="holsterweapon_now" />
	</Sink>
	<Sink Name="apply syringe" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/HolsterGadgetSyringe" Signal="apply_syringe" />
	</Sink>
	<Sink Name="malaria pill" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/HolsterGadgetPill" Signal="take_malaria_pill" />
	</Sink>
	<Sink Name="Change Seat" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/HoslterGadgetChangeSeat" Signal="change_seat_now" />
	</Sink>
	<Sink Name="Jump out" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/HolsterGadgetExitVehicle" Signal="jumpout_of_vehicle" />
	</Sink>
	<Sink Name="holster the phone" Start="0" End="100">
		<Connection Target="::Vehicles/Vehicles/States/HolsterGadget" Signal="select_next_weapon" />
	</Sink>
</State>
```

Now add this section directly below:

```xml
<State FullName="::Vehicles/Vehicles/States/HolsterToDrawGadget" Type="CGOStateAnim">
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
	<Connection Target="::Vehicles/Vehicles/States/PassengerDrawWeapon" />
	<Event Name="Holster" Type="CGOStateEventInventory" Start="99" End="100">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="1" />
		<Parameter Name="triggeredOnEnd" Value="1" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="4" />
		<Parameter Name="simpleEventID" Value="" />
	</Event>
</State>
```

Step 4: Editing other passenger states  
vehicles.gosm.xml (\\patch\_unpack\\scripts\\engine\\objects\\pawn\\statemachine\\)

There are a few other passenger sections that need to be edited so everything works together.

1. \<State FullName="::Vehicles/Vehicles/States/PassengerFireBullets" Type="CGOStateEquipment"\>

Within this section find these two lines:

```xml
<Parameter Name="animStateID" Value="Pawn_Generic_PassengerVehicle" />
<Parameter Name="layerStateID" Value="Pawn_Generic_Shoot" />
```

Edit them to this:

```xml
<Parameter Name="animStateID" Value="Pawn_Generic_DrivingVehicule" />
<Parameter Name="layerStateID" Value="Pawn_Generic_Aim" />
```

2. \<State FullName="::Vehicles/Vehicles/States/PassengerJam" Type="CGOStateAnim"\>

	Within this section find this line:

```xml
<Parameter Name="animStateID" Value="Pawn_Generic_PassengerVehicle" />
```

Edit it to this:

```xml
<Parameter Name="animStateID" Value="Pawn_Generic_DrivingVehicule" />
```

3. \<State FullName="::Vehicles/Vehicles/States/PassengerTryUnJam" Type="CGOStateAnim"\>

	Within this section find this line:

```xml
<Parameter Name="animStateID" Value="Pawn_Generic_PassengerVehicle" />
```

Edit it to this:

```xml
<Parameter Name="animStateID" Value="Pawn_Generic_DrivingVehicule" />
```

4. \<State FullName="::Vehicles/Vehicles/States/PassengerUnJamSuccess" Type="CGOStateEquipment"\>

Within this section find this line:

```xml
<Parameter Name="animStateID" Value="Pawn_Generic_PassengerVehicle" />
```

Edit it to this:

```xml
<Parameter Name="animStateID" Value="Pawn_Generic_DrivingVehicule" />
```

 Step 5: Adding everything to the existing driving systems  
vehicles.gosm.xml (\\patch\_unpack\\scripts\\engine\\objects\\pawn\\statemachine\\)

For this step we are going to add different states into the existing driving systems. These systems are basically just lists of titles. You’ll see that below these titles there are already lists, you simply need to add the new ones to the bottom of them.

1. \<Group FullName="::Vehicles/Vehicles/AttachedToVehicle" Type="BaseGroup"\>

	Add these to the list:

```xml
	<StateRef Path="::Vehicles/Vehicles/States/HolsterToDrawGadget" />
	<StateRef Path="::Vehicles/Vehicles/States/DriverDrawWeapon" />
	<StateRef Path="::Vehicles/Vehicles/States/DriverHolsterWeapon" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerHolsterGadgetSyringe" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerSyringeDriving" />
```

2. \<Group FullName="::Vehicles/Vehicles/VehicleBackups" Type="BaseGroup"\>

	Add these to the list:

```xml
	<StateRef Path="::Vehicles/Vehicles/States/HolsterToDrawGadget" />
	<StateRef Path="::Vehicles/Vehicles/States/DriverDrawWeapon" />
	<StateRef Path="::Vehicles/Vehicles/States/DriverHolsterWeapon" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerHolsterGadgetSyringe" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerSyringeDriving" />
```

3. \<Group FullName="::Vehicles/Vehicles/SittingGroup" Type="BaseGroup"\>

	For this one we are going to combine the “SittingGroup” section with the “Passenger section. So highlight both of these sections:

```xml
	<Group FullName="::Vehicles/Vehicles/SittingGroup" Type="BaseGroup">
		<StateRef Path="::Vehicles/Vehicles/States/Driving" />
		<StateRef Path="::Vehicles/Vehicles/States/DrawGadget" />
		<StateRef Path="::Vehicles/Vehicles/States/UseGadget" />
		<StateRef Path="::Vehicles/Vehicles/States/HolsterGadget" />
		<StateRef Path="::Vehicles/Vehicles/States/HolsterSwitchGadget" />
		<StateRef Path="::Vehicles/Vehicles/States/HolsterGadgetSyringe" />
		<StateRef Path="::Vehicles/Vehicles/States/HolsterGadgetPill" />
		<StateRef Path="::Vehicles/Vehicles/States/FlipMapVehicle" />
		<StateRef Path="::Vehicles/Vehicles/States/SyringeDriving" />
		<StateRef Path="::Vehicles/Vehicles/States/PillDriving" />
		<StateRef Path="::Vehicles/Vehicles/States/HolsterGadgetExitVehicle" />
		<StateRef Path="::Vehicles/Vehicles/States/HoslterGadgetChangeSeat" />
		<Event Name="StandUp" Type="CGOStateEventVehicle" Position="End">
			<Parameter Name="alwaysTrigger" Value="0" />
			<Parameter Name="triggerOnce" Value="0" />
			<Parameter Name="triggeredOnEnd" Value="0" />
			<Parameter Name="triggeredOnBegin" Value="0" />
			<Parameter Name="requestType" Value="2" />
		</Event>
		<Event Name="Sit" Type="CGOStateEventVehicle" Position="Begin">
			<Parameter Name="alwaysTrigger" Value="0" />
			<Parameter Name="triggerOnce" Value="0" />
			<Parameter Name="triggeredOnEnd" Value="0" />
			<Parameter Name="triggeredOnBegin" Value="0" />
			<Parameter Name="requestType" Value="1" />
		</Event>
		<Event Name="Vehicle Beautifier" Type="CGOStateEventBeautifier" Start="0" End="100">
			<Parameter Name="alwaysTrigger" Value="0" />
			<Parameter Name="triggerOnce" Value="1" />
			<Parameter Name="triggeredOnEnd" Value="0" />
			<Parameter Name="triggeredOnBegin" Value="0" />
			<Parameter Name="action" Value="1" />
			<Parameter Name="context" Value="vehicle" />
		</Event>
	</Group>
	<Group FullName="::Vehicles/Vehicles/Passenger" Type="BaseGroup">
		<StateRef Path="::Vehicles/Vehicles/States/Passenger" />
		<StateRef Path="::Vehicles/Vehicles/States/PassengerFireBullets" />
		<StateRef Path="::Vehicles/Vehicles/States/PassengerDrawWeapon" />
		<StateRef Path="::Vehicles/Vehicles/States/PassengerReloading" />
		<StateRef Path="::Vehicles/Vehicles/States/PassengerJam" />
		<StateRef Path="::Vehicles/Vehicles/States/PassengerTryUnJam" />
		<StateRef Path="::Vehicles/Vehicles/States/PassengerUnJamSuccess" />
		<StateRef Path="::Vehicles/Vehicles/States/PassengerExitHolster" />
		<StateRef Path="::Vehicles/Vehicles/States/PassengerChangeSeatHolster" />
		<StateRef Path="::Vehicles/Vehicles/States/HolsterIED" />
		<StateRef Path="::Vehicles/Vehicles/States/PassengerUseIED" />												<Event Name="Sit" Type="CGOStateEventVehicle" Position="Begin">
			<Parameter Name="alwaysTrigger" Value="0" />
			<Parameter Name="triggerOnce" Value="1" />
			<Parameter Name="triggeredOnEnd" Value="0" />
			<Parameter Name="triggeredOnBegin" Value="1" />
			<Parameter Name="requestType" Value="1" />
		</Event>
		<Event Name="Stand Up" Type="CGOStateEventVehicle" Position="End">
			<Parameter Name="alwaysTrigger" Value="0" />
			<Parameter Name="triggerOnce" Value="1" />
			<Parameter Name="triggeredOnEnd" Value="1" />
			<Parameter Name="triggeredOnBegin" Value="0" />
			<Parameter Name="requestType" Value="2" />
		</Event>
		<Event Name="Push Action Map" Type="CGOStateEventInput" Position="Begin">
			<Parameter Name="alwaysTrigger" Value="0" />
			<Parameter Name="triggerOnce" Value="1" />
			<Parameter Name="triggeredOnEnd" Value="0" />
			<Parameter Name="triggeredOnBegin" Value="1" />
			<Parameter Name="requestType" Value="2" />
			<Parameter Name="actionMapName" Value="passenger" />
		</Event>
		<Event Name="Pop Action Map" Type="CGOStateEventInput" Position="End">
			<Parameter Name="alwaysTrigger" Value="0" />
			<Parameter Name="triggerOnce" Value="0" />
			<Parameter Name="triggeredOnEnd" Value="0" />
			<Parameter Name="triggeredOnBegin" Value="0" />
			<Parameter Name="requestType" Value="5" />
			<Parameter Name="actionMapName" Value="passenger" />
		</Event>
	</Group>
```

	  
With all of the above highlighted, paste this over it:

```xml
<Group FullName="::Vehicles/Vehicles/SittingGroup" Type="BaseGroup">
	<StateRef Path="::Vehicles/Vehicles/States/Driving" />
	<StateRef Path="::Vehicles/Vehicles/States/DrawGadget" />
	<StateRef Path="::Vehicles/Vehicles/States/UseGadget" />
	<StateRef Path="::Vehicles/Vehicles/States/HolsterGadget" />
	<StateRef Path="::Vehicles/Vehicles/States/HolsterSwitchGadget" />
	<StateRef Path="::Vehicles/Vehicles/States/HolsterGadgetSyringe" />
	<StateRef Path="::Vehicles/Vehicles/States/HolsterGadgetPill" />
	<StateRef Path="::Vehicles/Vehicles/States/FlipMapVehicle" />
	<StateRef Path="::Vehicles/Vehicles/States/SyringeDriving" />
	<StateRef Path="::Vehicles/Vehicles/States/PillDriving" />
	<StateRef Path="::Vehicles/Vehicles/States/HolsterGadgetExitVehicle" />
	<StateRef Path="::Vehicles/Vehicles/States/HoslterGadgetChangeSeat" />
	<StateRef Path="::Vehicles/Vehicles/States/Passenger" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerFireBullets" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerDrawWeapon" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerReloading" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerJam" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerTryUnJam" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerUnJamSuccess" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerExitHolster" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerChangeSeatHolster" />
	<StateRef Path="::Vehicles/Vehicles/States/HolsterIED" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerUseIED" />
	<StateRef Path="::Vehicles/Vehicles/States/HolsterToDrawGadget" />
	<StateRef Path="::Vehicles/Vehicles/States/DriverDrawWeapon" />
	<StateRef Path="::Vehicles/Vehicles/States/DriverHolsterWeapon" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerHolsterGadgetSyringe" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerSyringeDriving" />
	<StateRef Path="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Driver Lowering arms" />
	<StateRef Path="::Pawn Weapons/Weapon Mechanics/States/Throwing grenade/Player/Driver Throwing grenade" />
	<Event Name="Sit" Type="CGOStateEventVehicle" Position="Begin">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="1" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="1" />
		<Parameter Name="requestType" Value="1" />
	</Event>
	<Event Name="Stand Up" Type="CGOStateEventVehicle" Position="End">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="1" />
		<Parameter Name="triggeredOnEnd" Value="1" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="requestType" Value="2" />
	</Event>
	<Event Name="Vehicle Beautifier" Type="CGOStateEventBeautifier" Start="0" End="100">
		<Parameter Name="alwaysTrigger" Value="0" />
		<Parameter Name="triggerOnce" Value="1" />
		<Parameter Name="triggeredOnEnd" Value="0" />
		<Parameter Name="triggeredOnBegin" Value="0" />
		<Parameter Name="action" Value="1" />
		<Parameter Name="context" Value="vehicle" />
	</Event>
</Group>
```

4. \<Group FullName="::Vehicles/Vehicles/VehicleCanUseGadget" Type="BaseGroup"\>

	Add this to the list:

```xml
	<StateRef Path="::Vehicles/Vehicles/States/Passenger" />
```

5. \<Group FullName="::Vehicles/Vehicles/VehicleNoNearZ" Type="BaseGroup"\>

Add this to the list:

```xml
	<StateRef Path="::Vehicles/Vehicles/States/HolsterToDrawGadget" />
	<StateRef Path="::Vehicles/Vehicles/States/DriverDrawWeapon" />
	<StateRef Path="::Vehicles/Vehicles/States/DriverHolsterWeapon" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerHolsterGadgetSyringe" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerSyringeDriving" />
```

6. \<Group FullName="::Vehicles/Vehicles/DriverActionMap" Type="BaseGroup"\>

	Add this to the list:

```xml
	<StateRef Path="::Vehicles/Vehicles/States/Passenger" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerFireBullets" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerDrawWeapon" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerReloading" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerJam" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerTryUnJam" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerUnJamSuccess" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerExitHolster" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerChangeSeatHolster" />
	<StateRef Path="::Vehicles/Vehicles/States/HolsterToDrawGadget" />
	<StateRef Path="::Vehicles/Vehicles/States/DriverDrawWeapon" />
	<StateRef Path="::Vehicles/Vehicles/States/DriverHolsterWeapon" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerHolsterGadgetSyringe" />
	<StateRef Path="::Vehicles/Vehicles/States/PassengerSyringeDriving" />
```

Step 6: Adding new controls  
inputactionmapcommon.xml (\\patch\_unpack\\config\\)

There are two parts to this, adding new controls and editing existing ones.

1. Adding new controls

Find the section with this title: \<ActionMap name="common\_driving"\>

Add these lines somewhere below the title:

```xml
<Binding input="mouse:lb" action="press" signal="startshooting"/>
<Binding input="mouse:lb" action="release" signal="stopshooting"/>
<Binding input="kb:r" action="press" signal="reload"/>
<Binding input="kb:2" action="press" signal="select_secondary_weapon"/>
<Binding input="kb:f" action="press" signal="select_next_throw_gadget"/>
<Binding input="kb:q" action="press" signal="driver_throw_grenade"/>
<Binding input="kb:x" action="press" signal="HolsterWeapons"/>	
```

```xml
<Binding input="pad:right_thumb_push" action="press" signal="startshooting"/>
<Binding input="pad:right_thumb_push" action="release" signal="stopshooting"/>
<Binding input="pad:x" action="press" signal="reload"/>
<Binding input="pad:right" action="press" signal="select_secondary_weapon"/>
<Binding input="pad:left" action="press" signal="select_next_throw_gadget"/>
<Binding input="pad:right_shoulder" action="press" signal="driver_throw_grenade"/>
<Binding input="pad:down" action="press" signal="HolsterWeapons"/>
```

2. Editing existing keyboard controls

   We are going to free up the left mouse button for shooting by removing it’s binding and making it so that you can look back using only the right mouse button.

   

   Find this section:

   

```xml
   <CompoundInput name="look_pov" device="mouse">
```

```xml
   	<Binding input="rb" axis="0"/>
```

```xml
   	<Binding input="lb" axis="0" invert="1"/>
```

```xml
   	<Binding input="" axis="1"/>
```

```xml
   </CompoundInput>
```

   

   

   Change it to this:

   

```xml
   <CompoundInput name="look_pov" device="mouse">
```

```xml
   	<Binding input="rb" axis="1" invert="1"/>										
```

```xml
   	<Binding input="" axis="0"/>
```

```xml
   </CompoundInput>
```

   

   

   

3. Editing existing controller controls

	First, find this section:

```xml
	<CompoundInput name="look_pov" device="pad">
		<Binding input="right_thumb_push" axis="1" invert="1"/>
		<Binding input="" axis="0"/>
     	</CompoundInput>
```

	Change it to this:

```xml {2}
	<CompoundInput name="look_pov" device="pad">
		<Binding input="left_thumb_push" axis="1" invert="1"/>
		<Binding input="" axis="0"/>
     	</CompoundInput>
```

	Also, find this line:

```xml
	<Binding input="pad:right_shoulder" action="press" signal="toggle_headlights"/>
```

	Change it to this:

```xml
	<Binding input="pad:up" action="press" signal="toggle_headlights"/>
```

Once you’ve made these changes, these are the new controller bindings when driving vehicles:  
Step 7: Make the new controls rebindable  
inputactionmapcommon.xml (\\patch\_unpack\\config\\)

Controls being rebindable is controlled by the “import actionmap” lines directly below the titles of each section of this file.

As we haven’t added any brand new controls we just need the following lines into the “import actionmap” section of the driver controls section:

```xml
<Import actionmap="common_weapons_remap" optional=""/>
<Import actionmap="common_shoot_remap" optional=""/>
<Import actionmap="common_reload_remap" optional=""/>
```

Your complete section should look like this:

```xml {4,5,6}
<ActionMap name="common_driving">
<Import actionmap="common_driving_remap" optional=""/>
<Import actionmap="common_gadget_remap" optional=""/>
<Import actionmap="common_weapons_remap" optional=""/>
<Import actionmap="common_shoot_remap" optional=""/>
<Import actionmap="common_reload_remap" optional=""/>
```

