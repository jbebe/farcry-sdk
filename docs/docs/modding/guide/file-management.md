---
sidebar_position: 4
sidebar_label: "File Management"
format: md
---

:::info[From the Almost Complete Guide]
This page reproduces a section of ["An Almost Complete Guide to Far Cry 2 Modding"](./index.md) by Boggalog, verbatim, with attribution. See the [guide index](./index.md) for the full attribution note.
:::

# File Management

This section is going to cover the different file types involved in Far Cry 2 modding and how we can edit them.

## .fat and .dat files

These are basically folders full of other files. Almost all the game files are contained in these, including the patch files that we use for modding. 

.fat and .dat files come as a pair, you need both for them to work so if you move them don’t leave one behind\!

For these files use the tools from the ‘Packing/unpacking’ section above, available [HERE](https://www.moddb.com/mods/far-cry-2-redux/downloads/far-cry-2-mod-tools).

### Unpacking .fat and .dat files

1. Copy both .fat and .dat files into the same folder as your modding tools.  
2. Drag either the .fat or .dat file onto "Gibbed.Dunia.Unpack.exe". This will unpack both files and create a folder called "EXAMPLE\_unpack", the ‘EXAMPLE’ section will match the name of the file you unpacked.

### Adding to .fat and .dat files

To add to .fat and .dat files you can simply copy and paste your files in. Pay attention however to the folder structure. When modding we edit patch.fat and patch.dat but we can add files from other places like worlds.fat/.dat and common.fat/.dat. All of these files use the same folder structure so if you want to add a file you found within an unpacked common.fat/.dat under \\scripts\\engine\\objects\\pawn\\ make sure you create that folder structure in patch.dat/.fat if it doesn’t already exist.

### Packing .fat and .dat files

1. Drag the "EXAMPLE\_unpack" folder onto "Gibbed.Dunia.Pack.exe" and this will create two new files: “EXAMPLE\_unpack.dat" and "EXAMPLE\_unpack.fat".

   Please note \- the patch.fat/.dat files from the default game use compression, so you’re using them your newly created patch.fat/.dat files will be significantly bigger. Once they have been unpacked and packed once they will no longer disproportionately increase in size with future edits.

2. To use these files remove the “\_unpack” section of the filename and they can replace the original .fat and .dat files you had.

## .fcb files

These files are also basically folders full of other files, except .fcb files always contain only .xml files. Use the tools from the ‘Packing/unpacking’ section above, available [HERE](https://www.moddb.com/mods/far-cry-2-redux/downloads/far-cry-2-mod-tools).

### Unpacking .fcb files

1. Copy the .fcb file into the same folder as your modding tools.  
2. Drag EXAMPLE.fcb onto “Gibbed.Dunia.ConvertBinary.exe” and this will create two things: an “EXAMPLE” folder and an “EXAMPLE.xml” file, both with the same name as the file you unpacked.

	The “EXAMPLE” folder contains .xml files, notice that each .xml file starts with a number e.g. “03\_…”.  
	The “EXAMPLE.xml” file is a list of the other .xml files contained in the new folder.

### Adding to .fcb files

1. To add a .xml file first copy it into the “EXAMPLE” folder.  
2. Add a number to the start of the filename that places your new file at the bottom of the list.  
3. Open “EXAMPLE.xml”. You can see that each .xml file in the “EXAMPLE” folder is listed in the format of “\<object external="38\_XXXXXX.xml" /\>”. Copy the last line with a filename and paste it directly below, so you have two identical lines that contain a filename.  
4. Edit the filename in the line you have pasted so it matches the filename of the .xml you added into the “EXAMPLE” folder. Don’t forget to change the number as well. It should look something like this:  
5. Once you are finished press ‘File \> Save’.

### Packing .fcb files

1. Drag “EXAMPLE.xml” onto “Gibbed.Dunia.ConvertBinary.exe”. This will create a new “EXAMPLE.fcb” which will overwrite your original .fcb file.

## .xml files

These are your most common files for Far Cry 2 modding and are used for almost all files controlling gameplay. They are easy to use, simply open them with Notepad++ (available [HERE](https://notepad-plus-plus.org/downloads/)), make your edits, and press ‘File \> Save’ when you’re done.

### Decoding .xml files

Decoding is only needed when information within the game files is in hex code. This is when you see sections that look like this: 

```xml
<object hash="BB04E184">
              <value hash="7D725133" type="BinHex">0000803F</value>
              <value hash="E49EEB82" type="BinHex">00</value>
              <value hash="DD39539B" type="BinHex">0000803F</value>
              <value hash="FB4ADD00" type="BinHex">8B6CA73F</value>
              <value hash="67872049" type="BinHex">0000003F</value>
              <value hash="5AF667C3" type="BinHex">0000003F</value>
              <value hash="1D75A72B" type="BinHex">0000003F</value>
              <value hash="F221024D" type="BinHex">00</value>
              <value hash="EAB69D7F" type="BinHex">00</value>
              <value hash="15E4D53E" type="BinHex">00</value>
              <value hash="D553AED0" type="BinHex">69726F6E7369676874667800</value>
              <value hash="C5152E83" type="BinHex">3EA563ED</value>
</object>
```

There is information hidden here which the decoder will reveal.

Be aware that you can’t copy a section of a non-decoded file into a decoded file or vice-versa. Both files need to be decoded to copy sections between them.

These are the steps:

1. Download my .xml decoder in the ‘Mod tools’ section.  
2. Copy any .xml files into the “Put xml files to decode in here” folder.   
3. Run “Start XML Decoder.bat”. A command prompt window will appear and show details about the files being decoded.  
4. Once the window says “All XML files processed” you can copy the files back into their original location.  
 


## .rml files

These files control all of the text you see in-game. There is one for each language where all the menus, subtitles, tutorial pop-pups etc can be changed. Use the tools from the ‘Packing/unpacking’ section above, available [HERE](https://www.moddb.com/mods/far-cry-2-redux/downloads/far-cry-2-mod-tools).

Unpacking .rml files

1. Copy your “EXAMPLE.rml” file into the modding tools folder.  
2. Drag the file onto “Gibbed.Dunia.ConvertXml.exe”. This will create a file called “EXAMPLE\_converted.xml” where you can make your edits.

### Packing .rml files

1. Drag “EXAMPLE\_converted.xml” onto “Gibbed.Dunia.ConvertXml.exe”. This will create a file called “EXAMPLE\_converted\_converted.rml”.  
2. Rename this file to “EXAMPLE.rml” and you can replace your original file.

## .xbt files

These are texture files. The .xbt file type is useless in itself so before working with it they need to be converted to .dds.

### Converting .xbt files

1. Download “RunGUI” from the ‘Texture conversion’ section above, available [HERE](https://www.nexusmods.com/farcry3/mods/214?tab=files).  
2. Copy your .xbt files into the “IN” folder within the “RunGUI” folder.  
3. Open RunGUI. A pop-up will appear asking if you want to update your Gibbed tools, press no.  
4. This tool has a variety of functions but I only use it for texture conversion, this section:

	  
The convert function is pretty simple. Pressing the “XBT to DDS” will convert your .xbt files into .dds and put them in the “OUT” folder.

5. Open your .dds image from the “OUT” folder, make your edits and save when you’re done.  
6. To convert back press the “DDS to XBT” button and your .dds image will be converted and put in the “IN” folder, which will overwrite your original file.

### Editing .xbt files

I’m going to describe doing this with Photoshop and the Intel Texture Works Plugin, available [HERE](http://gametechdev.github.io/Intel-Texture-Works-Plugin/).

1. Double clicking on your .dds should automatically open the file in Photoshop once the plugin is installed.  
2. This pop-up will appear before the file opens:

	Don’t tick the box and press ok. The mip-maps will be auto-generated when we save.

3. Make your edits and when you’re finished press File \> Save.  
4. This pop-up will appear, you should match the settings with those I have selected:

5. Once you’ve saved your file you can move it into the RunGUI “OUT” folder and carry on with the texture converting tutorial above.

## .xbm files

These are material files, they control an item’s physical appearance as a mix of textures but we can’t edit them like texture files.

A hex editor can be used to get an idea of what item they are a part of. Near the top of the column to the right in your editor there will be file paths that can give you a good hint, such as in the screenshot below in a file link to the hang-glider:

It is possible to change the filenames of these files to swap them around and replace each other, which is how I know to use them. It is also possible to edit the texture filenames to swap textures around but I haven’t found a need to do that.

## .mgb and .desc files

These files control the game menus. They can only be edited with a hex editor but they can also be swapped with each other by copying the file names.

The .mgb and .desc files come in pairs but I have found that I only needed to edit the .mgb files without touching or replacing the corresponding .desc file.

## .mab files

These are animation files. They can only be edited with a hex editor but they can also be swapped with each other by copying the file names.

## .lua files

These files control mission scripting. They can be editing the same as .xml files, so just open with Notepad++ and press File \> Save when you’re finished.

## .dll files

There is a single .dll file involved in modding, Dunia.dll. It controls lots of engine settings and can only be edited with a hex editor.

