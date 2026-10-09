---
slug: /textures
sidebar_position: 2
title: Replace a texture
description: Export a Far Cry 2 texture as DDS, edit it in an image editor and import it back with JackAll, on the Dart Rifle's shop icon
---

# Replace a texture

Far Cry 2's textures are `.xbt` files: an ordinary DDS image with a small game header in front.
JackAll splits them into the two parts, so you can edit the picture in any image editor that opens
DDS, and puts them back together.

The example is the Dart Rifle's icon in the weapon shop, `ui\textures\guns\gun_icon_sniperdart.xbt`,
replaced with the one VSS Vintorez ships.

:::tip[The result is a file confirmed in game]
Imported this way, the staged icon is byte for byte the one VSS Vintorez ships, which was played.
:::

## 1. Export the picture

On the **Files** tab, find `gun_icon_sniperdart` and select it. The preview shows the icon (3). Click
**Export DDS + XML…** (1) and pick a folder. You get two files next to each other:

- `gun_icon_sniperdart.dds`, the picture;
- `gun_icon_sniperdart.xml`, the game's header for it. You don't edit this one, but keep it with
  the picture.

![The shop icon with its Export and Import buttons](/img/jackall/textures/01-preview.png)

## 2. Edit it

Open the `.dds` in an image editor that reads and writes DDS: GIMP and Paint.NET both do. Two rules
keep the game happy:

- **Keep the size.** This icon is 256 × 64 pixels.
- **Keep the compression.** Save with the same DDS format the file came in. Your editor tells you
  which one it opened, usually DXT1 or DXT5.

## 3. Import it back

Click **Import DDS + XML…** (2) and select **both** files, the `.dds` and its `.xml`, with Ctrl+click.
JackAll builds the `.xbt` and stages it in your workspace. The details now say the file comes from
your workspace (1), the preview shows your picture (2), and **Revert** (3) undoes it.

![The new icon staged in the workspace](/img/jackall/textures/02-imported.png)

Then **Deploy mods** on the Mods tab.

## The HUD icon, and other textures

The icon you see in the bottom corner of the HUD is a separate file,
`ui\textures\hud\icons_weapons\hud_icon_sniperdart.xbt`. Replace it the same way.

A model's textures work the same too, with one difference: a weapon's big textures come in pairs, a
base file and a `_mip0` file holding the full-size level. Those are easier to replace through a model
pack, which splits them for you. [Texturing a weapon](/docs/modding/texturing-a-weapon) covers that.
