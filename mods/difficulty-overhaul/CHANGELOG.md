# Changelog

Notable changes to Difficulty Overhaul, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [0.1.0] - 2026-10-28

### Added
- **Real rounds.** Every gun hits as hard as its real cartridge, whoever fires it: one torso hit from
  a rifle drops a soldier, two from a pistol, a head hit from anything kills. A soldier's gun hurts
  you as much as yours hurts him, and no gun gains damage up close.
- **Difficulty is how much you can take.** From full health an AK-47 hit kills you on Infamous,
  the third on Hardcore, the fifth on Normal and about the eighth on Easy, wherever it lands; the
  soldiers aim and hit the same on every setting. On Easy and Normal your last bar still softens hits,
  as in the game, and on Hardcore and Infamous no longer does.
- **Wounds heal on their own.** Above your last bar health climbs back to full: on Easy from a
  second after the last hit, in 10 seconds; on Normal from 3 seconds, in a minute; on Hardcore from 5
  seconds, in 5 minutes; never on Infamous. The game refilled only the bar you were in. On your last
  bar you still bleed and need the surgery, as in the game.
- **Limb hits wound.** A hit to an arm or a leg that would kill a soldier puts him down wounded
  instead: he calls for help, crawls for cover and may shoot back from the ground. A torso hit never
  does: it wounds or kills.
- **Aim is earned.** A soldier who spots you up close fires at once; farther out he takes longer
  to make up his mind, 3 seconds at 50 m, and his aim settles meanwhile. Every shot is a real
  chance and none is certain, recoil spoils his follow-up shots, and you are harder to hit running
  across his line of fire than running at him. He never aims at your head in his first 3 seconds on
  you.
- **Fire discipline.** Soldiers hold their fire until they recognise you, waiting aimed where they
  are.
- **Suppression.** Your bullets shake a soldier's aim for a second but never silence him: hit him and
  he fires back in a panic.
- **Squads hunt where they last saw you** for as long as you stay near it; only slipping away unseen
  loses them. A soldier keeps his rifle on the spot where you ducked out of sight.
- **How soldiers see you.** By day, in the open and in front of him, a soldier recognises you
  walking upright from 60 m, crouched and moving from 50 m and still from 40 m, and notices
  something from a little farther; movement in daylight catches his eye from 100 m, unless you
  stand in shade. At his side, crouched in high grass, or in shade by day, it is half. Cover hides
  you only where it hides your head, and tall grass
  on a crest between you hides you as a rock would, unless your head shows above it. A bush between
  you hides you wherever its leaves are, where the game counted only its heart; one cuts how far he
  sees you to a tenth, and leaves shot off let him see through. At night he
  recognises you from 25 m at most, whatever you do, a quarter farther under a lamp, and in a
  sandstorm from 40 m at most. Behind
  him he never sees you, but hears you walk upright within 20 m; crouched, you can creep up unheard.
  Alerted soldiers see a quarter farther, and a soldier at the wheel only through his windscreen. The
  camo suit hides you crouched and still in high grass beyond 3 m, unless he is fighting you.
  Beyond 30 m in front of him, or 20 m at his side, he needs a moment to notice you, longer the
  farther and the less visible you are, up to 4 seconds: running across open ground at 50 m gives you
  about 2, so a dash from cover to cover can go unseen.
- **Weapons by range.** Pistols and SMGs lose accuracy with range. A shotgun is deadly out to 20 m
  and falls off fast past it.
- **No rockets at you on foot.** A soldier with an RPG or a Carl Gustav draws his other gun at you on
  foot, and keeps the launcher for you in a vehicle.
- **Alerts end.** A soldier who has lost you gives up after 90 seconds, walks back to where he was
  when the alert began and goes back to his post, where the game kept him searching for good.
- **A combat log** in `bin\difficulty-overhaul-logs`, one file per session and the last 20 kept:
  every hit on you and by you, and every shot at you with its chance.
- **Tuned on Hardcore.** The difficulties differ only in your health and how it comes back.
