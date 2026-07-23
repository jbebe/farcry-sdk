---
sidebar_position: 16
sidebar_label: "Open World Items/Objects"
format: md
---

:::info[From the Almost Complete Guide]
This page reproduces a section of ["An Almost Complete Guide to Far Cry 2 Modding"](./index.md) by Boggalog, verbatim, with attribution. See the [guide index](./index.md) for the full attribution note.
:::

# Open world items/objects

## Explosive Objects

xx\_OA\_Explosives.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Every explosive object in the open world has an entry in this file:

Explosives.BarrelDirectional01  
Explosives.ExplodingCar\_PREFAB  
Explosives.ExplodingTruck\_PREFAB  
Explosives.ExplosiveBarrel\_NEW  
Explosives.GasBottle01  
Explosives.GasBottle2  
Explosives.GasBottle3  
Explosives.GasBottle4  
Explosives.GasCan01\_NEW  
Explosives.GasCan02\_NEW  
Explosives.GasPump  
Explosives.Generator\_PREFAB  
Explosives.LiquidPropaneTank  
Explosives.LiquidPropaneTank\_Small  
Explosives.OilTank\_NEW  
Explosives.SmallPropaneTank  
Explosives.ThinPropaneTank

The most useful edit available here is changing the explosive properties of these objects.

All of the entries have an “ExtraStims” section where we can edit the explosive properties. In this section there may be two or three subsections under the titles “Stim”. Each “Stim” section is a different element of the explosion. We can tell the different explosive elements apart with the “selType” value, where “4” is a regular explosion and “7” is a fire explosion.

The main stats we can change here are: “nLevel” and “fRadius”.

“nLevel” controls the damage an explosion does. This doesn’t affect fire explosions as fire still does the same damage.

“fRadius” controls the size of an explosion.

This is an example regular explosion:

```xml {7,8,9}
<object type="Stim">
       <value name="hidEventName" type="String">Stims</value>
       <value name="eventMask" type="UInt32">2</value>
       <value name="hidTargetEntityId" type="UInt64">18446744073709551615</value>
       <value hash="FC25E1F1" type="String"></value>
       <value name="sDetail" type="Hash">FFFFFFFF</value>
       <value name="selType" type="UInt32">4</value>
       <value name="nLevel" type="UInt32">15</value>
       <value name="fRadius" type="Float">10</value>
       <value name="bFalloff" type="Bool">True</value>
       <value name="nFalloffMinLevel" type="UInt32">1</value>
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
```
     

## Weapons

xx\_pickups.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Every weapon found in the open world has an entry in this file. This includes weapons found in the armory, dropped by enemies, and those found in the open world either given by buddies for missions or in found in the wild like the golden ak47:

| Weapon Name | Armory entry | Dropped by enemies entry | Open world entry |
| :---- | :---- | :---- | :---- |
| AK47 | Weapons.AK47\_new.WeaponStorage | Weapons.AK47\_new.Dropped |  |
| Golden AK47 |  |  | Weapons.AK47\_new.AK47\_Gold |
| AS50 | Weapons.AS50\_new.WeaponStorage | Weapons.AS50\_new.Dropped | Weapons.AS50\_new.Persistent |
| Carl Gustaf | Weapons.CarlGustaf\_new.WeaponStorage | Weapons.CarlGustaf\_new.Dropped | Weapons.CarlGustaf\_new.Persistent |
| Dart Rifle | Weapons.DartRifle\_new.WeaponStorage | Weapons.DartRifle\_new.Dropped |  |
| Eagle .50 | Weapons.DesertEagle\_new.WeaponStorage | Weapons.DesertEagle\_new.Dropped |  |
| Dragunov | Weapons.Dragunov\_new.WeaponStorage | Weapons.Dragunov\_new.Dropped |  |
| Flare Gun | Weapons.FlareGun\_new.WeaponStorage | Weapons.FlareGun\_new.Dropped |  |
| FAL | Weapons.FNFAL\_new.WeaponStorage | Weapons.FNFAL\_new.Dropped |  |
| G3KA4 | Weapons.G3KA4\_new.WeaponStorage | Weapons.G3KA4\_new.Dropped |  |
| IED | Weapons.IED\_new.WeaponStorage | Weapons.IED\_new.Dropped |  |
| Ithaca | Weapons.Ithaca\_new.WeaponStorage | Weapons.Ithaca\_new.Dropped |  |
| LPO50 | Weapons.LPO50\_new.WeaponStorage | Weapons.LPO50\_new.Dropped | Weapons.LPO50\_new.Persistent |
| M16 | Weapons.M16\_new.WeaponStorage | Weapons.M16\_new.Dropped | Weapons.M16\_new.Persistent |
| M1903 | Weapons.M1903\_new.WeaponStorage | Weapons.M1903\_new.Dropped |  |
| M249 Saw | Weapons.M249\_Saw\_new.WeaponStorage | Weapons.M249\_Saw\_new.Dropped | Weapons.M249\_Saw\_new.Persistent |
| M67 |  | Weapons.M67.Dropped |  |
| M79 | Weapons.M79\_new.WeaponStorage | Weapons.M79\_new.Dropped |  |
| MAC10 | Weapons.MAC10\_new.WeaponStorage | Weapons.MAC10\_new.Dropped |  |
| Makarov | Weapons.Makarov\_new.WeaponStorage | Weapons.Makarov\_new.Dropped |  |
| MGL140 | Weapons.MGL140\_new.WeaponStorage | Weapons.MGL140\_new.Dropped | Weapons.MGL140\_new.Persistent |
| Molotov |  | Weapons.Molotov.Dropped |  |
| Mortar | Weapons.Mortar\_new.WeaponStorage | Weapons.Mortar\_new.Dropped | Weapons.Mortar\_new.Persistent |
| MP5 | Weapons.MP5\_new.WeaponStorage | Weapons.MP5\_new.Dropped |  |
| PKM | Weapons.PKM\_new.StorageRoom | Weapons.PKM\_new.Dropped |  |
| RPG7 | Weapons.RPG7\_new.StorageRoom | Weapons.RPG7\_new.Dropped | Weapons.RPG7\_new.Persistent |
| Silenced Makarov | Weapons.SilencedMakarov\_6P9.StorageRoom | Weapons.SilencedMakarov\_6P9.Dropped |  |
| SPAS12 | Weapons.SPAS12\_new.StorageRoom | Weapons.SPAS12\_new.Dropped |  |
| Star 45 | Weapons.Star45\_new.StorageRoom | Weapons.Star45\_new.Dropped |  |
| USAS12 | Weapons.USAS12\_new.StorageRoom | Weapons.USAS12\_new.Dropped | Weapons.USAS12\_new.Persistent |
| Uzi | Weapons.Uzi\_new.StorageRoom | Weapons.Uzi\_new.Dropped |  |

### Respawn time

xx\_pickups.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Respawn time of the weapons is controlled by the “fRespawnTime” stat in the “Components” section.

A value of “0” means that the weapons will never respawn, any other value is measured in seconds.

These timers only count down while you are in the same area as the weapon. If you leave the area and return so that the area is reloaded, the timer is reset.

```xml {5}
<object type="Components">
      <object type="CPickupWeapon">
      <value name="hidHasAliasName" type="Bool">False</value>
      <value name="bEnable" type="Bool">True</value>
      <value name="fRespawnTime" type="Float">0.1</value
      <value name="bCustomBoundingBox" type="Bool">True</value>
```

## Small Resource Pickups

This section will cover the small resource pickups in the game, so the small ammo/explosive/fuel/health boxes.

### Ammo/explosive/fuel boxes \- Ammo type/quantity

**Decoding required**  
xx\_pickups.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

These are the entry titles of the different ammo/explosive/fuel boxes:

Ammo.Small\_Ammo\_Pickup  
Ammo.Small\_Explosive\_Pickup  
Ammo.Small\_Fuel\_Pickup

Ammo type is controlled by two stats, “AB258E09” and “ammoAmmoType”. Gadget type (grenades/molotovs) is controlled by the “archGadgetType” stat.

Ammo quantity is controlled by the “iAmmoQuantity” stat and gadget quantity is controlled by the “iGadgetQuantity” stat.

The ammo types and amounts for each pickup type are shown in the table below. You can edit the quantities or add/remove ammo types between the different pickups.

| Ammo.Small\_Ammo\_Pickup |  | Ammo.Small\_Explosive\_Pickup |  | Ammo.Small\_Fuel\_Pickup |  |
| :---- | :---- | :---- | :---- | :---- | :---- |
| Ammo Type | Ammo Quantity | Ammo Type | Ammo Quantity | Ammo Type | Ammo Quantity |
| Pistol ammo  AB258E09: 6465736572746561676C6500 ammoAmmoType: 6D6540FA | 8 | Mortar ammo  AB258E09: 6D6F7274617200 ammoAmmoType: 4EE9BFD6 | 1 | Flamethrower ammo  AB258E09: 6675656C00 ammoAmmoType: 31BD6FE9 | 100 |
| Assault rifle ammo  AB258E09: 61737361756C747269666C6500 ammoAmmoType: BC6782FC | 30 | RPG/Crossbow ammo  AB258E09: 726F636B657400 ammoAmmoType: CEB9BB1E | 1 | Flare gun ammo  AB258E09: 666C61726500 ammoAmmoType: C86412FF | 3 |
| Sniper rifle ammo  AB258E09: 736E697065727269666C6500 ammoAmmoType: 7D6BD5F2 | 5 | M79 ammo  AB258E09: 6D373900 ammoAmmoType: 704CA95D | 2 | Molotovs archGadgetType: gadgets.Grenades.Molotov | 2 |
| Shotgun ammo  AB258E09: 73686F7467756E00 ammoAmmoType: EEAE53E1 | 6 | IED ammo  AB258E09: 69656400 ammoAmmoType: EA12131E | 1 |  |  |
| SMG ammo  AB258E09: 736D6700 ammoAmmoType: AA73EE0A | 30 | MGL140 ammo  AB258E09: 6D676C31343000 ammoAmmoType: E710123D | 4 |  |  |
| LMG ammo  AB258E09: 6C6D6700 ammoAmmoType: BD090A47 | 50 | Grenades archGadgetType: gadgets.Grenades.M67 | 2 |  |  |
| Dart rifle ammo  AB258E09: 646172747300 ammoAmmoType: FC2096BC | 2 |  |  |  |  |

### Health boxes \- Syrette quantity

**Decoding required**  
xx\_pickups.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

The small health boxes have a single entry, titled “pickups.Health.Syrette”.

The number of syrettes given by the small health pickups is controlled by the “iBullets” stat in the “Components” section.

```xml {3}
<value hash="AB258E09" type="BinHex">737972696E676500</value>
          <value name="ammoAmmoType" hash="5957C8C7" type="Hash">FCD0CC7A</value> <!-- type="BinHex" value="7ACCD0FC" -->
          <value name="iBullets" hash="C38CDB93" type="Int32">1</value> <!-- type="BinHex" value="01000000" →
```

