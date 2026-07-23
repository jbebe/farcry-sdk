---
sidebar_position: 19
sidebar_label: "DLC Unlocking"
format: md
---

:::info[From the Almost Complete Guide]
This page reproduces a section of ["An Almost Complete Guide to Far Cry 2 Modding"](./index.md) by Boggalog, verbatim, with attribution. See the [guide index](./index.md) for the full attribution note.
:::

# DLC unlocking

## Predecessor missions

Dunia.dll (\\Far Cry 2\\bin\\)

Find the hex string “85 C9 74 16 8B 44 24 04 50 E8” and change it to “85 C9 **EB 0E** 8B 44 24 04 50 E8”.

Your completed section should look like this:

## Primitive/Homemade machetes

To unlock the DLC machetes we need to add three new entries to the registry. 

The first step is to access the registry by opening the “Registry Editor” application. This is in all Windows installations so just search for it in the start menu and it’ll be there.

Once open you’ll see that it shows a directory list on the left and the actual registry keys on the right.

In the directory section, navigate to \\HKEY\_CURRENT\_USER\\Software\\Ubisoft\\Far Cry 2\\.

It should look like this:

To create a new entry here, right click the section on the right, press “New” and then “DWORD (32-Bit) Value”.

You need to create three new keys in the same way, and call them “MachetesKey”, “PartnerKey1” and “PartnerKey2”.

We then need to change the data value of each of these keys. They each need to be changed to a value of 1\.

To do this double click any of the keys and change the “Value data” to 1\.

Do this to all of the keys and you’re done. It should look like this:

If you want to create an installer for this, highlight all of the three keys and press “File” and “Export”. Name the file whatever and save it.

Go to the file, right click it and press edit. It will then open in Notepad.

Once it’s open, delete all of it except the top section, so it looks like this:

Save the file and then when anyone else runs it the keys will be automatically installed.

