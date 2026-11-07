# Changelog

Notable changes to Difficulty Overhaul, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [0.1.0] - 2026-10-28

### Added
- **Real rounds.** Every gun hits as hard as its real cartridge, whoever fires it: one torso hit from
  a rifle drops a soldier, two from a pistol, a head hit from anything kills. A soldier's gun hurts
  you as much as yours hurts him, and no gun gains damage up close.
- **Blood as the round would show it.** On you as on a soldier, a pistol or SMG round throws no
  spray of blood, a rifle round or a shotgun pellet only on a head hit, and only the .50 BMG on any
  hit.
- **Difficulty is how much you can take.** On Easy and Normal you take as many AK-47 hits as in the
  game, 37 and 28 from full; on Hardcore the third kills you and on Infamous the first, wherever it
  lands. The soldiers aim and hit the same on every setting. On Easy and Normal your last bar still softens hits,
  as in the game, and on Hardcore and Infamous no longer does.
- **Healing as in the game, but on Infamous.** The bar you are in refills, and a lost bar needs a
  syringe; on Infamous nothing refills. On your last bar you still bleed and need the surgery.
- **Limb hits wound.** A hit to an arm or a leg that would kill a soldier puts him down wounded
  instead: he calls for help, crawls for cover and may shoot back from the ground. A torso hit never
  does: it wounds or kills.
- **Aim is earned.** A soldier fires the moment he recognises you. One you surprise, your own shot
  included, sends his first shots wide while his aim settles, longer the farther you are; one who has
  been hunting you settles in a third of a second and can hit with his first. Every shot is a real
  chance and none is certain, recoil spoils his follow-up shots, and you are harder to hit running
  across his line of fire than running at him. He never aims at your head in his first 3 seconds on
  you.
- **Fire discipline.** Soldiers hold their fire until they recognise you, waiting aimed where they
  are. Once they are fighting you they fire at what they glimpse of you, a fifth farther out, but the
  less they see, the less they hit.
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
  Alerted soldiers see a quarter farther, those fighting you half as far again, and a soldier at the wheel only through his windscreen. The
  camo suit hides you crouched and still in high grass beyond 3 m, unless he is fighting you.
  In front of him he notices you at once. At his side, beyond 20 m, he needs a moment, longer the
  farther and the less visible you are, up to 2 seconds, so a dash from cover to cover past his side
  can go unseen. Your shots give you away to soldiers already hunting you: one who hears a shot, as
  far as your gun is heard (an AK-47 75 m, a sniper rifle 150 m, a silenced gun a few metres), makes
  you out wherever any of you shows, until he loses sight of you. A shot that alerts a soldier only
  tells him where it came from.
- **Weapons by range.** Pistols and SMGs lose accuracy with range. A shotgun is deadly out to 20 m
  and falls off fast past it.
- **Squad tactics.** Within 35 m half a squad pins you down from cover while the other half moves up,
  and the ones moving up fire from cover too: up over a low wall, out from a corner, or standing behind
  a tall one. Caught in the open with their cover more than 10 m off, they crouch and shoot where they
  stand. Once you are behind cover, they make for cover first, however far, and with none within a 20 m
  run they rush you. With no cover to reach they run out to your side rather than straight at you,
  farther the farther off they are, they go after you from a firing spot or a way up when you are above
  them, and they move once under fire or hurt.
- **No rockets at you on foot.** A soldier with an RPG or a Carl Gustav draws his other gun at you on
  foot, and keeps the launcher for you in a vehicle.
- **Alerts end.** A soldier who has lost you gives up after 90 seconds, walks back to where he was
  when the alert began and goes back to his post, where the game kept him searching for good.
- **A combat log** in `bin\difficulty-overhaul-logs`, one file per session and the last 20 kept:
  every hit on you and by you, and every shot at you with its chance.
- **Tuned on Hardcore.** The difficulties differ only in your health and how it comes back.
