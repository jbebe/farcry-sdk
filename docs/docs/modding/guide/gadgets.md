---
sidebar_position: 17
sidebar_label: "Gadgets"
format: md
---

:::info[From the Almost Complete Guide]
This page reproduces a section of ["An Almost Complete Guide to Far Cry 2 Modding"](./index.md) by Boggalog, verbatim, with attribution. See the [guide index](./index.md) for the full attribution note.
:::

# Gadgets

## Monocular

xx\_gadgets.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

The monocular’s entry title in this file is “Equipped.Monocular”.

### Look sensitivity

**Decoding required**  
xx\_gadgets.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Look sensitivity of the monocular while aiming is controlled by the “fLookSensitivity” stat in the “UseStrategy” section.

The default value is “0.1” and you can decrease this value to reduce sensitivity.

```xml {5}
<object hash="E1F6C228" type="UseStrategy">
       <object hash="1719D64A" type="Zoom">
          <value name="fFOV" hash="BEF721BA" type="Float">0.2</value> <!-- type="BinHex" value="CDCC4C3E" -->
          <value name="fTransitionTime" hash="0885811A" type="Float">0.5</value> <!-- type="BinHex" value="0000003F" -->
          <value name="fLookSensitivity" hash="B2E41011" type="Float">0.1</value> <!-- type="BinHex" value="CDCC4C3D" -->
```

### Zoom

**Decoding required**  
xx\_gadgets.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Zoom level of the monocular is controlled by the “fFOV” stat in the “UseStrategy” section.

The default value is “0.2” and you can decrease this value to increase zoom.

```xml {3}
<object hash="E1F6C228" type="UseStrategy">
      <object hash="1719D64A" type="Zoom">
         <value name="fFOV" hash="BEF721BA" type="Float">0.2</value> <!-- type="BinHex" value="CDCC4C3E" -->
         <value name="fTransitionTime" hash="0885811A" type="Float">0.5</value> <!-- type="BinHex" value="0000003F" -->
         <value name="fLookSensitivity" hash="B2E41011" type="Float">0.05</value> <!-- type="BinHex" value="CDCC4C3D" -->
```

