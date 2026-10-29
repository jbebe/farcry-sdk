# Difficulty Overhaul

Realistic damage and enemy shooting for Far Cry 2. A bullet that connects is serious for you and for
a mercenary, and whether it connects depends on range, movement, how well the shooter sees you,
recoil and the time he needs to settle his aim. Hard, but every death has a reason you can read.

- **Lethal both ways.** About four torso hits or one head hit kill you, and the last health bar no
  longer softens what you take. About two torso hits or four limb hits drop a soldier.
- **Healing in three steps.** On your last bar the surgery leaves you one rifle hit from death, and
  a syrette two. Only full health takes four, and it comes back slowly: nothing below a syrette's
  level, and from there to full in 6 minutes on Hardcore, in seconds on Casual and Experienced and
  never on Infamous. A syrette is not used where it would not raise your health. Beds and medic
  cabinets still heal you fully.
- **Limb hits wound.** A hit to an arm or a leg that would kill a soldier puts him down wounded
  instead: he calls for help, crawls for cover and may shoot back from the ground.
- **Aim is earned.** A soldier who spots you holds his fire for a moment, longer at range, so you
  have time to duck, and his aim settles meanwhile. Every shot is a real chance and none is certain,
  recoil spoils his follow-up shots, and you are harder to hit running across his line of fire than
  running at him. He never aims at your head in his first 3 seconds on you, so a first sighting
  cannot end in a headshot.
- **Fire discipline and grass.** Soldiers hold their fire until they can make you out, waiting aimed
  where they are, and high grass hides you crouched and still even in a firefight.
- **Suppression.** Your bullets shake a soldier's aim for a second but never silence him: hit him and
  he fires back in a panic.
- **Squads hunt where they last saw you** for as long as you stay near it; only slipping away unseen
  loses them. A soldier keeps his rifle on the spot where you ducked out of sight, so peek from
  somewhere else.
- **Eyesight.** Soldiers see farther by day, so a man standing in the open at 50 m is seen. They
  notice movement at the edge of their vision before they know it is you, and hear you walk or run
  behind them within 20 m. Crouched, you can creep up unheard.
- **Weapons by range.** Pistols and SMGs lose accuracy with range. A shotgun is deadly out to 20 m
  and falls off fast past it.
- **No rockets at you on foot.** A soldier with an RPG or a Carl Gustav draws his other gun at you on
  foot, and keeps the launcher for you in a vehicle.
- **A combat log** in `bin\fcse.log`: every hit on you and by you, and every shot at you with its
  chance.

Everything is tuned on Hardcore. In this version every difficulty plays at the Hardcore values
except healing; easier and harder settings come later. Fights between soldiers, and between soldiers and your
buddies, play as the game ships them.

## Requirements

- **FCSE 1.4.0 or later**, on Far Cry 2 1.03 (Steam or GOG).
- **JackAll 1.7.0 or the Vortex extension 0.6.0, or later**, to install it. Older versions install
  the plugin but leave the soldiers' brain as it was, without saying so.

## Installing

This archive is a JackAll layer: `plugins\` is the FCSE plugin, and `mods\` holds the changes to the
soldiers' brain, one file per plan or task, which the installer merges into the game's own brain.

- **Vortex** — with the Far Cry 2 extension installed, drop the zip in and enable it.
- **JackAll** — add the zip in the app, or from the command line:

```
jackall-cli mod build   --game "C:\Games\Far Cry 2" --layer difficulty-overhaul.zip
jackall-cli mod restore --game "C:\Games\Far Cry 2"
```

`mod restore` is the uninstall, and removes the plugin too.

## Compatibility

- **Mods that change the soldiers' brain** merge with it plan by plan, and a mod that replaces the
  brain whole still works: these changes go on top of it.
- **Mods that change weapon damage or health** change the numbers this mod multiplies.
- **Flashlight:** its guards noticing your light does not work while this mod is installed.
