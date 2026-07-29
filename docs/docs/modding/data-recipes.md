---
sidebar_position: 4
---

# Data Recipes

:::note[Community-reported]
Concrete gameplay-tuning recipes sourced from the OWG forum and Discord communities — see [Getting
Started](./getting-started.md) for the full provenance note.
:::

## Weapons

### Fire mode and accuracy

- **Full-auto AR-16/M16**: change every `iBurstLength` value from `3` to `0` in
  `41_WeaponProperties.xml` (multiple instances per weapon — use find-all), copy the *singleplayer*
  copy of the weapon (search `hidName` without the `.Multi` suffix) into
  `mymod\patch\21_WeaponProperties.xml`, add the file to `patch.xml`'s object list below the existing
  `22_weapons.xml` entry. The "correct" mechanism is the `selFireRateMode` enum — flipping it directly
  to `FullAuto` is cleaner but silences the weapon's firing sound as a side effect (`iBurstLength` is
  hard-coded to the sound trigger, not a bug specific to one weapon), so the community sticks with the
  `iBurstLength` workaround for audio-safe results.
- **Shotgun pellet count**: `iBulletsShot` (e.g. USAS-12) — raising from 7 toward ~20 turns it into a
  one-shot kill machine; going much higher risks performance slowdown.
- **Accuracy / bullet spread**: `bUseAngleSpread` defaults `False` on rifles (shotguns default
  `True`) — set `True` and tune `fAngleYawBulletSpread`/`fAnglePitchBulletSpread` (degrees). Whole
  numbers like `1` are still far too inaccurate; realistic spread needs small decimals (e.g. `0.002`).
  Conversion from a real-world group size (inches, at 1000 yards) to the in-game angle:
  **`2 * atan((GroupSizeInches/2) / 36000)`**. Weapon recoil lives in `42_weapons.xml`'s
  `<ReliabilityLevelsData>` block (see jam mechanics below), not in `WeaponProperties`.
- **Damage / hit locations**: base damage is *not* controlled by `gamemodesconfig.xml`'s cosmetic
  `<Summary>` block — it's in `41_WeaponProperties.xml`. Add `selHitLocation_Torso_Severity`/
  `selHitLocation_Limb_Severity` (`UInt32`, copy from an existing sniper-rifle entry) near
  `fHealthFailureChanceModifier` for realistic one-shot drops. A separate `Stim_ImpactDamage` block's
  `nLevel` sets a weapon's damage tier (reference values: MAC10=7, FAL=14, Dragunov=25, AS50=30).

### Recoil, jamming, and reliability

- **The `42_weapons.xml` crash workaround**: overriding `42_weapons.xml` directly in the patch folder
  crashes the game on load, every time, reproducibly — renaming it doesn't help. The only working
  method: copy the target weapon's entire data block out of `42_weapons.xml` (in `world1`/`world2`)
  and paste it **into the existing corresponding weapon section of `22_weapons.xml`** (normally
  MP-only) inside `mymod\patch`. This smuggles SP-relevant changes (recoil, jam probability) through a
  file the patch system will actually accept. Repeat per weapon.
- **Jam & reliability**: `42_weapons.xml`'s `<ReliabilityLevelsData>` block has `Failure`/`Low`/
  `Medium`/`High` sub-objects, each with `fHorizontalRecoilPerShot`, `fVerticalRecoilPerShot`,
  `fBulletDeviationMax`, and `fJamProbabilityPerReload` (e.g. Failure=0.12, Low=0.06, Medium=0.03,
  High=0) — the real jam-probability mechanism, tied to a reliability tier itself driven by
  `gamemodesconfig.xml`'s per-Act `CounterReliabilityRatingsTable` thresholds. `iClipsForSelfDestruct`
  (low value = degrades+jams faster) and `bIsIndestructible`/`bIsBreakable` also apply, but
  degradation and self-destruct are separate mechanisms, not one linear scale — and an indestructible
  weapon still shows cosmetic "rust" over time (functional and cosmetic wear are separate systems).
  The M-79's "jam" isn't a true jam — it's a grenade round physically misfiring at short range.

### Magazine capacity and ammo

- **Magazine capacity** (long unsolved, cracked in 2012): in `41_WeaponProperties.xml`, search hash
  `4FBDD114` to jump to any weapon's ammo block:

  | Hash | Meaning |
  |---|---|
  | `AB258E09` (BinHex) | ammo type string, e.g. `"assaultrifle\0"` |
  | `88596C97` (BinHex) | `iAmmoInClip` (magazine size) |
  | `2A0F1CC2` | `iMaxAmmoCasual` |
  | `C7DA96EA` | `iMaxAmmoExperimented` |
  | `EF3C58C3` | `iMaxAmmoHardcore` |
  | `DE33B3EC` | `iMaxAmmoInfamous` |

  A separate bool-like value = "ammo visible on HUD" (true only for belt-fed PKM/M249 — controls
  whether the visible ammo belt depletes). `iClipsForSelfDestruct` ties weapon-breaking to *magazines
  fired*, not total shots — adjust it inversely if you change magazine size. These are raw
  little-endian hex (`BinHex`); change the value's `type` attribute to `UInt32` (or `Bool`) before
  running `ConvertBinary` and it accepts plain decimal/bool (see [Getting Started](./getting-started.md)).
- **Small ammo/explosive/fuel pickups** (`28_pickups.xml`, world1): each `Ammo.Small_X_Pickup` block
  has per-weapon-category `AmmoEntry` objects keyed by hash pairs:
  - Ammo: `6D6540FA`=Pistols(Star45), `BC6782FC`=Assault Rifles(AK47), `7D6BD5F2`=Sniper Rifles(M1903),
    `EEAE53E1`=Shotguns(SPAS12), `AA73EE0A`=SMGs(MAC10), `BD090A47`=LMGs(PKM), `FC2096BC`=Dart Rifle
  - Explosive: `4EE9BFD6`=Mortars, `CEB9BB1E`=RPG-7, `704CA95D`=M-79, `EA12131E`=IED,
    `E710123D`=MGL-140 (+ a separate grenades category)
  - Fuel: `31BD6FE9`=LPO50 Flamethrower, `C86412FF`=Flare gun (+ a separate molotovs category)
  - Known open bug: setting a pickup's `fRespawnTime` very low (e.g. `0.1`) makes it visually respawn
    but become permanently un-collectable a second time — unsolved.
- **Named/unique weapon pickups**: e.g. Golden AK47 (`28_pickups.xml`,
  `Weapons.AK47_new.AK47_Gold`) — set `fRespawnTime` to `0.1` to make it respawn like an armory
  weapon. To make buddies/mercs wield a named variant, edit `gamemodesconfig.xml`'s
  `<pack name="buddy">` weapon-assignment section and swap in the variant name (e.g.
  `AK47.AK47_Gold`).

### Weapon archetype reference

Confirmed directly from the game's weapon definitions, useful for any `archetype="weapons.…"` string:

- **Assault rifles**: `AK47`, `AK47.AK47_Gold`, `FNFAL`, `G3KA4`, `M16`, `MP5`
- **Explosives (throwable)**: `M67`, `Molotov`, `IED`
- **Launchers**: `Carl_Gustaf`, `Carl_Gustaf.Carl_Gustaf_Merc`, `MGL140`, `M79`, `RPG7`, `RPG7.RPG7_Merc`
- **LMGs**: `M249_Saw`, `M249_Saw.M249_Saw_Merc`, `PKM`, `PKM.PKM_Merc`
- **Sniper rifles**: `AS50`, `Dragunov`, `Dragunov.Dragunov_Merc`, `M1903`, `M1903.M1903_Merc`
- **Shotguns**: `Ithaca`, `SPAS12`, `USAS12`
- **Special**: `Dart_Rifle`, `LPO50`
- **Pistols/SMGs**: `Star45`, `Makarov`, `DesertEagle`, `SilencedMakarov_6P9`, `MAC10`, `Uzi`

The `_Merc` suffix (confirmed by stoatoats) isn't a stat variant — it's the *same* weapon redefined as
a `Primary` weapon slot instead of `Special`, because mercs have no inventory slot for "special"
weapons (this is why mercs are seen carrying the AS50 sniper rifle on their backs — it's the
`.Merc`-suffixed primary-slot version). Additional variants exist in world-specific `weapons.xml`
files (World 1/2/DLC): mounted-weapon archetypes (`MountedWeapons.M249_FishingBoat`,
`MountedWeapons.M249_SandBags.Multi`), "rusty"/degraded pickup variants
(`Secondary.MAC10.Mikes_Rusty`), and more special-weapon entries (`Special.Flare_Gun`,
`Special.Mortar.Mortar_Merc`) — `28_pickups.xml` is theorized to define these degraded/rusty pickup
variants layered on top of the general `WeaponProperties.xml` definitions (unconfirmed but consistent
with observed behavior).

### Weapon-slot and sound

- **Weapon-slot reassignment**: each weapon can only occupy one slot (no simple "any weapon any slot"
  toggle, unlike FC3's "Ziggy's mod"). Removing the slot-assignment value from a weapon's definition
  entirely (rather than setting it to a valid slot number) causes the weapon to default into the
  machete slot **without replacing the machete** — carry more than the normal 4-weapon limit and cycle
  through "extra" weapons by repeatedly pressing the machete hotkey. Originally found in the "Jackal
  mod." The actual field controlling weapon-wheel slot placement is `selCategory` (confirmed by
  community member "LowPoly", distinct from the similarly-named `Priority` field, hash `9BD0BE71`,
  which does something else) — removing `selCategory` is what triggers the machete-slot fallback.
- **Weapon sound swapping**: replace a weapon's sound-hash string with another's (confirmed values:
  dart=`3078303034424635454100`, flare gun=`3078303034353643344200`, silent
  makarov=`3078303034424635433800`) *and* set `bIsSilent` to `True` together — confirmed to produce a
  genuinely silent weapon in play (mercs don't react to gunfire at range). `bIsSilent` alone does
  nothing; it needs the sound-hash swap too. Reliability is inconsistent: AS50↔Flare Gun and FN
  FAL↔Dragunov-with-fire-mode-change both worked for different testers, but at least one other
  combination resulted in losing sound entirely. A `bEmitLight` bool also exists per-weapon; no
  confirmed visual effect was ever observed from toggling it.
- **Full-auto conversion + sound looping**: a separate gotcha from the `iBurstLength` issue above — a
  `.spk` sound authored for a 3-round burst, used as the fire sound for a weapon modded to genuinely
  full-auto, audibly "repeats 3 times with a pause" instead of looping smoothly; the burst-length
  baked into the sound asset doesn't automatically stretch to match a changed fire rate/mode. Fix:
  manually re-edit the sound to loop as a short burst.
- **Weapon fields confirmed via live sound/scope modding** (Discord, `modding`, Dec 2025–Jan 2026,
  Gabor): `bUseHiResScope` (Boolean, in the weapon's `generated` entity data, not
  `41_WeaponProperties.xml`) — toggling to `False` changes scope behavior; `iAnimationValue`
  reportedly affects which crosshair is used. A recurring silent-failure cause: fdx4061 spent real
  time on an invisible-effect bug before realizing he needed `m16.multi` specifically, not the base
  `m16` entry.
- **Weapon skins/textures**: full texture replacement (no simple color/tint parameter exists) has been
  demonstrated across most base weapons plus UI/loading-screen images, via Gibbed's tools. Whole-mesh
  *swapping between existing characters* was achieved as early as 2022 (see the buddy model-swap
  recipe below); static *object import from another Dunia title* (Avatar) was achieved by 2024–2025
  (see [Engine Theory](./engine-theory.md) and [`.xbm`/`.xbg`](../file-formats/xbm-xbg.md)). Importing
  a wholly new, from-scratch custom model was the last unsolved piece — resolved as of July 2026 by
  Quiet_Joker's `Dunia-Engine-XBG-Blender-Importer`, see the [`.xbm`/`.xbg` format page](../file-formats/xbm-xbg.md).
- **Buddy character model-swap recipe** (cosmetic reskin between *existing* buddies, not custom
  import — Discord, `🔨-fc2-modding`, Jan 2022, Hunter/FC_Redux's author): locate the desired buddy's
  `.xbg` model inside `worlds.fat`'s `graphics` folder, copy it into the corresponding folder inside
  `entitylibrarypatchoverride.fcb`, and rename it to match the buddy slot you want to replace (e.g.
  copy `paul.xbg`, rename to `frank.xbg`, to make Frank appear with Paul's model). Useful for making a
  specific buddy "available" in an Act where they normally aren't. A `"cannot load world"` error
  almost always means `worlds.dat` itself has been moved/deleted from the data folder, not a problem
  with the swap itself.
- **Weapon pickup/UI icons are partly hardcoded in `Dunia.dll` itself**, a genuine exception to the
  "everything's in FCB/XML" pattern (Discord, `🔨-fc2-modding`, Jul 2022, extensive investigation by
  Boggalog/scubrah/Hunter/legendhavoc175): each weapon's HUD icon is selected via its `sName` value,
  looked up against an icon list; SP uses `hud.mgb`, MP uses `hud_mp.mgb` — both currently unworkable
  by any available tool. Machetes (and MP-specific mounted guns) have no icon entry, falling back to
  the sawed-off shotgun's icon. A working fix was found via direct `Dunia.dll` hex-editing: repointing
  an unused internal icon slot (the "dlc6" placeholder) to the machete texture, and reassigning the
  sawed-off's own `sName` so the fallback no longer collides — a confirmed real case of the community
  patching the game binary, not just its data files (see also the "Multi editor mod"'s modified
  `Dunia.dll` in [Engine Theory](./engine-theory.md)). Not shipped as a finished fix: a separate,
  unrelated bug means loading any save always force-equips whichever machete is selected from the main
  menu, overriding current loadout regardless of the icon fix.
- **Mortar/weapon neutering**: setting a weapon's damage/range/accuracy/reliability/firerate all to
  `0.1` (must be nonzero — `0` still deals damage in at least one tested case) leaves the sound/visual
  effect intact while making it nearly harmless — used to "de-fang" mortar-guy NPCs specifically.

## Vehicles

- **Vehicle armor**: reliability-degradation "manuals" (bought from the arms dealer) pushed toward
  100% can make a vehicle nearly immune to mortar/rocket fire while you're inside it (mercs' shots
  visibly bounce off, though bullets were observed passing through the windshield into the seats — not
  a real collision simulation, more a damage-immunity flag); you still take minor chip damage caught
  on foot nearby. Independently confirmed to also reduce damage the *player* takes while inside the
  vehicle, not just the vehicle's own durability.
- Vehicle max-HP modding is unreliable — see [Gotchas](./gotchas.md).

## Enemies & AI

- **Enemy loadouts**: `<PrimaryWeapon difficulty="19" probability="0.25"
  archetype="weapons.Primary.FNFAL" />`-style entries in `gamemodesconfig.xml`'s `InventoryPacks`
  section — swap `archetype` to rearm enemies with any weapon archetype (including named variants like
  `AK47.AK47_Gold`), or add a `<SecondaryWeapon probability="X" archetype="…" />` entry (probability is
  a straight 0.0–1.0 chance; `1.00` = 100% guaranteed spawn-with). Caveat: enemy AI has no
  self-preservation logic for player-granted heavy weapons — mercs re-armed with MGL-140s/IEDs
  frequently kill themselves and teammates with their own ordnance.
- **Faction infighting** (credited to modder StoatOats' "RealMod", reverse-engineered by forum member
  Vaatho): `10_Ghostpatrols.xml` lists every patrol type with a faction-color field (`BlueFaction`/
  `RedFaction`) — changing a patrol's color to be hostile toward its nominal "own" side produces real,
  dynamic inter-faction firefights in the open world (confirmed: wreckage/fire observed afterward).
  Patrol vehicles also have an often-empty passenger slot accepting an enemy-archetype string —
  confirmed working: `Sniper_`, `MortarMan_`, `LightMachineGunner_`. All patrol models default to a
  "Caucasian" race string, replaceable with "Nubian" for variety. `gamemodesconfig.xml` separately
  defines a `<ReinforcementArchetypes>`/`<MapArmy>` pair describing a 5×5 grid of which faction "owns"
  which map region (not the visible 3×3 in-game map division) — editing this in isolation showed no
  effect; the patrol-color method above is what actually works.
- **Camo/detection tuning** (`gamemodesconfig.xml` + `player.xml`):
  `<Plan name="stealth_camouflage"><bonus attr="stealth" value="1"/></Plan>` controls the camo suit's
  bonus (base game default is `value="10"`, not `1`), seemingly a 0/1 toggle rather than a gradient.
  Detection-duration values (`player.xml`) are counterintuitive: raising them makes mercs *slower* to
  confirm a detection (harder to detect you), not more alert. Full field list (guru3D, credit
  "Freelancer"): `PlayerAwarenessResetDelay`, `LongRangeDetectionDurationLevel1/2`,
  `MediumRangeDetectionDurationLevel1`, `PersonalRangeDetectionDurationLevel1/2`,
  `IntimateRangeDetectionDurationLevel1/2`, `StareAtDetectionDurationLevel1/2`,
  `AimingProvocationDetectionDurationLevel1-4`, `AimedAtDetectionDurationLevel1-3`,
  `RaisedWeaponLongRangeDetectionDuration`, `RaisedWeaponPersonalRangeDetectionDuration` — plus two
  adjacent movement fields in the same block, `BumpMinSpeed`/`ChargeMinSpeed`/`ChargeAngle` (minimum
  speed to trigger a bump/charge reaction, and its angle threshold). Per-biome FOV blocks exist
  separately for desert/savannah/jungle in `player.xml`'s `SensorySystem/FOVParameters`
  (`fLength`/`fAngle` for both `FocusFOV` and `PeripheralFOV`), plus global multipliers
  (`fNightTimeMultiplier` defaults to 0.5 vs. 4 for daytime). Combining biome-specific FOV narrowing
  with the stealth bonus is the community's most-praised realism tweak (credited to forum member
  Diablo_Lobo).
- **Sniper range**: vanilla `fMaxRange` for sniper rifles is **400** (Discord, `🔨-fc3-and-bd-modding`,
  2023-02-28, "Low"); `RangeMultipliers` were not applied on a per-difficulty basis in FC2.
- **Grenade drop chance**: `<ChanceToDropGrenade Casual="1" Experimented="0.5" Hardcore="0.33"
  Infamous="0.25"/>` in `gamemodesconfig.xml` — set any tier to `1` for every killed merc to drop a
  grenade.

## Player

- **Fall damage / high jump**: `fJumpHeight` in `player.xml` (default `1`; `5`+ is already
  superhuman, `~20` is common in community patches) combined with
  `fMinSpeedFallDamage`/`fMaxSpeedFallDamage` in `gamemodesconfig.xml`'s `DefaultCountersService` block
  (e.g. `1000` to eliminate fall damage) — needed together, since high jump without removing fall
  damage gets you killed on landing. Crouching instantly reverts to normal movement/jump as a built-in
  toggle.
- **Movement speed**: `player.xml`, replace *all* instances (many, with differing defaults) of
  `fWalkingMaxSpeed`, `fWalkingMaxSpeedCrouch`, `fWalkingAcceleration`, `fWalkingDeceleration`,
  `archSprintCurve` (references `Curves.Locomotion.Sprint` in `curves.xml`),
  `fSprintingDeceleration`, `fClimbSpeed`, `fSwimmingMinDepth`, `fSwimmingMaxSpeed`. A
  community-tested "fun but extreme" set: walking/swimming speed both `15`, crouch speed `2.5`,
  accel/decel `30`/`30`, sprint decel `20`, climb `1.4` — outruns vehicles entirely.
- **Player gravity**: `fGravity` in `player.xml` — a small/negative value (e.g. `-1`) approximates
  low/moon gravity for glider-less "flying" experimentation. Vanilla default confirmed as `-20`,
  identical between FC2 and FC3 (Discord, `🔨-fc3-and-bd-modding`, 2023-02-28, "Low").
- **The hang glider was never successfully modded** — flight time, agility, and glider-based actions
  (e.g. dropping grenades while gliding) all remained unrealized despite extensive searching of the
  vehicle data file.

## Economy & progression

- **Arms dealer / weapon bazaar** (`gamemodesconfig.xml`, search `cost=`): `availability` (0/1/2 =
  start/Leboa/Bowa), `needsUnlock` (0/1 = mission-gated), `cost` (must be ≥1 — `0` deletes the entry
  entirely), `unlockUpgrade` (governs whether the "new items available" message requires owning the
  base weapon first).
- **Full bandolier/max-ammo reference table** (`gamemodesconfig.xml`, `<!-- BANDOLIER BONUSES -->`):
  one `<Plan>` per bandolier type, each with per-weapon, per-`difficultyLevel` (0=Casual…3=Infamous)
  `maxammo` bonuses. Plans: `pistol_belt` (makarov/star45/deserteagle/6p9), `light_assault_webbing`
  (mac10/uzi), `shotgun_bandolier` (ithaca/spas12/usas12), `assault_webbing`
  (mp5/fnfal/g3ka4/ak47/m16), `marksmans_bandolier` (m1903/dragunov/as50/Dart_Rifle),
  `rocketeer_satchel` (rpg-7/carl_gustaf/mortar), `grenadier_webbing` (m67/m79/ied/MGL140),
  `pyrotechnic_satchel` (flare_gun/lpo50/molotov), `gunner_pack` (pkm/m249), plus
  `stealth_camouflage` (camo bonus), `health_basic`/`health_advanced` (healing time + syringe max).
  This is the complete object-name reference for every carryable weapon/gadget in the base game.
- **Weapon manuals** (`gamemodesconfig.xml`) — three separate bonus systems per weapon: `OPERATIONS
  MANUALS` (`accuracy`/`damage`/`sticky`[unresolved]/`recoil`, percent), `REPAIR&MAINTENANCE MANUALS`
  (`degradation`/`unjamtime`, percent — full default table is `-20%`/`-35%` for nearly every weapon,
  except the 6P9 which ships with an unusually strong `-500%` degradation bonus by default; M-79 and
  flare gun manuals have no `unjamtime` entry since they don't truly jam), `VEHICLE MANUALS`
  (`degradation`/`repairtime`, percent). Reflected live in the pause-menu upgrades screen. Setting
  `degradation` to `-999%` lets you drive an already-"destroyed"/smoking vehicle with no further
  consequence; `unjamtime` at `-99%` skips the unjam animation almost entirely.
- **Reputation/Infamy**: `gamemodesconfig.xml`'s `<Infamy><Acts>` block sets per-Act level ranges
  (`startlevel`/`maxlevel`); `<Levels>` sets `minRate`/`maxRate` bands each with a `medicine` flag,
  `failureChance`, and `maxNPCInFailure`. **Behavioral caveat**: directly setting a high starting
  reputation via save-edit does not reproduce the same NPC reactions as organically earning that level
  through play — dialogue/behavior appears keyed off actual earned progress/state, not just the raw
  displayed number.
- **Diamond count instability**: cheating a large lump of diamonds early (900–1000+) has been observed
  to break/crash the game. Earning the same or higher amounts gradually through legitimate play
  (2000+) was reported as stable — front-loading is the risky path, not the total amount.
- **Diamond case respawn/quantity data lives in `OA_MissionPickups.xml`** (Discord, `🔨-fc2-modding`,
  Aug 2022, "LowPoly"): individual diamond case pickups are defined here, distinguished by a value
  field — most regular cases use `1`, `2`, or `3`; the player's starting case uses `10`. Per-case
  diamond editing is a single well-structured file, not tedious per-sector hand-editing.

## Environment & sound

- **Malaria removal**: in `gamemodesconfig.xml`'s `<Malaria>` block, redirect all four curve
  references (`FirstAttackTime`, `BetweenAttackTime`, `MinorAttackQte`, `MinorAttackDuration`) to
  `Curves.PlayerSicknessCurves.HealthMax_Infamous` — an unrelated always-high curve, effectively
  disabling attacks. Confirmed working by multiple independent testers.
- **Ambient sound regions** (Discord, Far Cry 2 Multiplayer, `modding`, Mar 2026, Gabor): FC2 has
  exactly **7 defined ambient sound regions** — `Desert`, `Jungle`, `Savannah` (pure biomes) plus 4
  transition/blend regions `tDes_Jun`, `tDesSav`, `tJun_Sav`, `tLake`. Which region plays for a given
  patch of terrain is driven by which ground texture is assigned there — the sound-region name is a
  property of the texture slot, not the terrain geometry itself, so the practical map-wide lever is
  reassigning ground textures in the "textures inventory" to ones carrying the desired sound region,
  then optionally lowering sound intensity. Deeper per-instance control exists via `soundregions.xml`,
  but changes there are local-only and don't ship with an exported custom map. Untested: whether
  leaving a sound region assignment empty produces true silence.

## Map editor mechanics

Concrete map-editor tool mechanics, confirmed directly from the stock map editor's decompiled source
(`tools/third-party/FC2Editor_Source/FC2Editor.Tools/` — see [Getting Started](./getting-started.md)
for provenance, [the editor API surface page](../engine-internals/editor-api-surface.md) for the
underlying native calls these tools drive). For the tools themselves and their hotkeys, see [Engine
Theory](./engine-theory.md#map-editor).

- **Brush mechanics shared by every terrain/texture/foliage paint tool** (`ToolPaint.cs`, the common
  base class): radius clamped **1–128** world units; the Distortion slider scales to `distortion ×
  radius × 0.7` (max jitter offset is always 70% of the current radius).
- **Texture Painter tuning** (`ToolTexture.cs`): default brush hardness is **0.85** (harder-edged than
  the shared 0.4 base default); paint accumulates at `strength × 512 × dt` per second; auto-texture-
  by-constraint ranges are Min/MaxHeight **0–255**, Height Fuzziness **0–32**, Min/MaxSlope **0–90°**.
- **Terrain Terrace, the 8th terrain tool** (toolbar-only, no hotkey found in the decompiled source):
  steps terrain into level bands — default step height **2** (range 0–32), Strength scaled far gentler
  than the other brushes (`opacity = strength × 0.04`, ~25× softer than a typical paint tool).
- **Flatten (F3) has a hidden eyedropper**: Ctrl+click samples the terrain height under the cursor
  into the Height field instead of painting. Its Height range is 0–256 (default 32); Raise/Lower
  (F2)'s delta range is a separate, smaller -32 to +32 (default 5), since it's relative not absolute.
- **Erosion (F7)'s four knobs**: Density, Deformation, Channel Depth, Randomness (all 0–1, defaults
  0.5/0.5/0.5/0).
- **Ramp (F5) is exactly two mouse-*up* clicks** (release, not press): the first sets the start point,
  the second executes the ramp. Forcibly disables the square-brush and Distortion options
  (circle-only), using Hardness purely as edge-blend width.
- **Road width is clamped 4–16 world units** (default 8). While drawing a spline, a new point only
  auto-inserts once the cursor moves more than 15 world units from the last one; clicking to insert a
  point mid-segment has a 4-unit hit tolerance.
- **Object placement/move mechanics** (`ToolObject.cs`, ~3,500 lines, by far the largest pure-logic
  file in the editor source): auto-orientation tilts a placed object to match the raycast surface
  normal, not just camera yaw. Ctrl+drag while placing or moving performs a freehand yaw rotation at a
  fixed **0.025 rad (~1.43°) per pixel** of horizontal mouse movement. **Shift+drag duplicates the
  current selection in place** and immediately lets you drag the copy away, leaving the original
  untouched — the editor's built-in "duplicate-drag" idiom, not documented in any tutorial reviewed.
- **Move mode's grid-snap size is 1–16 world units** (default 1, step 0.25); "Snap Object Size" snaps
  movement to increments equal to the selected object's own bounding-box dimensions — the practical
  way to butt identical modular pieces (fences, wall segments) together with zero gaps. "Grab Anchor"
  grabs an object by its nearest pivot/anchor point instead of its geometric center when dragged.
  Rotation angle-snap (default 90°) advances one notch per 25 units of accumulated perpendicular mouse
  movement; "Reset Tilt" zeroes pitch/roll only, preserving yaw.
