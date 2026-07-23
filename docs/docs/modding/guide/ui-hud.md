---
sidebar_position: 7
sidebar_label: "UI/HUD"
format: md
---

:::info[From the Almost Complete Guide]
This page reproduces a section of ["An Almost Complete Guide to Far Cry 2 Modding"](./index.md) by Boggalog, verbatim, with attribution. See the [guide index](./index.md) for the full attribution note.
:::

# UI/HUD

## In-game text

\\patch\_pack\\languages\\ \- Each language has its own folder and “oasisstrings.rml” file

It’s possible to change the in-game text for each language independently. This includes all text: menus, tutorials, subtitles, weapon names, vehicle names etc.

The instructions for handling the files are in the “File Management” section.

Here are my tips:

1. When you open it you’ll see that the subtitles are numbered and everything else has a description.  
2. If you change the name of something make sure you Ctrl-F for every use of the old name. They can be used for a few different things.  
3. It’s possible to add entries to this file, just make sure your new entry is in the correct section (e.g. menu items go with the others).  
 


## Weapon images

Each weapon image file has a relevant hex string and these are linked to the ui weapon files which are found in your common.fat/.dat files (\\common\_unpack\\ui\\textures\\hud\\icons\_weapons\\). These hex strings are listed below, in brackets I have included extra values that are only needed when editing the convoy reward popups. I wasn’t able to find the bracketed values for every weapon image, I don’t know how essential they are so you’ll need to do your own experimentation if you want to use them.

Makarov \- 72 A5 F8 24 (1F)  
Silenced Makarov \- 44 C2 75 53 (1B)  
Star .45 \- 5E 97 08 17 (1E)  
Eagle .50 \- BC 19 47 CE (1A)

Mac 10 \- 71 95 5D 26 (1D)  
Uzi \- 8B 2E 0E B3 (1B)

Ithaca \- BE 5F 29 D6  
Spas 12 \- 8F DF A1 99 (1E)  
USAS 12 \- 76 E7 24 AB (1E)  
Silenced Shotgun \- CC 5E 7E 2D  
Sawed-off Shotgun \- 46 76 30 4F

G3KA4 \- 4A CF 24 D2  
AK47 \- 30 10 68 48 (1C)  
FAL \- B7 0C 32 74  
M16 \- 82 02 88 E7  
MP5 \- B3 CB 99 C7 (1B)

M1903 \- 4A 7E A8 CD  
Dragunov \- 19 63 FC B5 (20)  
AS50 \- 1A 46 22 DD (1C)  
Dart Rifle \- 45 48 B7 D2 (21)

PKM \- 69 0B 71 E1 (1B)  
M249 SAW \- 50 D1 26 BE (1C)

M79 \- 75 FE 20 19 (1B)  
MGL 140 \- 0F BF A3 2C (1D)  
RPG \- E5 8E A5 1E (1C)  
Carl G \- A3 8E 25 0C (22)  
Crossbow \- EF 1C B9 5B  
Mortar \- 07 4E 49 C9 (1E)  
Flamethrower \- 81 76 50 C7 (1D)  
IED \- 16 C9 1E 41  
Flare Gun \- 2D 5C 4F 4E

M67 \- D3 A4 CE DF  
Molotov \- 93 13 80 48

Machete \- 88 9E 61 5D

Syrette \- 29 4C 97 79  
Bullets \- 3B 1B 88 CA

### Tutorial images

hud.mgb (\\patch\_unpack\\ui\\)  
There are different folders for widescreen/non-widescreen aspect ratios and the different languages, it’s pretty self-explanatory when you see it.

In the tutorial each weapon type (primray, secondary, special, machete) has its own section with a few example weapons. Similarly, within “hud.mgb” each weapon type has its own section with its own title. To edit the tutorial weapons you must only change the weapon hex strings directly under these titles. This is relevant because when you Ctrl-F the values each weapon can show up multiple times. 

These are the titles and their hex codes:

Machete  
T.U.T.O.R.I.A.L.\_.W.E.A.P.O.N.\_.M.A.C.H.E.T.E  
54 00 55 00 54 00 4F 00 52 00 49 00 41 00 4C 00  
5F 00 57 00 45 00 41 00 50 00 4F 00 4E 00 5F 00  
4D 00 41 00 43 00 48 00 45 00 54 00 45

Primary weapons  
TU.T.O.R.I.A.L.\_.W.E.A.P.O.N.\_.P.R.I.M.A.R.Y.\_.3  
54 00 55 00 54 00 4F 00 52 00 49 00 41 00 4C 00  
5F 00 57 00 45 00 41 00 50 00 4F 00 4E 00 5F 00  
50 00 52 00 49 00 4D 00 41 00 52 00 59 00 5F 00  
33

Secondary weapons  
T.U.T.O.R.I.A.L.\_.W.E.A.P.O.N.\_.S.E.C.O.N.D.A.R.Y.\_.3  
54 00 55 00 54 00 4F 00 52 00 49 00 41 00 4C 00  
5F 00 57 00 45 00 41 00 50 00 4F 00 4E 00 5F 00  
53 00 45 00 43 00 4F 00 4E 00 44 00 41 00 52 00  
59 00 5F 00 33

Special weapons  
T.U.T.O.R.I.A.L.\_.W.E.A.P.O.N.\_.S.P.E.C.I.A.L.\_.4  
54 00 55 00 54 00 4F 00 52 00 49 00 41 00 4C 00  
5F 00 57 00 45 00 41 00 50 00 4F 00 4E 00 5F 00  
53 00 50 00 45 00 43 00 49 00 41 00 4C 00 5F 00  
34

Within these sections you can ctrl-f the weapons you want to swap and replace their hex strings with the ones i have listed about. For reference the default tutorial screen looks like this:

### Convoy mission reward images

hud.mgb (\\patch\_unpack\\ui\\)  
There are different folders for widescreen/non-widescreen aspect ratios and the different languages, it’s pretty self-explanatory when you see it.

There are 8 total reward screens for the different convoys but unfortunately they aren’t numbered within this file. To navigate each convoy entry they each have a reference to “TU70A message” (54 00 55 00 37 00 30 00 41\) and the weapons for each convoy are listed below this. It is possible to navigate between each weapon by searching for “W.E.A.P.O.N.B.A.Z.A.A.R” (57 00 45 00 41 00 50 00 4F 00 4E 00 42 00 41 00 5A 00 41 00 41 00 52\) but as you will see it will be useful to be able to navigate each convoy also.

The structure for each convoy is the same throughout. There is a lot of other hex between the listed sections but this is the order they appear as you go further down the file:

1) T.U.7.0.A.\_.M.E.S.S.A.G.E

2) Hex strings for the relevant weapon images

3) Sections for each weapon rewarded by the convoy mission

To edit the images there are three things to edit: 

1\) The hex string for the relevant weapon. The same that are listed above for every weapon.

2\) Each weapon section has a title that looks like this: W.E.A.P.O.N.B.A.Z.A.A.R.\_.U.Z.I.\_.C.R.A.T.E  
This title needs to be changed to the new weapon.  
   
3\) Directly before the title for each weapon there is a value that needs to be changed. This is always four hex values before the title. I have listed these above in brackets for each weapon for those I could find it for.

Once all three of these have been changed for the new weapon the image will be updated in game.

For reference these are the convoy rewards:

### 

### Weapon shop advert images

weapon\_bazaar.mgb (\\patch\_unpack\\ui\\)  
There are different folders for widescreen/non-widescreen aspect ratios and the different languages, it’s pretty self-explanatory when you see it.

For these images you can simply swap out the hex strings with those listed above. Each advert has a title at the end of it’s section and it looks like this, with the number directly after: C.O.M.P.U.T.E.R.\_.A.D.V.E.R.T (43 00 4F 00 4D 00 50 00 55 00 54 00 45 00 52 00 5F 00 41 00 44 00 56 00 45 00 52 00 54). There are nine adverts, not all of them show weapons and there are a few weapons that appear multiple times.

These are the different adverts:

Computer advert 1

Computer advert 2

Computer advert 3

Computer advert 4

Computer advert 5

# 

Computer advert 6

# 

Computer advert 7

Computer advert 8

# 

Computer advert 9

# 

## HUD fade time

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

HUD fade time is controlled by the “fadeOutDelay” stat in the line that starts with “UI name="CFCXMainHudUI”.

It is a countdown measured in seconds, with a default value of 3\.

```xml {1}
<UI name="CFCXMainHudUI" class="CFCXMainHudUI" fadeOutDelay="3.0" reloadPromptAmmoRatio="0.25" maxRockets="4" />
```

## Reload prompt

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

The reload prompt is controlled by the “reloadPromptAmmoRatio” stat in the line that starts with “UI name="CFCXMainHudUI”.

It is a proportional value which describes the amount of ammo left in a weapon’s magazine, so the default value of 0.25 means the reload prompt appears when there is 25% or less of a magazine remaining.

```xml {1}
<UI name="CFCXMainHudUI" class="CFCXMainHudUI" fadeOutDelay="3.0" reloadPromptAmmoRatio="0.25" maxRockets="4" />
```

## Removing the flashing save reminder from map/GPS safehouse icons

Dunia.dll (\\Far Cry 2\\bin\\)

1. Open Dunia.dll in your hex editor.  
2. For the GPS safehouse icon search for “gadgets.ObjectiveIcons.SaveDiskGPS” or the hex bytes “67 61 64 67 65 74 73 2E 4F 62 6A 65 63 74 69 76 65 49 63 6F 6E 73 2E 53 61 76 65 44 69 73 6B 47 50 53”. There’s only one instance of this.  
3. Directly below this there is “gadgets.ObjectiveIcons.SaveDisk” or the hex bytes “67 61 64 67 65 74 73 2E 4F 62 6A 65 63 74 69 76  
   65 49 63 6F 6E 73 2E 53 61 76 65 44 69 73 6B”.  
4. We’re going to remove either one of “gadgets.ObjectiveIcons.SaveDiskGPS” or “gadgets.ObjectiveIcons.SaveDisk” or both by entering 00 over all of the hex bytes. Put your zeroes in the hex section to the left, don’t put them in the section to the right. If removing both it should look like this:  
     
   

## 

## 

## 

## 

## 

## 

## Limited navigation

### Removing markers from the map/gps

xx\_gadgets.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Each item shown on the map, GPS and vehicle GPS has a separate entry in this file. You can remove as many as you prefer, for a comprehensive limited navigation feature I suggest removing the following:

Map icons:

gadgets.ObjectiveIcons.GuardPost\_Locked  
gadgets.ObjectiveIcons.MissionArrow  
gadgets.ObjectiveIcons.PlayerPosition  
gadgets.ObjectiveIcons.SubvertArrow  
gadgets.ObjectiveIcons.UnderGroundArrow

GPS/Vehicle GPS icons:

gadgets.ObjectiveIcons.CompassObjective  
gadgets.ObjectiveIcons.CompassObjective\_VEH  
gadgets.ObjectiveIcons.CompassSubvertAnte  
gadgets.ObjectiveIcons.CompassSubvertAnte\_VEH  
gadgets.ObjectiveIcons.CompassUndergroundObjective  
gadgets.ObjectiveIcons.CompassUndergroundObjective\_VEH  
gadgets.ObjectiveIcons.MissionArrowGPS  
gadgets.ObjectiveIcons.MissionArrowGPS\_VEH  
gadgets.ObjectiveIcons.PartnerMissionObjective\_GPS  
gadgets.ObjectiveIcons.PartnerMissionObjective\_GPS\_VEH  
gadgets.ObjectiveIcons.SafeHouse\_Locked\_GPS  
gadgets.ObjectiveIcons.SafeHouse\_Locked\_GPS\_VEH  
gadgets.ObjectiveIcons.SafeHouse\_Unlocked\_GPS  
gadgets.ObjectiveIcons.SafeHouse\_Unlocked\_GPS\_VEH  
gadgets.ObjectiveIcons.SubvertArrowGPS  
gadgets.ObjectiveIcons.SubvertArrowGPS\_VEH  
gadgets.ObjectiveIcons.UnderGroundArrowGPS  
gadgets.ObjectiveIcons.UnderGroundArrowGPS\_VEH

To disable these you need to remove the small section of each that starts with “\<object type="object"\>”:

```xml
<object type="object">
     <value name="hidIndex" type="UInt32">0</value>
     <value hash="BF9B3A5C" type="String">graphics\objects\mapcompass\icon_playerpos.xbg</value>
     <value name="objModel" type="Hash">4311B3CD</value>
     <value name="hidMeshName" type="String"></value>
     <value hash="E1A0EE56" type="String">Icon_PlayerPos</value>
     <value name="hidNodeName" type="Hash">BEDD52CA</value>
     <value hash="0D9C8B1A" type="String">Icon_PlayerPos_LOD0</value>
     <value name="hidNodeNameLOD0" type="Hash">591F2AD4</value>
     <value name="hidDetailObject" type="Bool">False</value>
</object>
```

For example, here is the entry for the map player arrow with the section removed:

```xml
<object hash="256A1FF9">
    <value name="Name" type="String">ObjectiveIcons.PlayerPosition</value>
    <object type="Entity">
      <value name="hidName" type="String">gadgets.ObjectiveIcons.PlayerPosition</value>
      <value name="disEntityId" type="UInt64">367</value>
      <value hash="D2B3429E" type="String">CEntity</value>
      <value name="hidEntityClass" type="Hash">50C95067</value>
      <value name="hidResourceCount" type="UInt32">1</value>
      <value name="hidPos" type="Vector3">
        <x>0</x>
        <y>0</y>
        <z>0</z>
      </value>
      <value name="hidAngles" type="Vector3">
        <x>0</x>
        <y>0</y>
        <z>0</z>
      </value>
      <value name="hidPos_precise" type="Vector3">
        <x>0</x>
        <y>0</y>
        <z>0</z>
      </value>
      <value name="hidConstEntity" type="Bool">False</value>
      <object type="Components">
        <object type="CFileDescriptorComponent">
          <value name="hidHasAliasName" type="Bool">False</value>
          <value hash="2A7BCA49" type="String">graphics\objects\mapcompass\icon_playerpos.xml</value>
          <value name="fileName" type="Hash">535B768A</value>
          <value name="hidDescriptor" type="Rml">
            <hidDescriptor>
              <component class="GraphicComponent" version="2" detail="0">
                <object index="0" boneName="Icon_PlayerPos_LOD0" bboxMin="-0.00730638,-0.00612232,-5.82406e-007" bboxMax="0.00712985,0.0116253,-5.82406e-007" />
                <resource fileName="graphics\Objects\MapCompass\Icon_PlayerPos.xbg" bboxMin="-0.00730638,-0.00612232,-5.82406e-007" bboxMax="0.00712985,0.0116253,-5.82406e-007" />
                <skeleton name="Icon_PlayerPos" pos="0,0,0" rot="1,-0,-0,-0" />
              </component>
            </hidDescriptor>
          </value>
        </object>
        <object type="CGraphicComponent">
          <value name="hidHasAliasName" type="Bool">False</value>
          <value name="bIntelHackGliderOn" hash="599225A9" type="Bool">False</value> <!-- type="BinHex" value="00" -->
          <value name="bCastShadow" type="Bool">False</value>
          <value name="bReceiveShadow" type="Bool">False</value>
          <value name="bCastAmbientShadow" type="Bool">False</value>
          <value name="olgLightGroup" type="Hash">00000181</value>
          <value name="bAllowCullBySize" type="Bool">True</value>
          <value name="agAmbientGroup" type="Hash">00000000</value>
          <value name="bBehaveLikeAPickup" type="Bool">False</value>
          <value name="bShowInReflection" type="Bool">False</value>
          <value name="bAlwaysShowInReflection" type="Bool">False</value>
          <value name="bOverrideLODSphere" type="Bool">False</value>
          <value name="fLODSphereRadius" type="Float">0</value>
          <value name="hidSkyOcclusion0" type="Hash">FFFFFFFF</value>
          <value name="hidSkyOcclusion1" type="Hash">FFFFFFFF</value>
          <value name="hidSkyOcclusion2" type="Hash">FFFFFFFF</value>
          <value name="hidSkyOcclusion3" type="Hash">FFFFFFFF</value>
          <value name="hidGroundColor" type="Hash">FFFFFFFF</value>
          <value name="hidObjectHeight" type="Float">3</value>
          <value name="hidHeightAbove" type="Float">0.0</value> <!-- type="BinHex" value="00000000" -->
          <value name="hidHasAmbientValues" type="Bool">False</value>
        </object>
      </object>
    </object>
  </object>
```

### Removing coloured road signs

**Decoding required**  
xx\_OA\_StreetSigns.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

This file has separate entries for every road sign that can have a different colour, there are quite a few:

MissionObjectiveSigns.AfriQaTelecomSign  
MissionObjectiveSigns.AirfieldSign  
MissionObjectiveSigns.CallingCardandPhoneRechargingStandSign  
MissionObjectiveSigns.CattleXingSign  
MissionObjectiveSigns.ClaesProductsSign  
MissionObjectiveSigns.DentalClinicSign  
MissionObjectiveSigns.DogFightsSign  
MissionObjectiveSigns.DogonVillageSign  
MissionObjectiveSigns.FortSign  
MissionObjectiveSigns.FreshFishSign  
MissionObjectiveSigns.GeneralStoreSign  
MissionObjectiveSigns.GokaFallsSign  
MissionObjectiveSigns.HardwareStoreSign  
MissionObjectiveSigns.LumberSign  
MissionObjectiveSigns.MarinaSign  
MissionObjectiveSigns.MertensSegoloCo  
MissionObjectiveSigns.MikesBarSign  
MissionObjectiveSigns.MokubaSign  
MissionObjectiveSigns.MosateSelaoSign  
MissionObjectiveSigns.MsPipeline  
MissionObjectiveSigns.NorthRailyardSign  
MissionObjectiveSigns.OGCSign  
MissionObjectiveSigns.PalaSign  
MissionObjectiveSigns.PetroSahelSign  
MissionObjectiveSigns.PoliceStationSign  
MissionObjectiveSigns.PolytechnicSign  
MissionObjectiveSigns.PostOfficeSign  
MissionObjectiveSigns.RailXingSign  
MissionObjectiveSigns.RangerStationSign  
MissionObjectiveSigns.SakoBreweries  
MissionObjectiveSigns.ScrapSalvageSign  
MissionObjectiveSigns.SedikoSign  
MissionObjectiveSigns.SefapaneSign  
MissionObjectiveSigns.SehlakalaseSign  
MissionObjectiveSigns.SepokoSign  
MissionObjectiveSigns.ShwasanaSign  
MissionObjectiveSigns.SlaughterhouseSign  
MissionObjectiveSigns.TaemoCoSign  
MissionObjectiveSigns.TobaccoandNewsStandSign  
MissionObjectiveSigns.UndertakerSign  
MissionObjectiveSigns.VeterinarianSign  
MissionObjectiveSigns.WeaponShop  
MissionObjectiveSigns.WeelegolVillage  
MissionObjectiveSigns.WellandWindmillShopSign  
MissionObjectiveSignsSafeHouse.SafeHouse\_A1BU00  
MissionObjectiveSignsSafeHouse.SafeHouse\_A1LM01  
MissionObjectiveSignsSafeHouse.SafeHouse\_A1LM02  
MissionObjectiveSignsSafeHouse.SafeHouse\_A1LM03  
MissionObjectiveSignsSafeHouse.SafeHouse\_A1LM04  
MissionObjectiveSignsSafeHouse.SafeHouse\_A1LM05  
MissionObjectiveSignsSafeHouse.SafeHouse\_A1LM06  
MissionObjectiveSignsSafeHouse.SafeHouse\_A2LM07  
MissionObjectiveSignsSafeHouse.SafeHouse\_A2LM08  
MissionObjectiveSignsSafeHouse.SafeHouse\_A2LM09  
MissionObjectiveSignsSafeHouse.SafeHouse\_A2LM10  
MissionObjectiveSignsSafeHouse.SafeHouse\_A2LM11  
MissionObjectiveSignsSafeHouse.SafeHouse\_A2LM12

To remove the colours they change to for missions, we are going to edit their sections with the title “Colors”.

By default this section looks like this:

```xml
<object hash="C512C6A9" type="Colors">
     <value name="None" hash="DFA2AFF1" type="BinHex">0000803F0000803F0000803F0000803F</value>
     <value name="Main" hash="1F1A625A" type="BinHex">0000803F000000000000000000000000</value>
     <value name="Subvert" hash="7D65CCD1" type="Vector4">
        <x>0.0</x>
        <y>0.2</y>
        <z>1.0</z>
        <w>0.0</w>
     </value> <!-- type="BinHex" value="00000000CDCC4C3E0000803F00000000" -->
     <value hash="590E69F7" type="BinHex">9A99593F9A99593F000000000000803F</value>
</object>
```

We are going to edit all of these sections so the signs have no colour for every mission type. The subvert section needs to be edited to be the same as the ‘None’ and ‘Main’ lines. You can copy and paste this completed “Colors” section over the same for each sign you want to edit:

```xml {3,4,5}
<object hash="C512C6A9" type="Colors">
     <value name="None" hash="DFA2AFF1" type="BinHex">0000803F0000803F0000803F0000803F</value>
     <value name="Main" hash="1F1A625A" type="BinHex">0000803F0000803F0000803F0000803F</value>
     <value name="Subvert" hash="7D65CCD1" type="BinHex">0000803F0000803F0000803F0000803F</value>
     <value hash="590E69F7" type="BinHex">0000803F0000803F0000803F0000803F</value>
</object>
```

## Guide \- Limited saving

This guide will cover how to remove quicksaving and saving from the pause menu. The player will be restricted to in-game save points.

Step 1: Removing quicksaving  
inputactionmapsingle.xml (\\patch\_unpack\\config\\)

There is a section of this file that controls quicksaving and quickloading, with the title “quicksaveload”. It looks like this:

```xml
<ActionMap name="quicksaveload" resendOnChange="0" >
    	  <Import actionmap="quicksaveload_remap" optional=""/>
        <Binding input="kb:f5" action="release" signal="quicksave"/>
        <Binding input="kb:f9" action="release" signal="quickload"/>
</ActionMap>
```

To remove quicksaving we can just delete the line that references it, so your finished section looks like this:

```xml
<ActionMap name="quicksaveload" resendOnChange="0" >
    	  <Import actionmap="quicksaveload_remap" optional=""/>
        <Binding input="kb:f9" action="release" signal="quickload"/>
</ActionMap>
```

Step 2: Removing saving from the pause menu  
Dunia.dll (\\Far Cry 2\\bin\\)

This is going to involve two edits. The pause menu has the option to “Save Game” and one edit will remove the save screen that would appear when you select it, and a second edit will remove that option so it appears blank.

To remove the save screen, find the section that says “CSaveGamePage” (43 53 61 76 65 47 61 6D 65 50 61 67 65). Once you’ve found it, replace all its  hex values on the left side of the screen with zeroes. It should look like this:

To remove the “Save Game” label, find the section that says “PAUSE\_SAVEGAME” (50 41 55 53 45 5F 53 41 56 45 47 41 4D 45). Once you’ve found it, replace all its hex values on the left side of the screen with zeroes. It should look like this:

# 

