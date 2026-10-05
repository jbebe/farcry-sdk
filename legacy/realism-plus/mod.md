---
name: Far Cry 2 - Realism Plus (Tom's Mod)
version: "Final"
author: Boggalog
url: https://www.nexusmods.com/farcry2/mods/292
archive: Realism Plus-292-Final-1713704969.7z
sha256: f2950b726d0e24a8c64b228404c933afcdb60c419df34119a14587625a21a871
baseline: steam
analyzed: 2026-10-05
source: the v2.1 summary lines quoted on gamepressure.com's mirror of the mod page; the Final (2024) description on Nexus and ModDB did not load (HTTP 403)
---

# Far Cry 2 - Realism Plus (Tom's Mod) Final

The realism member of Boggalog's "Vanilla+ Mod Collection" (Vanilla+, Realism+, Chill+, Insanity+),
in its final release of April 2024. It is a broad overhaul: weapons, enemy and buddy loadouts,
patrols, AI odds, vehicles, the player's movement and health, the economy, graphics settings and
textures, the HUD and map, and an engine binary with nine patches. It folds in scubrah's Functional
Outposts v1.0 ([`outposts-functional`](features/outposts-functional.md)) and fixes also found in
Scubrah's Patch.

The mod requires a new game. Its installation guide says so, and the weapon shop's items are
reshuffled under their old keys, so an older save would unlock the wrong weapons
([`economy-bazaar-reorder`](features/economy-bazaar-reorder.md)).

## How it ships

| In the archive | What it is |
|---|---|
| `1. Essential Files/1. Patch Files/<saving>/<navigation>/<gadget>/Data_Win32/patch.dat`, `patch.fat` | Eight full patch archives of about 269 MB each, one per combination of Limited Saving or Save Anywhere, Limited or Full Navigation, Flare Gun or IED gadget. |
| `1. Essential Files/2. Engine Files/<saving>/<flashing>/bin/Dunia.dll`, `FarCry2.exe` | Four engine variants, the GOG `Dunia.dll` with nine patches ([`engine-dunia-dll`](features/engine-dunia-dll.md)), and the GOG launcher made large-address-aware. |
| `2. DLC Machetes/Install DLC Machetes.reg`, `Uninstall …` | Registry values that unlock the bonus content on the GOG engine ([`dlc-machetes-registry`](features/dlc-machetes-registry.md)). |
| `3. Tutorial Skip Saved Games/` | 24 saves, one per buddy as the player character, made after the opening, "Regular Start" or "All Weapons Unlocked & Upgraded". They are not game files, and nothing here analyses them. |
| `4. Customisations/` | 15 optional add-ons as loose `patch_unpack` files, merged into `patch.dat` with the bundled Gibbed tools (`0. Tools`). They are not part of the analysed patch; see below. |
| `Installation Instructions.pdf`, `… for Steam Deck.pdf` | The install guide. It warns that the pause menu's Upgrades page is broken and that DirectX 9 should be used. |

### What was analysed

One combination: the **Limited Saving / Limited Navigation / Flare Gun Gadget** patch with the
**Limited Saving / No Flashing Items** engine. It switches on every optional feature but one, the
IED gadget. The other choices change very little, and each page that a choice touches says what the
other choice does:

- **Save Anywhere:** keeps the F5 quicksave and F9 quickload controls
  ([`saving-quicksave-unbound`](features/saving-quicksave-unbound.md)). Its `Dunia.dll` keeps
  `CSaveGamePage` and `PAUSE_SAVEGAME` ([`saving-no-pause-save`](features/saving-no-pause-save.md)).
- **Full Navigation:** keeps the 20 GPS and map icon models and the road-sign tints
  ([`nav-gps-icons-removed`](features/nav-gps-icons-removed.md),
  [`nav-plain-road-signs`](features/nav-plain-road-signs.md)). One cell of the map's
  `final_objective_icons` atlas is drawn differently.
- **IED Gadget:** the IED, not the flare gun, loses its weapon slot
  ([`weapons-flare-gun-gadget`](features/weapons-flare-gun-gadget.md)).
- **Flashing Items:** the engine keeps `Mesh_Highlight`
  ([`graphics-no-flashing-items`](features/graphics-no-flashing-items.md)).

### Baseline

The baseline is Steam, although the mod ships GOG's `Dunia.dll` (19,412,104 bytes), which the
procedure would take as GOG. The patch archive was mostly built from Steam's. Its `hud_mp.mgb` and
`.desc` match Steam's patch copies byte for byte, and its string tables differ from GOG's in about
1,300 more places than from Steam's. Only `common.mgb` is the retail copy, which on Steam reverts a
newer one ([`noise-ui-retail-common-mgb`](features/noise-ui-retail-common-mgb.md)).

GOG's vanilla patch has no override library at all. Against GOG, the mod's override library is
therefore a new file with nothing to compare to: its roughly 3,560 archetype edits collapse into one
whole-file change, and about 2,900 Steam-versus-GOG UI and string differences show up as changes. Against Steam the data is granular. The cost is the engine, which
reads as one whole different build. Its nine patched sites were therefore found by diffing it
against GOG's own `Dunia.dll`, and each is documented with both builds' addresses.

### Customisations, not analysed

Each is an alternative to a preinstalled default, delivered as loose files to merge by hand. Ask for
any of them to be analysed as its own folder:

1. Outpost respawn timer (30, 45 or 60 minutes) and an optional "Outpost Cleared" popup. 45 minutes
   with no popup is preinstalled ([`ui-outpost-cleared-popup`](features/ui-outpost-cleared-popup.md)).
2. Playable NPCs: other character models for the player.
3. Weapon idle animations, standing and crouched, in four styles.
4. Weapon icons in several art styles.
5. Map icons: hand drawn, or the originals "AI enhanced".
6. Syrette or syringe HUD icon.
7. Removing individual HUD icons.
8. AR-16 and MGL-140 scope colours.
9. Malaria pill animation, 1, 2 or 3 pills; 2 is preinstalled.
10. FPS cap: 30, 60 (preinstalled), 144 or unlimited.
11. Original graphics settings, with the same four caps.
12. Original colour saturation.
13. The 'Colourful Far Cry 2' ReShade preset, which is the published "custom reshade".
14. Removing the aiming and sprinting blur.
15. Shooting and throwing grenades while driving or on a mounted gun, "Lots of bugs".

## Bundles

- `graphics`, `weapons`, `gameplay`, `fixes`, `dlc-unlocks`, `saving`: the six published lines.
- `navigation`: the Limited Navigation option, which the Final release added.
- `ui`: HUD and menu textures and notices.

## Coverage

`legacy check` is clean: all 18,636 changes are claimed by exactly one page, and all 6 published
lines are covered. 134 pages: 114 components, 2 shared pieces, 8 bundles, 10 noise rules.

- **The engine (`verified: re`):** all nine `Dunia.dll` patches are traced in Ghidra, eight
  component pages plus the shared binary, with Steam and GOG addresses.
  - Two of them explain things the guide only reports. The pause-menu Upgrades page is broken by an
    edit that renames the widgets it fills ([`ui-upgrades-page-blank`](features/ui-upgrades-page-blank.md)).
    The sawed-off shotgun is moved onto a spare HUD icon slot
    ([`weapons-sawedoff-hud-icon`](features/weapons-sawedoff-hud-icon.md)).
- **Dead edits:** 1,355 changes edit archetype copies the game never reads. All of them are on two
  noise pages: placeholder DLC vehicles and DLC weapon stubs copied into the override library.
- **One no-artifact page:** the machete unlock lives in a registry file, outside the game files.
- **Already in this repo:** the Jackal tapes, predecessor tapes and machetes (UFCP), the mouse cap,
  frame cap and iron-sight FOV (UFCP options), and the rim light and grade (Sky Overhaul).
- **The detailed v2.1 notes promise some things the data does not show.** Those notes are not the
  published claims, so they are not coverage gaps:
  - no "Colt 1911" rename;
  - no grass change for DirectX 9;
  - a 60 fps cap, not an unlocked frame rate;
  - no heal-without-syringes first aid;
  - no Desert Eagle ammo change;
  - no respawning golden AK;
  - SMG recoil goes up, not down;
  - rocket speed is doubled, not raised 40 %;
  - the bouncing-NPC fix covers only the opening checkpoint's soldiers.
- **Needs an in-game check:** every page's "Uncertain". Above all:
  - the six new inventory packs. [The guide](../../docs/docs/modding/guide/patrols.md) says new
    packs cannot be created, and whether the engine resolves them is not traced
    ([`weapons-new-enemy-packs`](features/weapons-new-enemy-packs.md),
    [`ai-specops-a1lm04`](features/ai-specops-a1lm04.md));
  - [`world-sniper-moved`](features/world-sniper-moved.md), which moves the sniper to a different
    spot than Scubrah's Patch does, so the two conflict.

## Published feature list

- Improved graphics – further draw distance with improved LOD, better shadows, increased decals, custom reshade
- Weapon changes – either the flare gun or IEDs are usable as gadgets, the MP5 is a secondary, buffed shotguns, buffed pistols, various weapon fixes
- Gameplay changes – silent machete assassinations, enemies and buddies use more weapons, diverse patrols, dynamic enemies, enemy infighting, different colours for DLC vehicles, faster vehicles, better stealth, more stamina, more diamonds in briefcases
- Bug fixes – restored truck sounds, restored infamous healing animations, jackal tape fix
- Unlocked dlc – predecessor tapes, homemade and primitive machetes
- Optional feature that disables quicksave and saving from the pause menu - limiting saving to safehouses, gun shops, bus stops and mission givers
