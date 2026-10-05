---
title: Functional Outposts
kind: component
bundle: features
claims:
  - "Functional Outposts: After clearing an outpost, the enemies within it will not respawn until 45 minutes (real time) has passed"
status: located
systems: [missions, ai]
match:
  # the guards' new mission layers: sector layout (what gates spawning) and each entity's filing
  - "levels/*/generated/worldsectors/worldsector*.data.fcb/_layout.xml#layer[missions\\outposts\\*]"
  - "levels/*/generated/worldsectors/worldsector*.data.fcb/_layout.xml#remove[*]"
  - "levels/*/generated/worldsectors/worldsector*.data.fcb/*#Components/CMissionComponent"
  - "levels/*/generated/worldsectors/worldsector*.data.fcb/*#Components/CMissionComponent/{hidMissionLayerPath,text_hidMissionLayerPath}"
  # one mission per outpost, one omni entity hosting its script (generated names are 7 characters)
  - "worlds/*/generated/world*.game.xml/missions/outposts/**"
  - "worlds/*/generated/world*.omnis.fcb/dominoomnientity_???????.*.xml"
  # the 58 generated outpost scripts
  - "_hash/{01257145,03a8392a,0ec4e621,122e9803,1b338b25,1d7cd41b,1ef010f9,20980670,20cea93e,23903949,23f18db6,249756f8,255b5cc0,2e5b0c9a,31553a84,3ca1368a,59506a29,5ac3411e,5adcaecb,6379a7a6,64304399,65f82f9f,6bc52376,6c274ece,6fe63b3a,745722b4,7a2056b7,7c8c1bc7,856d5343,87e34758,8c6fa9cb,90874916,91af0e71,933c8d6a,994acda3,99e72643,9a55fdab,9bdc1007,a4c39e5a,a9ebe81e,aa3f7dd1,adb3626f,ade0e52e,c28850c7,c9b66285,ce606421,d13efd0a,d307f917,d5dabae6,d62db899,d642ef66,eb70b639,f212eede,f6569bcb,fb9e19b1,fc6c2649,fd92d985,feda5a2d}.lua"
  # the "Outpost Cleared" popup
  - "languages/*/oasisstrings.fragment.xml#SafeHouse/PGPUnlockedSwoosh"
  - "onscreenpopup/onscreendata.xml#Objective[PGPUnlocked]"
exclude: []
requires: [outposts-delay-presets]
verified: diff
---

# Functional Outposts

Every guarded outpost becomes its own respawn group. Once the player kills all of an outpost's
guards an "Outpost Cleared" popup shows, and the guards stay dead until `OutpostDelay` seconds have
passed (2700 s, 45 minutes, by default), after which the whole group spawns again. scubrah also
released this as the standalone mod "Functional Outposts" v1.1; the scripts here are headed
`-- Generated for "Functional Outposts" v1.0`.

## How

- **58 outposts, one mission each.** New missions `Missions/Outposts/<cell>/<name>` in
  `world1.game.xml` (32) and `world2.game.xml` (26), each owning one layer
  `missions\outposts\<cell>\<name>`, active by default (`MissionLayerActiveDflt="1"`, `State="1"`).
  Names are random seven-character tags (`zXmOLgx`) or `MANUAL1`-`MANUAL9`, `MANUALA`-`MANUALC`.
- **Guards moved into those layers.** In the world sectors, 90 `_layout.xml` `layer[...]` ops place
  303 entities (the guards and some of their pickups and props) under the outpost layers; this is
  what makes the layer control whether they spawn (see
  [entity instancing](../../../docs/docs/engine-internals/entity-instancing.md)). Each moved
  entity's `CMissionComponent` is refiled to match: 126 entities change `hidMissionLayerPath` (and
  its `text_` twin) away from their old layer, and 178 entities from `main` gain a
  `CMissionComponent` naming the outpost layer. The old mission layers were
  `missions\_disableformission\` `pgp_ai`, `w1d4_a09centralroad_enemiesstp`, `w2b4_a17_enemies`,
  `a21_bridgearmsbazaar_ai`; `missions\convoymissions\convoy_0N_disable`;
  `missions\librarymissions\a1lm01\misnbase_patrol_hide` and `a1lm04`;
  `missions\storymissions\a2sm05\a2sm05_ai_disable`; `missions\ubidays\5a_ai_minepgp_01`. 30
  `remove[...]` ops drop the old layers a move left empty in a sector.
- **One script per outpost.** 58 new `DominoOmniEntity_<name>` entities in `world1.omnis.fcb` /
  `world2.omnis.fcb` each run `domino\User\Outpost_V2\<cell>-<name>.lua`, stored nameless as the 58
  `_hash/*.lua` above. Each script watches every guard with a `HealthEvents` box and counts kills;
  a `StatusListener` on one guard resets the count when the group spawns or is removed. At the full
  count it pushes the popup (`SafeHouse` section, `PGP` icon, text `PGPUnlockedSwoosh`), disables
  the outpost's mission through `SetMissionState`, then starts a `Delay` with `Seconds = "Outpost"`;
  when that elapses it enables the mission again, respawning the group.
- **Popup text.** `SafeHouse/PGPUnlockedSwoosh` = "Outpost Cleared" in all nine languages.
  `onscreendata.xml` also gains an `Objective[PGPUnlocked]` popup with that text; no script in the
  mod names `PGPUnlocked`, so it looks unused (inference).

## Depends on

- `outposts-delay-presets` turns `Seconds = "Outpost"` into `Globals.MASTER_GameGlobals.OutpostDelay`.
- That global's default, `OutpostDelay = 2700`, is declared in the shared globals hunk
  `domino/user/master_gameglobals.globals.lua@L90`, and `ScubrahsPatch.lua` beside the game can
  change it (User Configurable); neither is claimed here.
- The town guard post (`w1_c_3\zXmOLgx`, the old `pgp_ai` layer) is switched off at game start and
  back on at the end of the intro by hunks the Skippable Intro feature owns
  (`master_world1.world1.lua@L686`, `a1sm01_townescape.a1sm01_mission.lua@L721`).

## Uncertain

- Moving guards out of the `_disableformission`, convoy, library and story layers also takes them
  out of reach of the vanilla scripts that switch those layers off for a mission (A1LM01, A1LM04,
  A2LM11, A2SM05, convoy missions, the opening sequence for `PGP_AI`), so those missions now meet
  the outpost's guards instead of an emptied area (inference from the vanilla scripts).
- The omni entities carry `hidStartOnLoad False`, as vanilla's own `MASTER_World1` omni does; they
  are assumed to start with the world the same way.
