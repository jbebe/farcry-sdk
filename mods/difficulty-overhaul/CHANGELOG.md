# Changelog

Notable changes to Difficulty Overhaul, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [0.1.0] - 2026-10-28

### Added
- **Lethal both ways.** About four torso hits or one head hit kill you, and the last health bar no
  longer softens what you take. About two torso hits or four limb hits drop a soldier.
- **Healing in three steps.** On your last bar the surgery leaves you one rifle hit from death, and
  a syrette two. Only full health takes four, and it comes back slowly: nothing below a syrette's
  level, and from there to full in 6 minutes on Hardcore, in seconds on Casual and Experienced and
  never on Infamous. Beds and medic cabinets still heal you fully.
- **Limb hits wound.** A hit to an arm or a leg that would kill a soldier puts him down wounded
  instead: he calls for help, crawls for cover and may shoot back from the ground.
- **Aim is earned.** A soldier's aim settles after he spots you, longer at range. Every shot is a
  real chance and none is certain, recoil spoils his follow-up shots, and you are harder to hit
  running across his line of fire than running at him. He never aims at your head in his first 3
  seconds on you.
- **Fire discipline and grass.** Soldiers hold their fire until they see you clearly, and high grass
  hides you crouched and still even in a firefight.
- **Suppression.** Bullets landing near a soldier pin him for a second and spoil his aim.
- **Squads hunt where they last saw you** for as long as you stay near it; only slipping away unseen
  loses them. A soldier keeps his rifle on the spot where you ducked out of sight.
- **Eyesight.** Soldiers see farther by day, notice movement at the edge of their vision before they
  know it is you, and hear you walk or run behind them within 20 m. Crouched, you can creep up
  unheard.
- **Weapons by range.** Pistols and SMGs lose accuracy with range. A shotgun is deadly out to 20 m
  and falls off fast past it.
- **No rockets at you on foot.** A soldier with an RPG or a Carl Gustav draws his other gun at you on
  foot, and keeps the launcher for you in a vehicle.
- **A combat log** in `bin\fcse.log`: every hit on you and by you, and every shot at you with its
  chance.
- **Tuned on Hardcore.** In this version every difficulty plays at the Hardcore values except healing.
