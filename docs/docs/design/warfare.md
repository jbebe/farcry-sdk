---
sidebar_position: 4
---

# Warfare

**Status:** idea collection, 2026-10-20, drawn from the engine-internals pages, the community guide and
the analyzed legacy mods. Nothing here is built or seen in game. The ideas are grouped into
[seven mods](#the-mods): six new, and Vehicle Overhaul, which exists already. The first steps are
[six one-launch experiments](#experiments-in-order).

## The goal

Combat in Far Cry 2 should feel like a modded *STALKER*:

- **The weapon is heavy.** It comes up slowly, drifts while held and kicks hard.
- **Every shot matters.** A body hit wounds, a head hit kills, and the player dies just as fast.
  Full auto is for the moment a fight goes wrong, not the default.
- **Stealth is the plan, not a style.** Some posts cannot be taken by a frontal assault, and the game
  shows it: the alarm brings more men than the player has bullets.
- **Being hit has consequences beyond a health bar.** A bad hit puts the player on the ground, and
  whether he gets up depends on a syrette or a buddy.
- **The war goes on without the player.** The two factions fight each other on the roads and for posts.

This is the combat-number tuning that [realistic sound](./realistic-sound.md) put second. Sound came
first and has shipped.

## What the engine gives us

The parts that decide the verdicts below:

- **Damage is stims, and the numbers are data.** A shot is a Pierce stim whose level goes through a
  curve to health. The player's curve and health maxima are documented
  ([the player](../engine-internals/stims-and-damage.md#the-player)). A merc has 250 health, set per
  army, and goes down below 50 ([soldiers](../engine-internals/stims-and-damage.md#soldiers)); in game
  that took five stock torso hits or one head hit.
- **Hit-location multipliers exist for damage the player deals**: Head 6, Torso 1, Arms 1, Legs 0.5
  (`DefaultCountersService/HitLocations`). Every realism mod raised them. They are off for the player.
- **Downed soldiers soak lead.** A hit that takes a soldier to 49 health or less downs him at 49. Hits
  then do nothing for 0.4 s, and after that torso hits do ×0.5 and limb hits ×0.2. This is traced in the
  server build only ([todo page](/farcry-sdk/todos)).
- **AI accuracy is a dice roll, not ballistics.** The chance of a hit is a product of stance, speed,
  range curve and group size. Each hit forces a number of misses after it, set per weapon and per
  difficulty, and difficulty acts on accuracy only through those misses
  ([shooting](../engine-internals/ai.md#shooting)).
- **The player's shot is a ray along the view**, bent by a random noise per shot. There is no bullet
  drop, and spread grows while moving and under sustained fire
  ([where a shot goes](../engine-internals/first-person-aiming.md#where-a-first-person-shot-goes)).
  Penetration has a field per material (`iPiercingResistance`), but how it is compared with the weapon
  is not traced.
- **Nothing sways.** The engine's iron-sight "breathing" is sound only
  ([down the sights](../engine-internals/first-person-aiming.md#down-the-sights)). Aiming Overhaul's
  sway is visual and does not move the shot. **There is no hold-breath and no fire selector**: the
  fire mode is fixed per archetype, and a full-auto weapon plays a sound loop instead of a sound per
  round ([automatic fire](../engine-internals/audio-runtime.md#automatic-fire)).
- **The player has no downed state.** At zero health every ending starts with the rescue fall. A
  buddy rescue needs three living, unlocked buddies ([buddies](../engine-internals/campaign.md#buddies)),
  and otherwise it is game over ([player death](../engine-internals/player-death.md)).
  Wildlife Overhaul's knockdown prototype already takes control away, plays a get-up and hands control
  back, from Lua. Both of its endings were seen in game.
- **Soldiers see with cones, not light.** Cones shorten at night, but scene light never reaches the AI
  (`fAmbientLightEvaluatorWeight` is loaded and never read). After a soldier acquires the player he
  knows his true position for 6 s, seen or not ([intuition](../engine-internals/ai.md#intuition)).
- **Half the optional tactics are dead.** Five of the twelve `AdaptativeBehavior` rows have no reader:
  `Grenade`, `GrenadeAndBuilding`, `ShootFlare`, `RescueVictim` and `RangeWeapon`. The cover tasks are
  registered but unused ([implemented but unused](../engine-internals/ai.md#implemented-but-unused)).
- **The factions can fight.** Town Escape (`A1SM01`) sets blue against red in Pala, and a ghost patrol
  crewed with `Red_Faction` soldiers fights the posts it passes
  ([enemy infighting](../modding/guide/patrols.md)). What stops red and blue from meeting otherwise,
  and what `bOppositeArmy` does, is not traced
  ([sides and hostility](../modding/adding-an-npc.md#sides-and-hostility)).
- **The multiplayer passenger states ship in single player.** `vehicles.gosm.xml` has `Passenger`,
  `PassengerFireBullets`, `PassengerReloading` and their jam states, and the guide wires the driver
  into them. That works, but it is buggy ([vehicles guide](../modding/guide/vehicles.md)).
- **Nothing from JackAll's AI tab has been loaded in game yet.** Every AI idea below starts with that
  check.

## What stays out

Decisions from earlier work that bind this one:

- No sound arriving late with distance; the user rejected it for sound.
- No dark adaptation, and no dazzle from lights at night. The game gets harder through combat, not by
  taking the player's sight away.
- No modern kit, from the [lore rules](./extending-the-world.md#rejected-outright): no plate armour,
  no drones, no thermal sights.
- Nothing that ends the war or makes a faction good. Faction fights are between two bad sides, and the
  player stays everyone's target.

## The mods

**Feasibility:**

- **data:** archetype, curve or config edits with proven tooling
- **data + script:** also needs Lua or a Domino graph
- **plugin:** needs an FCSE plugin
- **unknown:** the deciding piece is not traced

**Status:** `requested` (asked for by the maintainer) or `idea`.

| Mod | Folder (working name) | In one line | Built from |
| --- | --- | --- | --- |
| [Lethal Combat](#lethal-combat) | `mods/lethal-combat` | Few hits kill, both ways | data, one plugin |
| [Weapon Handling](#weapon-handling) | `mods/weapon-handling` | Heavy guns, full auto as a last resort | plugin + data |
| [Wounds](#wounds) | `mods/wounds` | Downed, bleeding out, a buddy on the phone | plugin + Lua + data |
| [AI Overhaul](#ai-overhaul) | `mods/ai-overhaul` | Posts you cannot storm, soldiers who hunt | data + brains + plugin |
| [Faction War](#faction-war) | `mods/faction-war` | Red and blue fight on the roads and for posts | data + script, one trace |
| [Vehicle Overhaul](#vehicle-overhaul-existing) | `mods/vehicle-overhaul` (exists) | Shooting out of the car | data + plugin |
| [Hit Effects](#hit-effects) | `mods/hit-effects` | No blood puffs; hits read through the body | data |

Each mod is meant to install alone. Where two touch the same thing, the note says so.

### Lethal Combat

The numbers. A torso hit should matter, a head hit should end it, and the player should be as fragile
as the men he fights.

| Idea | What changes | Feasibility | Status |
| --- | --- | --- | --- |
| **Lethal hit locations** | Head kills outright, torso hits double. Realism Plus used Head 12, Torso/Arms 2.5 | data: `HitLocations` | idea |
| **Downed soldiers die** | Remove the soak: no 0.4 s immunity, and full damage on a downed man. He can still drop, crawl and call out | data: `fHealthFailure*` on 135 archetypes; check the server trace in `Dunia.dll` first | idea |
| **A fragile player** | Lower `HealthMax_*` with `HealthBarSize_*`, one value for every difficulty | data | idea |
| **The player's own hit locations** | A head hit on the player is near fatal, a leg hit is not | plugin if `HitLocations` turns out not to reach the player; the stims carry a direction (`sndPierceHead/Front/Left/Right`) | idea |
| **Deadly at close range, not an aimbot** | Fewer forced misses close in and more at range, so the AI suppresses from afar and kills up close | data: `nForcedFailure*`, `fTimerToMissTarget`, `DistanceAccuracy` | idea |
| **Cover that is only concealment** | Rifle rounds through shack walls, sheet metal and crates; concrete and rock stop them | unknown: `iPiercingResistance` is there, how it is compared is not | idea |
| **Real projectile speeds** | RPG at 50 m/s, and Carl Gustaf impulse doubled, as Realism Plus did | data | idea |
| **Pistols and SMGs fall off with range** | `vectorEffectiveRange` read as damage falloff | unknown: what the field does is not traced | idea |
| **Flak vests on some soldiers** | A heavy gunner shrugs off one torso hit, never a head hit | plugin: a torso modifier per archetype through `CFCSEDataComponent`; whether kits can show a vest is unchecked | idea |

### Weapon Handling

How the weapon itself behaves: its recoil, fire modes, magazines and weight. How the player aims it
is Aiming Overhaul's (see [Decisions](#decisions)).

| Idea | What changes | Feasibility | Status |
| --- | --- | --- | --- |
| **Recoil that stays** | Muzzle climb that the player pulls down, instead of a kick that recovers in a tenth of a second | data: `fRecoilAchieveTime`, `iRecoilRecoveryLevel`, `f*RecoilPerShot` per reliability level | idea |
| **Full auto spreads** | Spread grows quickly with sustained fire and the first round is the only accurate one; bursts of two or three stay usable | data first (spread blocks, sustained-fire scaling); plugin if the growth rate is not a field | idea |
| **A fire selector** | A key flips select-fire rifles between semi and auto | plugin + data: the mode is fixed per archetype, and auto needs its sound loop while semi needs per-round sounds, so each rifle needs both | idea |
| **Slow to raise** | Longer `fIronsightTransitionTime` per class: pistol fast, PKM slow. Slower turning and walking while aimed | data | idea |
| **Carried weight** | The loadout's weight slows walking, sprinting and stamina recovery. A PKM and an RPG together are a burden | plugin: no weight field exists; weights as entity data on each weapon | idea |
| **Sprint before you shoot** | A short delay between sprint and the first shot | plugin; `fBusyDuration`'s role is unknown | idea |
| **Real magazines, no auto-reload** | Magazine sizes as in Redux (FAL 20, SPAS 9), `bAutoReload` off | data | idea |
| **Scarce ammunition** | Smaller `iMaxAmmo*`, fewer clips from bodies (`ClipMultiplierForPickup`) | data | idea |
| **No hip crosshair** | Point shooting by feel; iron sights to hit anything at range | data: a HUD `.mgb` widget, not yet identified | idea |

**Moved to Aiming Overhaul:**

| Idea | What changes | Feasibility | Status |
| --- | --- | --- | --- |
| **Hold breath, on sprint while aiming** | Holding sprint down a magnifying scope (the guns whose archetype sets `AimingOverhaul.HoldBreath`) steadies the aim for a few seconds, then the hands shake harder. The breath is drawn and let out audibly, and the heart beats while it is in | built in Aiming Overhaul and seen working. Sprint is refused at the input listener's press site while the iron-sight bit is set ([the look](../engine-internals/first-person-aiming.md#the-look)) | requested |
| **Sway that moves the shot** | Drift from breath and stance, applied to the look the shot follows. Hold breath only matters once this exists | built in Aiming Overhaul and seen working; an offset's change added to the desired look after `UpdateLook` moves view and gun together. Where the shots went was not checked | requested |
| **A breathing cycle with a natural pause** | The breath raises and lowers the aim in a cycle, about 1.5 s in, 2 s out and a 1.5 s pause at the bottom where the aim is calm, to time a shot in without a key. Holding the breath settles the cycle into its rest within half a second and keeps it there; after a hold the breath has to recover to half before the next | plugin, replacing the breath wave in the sway | built |
| **Heart rate** | 70 to 170 bpm: up with sprinting, low stamina, damage and nearby blasts, slowly down at rest. Each beat jolts the aim; a faster heart shortens the hold (4 s to 2 s), quickens the breath and widens the sway. The heartbeat follows it, heard while holding and faintly whenever the heart runs fast down the sights | plugin: stamina and health are counters on `CFCXCountersComponentPlayer`, blasts show as the camera shake | built |
| **Stances** | Crouched and still × 0.5 sway and the heart calms twice as fast; standing still × 1; crouched moving × 1.4; standing moving × 1.8. Leaning keeps the stance leaned from. Holding the breath stacks on top. Nothing counts as resting the gun | plugin: the stance is the effective data's `+0xA4`, `1` crouched, as the spread picker reads it; moving is the pawn's speed along the ground | built |
| **Less random spread down a scope** | The engine's own spread down the sights shrinks on the scoped guns, so a shot goes where the reticle is when it leaves | data: the four sniper rifles already have none; the AR-16's 0.4° and the MGL-140's crouched 1° go to 0 | built |
| **Lean** | Q and E slide and bend the body out past cover, tapped to stay or held | built in Aiming Overhaul and seen working: the view, the arms and every gun lean together, by the skeleton's bones ([leaning by the bones](../engine-internals/first-person-aiming.md#leaning-by-the-bones)) | requested |
| **Auto-lean at a corner** | Aiming at a corner's edge leans by itself, and the look stops where the wall starts | plugin: built on the lean and seen to find the corner and hold the look at the wall's edge, by rays from where the lean puts the eye. Its first version jittered because the eye it reasoned from was not where the lean had put it. Shelved until the lean works | requested |
| **The gun near walls** | A gun that would go into a wall swings down out of it and cannot aim or fire | built in Aiming Overhaul and seen working | requested |

### Wounds

The player's body. A hard hit puts him down, and a buddy, a syrette or nobody decides what happens
next.

| Idea | What changes | Feasibility | Status |
| --- | --- | --- | --- |
| **Downed, not dead** | A heavy hit or low health makes the player sit down against something or fall onto his back. Control is taken away, with a pistol in hand | plugin + Lua: Wildlife Overhaul's knockdown already does the take-over and hand-back (`SetScriptedDeath`, an anchored animation, the action-map reset). The on-the-ground pose is probably the rescue's: the combat rescue already hands the player a sidearm. That clip is not identified | requested |
| **Bleeding out** | While down, health drains. A syrette gets him up wounded; a buddy gets him up healed; otherwise he dies | plugin + Lua: the drain and the timer are ours; dying is the knockdown's `Die()` path | requested |
| **Call a buddy by phone** | One key, and the player's buddy answers and comes, instead of waiting to faint. Called while bleeding, he comes faster | plugin + Lua: `PhoneCall` exists for incoming calls, and `SpawnBuddy` and `SetBuddyRescueActive` exist on the buddies manager. Their signatures are unknown, and nothing calls a buddy on demand | requested |
| **One buddy is enough** | The rescue works with a single living buddy, not three | plugin: `SelectPrimaryBuddies`' third rule | idea |
| **Bleeding wounds** | A hit opens a wound that keeps draining until treated, as in *STALKER* | plugin on the damage stim | idea |
| **Limb hits** | A leg hit slows walking, an arm hit adds sway | plugin; needs the hit location on the player's stim, which is not traced | idea |
| **Healing takes time** | The critical-heal animations (pulling out a bullet, setting a bone) every time, a longer syrette, fewer syrettes carried | data: `HealthDegenerationLevels`, `MaxAmmo[syringe]`; the critical-heal threshold is unknown | idea |
| **Hearing goes when hurt** | The empty low-health, critical-health and low-stamina mix presets get a muffle and a heartbeat | data: the presets are wired and empty ([mix presets](../engine-internals/audio-runtime.md#mix-presets)) | idea |
| **A shell-shock** | A near explosion muffles hearing and blurs the screen for a few seconds | data + plugin: a mix preset plus a post effect on blast stims; kept short, per [what stays out](#what-stays-out) | idea |

### AI Overhaul

The enemy. Posts that cannot be stormed, soldiers who listen and search, and an alarm that means
something.

| Idea | What changes | Feasibility | Status |
| --- | --- | --- | --- |
| **Hearing that matches the gun** | An unsuppressed rifle is heard across the post; silenced weapons and the dart rifle stay quiet; the MAC-10's 3 m radius bug fixed | data: the weapon's Bang stim radius ([hearing](../engine-internals/ai.md#hearing)) | idea |
| **No 6 s wallhack** | After losing sight of the player, soldiers search where they last saw him, not where he is | plugin: the 6 s is one constant | idea |
| **Shadows hide** | Light reaches detection: shade and night hide a still player, a lit one is seen | plugin: the light evaluator is loaded and never read, so it has to be written | idea |
| **The whole post wakes** | An alarm brings every man in the post and fires a flare; the region's reinforcements come in 30 s, not 120 s | data for the wait; `ShootFlare` has no reader, so the flare is a brain edit or plugin | idea |
| **Unwinnable posts** | A few posts with a large garrison, a mounted MG and a sniper, built for stealth or avoidance | data: more placed soldiers in mission layers ([adding an NPC](../modding/adding-an-npc.md)) | idea |
| **Grenades and cover** | Give the dead `Grenade` and `GrenadeAndBuilding` rows a reader, and the soldier brain the unused cover tasks | brains (`.ai.rml` recompile) + plugin; the unused tasks' XML loading is not traced | idea |
| **Wounded who fight on** | A downed soldier drags himself to cover and fires a pistol; a comrade pulls him out | unknown: `RescueVictim` has no reader, and crawling is not documented | idea |
| **Enemy flashlights at night** | Patrols carry lights, which give them away and find the player | plugin + data, from the Flashlight mod; already on the todo page | idea |
| **Surrender** | A lone, wounded soldier drops his weapon | unknown: nothing in the brains is known to do it | idea |

### Faction War

The war between the factions, with the player in neither.

| Idea | What changes | Feasibility | Status |
| --- | --- | --- | --- |
| **Red patrols** | Some ghost patrols crewed with `Red_Faction`, so patrols fight posts and each other | data: proven by three community mods | requested |
| **Bring back the cut road skirmishes** | Find what turned them off and turn it back on | unknown: trace `bOppositeArmy`, the `AIvsAI` strategic-point type and target selection in `CArmy` | requested |
| **Learn from Far Cry 3** | List what makes FC3's Rakyat-against-pirates fights and outpost captures work, and which pieces FC2 lacks, as [far-cry-3-animals](../engine-internals/far-cry-3-animals.md) did for animals | research: the repo has nothing on FC3's human factions yet | requested |
| **Posts that change hands** | A post cleared by either side is taken over by the other, and stays taken for a while | data + script: Functional Outposts' layers and delays, with the new garrison's faction | idea |
| **Cleared posts stay cleared** | No full garrison back on the next sector load | unknown: `PersistenceBudget` −1 is untested | idea |
| **Broken cease-fires turn into battles** | Breaking a town's cease-fire starts a fight between the factions, as Scubrah's Patch does with `StartTownEscape` | data + script | idea |
| **Battlefields** | After a fight, bodies and wrecks stay where they fell | data: persistence and ragdoll limits | idea |

### Vehicle Overhaul (existing)

| Idea | What changes | Feasibility | Status |
| --- | --- | --- | --- |
| **Shoot out of the car** | The driver draws the sidearm and fires out of the window; aiming slows the car down | data + plugin: the guide's recipe wires the driver into the multiplayer passenger states. The plugin fixes the view jolts and the arm, and limits the arc. Already on the todo page | requested |

### Hit Effects

| Idea | What changes | Feasibility | Status |
| --- | --- | --- | --- |
| **No blood puffs** | Enemies hit by bullets no longer spray red sprites | data: the flesh entry in `materialimpacts.xml`; the `Base.Flesh` decal also marks hits on the player, so it is separate | requested |
| **A small, lasting wound** | A dark entry mark and a puff of cloth dust in place of the spray, with wound decals that last (Redux uses 30 s) | data | idea |
| **No hit direction marker** | The red arc that shows where a shot came from | done: Legacy Plugin's *No hit indicator* | idea |

## Experiments in order

Each is one launch.

1. **Blood puffs off.** Blank the flesh particle in `materialimpacts.xml` and shoot a soldier. This is the
   cheapest change, and it proves the Hit Effects layer.
2. **Count shots to kill.** Load a JackAll AI-tab edit and change `HitLocations`, then count torso shots
   on one merc, before and after. This is the first AI-tab edit ever loaded, and it settles the unknown
   number of hits to kill.
3. **Sprint while aiming.** Log the input byte and the iron-sight state with both keys held. This
   decides whether Shift can mean hold breath without a fight with the engine.
4. **One red patrol.** Crew one ghost patrol with `Red_Faction` and watch it meet a post.
5. **Down on the ground.** Run the knockdown from DevTools' Lua tab with the rescue's ground pose in place
   of the get-up, and a timer before `Die()`.
6. **The driver's sidearm.** Apply the guide's recipe as written and see how bad the view and arms are.

## Open questions

- **Difficulty:** one set of numbers for every difficulty, or harder only on Infamous?
- **Health regeneration:** keep the regeneration to the top of a bar, or none at all?
- **Downed state:** does every heavy hit put the player down, or only the one that would have killed him?

## Decisions

- 2026-10-20: the warfare work is split into seven mods: six new (Lethal Combat, Weapon Handling,
  Wounds, AI Overhaul, Faction War, Hit Effects) and Vehicle Overhaul for shooting from cars. Research
  only; no mod code yet.
- 2026-10-21: each mod owns one part of the game, so no two write the same value. Aiming Overhaul owns
  the player's aim, from input to the shot leaving the gun: its sway, hold breath, leaning and the gun
  near walls. The warfare mods own what follows: damage, AI, wounds and the weapons' own data. A lean
  rolls the real body, so the AI sees less of the player without an AI change.
- 2026-10-22: Aiming Overhaul keeps hold breath with the sway that moves the shot, the gun near walls
  and the first lean, which rolls the view alone; auto-lean is taken out, to come back once a lean
  moves the gun with the view.
- 2026-10-24: the lean works by the skeleton's bones, and the sway becomes layers, built in this order:
  a breathing cycle with a natural pause that holding the breath settles into, then heart rate, then
  stances, then less random spread down a scope. Nothing counts as resting the gun, leaning
  included: a lean keeps its stance's steadiness. The heartbeat is heard while holding and faintly
  when the heart runs fast, and iron sights get the whole model at their smaller scale, for now.
