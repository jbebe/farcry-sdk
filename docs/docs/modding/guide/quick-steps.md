---
sidebar_position: 2
sidebar_label: "Quick Steps - Editing an Existing Mod"
format: md
---

:::info[From the Almost Complete Guide]
This page reproduces a section of ["An Almost Complete Guide to Far Cry 2 Modding"](./index.md) by Boggalog, verbatim, with attribution. See the [guide index](./index.md) for the full attribution note.
:::

# Quick Steps \- Editing an existing mod

**Step 1: Get your tools ready**  
Get the packing/unpacking tools from the guide tools package (also available online [here](https://www.moddb.com/mods/far-cry-2-redux/downloads/far-cry-2-mod-tools)). 

I’d also recommend getting Notepad++ to edit the game’s .xml files. It’s available from [here](https://notepad-plus-plus.org/downloads/), although it is possible to just use the Notepad built into Windows.

**Step 2: Unpacking the patch files**  
All Far Cry 2 mods come in two files, “patch.fat” and “patch.dat”. Get these from your chosen mod and copy them into the same folder as the packing/unpacking tools.

Drag either patch file onto “Gibbed.Dunia.Unpack.exe”. This will unpack all the mod’s files into a new folder called “patch\_unpack”.

At this point you can do some edits, such as with the graphics, controls, scripts and some gameplay tweaks. If this is all you need then you can skip to step 5 to repack the files. If your chosen edit requires a file within “entitylibrarypatchoverride.fcb” then you need the next step.

**Step 3: Unpacking “entitylibrarypatchoverride.fcb”**  
Find “entitylibrarypatchoverride.fcb” within \\patch\_unpack\\generated\\ and copy it into the same folder as the packing/unpacking tools.

Drag “entitylibrarypatchoverride.fcb” over “Gibbed.Dunia.ConvertBinary.exe”. This will unpack the contained files into a new folder called “entitylibrarypatchoverride” and also create a new file called “entitylibrarypatchoverride.xml”.

You can now edit the files in the “entitylibrarypatchoverride” folder.

**Step 4 (Optional): Decoding .xml files**  
Some .xml files found in the “entitylibrarypatchoverride” folder need to be decoded for all of their contents to be edited. If you have tried to make an edit but can’t find the section listed in the guide then this is probably the case.

There is a xml decoder included in the tools folder for the guide (also available online [here](https://www.moddb.com/downloads/far-cry-2-xml-decoder)).

Put any files you want to decode into the “Put xml files to decode in here” folder and then run the file “Start XML Decoder.bat”. This will run and when finished it will show “All XML files processed.”. 

The files you copied into the “Put xml files to decode in here” folder are now decoded. You can now copy them back to where they came from, and then make your edits.

**Step 5: Packing “entitylibrarypatchoverride.fcb”**  
Drag “entitylibrarypatchoverride.xml” onto “Gibbed.Dunia.ConvertBinary.exe”. This will pack the files and overwrite the original “entitylibrarypatchoverride.fcb” with a fresh copy.

You can now copy the new “entitylibrarypatchoverride.fcb” back into \\patch\_unpack\\generated\\ and overwrite the old file.

**Step 6: Unpacking the DLC files**  
You only need to do this if you want to edit the DLC weapons/vehicles.

Find “entitylibrary.fcb” within \\patch\_unpack\\downloadcontent\\dlc1\\generated\\ and copy it into the same folder as the pack/unpacking tools.

Drag “entitylibrary.fcb” over “Gibbed.Dunia.ConvertBinary.exe”. This will unpack the contained files into a new folder called “entitylibrary” and also create a new file called “entitylibrary.xml”.

You can now edit the files in the “entitylibrary” folder.

**Step 7: Packing the DLC files**  
Drag “entitylibrary.xml” onto “Gibbed.Dunia.ConvertBinary.exe”. This will pack the files and overwrite the original “entitylibrary.fcb” with a fresh copy.

You can now copy the new “entitylibrary.fcb” back into \\patch\_unpack\\downloadcontent\\dlc1\\generated\\ and overwrite the old file.

**Step 8: Packing the patch files**  
Drag the “patch\_unpack” folder onto “Gibbed.Dunia.Pack.exe” and this will create new patch files called “patch\_unpack.fat” and “patch\_unpack.dat”.

You can rename these files to “patch.fat” and “patch.dat” and they now replace your original files. You’re done\!

