---
sidebar_position: 20
sidebar_label: "Controls"
format: md
---

:::info[From the Almost Complete Guide]
This page reproduces a section of ["An Almost Complete Guide to Far Cry 2 Modding"](./index.md) by Boggalog, verbatim, with attribution. See the [guide index](./index.md) for the full attribution note.
:::

# Controls

## Guide \- Make a key rebindable

Step 1: Adding the key binding to the default controls list  
defaultusercontrols.xml (\\patch\_unpack\\config\\)

To add a new key binding to the “Actions” in-game controls menu you need to add a line to the “CATEGORY\_ACTIONS” section, for example:

```xml
<Control name="holster" key1="kb:x" actionmap="common_holster_remap" group="1" conflictmask="12"/>
```

The completed section should look something like this:

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

Step 2: Link the changes to the default controls to the controls system  
inputactionmapcommon.xml (\\patch\_unpack\\config\\)

To link our changes to the controls system we need to first find which action map the control is located under. There are several action maps in this file, under titles like this: \<ActionMap name="common\_weapons"\>

Once you’ve found the right section, we need to add a new line to the group directly under the title. There is a list of “Import actionmap” lines, and we’re going to create a new one, for example: \<Import actionmap="common\_holster\_remap" optional=""/\>

The completed section should look something like this:

```xml {6}
<ActionMap name="common_weapons">
<Import actionmap="common_weapons_remap" optional=""/>
<Import actionmap="common_shoot_remap" optional=""/>
<Import actionmap="common_iron_remap" optional=""/>	
<Import actionmap="common_reload_remap" optional=""/>
<Import actionmap="common_holster_remap" optional=""/>
```

Step 3: Add a new control label  
\\patch\_pack\\languages\\ \- Each language has its own folder and “oasisstrings.rml” file.

Find the section with the title “\<section name="Actions"\>”.

We are going to add a new line to this section, referencing the “Control name” value from our new line in step 1\. The “Control name” value should match the “string enum” value in the line we create in this file. 

For example, it should look something like this:

```xml
<string enum="holster" value="Holster" />
```

