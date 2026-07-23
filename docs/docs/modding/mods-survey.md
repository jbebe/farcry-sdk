---
sidebar_position: 6
---

# Mods Survey

:::note[Community-reported]
Sourced from Nexus Mods, ModDB, and the OWG forum — see [Getting Started](./getting-started.md)
for the full provenance note.
:::

Purpose: figure out which existing mods/tools are worth studying before we build anything, vs. dead ends (pure shader presets, cheats, etc). Sources: [Nexus Mods](https://www.nexusmods.com/games/farcry2/mods) (67 mods), [ModDB](https://www.moddb.com/games/far-cry-2/mods) (60 mods), surveyed 2026-07-09, plus a handful of mods surfaced while reading the [OpenWorldGames forum](https://www.openworldgames.org/owg/forums/index.php?board=169.0) for technical knowledge (see [Getting Started](./getting-started.md)). ~22 Nexus/ModDB mods are cross-posted to both — those are listed once with both links.

## Key technical facts surfaced by this survey (not just mod recommendations)
- **Archive format confirmed**: multiple mod descriptions reference `patch.dat` / `patch.fat` as the pair of files that hold the game's packed assets — "Far Cry 2 Rewards" explicitly warns Desura users to back these up before installing. This is the pak format to target for extraction tooling.
- **Official/community map editor tool name confirmed**: `FARCRY2_MAP_EDITOR` — referenced directly by two independent map-mod authors (DESERT ASSAULT, DEEP in the WOODS) as the tool they built their maps with.
- **A "Gibbed"-style tool exists for FC2**: the ModDB mod "Oversaturated Far Cry 2 to Cure your Depression" says to "use Hunter's version of gibbed tools to replace worlds folder in your patch files" — confirms a Gibbed.FarCry2-style unpacker/repacker exists, authored by a modder called **Hunter**, who also authored "Hunter's Far Cry 2 Update" and is credited as a base for "Redux Dawn Remake" (Hunter's Redux 2.9). Hunter looks like a key name to track down directly (ModDB profile, possible GitHub).
- **Two dedicated map-editor tools beyond the stock in-game editor**: "Ultimate Map Editor" (UME) and "Finos Editor Mods" both claim to unlock/merge/expand the object set available to the map editor — these are probably the highest-leverage tools for understanding the map/level format.
- **A mod-merging tool exists**: "Far Cry 2 Universal Patcher" claims to combine arbitrary mods with any master mod "without any prior knowledge of the modding tools" — worth understanding its merge logic even if we don't use it directly.

## Bucket key
- **START HERE** — documentation/tooling, read/use before anything else
- **ENGINE PATCH** — fixes or alters actual engine behavior/bugs, likely touches exe or core data
- **OVERHAUL/DATA** — large data-driven gameplay mod, good for learning the stats/behavior schema
- **MAP/LEVEL** — level design, editor use, or map-format precedent
- **ANIM/PHYSICS** — touches animation, physics, or particle/effect systems specifically
- **ART ASSET** — textures/icons/models only, no logic/engine insight
- **JUST ENB/RESHADE/SweetFX** — post-process shader injector preset, external to game data, ~zero engine insight
- **MINOR TWEAK** — small ini/stat/config tweak, low insight
- **SAVE/CHEAT ONLY** — savegame or cheat-engine edit, not a real mod
- **EXTERNAL TOOL** — runs outside the game (AHK, overlay), not modding the engine itself

## Promising mods for inspection (merged, non-ENB/ReShade/SweetFX, non-AHK)

### Documentation & tooling — start here
- [An Almost Complete Guide to Far Cry 2 Modding](https://www.nexusmods.com/farcry2/mods/299) ([ModDB mirror](https://www.moddb.com/mods/an-almost-complete-guide-to-far-cry-2-modding)) — Boggalog. The closest thing to an SDK doc. **Flagged as a top pick since the original survey but never actually opened/read — still an open gap, worth prioritizing.** Update: the **direct live Google Doc URL** was found via Discord (`🔨-fc2-modding`, Nov 2021, shared by "Hunter" — see [sources](./sources.md)): `https://docs.google.com/document/d/1ozhN9s_4puzSXVYs12ZAOayyL036hgQuFCVS_jSXbd0/edit` — the Discord embed preview shows its table of contents: Contributors · Quick Steps (editing an existing mod) · Modding Tools · Packing/unpacking · XML Editing · XML Decoding · Hex editing · Texture conversion · Texture editing · File Management · .fat and .dat files · Unpacking .fat... This confirms it's exactly the comprehensive walkthrough its Nexus description promises — now saved locally as [the Almost Complete Guide](./guide).
- **[Far Cry 2 Fortunes Pack installer](https://www.moddb.com/games/far-cry-2/downloads/far-cry-2-fortunes-pack)** (ModDB, surfaced via Discord) — not a mod, an **environment-setup utility**: installs the Fortune's Pack expansion content (extra maps/weapons/vehicles) that Steam/Uplay/GOG copies already bundle but disc/ISO installs lack. Relevant to us because several community map-editor mods and multiplayer maps explicitly require Fortune's Pack to be installed to load correctly — worth applying to any disc-based test install before assuming a map/mod is broken.
- [SCHTEVE - FarCry 2 Modding Utility](https://www.nexusmods.com/farcry2/mods/316) — PuppyUnicorn (Nexus only). PowerShell CLI automating modding tasks.
- [ScriptLoader](https://www.nexusmods.com/farcry2/mods/360) — Dzert14 (Nexus only).
- [Far Cry 2 Editor With AI - Enemies and Ally](https://www.nexusmods.com/farcry2/mods/354) — Princeton73 (Nexus only).
- [Ultimate Map Editor](https://www.moddb.com/mods/ultimate-map-editor) — ModDB only. "Combines the already existing custom Map Editors into 1, expanded with 100s of new objects." **New major find.**
- [Finos Editor Mods](https://www.moddb.com/mods/finos-editor-mod) and [Far Cry 2 Finos Editor Mod 3.8.49](https://www.moddb.com/mods/far-cry-2-finos-editor-mod-3849) — ModDB only. Unlocks new objects for the map editor; 3.8.49 adds usable weapons, DLC vehicles, working respawn. Also mirrored on fc2mp.com's Map editor mods page.
- **Janne252's editor mod** (hosted on [fc2mp.com](https://www.fc2mp.com/Map-editor-mods/), 18.4mb) — adds new content/textures to the map editor with its own install/uninstall launcher; this specific build also fixes a Windows 10 crash bug present in the original that affected maps made with it.
- **Al's editor mod** (hosted on [fc2mp.com](https://www.fc2mp.com/Map-editor-mods/), 269mb) — adds many new objects to the map editor; this build has Janne252's textures merged in (credited).
- **Multi editor mod v1.3.2.1** (hosted on [fc2mp.com](https://www.fc2mp.com/Map-editor-mods/), 6.6mb) — adds extra objects/textures. **Technically distinct from every other mod on this list: ships a modified `Dunia.dll`**, i.e. actual engine-binary patching, not just FCB/XML data edits (see [Engine Theory](./engine-theory.md)). Explicitly incompatible with the FC2MPPatcher multiplayer tool — must be uninstalled to play multiplayer.
- **Gabor's "FC2 editor mod"** (not publicly released as of this writing, shared ad hoc within the "Far Cry 2 Multiplayer" Discord's `map-editor`/`modding` channels) — imports **1,700+ objects from Avatar: The Game (2009)** into the FC2 map editor's object library as static decoration (vehicle animations don't port due to third-person-vs-first-person rig incompatibility — see [Engine Theory](./engine-theory.md)). Notable as the origin point of the whole FC2/Avatar shared-Dunia-engine thread of investigation now running through this project's Discord findings.
- [Far Cry 2 Universal Patcher](https://www.moddb.com/mods/far-cry-2-universal-patcher) — ModDB only. Combines arbitrary mods with any master mod without needing the modding tools directly.
- [Far Cry 2 Rewards](https://www.moddb.com/mods/far-cry-2-rewards) — ModDB only. Minor mod, but its description is the clearest confirmation of the `patch.dat`/`patch.fat` archive pair.

### Engine/bugfix patches
- [Far Cry 2 - Patched](https://www.nexusmods.com/farcry2/mods/317) ([ModDB](https://www.moddb.com/mods/far-cry-2-patched)) — Boggalog. Foundational bugfix mod most overhauls build on.
- [Scubrah's Patch](https://www.nexusmods.com/farcry2/mods/328) ([ModDB](https://www.moddb.com/mods/scubrahs-patch)) — scubrah. Second major patch lineage.
- [Fix Bouncing NPCs](https://www.nexusmods.com/farcry2/mods/309) — scubrah (Nexus only).
- [Far Cry 2 VanillaPatchedDamage](https://www.nexusmods.com/farcry2/mods/353) — krieger857 (Nexus only, built on Patched).
- [Hunter's Far Cry 2 Update](https://www.moddb.com/mods/hunters-far-cry-2-update) — ModDB only. "Return of the Jackal tapes! No blinking items! Dynamic enemy AI! Improved Patrols! Infamous heal animations restored! Better ballistics!" — sizeable engine-behavior patch by the same "Hunter" behind the Gibbed-style tools.
- [Far Cry 2 - Autosave Mod](https://www.moddb.com/mods/far-cry-2-autosave-mod) — ModDB only. Hooks the save system (save at start/end of mission).

### Full overhaul / data-driven mods
- [Far Cry 2 - Realism Plus Redux](https://www.nexusmods.com/farcry2/mods/326) ([ModDB](https://www.moddb.com/mods/far-cry-2-realismredux)) — Boggalog.
- [True Misery Redux](https://www.nexusmods.com/farcry2/mods/332) ([ModDB](https://www.moddb.com/mods/true-misery-a-truly-hardcore-far-cry-2-experience)) — demonshadow199.
- [Oasis](https://www.nexusmods.com/farcry2/mods/330) — julian0451 (Nexus only).
- [G.O.R.E.](https://www.nexusmods.com/farcry2/mods/315) ([ModDB](https://www.moddb.com/mods/gore-glams-overly-realistic-edits-another-realism-mod)) — werozx.
- [Far Cry 2 - Vanilla Plus (Tom's Mod)](https://www.nexusmods.com/farcry2/mods/288) ([ModDB](https://www.moddb.com/mods/far-cry-2-vanilla-toms-mod)) — Boggalog.
- [Far Cry 2 - Chill Plus (Tom's Mod)](https://www.nexusmods.com/farcry2/mods/293) ([ModDB](https://www.moddb.com/mods/far-cry-2-chill-toms-mod)) — Boggalog.
- [Far Cry 2 - Insanity Plus (Tom's Mod)](https://www.nexusmods.com/farcry2/mods/294) ([ModDB](https://www.moddb.com/mods/far-cry-2-insanity-toms-mod)) — Boggalog.
- [Far Cry 2 - Realism Plus (Tom's Mod)](https://www.nexusmods.com/farcry2/mods/292) ([ModDB](https://www.moddb.com/mods/far-cry-2-realism-toms-mod)) — Boggalog.
- [Far Cry 2 Redux](https://www.nexusmods.com/farcry2/mods/286) ([ModDB](https://www.moddb.com/mods/far-cry-2-redux)) — BigTinz.
- [Far Cry 2 Modernized](https://www.nexusmods.com/farcry2/mods/308) — PuppyUnicorn (Nexus only).
- [Far Cry 2 Remastered (New Dunia)](https://www.nexusmods.com/farcry2/mods/297) — dannyhl2 (Nexus only).
- [Far Cry 2 Ultra hardcore realism mod](https://www.nexusmods.com/farcry2/mods/310) ([ModDB](https://www.moddb.com/mods/far-cry-2-ultra-hardcore-realism-mod)) — DungeonMaster34.
- [Functional Outposts](https://www.nexusmods.com/farcry2/mods/324) — scubrah (Nexus only). Best AI/behavior lead.
- [Unlocked and reorganized weapons](https://www.nexusmods.com/farcry2/mods/344) — Dan88RO (Nexus only).
- [FC2 Easier](https://www.nexusmods.com/farcry2/mods/291) — bnoabody (Nexus only).
- [Far Cry 2 Relaxed](https://www.nexusmods.com/farcry2/mods/279) — daninthemix (Nexus only).
- [Dylan's Far Cry 2 Realism Mod](https://www.moddb.com/mods/dylans-far-cry-2-realism-mod) — ModDB only.
- [Far Cry 2 Jackal Mod](https://www.moddb.com/mods/far-cry-2-jackal-mod) — ModDB only. Patrol/AI/malaria tweaks.
- [Post Apocalyptic Overhaul](https://www.moddb.com/mods/post-apocalyptic-overhaul) — ModDB only.
- [Redux Dawn Remake](https://www.moddb.com/mods/redux-dawn-remake) — ModDB only. Merge of Hunter's Redux 2.9 and Tom's Mods.
- [RealMode - Another Realism Mod](https://www.moddb.com/mods/realmode) — ModDB only.
- [Minimod](https://www.moddb.com/mods/minimod) — ModDB only. Lightweight, mostly AI/equipment.
- [Infamous Fusion](https://www.moddb.com/mods/infamous-fusion) — ModDB only.
- [FarCry 2 Immersion](https://www.moddb.com/mods/farcry-2-immersion) — ModDB only. Weapon/vehicle/checkpoint-respawn tweaks.
- [Far Cry 2 Zombie Mod](https://www.moddb.com/mods/far-cry-2-zombie-mod) — ModDB only. Notable for how far enemy-type conversion can go.
- [FC Ballistics Pro](https://www.moddb.com/mods/fc-ballistics-pro) — ModDB only. Narrow but precise weapon-ballistics customization (damage/recoil/weight/rate of fire).
- [Far Cry2 - Garamond's mod](https://www.moddb.com/mods/far-cry2-garamonds-mod) — ModDB only. Technical fixes + weapon rearrangement.
- [FC2 Realistic Weapons Pack (V1.1.1 Hotfix)](https://www.openworldgames.org/owg/forums/index.php/topic,2718.0.html) — TheFishlord, hosted on the OWG forum/downloads. One of the earliest deep weapon-data mods (magazine capacity, ammo types) — same author cracked the hash-only magazine-size values documented in [Data Recipes](./data-recipes.md).
- ["FC2-PZs-modded patch files"](https://www.openworldgames.org/owg/forums/index.php/topic,3516.0.html) — PZ (OWG). A maintained personal patch combining many of the thread's discoveries (arms dealer, weapon stats, camo/detection tuning) into one file — good as a single reference diff against vanilla.
- ["My FC2 patch"](https://www.openworldgames.org/owg/forums/index.php?action=downloads) (OWG downloads, by OWGKID) — another maintained personal patch, most-downloaded file in OWG's "Other Games and Stuff" category.

### Maps/level design
- [Merc Isle](https://www.nexusmods.com/farcry2/mods/322) ([ModDB](https://www.moddb.com/mods/httpswwwmoddbcommemberstenate108)) — TenAte108.
- [Nuketown in far cry 2 from cod mw3](https://www.nexusmods.com/farcry2/mods/319) — dhruv989 (Nexus only).
- [Far Cry 2 - Complete Map Collection](https://www.nexusmods.com/farcry2/mods/313) ([ModDB](https://www.moddb.com/mods/far-cry-2-complete-map-collection)) — Boggalog (reference only).
- [The Sinkhole](https://www.moddb.com/mods/the-sinkhole) — ModDB only. Small custom map.
- [dm_train_station3](https://www.moddb.com/mods/dm-train-station3) — ModDB only. Multiplayer map.
- [Battle Cry(Nuketown)](https://www.moddb.com/mods/battle-crynuketown) — ModDB only. Unrelated to the Nexus CoD-Nuketown port — this is the author's original early map.
- [Calls from the Mountain](https://www.moddb.com/mods/calls-from-the-mountain) — ModDB only. Built with FC2's first-version map tools; 10-year-old map.
- [CtrlAltComplete-Far cry 2-map pack](https://www.moddb.com/mods/ctrlaltcomplete-far-cry-2-map-pack) — ModDB only.
- [DESERT ASSAULT](https://www.moddb.com/mods/desert-assault) — ModDB only. Built with `FARCRY2_MAP_EDITOR`.
- [DEEP in the WOODS](https://www.moddb.com/mods/deep-in-the-woods) — ModDB only. Built with `FARCRY2_MAP_EDITOR`.

### Animation/physics/effects
- [Far Cry 2 - Rare Malaria Pill Animation](https://www.nexusmods.com/farcry2/mods/311) ([ModDB](https://www.moddb.com/mods/far-cry-2-rare-malaria-pill-animation)) — Boggalog.
- [Rare Malaria Animation](https://www.moddb.com/mods/rare-malaria-animation) — ModDB only, different upload/author — likely the same underlying trick as Boggalog's, worth comparing notes rather than treating as a second independent source.
- [Play as Female](https://www.moddb.com/mods/play-as-female) — ModDB only. Swaps player model to the female buddy characters — relevant to character rigging/model-swap pipeline.
- [Dans Far Cry 2 Blood and Gore Mod](https://www.moddb.com/mods/dans-far-cry-2-blood-and-gore-mod) — ModDB only. Blood/particle system tuning.

### Art assets (textures/icons/models — asset pipeline, not logic)
- [Far Cry 2 - Enhanced Texture Pack](https://www.nexusmods.com/farcry2/mods/300) ([ModDB](https://www.moddb.com/mods/far-cry-2-enhanced-texture-pack)) — Boggalog.
- [Far Cry 2 - Hand Drawn Map Icons](https://www.nexusmods.com/farcry2/mods/307) ([ModDB](https://www.moddb.com/mods/far-cry-2-hand-drawn-map-icons)) — Boggalog.
- [Far Cry 2 - Actual Syrette Icon](https://www.nexusmods.com/farcry2/mods/305) ([ModDB](https://www.moddb.com/mods/far-cry-2-actual-syrette-icon)) — Boggalog.
- [Far Cry 2 - Detailed and Colourful Weapon Icons](https://www.nexusmods.com/farcry2/mods/304) ([ModDB](https://www.moddb.com/mods/far-cry-2-detailed-colourful-weapon-icons)) — Boggalog.
- [Farcry 2 Weapons Skin And Player Skins](https://www.nexusmods.com/farcry2/mods/348) — ClassicGamesUnited (Nexus only).
- [Smaller Moon](https://www.nexusmods.com/farcry2/mods/318) — DeathWrench (Nexus only).
- [New Buggy (from avatar)](https://www.moddb.com/mods/new-buggy-from-avatar) — ModDB only. Replaces the buggy model with one imported from the game *Avatar* — real precedent for cross-game model import into Dunia.

### Minor tweaks / config-level
- [Vands FC2 Lighting Controller](https://www.nexusmods.com/farcry2/mods/364) — Vandarion (Nexus only).
- [Simple Graphics Improvement](https://www.nexusmods.com/farcry2/mods/362) — gordonfreeman242 (Nexus only).
- [critical healing everytime](https://www.nexusmods.com/farcry2/mods/357) ([ModDB "Critical Heal Everytime"](https://www.moddb.com/mods/critical-heal-everytime)) — spartix32.
- [No-HUD-No-Effects](https://www.nexusmods.com/farcry2/mods/356) — Gametism (Nexus only).
- [Fixed 16x Anisotropic Filtering](https://www.nexusmods.com/farcry2/mods/349) — Ev3rgr33n (Nexus only).
- [Accurate hip-fire weapons and raw mouse](https://www.nexusmods.com/farcry2/mods/352) — zdgfdhnxcbgfxn1 (Nexus only).
- [Skip Intro](https://www.nexusmods.com/farcry2/mods/320) — scubrah (Nexus only).
- [Far Cry 2 De-Filter](https://www.moddb.com/mods/far-cry-2-de-filter) — ModDB only. Removes desaturation/brown color filters without a shader injector — thanks credited to Scubrah and BigTinz.
- [Far Cry 2 Turbo Machete](https://www.moddb.com/mods/far-cry-2-turbo-machete) — ModDB only.
- [Get Lost](https://www.moddb.com/mods/get-lost) — ModDB only. Removes map location indicators.
- [Night Vision Mod for Far Cry 2 by Masson](https://www.moddb.com/mods/night-vision-mod-for-far-cry-2-by-masson) — ModDB only. SweetFX-based but adds an actual night-vision toggle, not just color grading.

### External standalone tool (not ENB/AHK, but doesn't touch game files)
- [Mortar Mate](https://www.nexusmods.com/farcry2/mods/333) — 17627346 (Nexus only).

### Save/cheat only (barely "mods")
- [Unlimited Diamonds](https://www.nexusmods.com/farcry2/mods/343) — ModEngine (Nexus only).
- [Far Cry 2 Free Roam and Starting Save Game](https://www.nexusmods.com/farcry2/mods/301) — SilverBulletWarrior (Nexus only).
- [Save game with 999 diamonds](https://www.nexusmods.com/farcry2/mods/277) — Alehazar (Nexus only).
- "Merc War - Mercs vs Mercs, real firefight" — Art Blade (OWG, savegame-based scenario referenced in the malaria-removal thread; direct download link not resolved this pass, findable via OWG's Downloads section).

## Top picks to actually go read/download first
1. **Ultimate Map Editor** and **Finos Editor Mods / Finos Editor Mod 3.8.49** (ModDB) — the real map/level tooling, beyond the stock editor.
2. **Far Cry 2 Universal Patcher** (ModDB) — understand its merge logic even before writing our own tooling.
3. ~~**An Almost Complete Guide to Far Cry 2 Modding** (Boggalog) — closest thing to an SDK doc.~~ Done — saved locally as [the Almost Complete Guide](./guide) (7,992 lines). Not parsed line-by-line into these notes; treat it as a standing reference to search/open directly when a specific modding question comes up, same way the Discord source archives are used.
4. **Hunter's Far Cry 2 Update** (ModDB) and tracking down **Hunter's Gibbed-style tools** directly — Hunter appears repeatedly as a foundational tool/patch author across both sites.
5. **Far Cry 2 - Patched** and **Scubrah's Patch** — the two base patch lineages nearly everything else builds on.
6. **SCHTEVE** and **ScriptLoader** (Nexus) — smaller tooling entries worth direct inspection.
7. **Functional Outposts** (Nexus) and **Far Cry 2 Jackal Mod** / **Minimod** (ModDB) — best AI/behavior leads.

## Dead weight (skip)
Pure ReShade/ENB/SweetFX presets (~22 on Nexus, ~8 more ModDB-exclusive ones: Hazza's Reshade Preset, Real Africa SweetFX Config, sproyd's SweetFX Config, Far Cry 2 - Graphical Enhancement Suite, plus the Nexus/ModDB cross-posted Burnt Food and Next Gen ReShade presets), the AHK toggle-ADS script, and 3 savegame/cheat uploads. Zero engine or data insight for our purposes.
