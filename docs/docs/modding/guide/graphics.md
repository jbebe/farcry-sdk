---
sidebar_position: 6
sidebar_label: "Graphics"
format: md
---

:::info[From the Almost Complete Guide]
This page reproduces a section of ["An Almost Complete Guide to Far Cry 2 Modding"](./index.md) by Boggalog, verbatim, with attribution. See the [guide index](./index.md) for the full attribution note.
:::

# Graphics

This section will cover editing graphics settings and how to do some texture editing.

Almost all of the graphics settings we’re going to make changes are in the file “defaultrenderconfig.xml”, found in \\patch\_unpack\\engine\\settings\\. 

There are three main sections to this file: 

1. At the top there’s a list of general settings. Some of these are in the in-game options menu but most can only be edited here.

2. Below, under the title “\<RenderQuality\>” it lists the different graphics quality presets (Ultra High, Ultra High w/ DX10, Very High, Very High w/ DX10, High, High w/ DX10, Medium, Low) and what individual graphics setting presets (e.g. Textures \- Ultra High) are included in them.

3. Further down is a long list of the different graphics settings, most of which are under the same names shown in the in-game options menu: Vegetation, Water (shown in-game as ‘Shading’), Terrain, Geometry, Post, Textures, Shadow and Ambient. When making changes to these note that each has sections for their different presets (Ultra High, Very High etc) so your changes will only apply when you choose the preset where you made them.

You can explore this file and see there’s a lot you can tweak. I’m going to go over everything I used in my modding and some other settings that seem significant. If you do try doing other tweaks I would advise you to take it slow and test your changes, it’s really easy to introduce bugs and not know which setting is causing them.

## LOD and Draw Distance

### LOD distance

defaultrenderconfig.xml \- \\patch\_unpack\\engine\\settings\\

These are within the Geometry settings. The lower the values, the further the distance, so ‘0’ is the max. 

“KillLodScale” can be set to 0.7 with a minimal performance hit but when setting it any lower I had trees and bushes flashing in and out of existence in map 2\.

“LodScale” I don’t advise changing but it’s up to your preference. A lower value increases the lod pop-in distance for almost everything but for some reason decreases the lod pop-in distance for roads. This sucks as while 99% of the game looks way better with this set to 0 the roads look really bad and buggy.

**KillLodScale="1.0"**  
**LodScale="1.0"**

### LOD distance \- Terrain

defaultrenderconfig.xml \- \\patch\_unpack\\engine\\settings\\

This is within the Terrain settings. It can be increased to 128 with a small performance hit.

**TerrainDetailBlendViewDistance="128"**

### Draw distance \- Trees

defaultrenderconfig.xml \- \\patch\_unpack\\engine\\settings\\

This is within the Geometry settings. The lower the value, the further the distance, so ‘0’ is the max. You can easily set this to the max with only a small performance hit.

**RealTreesLodScale="1.0"**

### Draw distance \- Clusters

defaultrenderconfig.xml \- \\patch\_unpack\\engine\\settings\\

This is within the Geometry settings. The lower the value, the further the distance, so ‘0’ is the max. This setting is very performance heavy but max isn’t too difficult for a recent graphics card.

Clusters are subtle parts of the environmental like rocks and other small details.

**ClustersLodScale="0.8"**

## Shadows

### Dynamic shadows \- Softness, ‘filter line’ and distance

defaultrenderconfig.xml \- \\patch\_unpack\\engine\\settings\\

These are within the Shadow settings. How they work is complex and I don’t understand it but I’ll share what I know. The only setting that significantly affects performance here is “SunShadowRange2”.

Increasing “SunShadowRange0” will make the shadows softer and push back the obvious ‘filter line’ where high quality shadows become low quality. You can fiddle around with this to your preference but I suggest setting “SunShadowRange0” to 20\. 

“SunShadowRange2” controls the max distance shadows will appear and although it is tempting to increase this that will cause a bug where shadows on trees bug out and flicker black. I have seen this bug even on the default setting of 140 so I personally set “SunShadowRange2” to 135 and I haven’t seen this bug since, you won’t notice a visual difference between 140 and 135\. There is another bug where distant shadows flash black when they first appear which can be fixed by setting “SunShadowRange2” to 100, there is a visual difference with this though and the bug is quite subtle so that’s up to you.

I don’t see a big difference with “SunShadowRange1”, I set it to 40\.

I also don’t see a difference with “SunShadowFadeRange” but others have recommended setting this to 12\.

**SunShadowFadeRange="10"**  
**SunShadowRange0="4"**  
**SunShadowRange1="20"**  
**SunShadowRange2="140"**

### Dynamic shadows \- Vegetation

defaultrenderconfig.xml \- \\patch\_unpack\\engine\\settings\\

This is within the Shadow settings. It can be easily increased to 1 so that vegetation casts more shadows.

**LeavesShadowRatio="0.5f"**

### Dynamic shadows \- Resolution

defaultrenderconfig.xml \- \\patch\_unpack\\engine\\settings\\

These are within the Shadow settings. They can be increased to 2560 without a huge performance hit.

**ShadowMapSize="2048"**  
**CascadedShadowMapSize="2048"**

### Static shadows \- Distance

defaultrenderconfig.xml \- \\patch\_unpack\\engine\\settings\\

This is within the Ambient settings. It can be easily increased to 512 with a small performance hit. There’s no point increasing this higher as the map won’t load much further so it won’t need shadows.

**MaxHemiMapDistance="160"**

## Decals

### Decals \- Max number

defaultrenderconfig.xml \- \\patch\_unpack\\engine\\settings\\

This is within the Geometry settings. This setting can be increased easily, the default setting has a 1:4 ratio between max per decal count and overall max, so this is worth maintaining.

**MaxDecalCount="200"**  
**MaxDecalCountPerType="50"**

### Decals \- Lifetime

decal.xml \- \\patch\_unpack\\databases\\generic\\

These are within a different file to the other graphics settings, make your edits to “decal.xml” found in \\patch\_unpack\\databases\\generic\\. Open the file and you’ll see the different base materials and decal types. The lifetime and fade out time of each can be increased to your preference. You should note though that increasing the lifetime of decals on flesh (Base.Flesh) includes when the player receives damage, so can lead to bugs where the marks where you were hit remain floating in your view.

**fLifeTime="1"**  
**fFadeOutDuration="0.3"**

## Texture editing

Full disclosure before I start this section \- I don’t really know what I’m doing and I’m no graphic designer. I’ll only be able to share some tips that helped me do minor retextures.

A general principle to be aware of is that you can save texture files in sizes larger than the original files and they will display correctly if they are in the same aspect ratio. This is useful if you want your additions to appear in higher resolution.

I’m not going to include the files for my examples below but I will list where they can be found.

### Watermarks

logo\_new\_01.xbt/logo\_new\_02.xbt \- \\ui\\textures\\common\\ \> common.fat/.dat

These files are those typically used for mod watermarks but you can find the other main menu textures in the same folder, so feel free to be creative\!

When you open this file, you’ll see this pop-up. Don’t tick the box as you don’t need the transparency in a separate channel:

Once your file is open there isn’t much more to say. You find the Far Cry font [HERE](https://www.dafont.com/farcry.font) if you want to use it but you can add whatever you like and then just save, convert and put it into your patch in the right folder.

### Road signs

afriqatelecom\_m.xbt \- \\graphics\\objects\\\_signskit\\ \> worlds.fat/.dat

The use of afriqatelecom\_m.xbt is an example, you can find the rest of the road sign files in the same folder. 

When you open these files you’re going to notice that it looks a bit pink. To see what’s going on go into the ‘Channels’ tab that I’ve highlighted in this image: 

The file has separate red, green and blue channels and the red and green channels are used solely for the dirt effect. The actual sign text is contained in the blue channel, as you can see in the image below where only the blue channel is enabled:

![][image2]

Working only with the blue channel is limited and some of Photoshop’s functions won’t work, so I recommend creating a separate file of the same size that you can do your work in and then copy it over.

The use of certain channels for the dirt effect is used in other texture files also so be aware of it\!

## Other visual tweaks

### Max FPS

#### Option 1: Add a launch property

Right click your Far Cry 2 shortcut and press properties, either in Steam if using that or the actual shortcut itself.

In Steam you can add “-RenderProfile\_MaxFps 60” as a launch option, for regular windows add it at the end of the “Target” section.

\-RenderProfile\_MaxFps 60

#### Option 2: Edit the game files

defaultrenderconfig.xml \- \\patch\_unpack\\engine\\settings\\

The default setting for this is an unlocked framerate (9999), I advise setting this to 60 for stability. I would only set it higher to match the refresh rate of your monitor but there may still be bugs.

MaxFPS=”**9999**”

### No blinking items

Dunia.dll \- \\Far Cry 2\\bin\\

1. Open “Dunia.dll” with your hex editor.  
2. Search the file for “Mesh\_Highlight” or the hex bytes “4D 65 73 68 5F 48 69 67 68 6C 69 67 68 74”. There’s only one instance of this.  
3. We’re going to remove “Mesh\_Highlight” by entering 00 over all of the hex bytes. Put your zeroes in the hex section to the left, don’t put them in the section to the right. It should look like this:

### FOV

xx\_cameras.xml \- entitylibrarypatchoverride.fcb \> \\patch\_unpack\\generated\\

Find the right section of this file by searching for “Camera.First”, the value to change within this section is “fFOV”. Note that going above the default fov causes visual bugs for some of the playable characters. Edges of the body you aren’t meant to see will be visible while sprinting and jumping.

```xml {31}
<object hash="256A1FF9">
    <value name="Name" type="String">Camera.First</value>
    <object type="Entity">
      <value name="hidName" type="String">cameras.Camera.First</value>
      <value name="disEntityId" type="UInt64">118</value>
      <value hash="D2B3429E" type="String">CEntity</value>
      <value name="hidEntityClass" type="Hash">50C95067</value>
      <value name="hidResourceCount" type="UInt32">0</value>
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
        <object type="CCameraPawnComponent">
          <value name="hidHasAliasName" type="Bool">False</value>
          <value name="fCameraBlendTime" type="Float">0.5</value>
          <value name="fNearDistance" type="Float">0.1</value>
          <value name="fFarDistance" type="Float">1000</value>
          <value name="fFOV" type="Float">75</value>
          <value hash="920A6E7C" type="String">Camera</value>
          <value name="Bone" type="Hash">3CB0EB33</value>
          <value name="DebugOffset" type="Vector3">
```

### Guide \- Removing rim lighting (blue tint at night)

This guide will cover how to remove rim lighting, which is extra blue light spread across scenes at night. This is an attempt at realism but the blue can look unnatural, especially when the player’s arms look so blue they must be either very short on blood or an alien.

Rim lighting is quite a subtle effect until you’ve had it pointed out, and I’ve created the image below to compare. The top picture is the default game, and the bottom picture is with rim lighting removed. Notice the slight extra brightness and blue tinge to both the player and surrounding vegetation in the default game’s night.

If you can’t see any difference then I would encourage you to do some testing of your own during actual gameplay and see if you prefer the image with or without rim lighting.

To remove rim lighting we are going to make the same changes to two different files:

world1.managers.xml \< world1.managers.fcb (\\patch\_unpack\\worlds\\world1\\generated)

world2.managers.xml \< world2.managers.fcb (\\patch\_unpack\\worlds\\world2\\generated)

Do not decode the files\! For some reason they can’t be converted back to .fcb files once they’ve been decoded. We’re going to make our changes to the BinHex values as they are.

There are multiple sections in both of these files with the title “\<object hash="777FE977"\>”. Below these lines are sections of BinHex values, they look like this: 

```xml
<object hash="777FE977">
    <value hash="AECEF355" type="BinHex">08000000</value>
    <object hash="F32C0E1C">
         <object hash="9A152447">
              <value hash="DCB67730" type="BinHex">ED0DBE3B3B014D3E0000003F00000000</value>
              <value hash="6BBB9E69" type="BinHex">0000803F0000803F0000000000000000</value>
              <value hash="2CECF817" type="BinHex">00000000</value>
         </object>
         <object hash="9A152447">
              <value hash="DCB67730" type="BinHex">CEAA4F3EF1634C3E0000003F00000000</value>
              <value hash="6BBB9E69" type="BinHex">0000803F0000803F0000000000000000</value>
              <value hash="2CECF817" type="BinHex">00000000</value>
         </object>
         <object hash="9A152447">
              <value hash="DCB67730" type="BinHex">CEAA4F3EF1634C3E0000003F00000000</value>
              <value hash="6BBB9E69" type="BinHex">0000803F0000803F0000000000000000</value>
              <value hash="2CECF817" type="BinHex">00000000</value>
         </object>
         <object hash="9A152447">
              <value hash="DCB67730" type="BinHex">6154023F9F3C8C3E0000003F00000000</value>
              <value hash="6BBB9E69" type="BinHex">0000803F0000803F0000000000000000</value>
              <value hash="2CECF817" type="BinHex">00000000</value>
         </object>
         <object hash="9A152447">
              <value hash="DCB67730" type="BinHex">6154023F9F3C8C3E0000003F00000000</value>
              <value hash="6BBB9E69" type="BinHex">0000803F0000803F0000000000000000</value>
              <value hash="2CECF817" type="BinHex">00000000</value>
         </object>
         <object hash="9A152447">
              <value hash="DCB67730" type="BinHex">91ED4C3F166A4D3E0000003F00000000</value>
              <value hash="6BBB9E69" type="BinHex">0000803F0000803F0000000000000000</value>
              <value hash="2CECF817" type="BinHex">00000000</value>
         </object>
         <object hash="9A152447">
              <value hash="DCB67730" type="BinHex">91ED4C3F166A4D3E0000003F00000000</value>
              <value hash="6BBB9E69" type="BinHex">0000803F0000803F0000000000000000</value>
              <value hash="2CECF817" type="BinHex">00000000</value>
         </object>
         <object hash="9A152447">
              <value hash="DCB67730" type="BinHex">B7D1803F4D844D3E0000003F00000000</value>
              <value hash="6BBB9E69" type="BinHex">0000803F0000803F0000000000000000</value>
              <value hash="2CECF817" type="BinHex">00000000</value>
         </object>
    </object>
</object>
```

We are going to replace all of the BinHex values with zeros, so it looks like this:

```xml
<object hash="777FE977">
    <value hash="AECEF355" type="BinHex">00000000</value>
    <object hash="F32C0E1C">
         <object hash="9A152447">
              <value hash="DCB67730" type="BinHex">00000000000000000000000000000000</value>
              <value hash="6BBB9E69" type="BinHex">00000000000000000000000000000000</value>
              <value hash="2CECF817" type="BinHex">00000000</value>
         </object>
         <object hash="9A152447">
              <value hash="DCB67730" type="BinHex">00000000000000000000000000000000</value>
              <value hash="6BBB9E69" type="BinHex">00000000000000000000000000000000</value>
              <value hash="2CECF817" type="BinHex">00000000</value>
         </object>
         <object hash="9A152447">
              <value hash="DCB67730" type="BinHex">00000000000000000000000000000000</value>
              <value hash="6BBB9E69" type="BinHex">00000000000000000000000000000000</value>
              <value hash="2CECF817" type="BinHex">00000000</value>
         </object>
         <object hash="9A152447">
              <value hash="DCB67730" type="BinHex">00000000000000000000000000000000</value>
              <value hash="6BBB9E69" type="BinHex">00000000000000000000000000000000</value>
              <value hash="2CECF817" type="BinHex">00000000</value>
         </object>
         <object hash="9A152447">
              <value hash="DCB67730" type="BinHex">00000000000000000000000000000000</value>
              <value hash="6BBB9E69" type="BinHex">00000000000000000000000000000000</value>
              <value hash="2CECF817" type="BinHex">00000000</value>
         </object>
         <object hash="9A152447">
              <value hash="DCB67730" type="BinHex">00000000000000000000000000000000</value>
              <value hash="6BBB9E69" type="BinHex">00000000000000000000000000000000</value>
              <value hash="2CECF817" type="BinHex">00000000</value>
         </object>
         <object hash="9A152447">
              <value hash="DCB67730" type="BinHex">00000000000000000000000000000000</value>
              <value hash="6BBB9E69" type="BinHex">00000000000000000000000000000000</value>
              <value hash="2CECF817" type="BinHex">00000000</value>
         </object>
         <object hash="9A152447">
              <value hash="DCB67730" type="BinHex">00000000000000000000000000000000</value>
              <value hash="6BBB9E69" type="BinHex">00000000000000000000000000000000</value>
              <value hash="2CECF817" type="BinHex">00000000</value>
         </object>
    </object>
</object>
```

There are loads of sections like this in both files, some longer than others but the process is always the same.

## Bug fix \- White flashes on distant terrain with low settings

defaultredefaultrenderconfig.xml \- \\patch\_unpack\\engine\\settings\\

This bug occurs when playing with the “Terrain” setting on low or medium. When a new area is loaded there are white flashes on the terrain.

To fix this, the “TerrainDetailViewDistance” setting should have a minimum value of 256 and “TerrainDetailBlendViewDistance” should have a minimum value of 64\.

There is a small performance impact with this edit.

TerrainLodScale="1.7"  
**TerrainDetailViewDistance="256"**  
**TerrainDetailBlendViewDistance="64"**  
TerrainComputeMaxErrorLODs="1"  
TerrainAffectedByMuzzleFlash="0"  
TerrainMaxErrorTolerance="0.03f"

## 

## Bug fix \- White flashes on vehicles with low settings

defaultredefaultrenderconfig.xml \- \\patch\_unpack\\engine\\settings\\

This bug occurs when playing using low shading settings. Vehicles will flash white.

To fix this HDR and Bloom need to be enabled in the graphics settings. The low and medium quality settings do not have both HDR and Bloom enabled, so they can be edited to include them.

```xml
<quality id="low"
```
      ResolutionX="800"   
      ResolutionY="600"   
      ShaderModel="30"   
			  
      AntiPortalQuality="high"   
      **Hdr="1"**  
      HdrFP32="0"  
      **Bloom="1"**   
      PostFxQuality="low"   
			  
      TextureResolutionQuality="low"   
			  
      WaterQuality="low"   
      TextureQuality="low"   
      EnvironmentQuality="low"   
				  
      GeometryQuality="low"   
      TerrainQuality="low"  
      AmbientQuality="low"   
      ShadowQuality="off"  
      DepthPassQuality="low"  
      VegetationQuality="low"

## Bug fix \- Edges of distant shadows flickering

defaultredefaultrenderconfig.xml \- \\patch\_unpack\\engine\\settings\\

This is a very rare bug with default settings, it is more commonly seen in mods that increase max shadow distance (New Dunia).

Max shadow distance is controlled by the “SunShadowRange2” stat in the “Shadow” section.

The default value for the ultra high shadow setting is 140 and increasing it at all will cause almost all distant shadows to flicker. I’ve also seen this on default settings and fixed it by reducing the value to 135\. I haven’t seen it since and notice no other difference in image quality.

```xml
<quality id="ultrahigh"
```
     SunShadowFadeRange="10"  
     SunShadowRange0="4"  
     SunShadowRange1="20"  
     **SunShadowRange2="135"**  
     ShadowMapSize="2048"

## Bug fix \- Black flashes on distant shadows

defaultredefaultrenderconfig.xml \- \\patch\_unpack\\engine\\settings\\

This is a bug where distant shadows will flash black when they first appear, so when the player enters a new area.

It can be fixed by reducing the max shadow distance but I found it required reducing the “SunShadowRange2” value to 100\. This means there are no shadows beyond 100m which is noticeable when looking into the distance.

You’ll have to decide for yourself if you even notice the flashes in the first place, it is quite subtle.

```xml
<quality id="ultrahigh"
```
     SunShadowFadeRange="10"  
     SunShadowRange0="4"  
     SunShadowRange1="20"  
     **SunShadowRange2="100"**  
     ShadowMapSize="2048"

