---
title: Randomized patrol system
kind: component
bundle: features
status: located
systems: [patrols, ai, vehicles, missions]
match:
  # one rotation script per patrol group (59 here; the 4 Pala-route groups are unused-patrol-routes)
  - "_hash/{0585954c,07ce37d3,09765816,10291937,1c019f02,1d1cbf45,23104cd1,2630736b,296380c1,29d19e30,304b2ac3,356299e6}.lua"
  - "_hash/{37398bd2,4167956e,435f33ae,451aec5e,4880afb2,4b285760,4c76e09b,4c85cdfc,50a4315f,546c2ddf,56b2f8bb,6810f08c}.lua"
  - "_hash/{6ef248f1,70408786,78ea7097,7aa4f9d2,7b3ce25d,7b4e19be,7f66ce88,80a4ba79,910c43d6,9131e437,9d420d9a,9ec0353b}.lua"
  - "_hash/{a1fb8727,ab150ce2,b78aaf8d,b988f2bd,bb4b0c05,bbd096e2,c11bbcca,c20a02c7,c2562e25,c39e34c0,c5828238,c92c1155}.lua"
  - "_hash/{c9cedd0e,d053ca7e,d4790e90,d78ccc45,dc222958,de745247,e32e5e6c,e91fc152,e9b1d07c,eff075f6,fcbf1f9c}.lua"
  # the omni entity that hosts each group's script
  - "worlds/*/generated/world*.omnis.fcb/dominoomnientity_patrols_*"
  # 17 mission layers per group, the layout that files one vehicle under each, and the vehicles
  - "worlds/*/generated/world*.game.xml/missions/ghostpatrols/**"
  - 'worlds/*/generated/world*.mapsdata.fcb/_layout.xml#layer[missions\ghostpatrols\*]{,/**}'
  - "worlds/*/generated/world*.mapsdata.fcb/patrols.**"
  # new patrol archetypes (the red-faction ones are faction-conflicts)
  - "**/entitylibrary*.fcb/ghostpatrols/patrols/**.xml{,#Entity/disEntityId}"
  - "worlds/*/generated/entitylibrary.fcb/enemy_archetypes/neutral_faction/**"
  - "worlds/*/generated/entitylibrary.fcb/buddies/{civilians,grin}/*_patrol.xml"
exclude:
  - "worlds/world1/generated/world1.omnis.fcb/dominoomnientity_patrols_20564256571554{11111,22222,33333,44444}.*"
  - "worlds/world1/generated/world1.game.xml/missions/ghostpatrols/20564256571554{11111,22222,33333,44444}/**"
  - 'worlds/world1/generated/world1.mapsdata.fcb/_layout.xml#layer[missions\ghostpatrols\20564256571554{11111,22222,33333,44444}\*]{,/**}'
  - "worlds/world1/generated/world1.mapsdata.fcb/patrols.rover_3_{arena,fishing,lumber,slaughter}.**"
  - "**/entitylibrary*.fcb/ghostpatrols/**_redfaction.xml"
requires: [faction-conflicts, outposts-delay-presets, patrols-set-mission-state]
verified: diff
---

# Randomized patrol system

Every road patrol in both worlds becomes a slot that a script re-rolls every few minutes: each roll
picks one of 17 vehicle/crew/faction combinations, so the same stretch of road can carry a buggy one
time and an MK19 Rover of the opposing faction the next. scubrah's generator tags the scripts
`-- Generated for "Randomized Patrols" v1.1`.

## How

- **Groups.** Each vanilla patrol vehicle on a path becomes a group named after its entity id: 29 in
  `world1`, 30 in `world2` (the 4 further `world1` groups on new Pala-area routes are
  `unused-patrol-routes`).
- **17 missions per group.** `world*.game.xml` gains `Missions/GhostPatrols/<id>/BluePatrol0`-`7`
  and `RedPatrol1`-`9`, each owning one layer `missions\ghostpatrols\<id>\<name>`, off by default
  (`State="0"`, `MissionLayerActiveDflt="0"`).
- **One vehicle per mission.** The mapsdata `_layout.xml` files one patrol entity under each of those
  layers (`layer[missions\ghostpatrols\...]`). Per group, the vanilla patrol entity is moved into
  the slot that matches its vehicle (`hidMissionLayerPath` and its `text_` twin rewritten, or a
  `CMissionComponent` added where it had none) and 16 new entities `patrols.*.<id>.xml` are added on
  the same `entPathToFollow`. The vanilla entities lose their old layers in the process
  (`convoy_0N_disable`, `disableforopeningsequence`, `a1lm01\misnbase_patrol_hide`,
  `a2sm05_ai_disable`, `w1d3_a15airstrip_enemiesstp`); for convoys and the opening the
  `patrols-set-mission-state` box takes over that hiding, for the two story missions nothing does.
- **Slot to archetype**, the same in every group (`tplCreatureType`): `BluePatrol0` Buggy, `1`
  Datsun, `2` JeepLiberty, `3` JeepWrangler, `4` Rover.M2_Mounted, `5` Rover.M249_Mounted, `6`
  Rover.MK19_Mounted, `7` Rover; `RedPatrol1`-`9` the `_RedFaction` variants of Datsun,
  JeepLiberty, JeepWrangler, M2, M249, MK19, Rover, Buggy and `Quad_RedFaction` (which, despite its
  name, drives `vehicle.Land.DLC_Vehicle2_DLC1`, the Unimog).
- **New archetypes** in `worlds/world1` and `worlds/world2` `entitylibrary.fcb/ghostpatrols/patrols/`:
  `Buggy`, `JeepLiberty` (world1 only; world2 already has it), `Rover.M2_Mounted`,
  `Rover.MK19_Mounted` (world1 only), plus `Buggy_FriendlyFaction`, `JeepLiberty_FriendlyFaction`
  and `Quad_FriendlyFaction` (civilian / GRIN crews) that no entity uses.
- **The scripts** (`domino\User\Patrols\<id>.lua`, stored as `_hash/*.lua`), one per group, each
  run by a new `DominoOmniEntity_Patrols_<id>` in `world*.omnis.fcb` (persist level `Critical`). On
  `In` a `Delay` box waits `random(1, 300)` s; then the script disables the last enabled mission,
  draws `random(0, 16)`, enables that `BluePatrol`/`RedPatrol` mission through `SetMissionState`,
  and restarts the delay with `Seconds = "Patrol"` - the named preset that `outposts-delay-presets`
  maps to `Globals.MASTER_GameGlobals.PatrolDelay` (300 s, set in `ScubrahsPatch.lua`).

## Depends on

- `faction-conflicts` for the nine `_RedFaction` archetypes the `RedPatrol` slots spawn; nine of the
  17 rolls use them, so this page cannot be picked without it.
- `outposts-delay-presets` for `Seconds = "Patrol"`. `PatrolDelay` itself is declared in
  `domino/user/master_gameglobals.globals.lua@L90` and overwritten from the installed
  `ScubrahsPatch.lua` by the `OnLoad` omni (both on other pages).
- `patrols-set-mission-state`: the `SetMissionState` box the scripts enable missions through, which
  keeps patrol groups on convoy routes off while that convoy runs and keeps every patrol off until
  `FinishedIntro`.
- The crews' weapons and the extra seats come from `patrol-diverse-weapons` and `patrol-every-seat`.

## Uncertain

- What calls each omni's `In` is not visible in the data (`hidStartOnLoad` is `False`); the
  Functional Outposts omnis are built the same way.
- `_hash/a2240722.rml` and `_hash/fa8791e2.rml` are whole compiled copies of `world1.game.xml` and
  `world2.game.xml` (they carry these missions too) under hashed names; they are not claimed here.
