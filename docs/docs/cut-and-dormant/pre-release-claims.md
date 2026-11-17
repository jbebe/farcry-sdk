---
sidebar_position: 3
---

# Pre-release promises against the retail game

:::info[Verified via reverse engineering]
What retail holds was traced in `FarCry2_server` (addresses are the server's; Steam `Dunia.dll`
addresses are marked) and read from retail data: world 1 and 2 `managers.fcb`, the entity libraries,
the 14 retail brains unpacked with `jackall-cli ai unpack`, every campaign sector scanned for placed
archetypes, and two real campaign saves decoded. What was shown or claimed comes from web sources, each
linked; they are facts about the source, not about the engine. Claims marked *(inferred)* were not read
in code or data. Nothing here was seen in game.
:::

Far Cry 2 was shown at Games Convention (Leipzig, 23 to 25 August 2007) as a PC-only pre-alpha, at PAX
(25 to 26 August 2007), at GDC (27 February 2008, the "Tree Regeneration" and "Day and Night Cycle"
tech trailers), at Ubidays (Paris, May 2008) and at E3 (July 2008), and described by developers after
release. It launched on 21 October 2008. The September 2008 prototype already matches retail in data
([prototype comparison](./prototype-comparison.md)), so these claims concern the 2007 to mid-2008
builds, which are not available.

Each entry gives what was said, then what retail holds, with one of these states:

- **Present**: shipped and working.
- **Present but gated**: the code and data are there and something keeps it from showing; the gate is
  stated and the topic page details it.
- **Absent**: verified that no code, data or asset for it exists in retail.

## Why things changed, in the developers' words

- The project was retargeted from PC-only to a simultaneous PC, Xbox 360 and PlayStation 3 launch
  (Hocking's [postmortem](https://www.gamedeveloper.com/design/the-making-of-i-far-cry-2-i-)). In
  August 2007 [GameSpot](https://www.gamespot.com/articles/gc-07-far-cry-2-first-look/1100-6177348/)
  reported Ubisoft Montreal "focusing entirely on the PC version". In July 2008 Dominic Guay said "we
  won't remove any of those systems" and that the platforms differ in "the amount of detail"
  ([Gamasutra](https://www.gamedeveloper.com/game-platforms/in-depth-i-far-cry-2-i-s-guay-talks-dunia-engine-state-of-pc)).
- The postmortem says prioritising combat AI "prevented us from building a general purpose AI", that
  the smart-terrain behaviour system "was not compatible with the briefing AI", and that reputation
  could not be reworked because of "the impossibility of testing even minor changes to the game flow".
- Loss of control was cut because it was too hard to test (Hocking, July 2008); weather reacting to
  the player was cut for clarity (Patrick Redding, January 2009); predators were cut for balance
  ([bit-tech](https://www.bit-tech.net/previews/gaming/pc/far-cry-2-hands-on-preview/2/), June 2008);
  fire was capped because unlimited spread "is just not fun" and particle counts were cut for frame
  rate (Jean-Francois Levesque,
  [Gamasutra](https://gamedeveloper.com/game-platforms/interview-how-i-far-cry-2-i-s-fire-fuels-spreads),
  November 2008).

## World and biomes

**Claimed.** The tech-demo walkthrough: jungle, savannah and desert biomes, the desert playing the role
the ocean played in the first Far Cry while holding "a lot of gameplay"; the map sheet shown covers one
square kilometre of a 50 square kilometre world, and crossing the demo's route on foot would take at
least 30 minutes.

**Retail: present.** Each of the two worlds is an 80 by 80 grid of 64 m sectors, 5.12 km on a side, so
the pair is about 52 square kilometres; the desert is its own environment zone
([time of day and lighting](../engine-internals/time-of-day-and-lighting.md)).

## Weather

### Rain, lightning and thunder

**Claimed.** GameSpot at Leipzig: the game "simulates full weather patterns ... The same goes for
falling rain and howling wind". At PAX 07 Hocking said "We don't have lightning and rain in yet, but
hopefully we will" ([Shacknews](https://shacknews.com/article/48677/pax-07-far-cry-2)). 3DNews
(8 December 2007) wrote that raindrops flatten foliage and that a lightning strike can set a sapling
on fire ([3DNews](https://3dnews.ru/271843/print)). Kikizo (3 March 2008) quoted Hocking on rain
beating down on the player ([Kikizo](https://games.kikizo.com/news/200803/002_p2.asp)). At Ubidays,
rain was said to put fires out ([Game*Spark](https://www.gamespark.jp/article/2008/05/29/16067.html)).

**Retail.**

- **Rain: present but gated.** The rain machinery is complete: `CDynamicEnvironmentManager`
  `EvaluateRainProbability` (server `0x09158950`, Steam `0x10118D00`), `UpdateRain` (`0x0915F650`), and
  a `CRainComponent` on the player entity with rain and rain-splash effects. Natural rain needs the
  storm factor above about 0.57 to 0.71 (cloud coverage above 0.63 to 0.75) held for 180 to 195 real
  seconds, outside the desert. The retail storm curve `Default.StormIntensity.Default` is above 0.565
  only in 6.4 percent of its loop, and the storm clock runs at `TimeScale` 6, so the first window opens
  after about six real hours of unpaused play and the loop takes about 15.4 hours *(RE-verified,
  server build; outcome inferred)*. A save stores `StormHour`, so a loaded game resumes inside a peak
  it was saved in. Scripts can force a storm with `SetScriptedStormFactorOverride`; only mission
  `a3sm15` does. The two `Default.Transition.Rain` presets, which step from about 0 to 1 at storm
  factor 0.70 or 0.75, are never bound. See [vegetation, fire and weather](./vegetation-fire-weather.md).
- **Rain putting fires out: present.** While the player's `CRainComponent` is started, every second
  each burning fire loses `BudgetDecreaseRatePerSecond` of its budget
  (`CFireManager::SRainEffect::Update` `0x08816E50`). Wet ground is stubbed: the retail `WetnessLevel`
  list is one empty entry with `NodeBufferIncrease` and `PropagationDecrease` 0, though both have
  readers (`GetCurrentWetnessNodeBufferModifier` `0x0880D4F0`, `GetCurrentWetnessPropagationModifier`
  `0x0880E180`).
- **Lightning and thunder: absent.** Neither binary has the strings. `gfx_lightning.xbt` is the texture
  of the electric-sparks impact particle, not a sky bolt. No thunder cue is in the sound banks (*likely*;
  the bank names are hashes). Fire is never started from the weather: ignition accepts only Burn,
  Pierce and Crush stims, per object (`CFireComponent::IgniteOnStim` `0x08B445A0`).

### Weather reacting to the player and to infamy

**Claimed.** TechPowerUp, from Ubisoft and a developer blog (17 August 2007): "a dynamic weather system
that adapts to how well you are currently doing" ([TechPowerUp](https://www.techpowerup.com/37572/more-information-has-been-released-about-far-cry-2)).
At PAX the weather would be manipulated by player actions, "not as cheesy as a sudden rainstorm
breaking" ([Engadget](https://www.engadget.com/2007/08/26/pax-07-the-far-cry-2-public-demo/)); at
Leipzig, if a buddy goes down "you might even get a full blown storm"
([HEXUS](https://hexus.net/gaming/news/pc/9674-far-cry-2-pc/?page=3)). Redding, in January 2009, said
the team had considered altering weather dynamically and tying saturation filters to infamy, and
dropped both for clarity ([Gamasutra](https://gamedeveloper.com/design/beyond-i-far-cry-2-i-looking-back-moving-forward)).

**Retail.**

- **Weather reacting to the player: absent** (verified for `Update`, `UpdateWeather`, `UpdateSky`,
  `EvaluateRainProbability` and `UpdateRain`). Their inputs are the clock, the storm curve, zone
  weights and scripted overrides (`SetScriptedStormFactorOverride`, `Set{Fog,Cloud,Wind,Lighting,
  AdaptiveBloom,DepthOfField}Override`), which missions use. See
  [time of day and lighting](../engine-internals/time-of-day-and-lighting.md).
- **Infamy screen filters: absent, with leftovers.** `PostFx.InfamyStrike.*` (7) and
  `InfamyRedLine.*` (2) ship emptied: each track is a single zero key. `PostFx.Infamy.Loop` keeps its
  content (an `infamy.png` pulse and the keep-red flag), but its hash `0xD402430B` appears in no code,
  state machine or script. The red pass is also gone from the final shader: the 12
  `SINCITY_EFFECT` permutations map to the same objects as their plain twins and no object binds
  `SinCityEffectIntensity`, though the renderer still builds the mask (`FUN_1038DC00`, Steam). See
  [rendering](./rendering.md).
- **Infamy barks and mix: present but never raised.** The stimuli `PlayerInfamyApproachingRedline`,
  `PlayerInfamyInRedline` and `PlayerInfamySpiking` are registered only in the stimulus-table init
  (`DoInit` `0x087BF5D0`) with no other reader; `CTaskCheckPlayerInfamy` is used by no brain; mix slot 22
  `Infamy_FX` has no reader; `sndHighInfamySoundCue` in `music.xml` is unset (`0xFFFFFFFF`).
  See [AI and combat](./ai-and-combat.md).

### Weather and heat changing how soldiers behave

**Claimed.** 3DNews: few soldiers want to stand watch in scorching sun or patrol in heavy rain. Ubidays
2008: sneaking is easier at night or in thunderstorms. Hooked Gamers (April 2008): the day and night
cycle "influences NPC behaviour and tactics"
([Hooked Gamers](https://www.hookedgamers.com/pc/far_cry_2/preview/article-354.html)).

**Retail: absent** for weather and heat. Night halves the vision cone, and nothing else about darkness
or weather enters perception ([AI](../engine-internals/ai.md)). Time of day does change which smart
terrain points idle soldiers use (see sleeping guards below).

### Strong winds and sandstorms

**Claimed.** The r/farcry2 post "List of Cut and Removed Content from Far Cry 2 (Alpha & Demo)"
(u/Unkn0wn2010), working from alpha and demo footage, lists "better dynamic weather": strong winds,
sandstorms, and weather that visibly moved grass and trees.

**Retail: present, milder.** The storm presets raise the wind force that bends grass and trees, and
the sandstorm particle effects ship in a layer that is on by default. See
[time of day and lighting](../engine-internals/time-of-day-and-lighting.md) and
[vegetation, fire and weather](./vegetation-fire-weather.md).

### Wind snapping branches off trees

**Claimed.** At PAX 07: "Trees are actually losing branches". HEXUS at Leipzig: "If there's a stiff
breeze blowing, branches might snap off trees and blow away". 3DNews: the wind carries fallen leaves
and branches. In the tech-demo walkthrough a storm builds as clouds cover the sky, the wind rises and the
grass reacts; its narrator admits the wind was exaggerated to 160 km/h for the demo, and it breaks
branches off, a shot branch flies with the wind, and a grenade's particles drift with it. The r/farcry2
list says wind swayed branches and that shot branches broke off and flew,
shown in a GIF; shot branches do break in retail
([trees shot apart](#trees-shot-apart-felled-uprooted)).

**Retail: present but dead at its call site.** The code exists (`ApplyWindDest`, and the
`real_tree.wind.*` particles ship), but `RTxcManager::CheckWindDestruction` has no caller and
`DestructionStormRadius` is read nowhere *(RE-verified, server build)*. Storms bend trees; they do not
break them. See [vegetation, fire and weather](./vegetation-fire-weather.md).

## Fire

### Fire in rain, and wetness

Covered under rain above: the budget drain is present; wet ground is a data stub.

### Whole buildings burning down

**Claimed.** The Ubidays 2008 tech demo showed fire "burning buildings completely" (Game*Spark);
Joystiq (May 2008) wrote of burning "even entire buildings"
([Joystiq](https://www.engadget.com/2008-05-28-joystiq-hands-on-far-cry-2.html)).

**Retail: present, reduced.** Fire spreads through grass, trees and burnable objects. Wooden structures
burn and break in parts; buildings are not consumed whole (shells are static meshes, *likely*). A
PC Perspective preview in June 2008 reported a plan to burn down huts that failed.

### How far fire spreads

**Claimed.** Levesque (November 2008): the plan began as fire only on patches of grass no bigger than 10
by 10 metres; management asked for the whole savanna to burn; spread is capped because endless fire "is
just not fun". He names uphill spread and humidity. The tech-demo walkthrough shows fire spreading
from a flamethrower toward a fuel depot; the third has a burning soldier set the grass around him alight
as he leaves a vehicle, and promises that "what should burn will burn", the savannah included.

**Retail: present.** `CFireComponent::PropagateFire` (`0x08B4CD80`) spreads per burning node by a wind
term (force over 30, projected on the node direction), the fire damage, and a vertical term that makes
uphill spread faster, reduced by the wetness modifier and gated by the fire budget (400 in data;
`iCostRealtree` is 0, so tree fires cost no budget). Nothing in propagation is switched off. Smoke
alone is off at Medium and Low (`FireSmokeEnabled`), and no retail preset disables fire. Burnt grass
does not respawn, because `ResetAfterBurnDelay` is 0 on the savannah, jungle, small-patch and
tire-pile regions (the code to respawn it, `RespawnGrass` `0x08B41FD0`, runs). See
[vegetation, fire and weather](./vegetation-fire-weather.md).

### Soldiers reacting to fire

**Claimed.** 3DNews: guards who notice a fire raise the alarm, evacuate before the flames reach them,
then search for the arsonist.

**Retail: present, reduced.** `mercbrain` has a live `EscapeFire` plan (`CScannerFireProximity`,
`CTaskCheckFireProximity`, `FireDistTrigger` 7); fear stims from burning ground set the fire alert and
soldiers flee burning ground. No arsonist search exists.

## Vegetation and destruction

### Regrowth

**Claimed.** GameSpot (22 January 2008): burnt fields and blasted trees "will remain in the world until
it actually gradually grows back"
([GameSpot](https://www.gamespot.com/articles/far-cry-2-exclusive-impressions-dynamic-environments-and-savannah-battles/1100-6184924/)).
Joystiq and Everyeye (May 2008): burned flora regrows and shows stages of growth. The GDC "Tree
Regeneration" trailer and the Ubidays demo showed sprouting
([TechPowerUp](https://techpowerup.com/53774/far-cry-2-tech-trailers-released)). At MIGS in November
2008 Guay said the sapling-to-adult growth is "not available to the player in the game"
([MIGS](https://www.gamedeveloper.com/game-platforms/migs-i-far-cry-2-i-s-guay-on-the-importance-of-procedural-content)).

**Retail.**

- **Cut branches regrow: present.** A broken branch shrinks away over 20 s (`ScaleOutSpeed` 0.05), then
  `RTxcRegenNode::RegenStart` (`0x099AA120`) regrows it node by node: savannah `rt_*` trees at a
  hard-coded 0.05 m/s (`gRT_GENERATION_SPEED`, server `0x0A43C664`, Steam `0x10F97F9C`), jungle `hy_*`
  trees at `RegenSpeed` 0.02 m/s. Gate: it runs only while the tree is a unique simulation; off-camera
  or over the budget the tree snaps back pristine (`ReturnClonedToOriginal` `0x0997E090`).
- **Sapling to adult: present but gated.** `RTGenesis` (`RTxcManager::StartGenesis` `0x0997D070`)
  regrows a whole tree from the root, but the only triggers are the console and Lua `Game:RTGenesis`,
  and only on trees already simulated.
- **Burnt grass regrowth: present but data-off** (`ResetAfterBurnDelay` 0, and instant rather than
  staged). **Burnt trees: absent**; nothing lowers the char factor, so they revert only when the clone
  returns or the sector unloads *(likely)*.

### Trees shot apart, felled, uprooted

**Claimed.** At PAX 07 Hocking said "everything in the world is destructible down to the pixel" and shot
branches off a distant tree with a sniper rifle. GameSpot: "trees can be blasted out of the ground with
grenades". Kikizo: bullets rip leaves and branches from trees, "reducing the foliage to
near-nothingness". PC Zone: a tree-toppling machete. A narrated tech-demo walkthrough (event and date
not recorded): vegetation "is going to break exactly where you shoot", it reacts to every impact,
"a Jeep, bullet, whatever", and an RPG-7 rocket breaks trees 300 to 350 m away, to show that distant
trees keep their destruction rather than falling back to static levels of detail. Whether retail breaks
branches at that distance is not traced.

**Retail.**

- **Branches and leaves shot, blasted or burnt off: present**, for thin branches only: explosions break
  branches up to 6 cm (`RTMaxDestRadius[3]`, `0x0A43C62C`).
- **Felling, toppling, uprooting: absent.** Vehicles shake trees; trunks never break. No code exists
  for it. The machete is a melee weapon.

### Vehicles against bushes and trees; flattened grass

**Claimed.** TechPowerUp (August 2007): running into a tree "can cause it to break and fall down, just
shake, or the player to crash to the floor, all depending on the size of the tree". Grass flattened by
a jeep or truck was reported at Leipzig and PAX.

**Retail.**

- **Grass flattened by a vehicle: present.** Grass bends under a vehicle and springs back. Only vehicles
  flatten it, and `bBreaksGrass` is False on every BigTruck archetype.
- **Bushes torn up by a vehicle: present but gated.** The chain is live (Steam:
  `CAABBPartitionManager::Update` `0x105329B0` to `ApplyMvObjectReaction` `0x104752F0` to the heavy reaction
  `0x10474BF0` to `ApplyDestructionHeavy` `0x10473FD0`, Steam addresses). It fires only above 10 m/s
  (float at Steam `0x10F9836C`, its only reader) and heading at the bush; each hit breaks at most 10
  branches (immediate at Steam `0x1046C0DD`) and throws them up at 0.9 times the speed, capped at 40
  m/s, with a leaf burst. The pool of broken branches is `DestructionNbMaxBrokenBranches`, 50 at Ultra.
  The PC profile writes `Quality="VeryHigh"`, which matches no tier, so the custom quality block is
  used, with `ResponseToAnimal` 0.
- **Trees broken by a vehicle: absent.** They shake and burst leaves.

### Tracking by trails in the grass

**Claimed.** HEXUS at Leipzig: "you can track enemies by the trails they leave in the grass and they
can do the same to you."

**Retail: absent.** No AI code reads trampled grass or footprints; the player's `Stim_FootSteps`
(7 m, Snap) is the only trace soldiers get.

### Jungle density

**Claimed.** GameSpot at Leipzig: a demo that "began in a dense, oppressive jungle like those from the
original Far Cry". Producer Louis-Pierre Pharand: savannah woodlands approximate the density of the
original's jungles
([GamesRadar](https://www.gamesradar.com/far-cry-2-first-look-part-two/)). The third walkthrough: dense,
dark jungles like those of the Congo, several kinds of savannah, and forests seen hundreds of metres
away. A fan comparison of the
pre-alpha video, relayed by [unseen64](https://www.unseen64.net/2009/10/01/far-cry-2-pc-xbox-360-ps3-beta-alpha/),
found its jungle "more realistic" and its grass greener.

**Retail: present, unchanged against the prototype.** The September 2008 data places the same 4.6
million instances as retail, to within 35. Retail's High preset culls vegetation sooner than the
prototype's High ([prototype comparison](./prototype-comparison.md)). Any difference from the 2007
video predates the available data.

## Day and night

**Claimed.** The tech-demo walkthrough: a 24-hour day and night cycle with fully dynamic lighting, god
rays through moving trees and shadows moving with the sun, so the player can scout a place by day and
come back at night; open skies give sharp shadow edges.

**Retail: present.** See [time of day and lighting](../engine-internals/time-of-day-and-lighting.md).

## Physics and destruction

**Claimed.** Ubisoft's fact sheet and the Steam store page: "Everything is breakable and alterable".
TechPowerUp: "totally destructible to the point of wind blowing a leaf off of a tree". HEXUS at Leipzig:
shanty towns that "can be almost totally raised to the ground". Hooked Gamers (April 2008): "blow up
rocks and boulders". PAX: ammo piles send "hundreds of bullets" flying and chain explosions. A
narrated tech-demo walkthrough (event and date not recorded) shoots an ammo crate that "will just start
spraying bullets all over the place", and fights through a shanty town with "destructibles all over".
The third walkthrough: "whatever looks destroyable is", so the player can shoot through walls the enemy
hides behind; a shot ammo pile sends the soldiers into hiding.

**Retail.**

- **Compound props: present.** Walls, roofs, panels and kiosks are state machines with their own health
  and damage masks ([stims and damage](../engine-internals/stims-and-damage.md)); parts break, mostly in
  one step. Whole collapsing shacks exist (`ShantySm01Dest_01`, `_02`, `ShantySm02Dest_02W2_BK`, 49
  placements), but shells are static meshes (*likely*), and most roofs and shelters have bullet and
  fire damage off in one of their two damage masks.
- **Rocks: absent.** None of the 298 rock models is destructible; `CrumblingRock_BK` is a scripted
  mission piece placed once.
- **Never placed:** the staged walls `DogonWallDestroy` and `TolloyWallDestroy`, `BreakableOutPost01`,
  `WatchTower01`, `BridgeWood03` and `DustTownWall_B/C/D_BK`. `CDestructibleBridge` is an empty class.
  `WatchTowerLeipzig`, named for the 2007 demo, has 18 planks that ignore bullets and blasts while
  attached (9 placements).
- **Parts thrown by explosions: present but gated.** `fPartsSpeed` is 0 on 303 of 339 explosion blocks,
  so pieces drop in place. See [physics and explosions](./physics-and-explosions.md).
- **Ammo dumps firing rounds: not traced.** Ammo caches explode.
- **RPG-7 rockets arming late and bouncing: not traced.** The tech-demo walkthrough says the rocket does
  not fuse until four or five rotations, so it can be bounced off the ground under a vehicle or off a
  wall.
- **Persistent damage: reduced.** Guard posts repopulate and the world resets as the player travels.

## Animals

### Predators

**Claimed.** At PAX 07: "There will not be predator cats and there won't be any endangered species"
(Engadget). Everyeye at Ubidays (30 May 2008) wrote that predators hunt in packs
([Everyeye](https://www.everyeye.it/articoli/hands-on-far-cry-2-7744.html)). bit-tech (3 June 2008): no
animals that prey on humans "for the sake of balancing", playtesters having tired of being "outrun by
packs of lions". Generation NT (24 September 2008) relayed a possible DLC with vicious animals; none
was released ([Generation NT](https://www.generation-nt.com/actualites/far-cry-animaux-predateurs-157641)).
In a Reddit thread on a Far Cry 2 remake, players say, unsourced and hedged ("according to some
reports", "iirc"), that the game was meant to have many dangerous animals, especially lions, elephants
and hippos, and that a trailer showed African elephants.

**Retail: absent.** No elephant or hippo archetype or art ships. No animal has attack, charge or melee code, not even unused. The animation species
enum has a Crocodile value (6) with no animations. See [animals](../engine-internals/animals.md).

### Species and routines

**Claimed.** "About 12 different types of animals" (PAX 07), with GamesRadar naming zebras, wildebeest,
gazelle, buffalo, impalas and oryx. Pharand: gazelles "trek to watering holes each morning". bit-tech:
a spooked herd flees and "reveals your position"
([GamesRadar](https://gamesradar.com/it-came-from-africa)).

**Retail.**

- **Species: reduced.** Gazelle, zebra, wildebeest, cape buffalo, goat and chicken, plus birds and bats.
  No impala and no oryx.
- **Herd panic: present.** A panic stimulus (radius 20 m) is relayed to the other animals, every 0.25 s
  (`CoreRun` `0x0855D770`). Soldiers are not recipients, so a fleeing herd does not reveal the player to
  them; a plugin forwarding the stimulus would reach existing code (`ProcessStimIdle` `0x08736E20` has a
  Panic case).
- **Fleeing fire: present, reduced.** Animals are threatened by Bang, Snap, Pierce, Cut and Burn
  stimuli; the fire's Fear stimulus (radius 10) is ignored by them, so they flee only when flames touch
  them *(likely)*.
- **Routines to water: absent as a schedule.** Animals graze, lie down and panic at smart-terrain
  points.

## AI behaviour

### Morale, surrender, enemies who drop their guns and run

**Claimed.** Ubisoft's fact sheet
([WorthPlaying](https://worthplaying.com/event/Ubisoft_Gamers_Day/UbiDays_2008/67041-far-cry-2-ps3x360pc-facts-features-screens-trailer/))
and the [Steam store page](https://store.steampowered.com/app/19900/Far_Cry_2/): "Your reputation and
in-game actions will make enemies drop their guns and run for their lives." TechPowerUp: "shoot their
commander and their moral gets worse". HEXUS: injured enemies "don't just retreat but outright flee".

**Retail: absent** as a working feature; a skeleton remains. `EVENT_MORAL_THRESHOLD_CROSSED` is defined
and read by nothing, and `SetPillarValue` and `GetPillarValue` accept only counters 0 and 1 (health and
reliability), so there is no morale counter. `hmr.gosm.xml` has `Victim_MoraleFailure`,
`Boss_MoraleFailure_Rescue` and `Boss_MoraleFailure_Execution` states, all with `animStateID` 0 (no clips
assigned), and no brain sends their signals. `TryFlee` is never reached; no surrender state or weapon
drop exists. Hands-up and panic gestures play only as bark gestures. See [AI and combat](./ai-and-combat.md).

### Medics dragging the wounded; squad classes

**Claimed.** The fact sheet and Steam page: "Medics will drag wounded soldiers to safety." TechPowerUp:
"Classes: Grunt, Engineer, Medic, Sniper". Kikizo: enemies help downed compatriots. The tech-demo
walkthrough: you can shoot to wound rather than kill, the wounded soldier causes chaos around him and
crawls for cover, and medics bring him to safety, patch him up and send him back into combat. An Overclockers
thread (2008) asks whether anyone had managed to see a rescue in the game.

**Retail: present, by any comrade.** `mercbrain` carries a complete rescue: `RescuerWalkToVictim`,
`RescuerAttachToVictim`, `RescuerCarryVictimToCover`, `RescuerDropVictimAtCover`,
`RescuerHealVictimAtCover`, the victim states, and 34 `pawn_hmrhealth_*` clips, driven by the
lieutenant's `ManageRescue` (see [stims and damage](../engine-internals/stims-and-damage.md)). It is
not a medic class. The class field `selODU` exists (Grunt, Medic, Boss, Engineer, Sniper) and is read at
the end of a rescue (`CBrainMercThreshold::CleanUpThresholdStuff` `0x083C7FC0`): a rescuer with
`selODU` 1 (Medic) refills the victim's health counter completely (`CCounter::Fill` `0x09107C90`),
and one with 3 (Engineer) refills reliability. In world 1's library, 141 archetypes are Grunt, the four
snipers are Sniper, and Medic is set only on `SpecOps_Assault`, `SpecOps_Shotgun` and Doctor Obua.
No archetype is an Engineer or Boss, and nothing writes the reliability threshold an Engineer would
answer. Wounded enemies who crawl or limp to cover and fire a pistol are present.

### Command hierarchy with promotions

**Claimed.** GameSpot at Leipzig (Hocking's demo): "if you happen to kill one of those underlings, the
guys below him will move up to fill the role", up to a warlord's number two becoming the new warlord.

**Retail: absent** at warlord level: faction leaders are fixed story characters. Fire teams with
leaders are code that is never called: `CDispatcherSquadLieutenant::DistributeSquads` (`0x086A30D0`)
splits the army into up to ten teams of two or three, redistributing when a leader dies, and
`DistributeSquadRoleAndAction` (`0x086904C0`) sets roles and a bounding-advance action; neither has a
caller, and no brain reads the roles. See [AI and combat](./ai-and-combat.md).

### Camp-to-camp awareness and flare-called reinforcements

**Claimed.** PAX 07: "if somebody dies the rest of the world will know it. They'll know camp to camp".
Shacknews (27 May 2008, Alexandre Amancio): enemies are "sometimes called in from other areas by signal
flares".

**Retail.**

- **Flare-called reinforcements: present.** A soldier fires a flare (`FindAgentToSendFlare` from
  `ManageReinforcement`); a flare explosion event makes agents ask the reinforcement system for a
  vehicle reinforcement at the flare (`CReinforcementSystem::WildReinforcement` `0x08623C30`, called
  from `CPawnAgent::OnEvent` `0x085B2A90`, whose gating predicate `+0xC0` was not identified). 247
  generic and about 70 named reinforcement points are placed. A Steam thread reports never seeing
  reinforcements arrive.
- **Camp-to-camp radio: absent.** There is no inter-camp messaging; each army's lieutenant is local.

### Vehicle chases

**Claimed.** The tech-demo walkthrough: enemies are "super aggressive", jump into vehicles to chase the
player, shoot at him and try to ram him off the road, and work together.

**Retail: present.** Soldiers take vehicles after a target (`ChaseWithVehicle` in `mercbrain`) and
vehicle crews escalate the chase (`VehicleChaseLevel2`, `3` in `vehiclebrain`); see
[AI](../engine-internals/ai.md).

### Needs-driven schedules and sleeping guards

**Claimed.** Pharand (GamesRadar, 2007): "Our AI is needs-driven, and the main needs are Rest, Duty and
Social", with smart-terrain points such as a sleep point. PC Zone: "more likely you'll find sleeping
guards during the wee hours". GameSpot (January 2008): enemies have "general schedules". The tech-demo
walkthrough shows a camp at lunchtime with the guards eating as a patrol arrives by vehicle, and says
soldiers patrol and behave differently in the morning, at midday and at night.

**Retail: present, as a time-of-day table.**

- 35 `Rest.SleepOnBed` and 98 `Rest.SleepOnGround` points are placed. Idle soldiers pick duty, rest or
  social points by time of day from a probability table
  (`CFCXAIBehaviorService::GetSTPProbabilitySet`; rest 70 percent from midnight to sunrise). There are
  no needs meters that build up.
- A sleeper sees nothing: `bIsUnawareSTP` on the two sleep points sets agent flag 60, and
  `CSensorySystem::UpdateVision` (`0x08705110`) then skips all vision work, so he sees no objects or
  pawns and his knowledge base does not age. Waking by sight is impossible; waking comes through other
  paths (not traced). This corrects the common reading that night only halves the vision cone: that
  applies to awake soldiers.
- The postmortem gives the limit: smart terrain was built for combat first, and idle behaviours were
  "hacked together in desperation at the last minute". `CBrainSmartTerrain` is unused for soldiers.
- Nine guard activities are never placed (sit, lean, torch lighting, ask directions).

### Faction standing and towns

**Claimed.** GameSpot (January 2008): standing with mercenary groups rises or falls "depending on which
camps you raid", and with enough reputation "you may be able to walk right into one of these larger
towns".

**Retail: absent as a changing quantity; present as frozen code.** Each army's strength (the health and
weapon reliability its soldiers get, `ApplyArmyPillar`) is set once to the top rating
(`CFCXCountersService::ResetPillars` `0x087A7460`, called from `DoInit`) and changed only by the console
command `cheat_set_pillar` (`SetPillarValue` `0x087A7400`). The medium, low and failure rows of the
rating tables are never used. Everyone armed outside the ceasefire towns is hostile whichever faction
the player works for (Lifschitz, GDC 2010 narrative review; the postmortem says reputation should have
been relationship-based).

## Player wounds and madness

### Wounds with gameplay effects

**Claimed.** Multiplayer.it at Leipzig: 16 critical wound locations with their own animations; a bullet
in the hand stops the player shooting until removed; "a graze to the head literally made us lose
consciousness"; burning clothes must be beaten out. GameSpot: one first-aid key chooses the right
interaction. Engadget at PAX: "over 60 animations for first-aid". The tech-demo walkthrough: there are
no medkits, the player performs actions on himself to get back into the fight, and an overused weapon
jams. Both ship as shown: the surgery animations and weapon jams are in retail. The third walkthrough
promises over 60 healing animations, among them popping a shoulder back after a car crash.

**Retail.**

- **First-aid animations: present.** 50 files, all reachable: 35 surgeries (15 bullet, 16 blunt or
  shrapnel, 3 burn pat-outs, 1 machete), the syrette and 12 malaria clips. `Pawn_Generic_Heal` picks a
  clip by the last damage type, at random by weight, ignoring the hit location. There is no bullet
  clip for the torso or head.
- **Burning clothes: present.** Burn hits fill a pool capped at 170 that drains into health at 85 per
  second (`0x087F7240`, `0x087F80B0`); the heal key empties it with a patting clip.
- **Per-location wounds: absent.** There is no hand wound, knock-out or limp. The player has 11 body
  parts, not 16, and `bEnableHitLocations` is False for the player. The `HealthWoundThreshold` curve
  (300 to 150) has no reader. Singleplayer plays only the BleedOut and Death wound post-effects; Down,
  BleedOut_to_Down, Down_to_Normal and Critical are multiplayer-only.
- **Red damage flash:** the early build's bright red flash on wounding (GameSpot, January 2008) is not
  in retail.

### Loss of control (madness)

**Claimed.** Shacknews (27 May 2008, Amancio): after sadistic acts "you may become temporarily mad,
actually losing control of the character as he goes on a rampage" ([Shacknews](https://www.shacknews.com/article/52875/far-cry-2-hands-on)).
Hocking (22 July 2008): "we dialed back on that significantly, just because it was too hard for us to
get it in with all of the testing"; "so we cut it" ([Shacknews](https://shacknews.com/article/53812/far-cry-2-interview-ubisofts)).

**Retail: absent.** No code, state, animation value or string for it exists in either binary, in Lua,
XML or the libraries (Dunia's "Psychotic", "Dizzy" and "Drugged" are stock EAX preset names, Steam
`0x10EDFC2C`). The emptied `InfamyStrike` post-effects, beside the malaria attack, are the nearest
shape *(speculative link)*. Malaria itself is present, including a bubble mode in which leaving a
type-0 `CGameRegion` for over 10 s blacks the player out (`0x088085A0`, used in A1SM01).

### Enemy hit reactions

**Claimed.** Hooked Gamers: an enemy's movements "will be influenced by his injury, as well as any
previous wounds he has suffered", and weapons affect the movements. A third narrated walkthrough (event
and date not recorded, "a pre-alpha build") describes a hybrid of animation and ragdoll: a soldier shot
in the neck, shoulder or leg reacts there, grabbing his leg or putting his hands to his neck.

**Retail: present in part.** Limping, falling and firing a pistol from the ground, and crawling to cover,
ship (see [stims and damage](../engine-internals/stims-and-damage.md)). Persistent wounds and
weapon-specific reactions are not confirmed.

## Look and interface

**Claimed.** The r/farcry2 list says the alpha and demo had no brown filter, with natural lighting and
bright colours, and a completely different HUD and loading screens. Commenters on that post note that
every screenshot on the game's Steam store page shows alpha or beta builds.

**Retail.** The colour grade is per environment preset ([environment presets](../modding/environment-presets.md)).
The alpha and demo builds the post describes are not available, so their HUD and loading screens can
be compared only against footage.

## Navigation and equipment

**Claimed.** The r/farcry2 list says:

- A compass stood where the GPS is. Its design changed several times and ended as a compass with an LED
  that pointed to nearby diamond cases.
- Cars and boats had no GPS, because the game used the compass.
- The wristwatch model was different, and the watch could be used anywhere, not only at a bed,
  probably to change the time of day.
- The AR-16 had no scope; it was not full-auto, as in retail.
- The grenade had a handle, like a stick grenade.
- A vehicle that is not in the final game was shown (the text does not name it).

**Retail.**

- **Compass: present, unused.** The world libraries keep `gadgets.Equipped.CompassMulti`,
  `CompassSingle` and `Compass_Vehicle`. Scubrah's Patch swaps the GPS for the compass.
- **Watch:** the player raises it only at a bedroll, to choose when to wake. Scubrah's Patch puts the
  raise on a key anywhere; it shows the watch and does not change the time.
- **Grenades:** retail has `M67`, `UnstableM67`, `Molotov`, `Flare` and the launcher rounds; no
  stick-grenade archetype ships.
- Vehicles nothing places are listed in [unused assets](./unused-assets.md).

## Buddies

**Claimed.** The r/farcry2 list says the best buddy and the second-best buddy waited at the safe house,
not at the bar, and gave their missions there.

**Retail.** Not checked against data.

**Claimed.** The tech-demo walkthrough: Jack Carver has retired; the player picks one of ten
mercenaries, and the other nine are buddies, independent mercenaries doing their own jobs who help the
player once unlocked.

**Retail: present, with nine.** Twelve mercenaries appear, nine of them playable; the ones not chosen
are buddies ([characters](../lore/characters.md)). The third walkthrough adds that buddies are "not to be
trusted all the time"; retail ends with them turning on the player in the final battle.

## Sound

**Claimed.** Hooked Gamers: wind will carry sounds. bit-tech lists "acoustics" among the world's rules.
A dynamic music system was claimed.

**Retail.** The September 2008 Xbox build had no environmental reverb at all (all level reverb presets
silent); retail's EAX presets are the only authored ones, reached on PC through a DSOAL device
([prototype comparison](./prototype-comparison.md)). No credible source for a removed gunshot echo was
found, and the weapon-echo, near-miss whizz and distance-multilayer fields are empty on all 91 weapons
in the prototype too. The dynamic music is present. Wind carrying sound is not confirmed: the wind
force is fed to the ambience manager.

## Uncertain

- Whether rain, ammo cooking off, per-species hit reactions and wind carrying sound occur in play is
  untested; the code state is given above.
- Weapons rusting from water and binoculars marking enemies on the map are claims no retail code or
  data was checked against.
- The predicate at `+0xC0` that gates flare-called reinforcements was not identified.
- The claims about the 2007 pre-alpha's lighting and jungle can be settled only against the pre-alpha
  video, not against data.
