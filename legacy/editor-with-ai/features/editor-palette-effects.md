---
title: Waterfalls, smoke, animals and corpses in the editor palette
kind: component
bundle: editor-content
status: located
systems: [world, graphics, ai, ui]
match:
  - "ingameeditor/object_inventory.xml#Directory[+9]"
  - "languages/english/oasisstrings.fragment.xml#InGameEditor_Objects/{Razor2,Razor3,Razor4,Razor5,Razor6,Razor7,Razor8,Razor9,Razor10,Razor11,Razor12,Razor13,Razor14,Razor17,Razor18,Razor23,Razor24}"
  - "languages/english/oasisstrings.xml#section[InGameEditor_Objects]/string[{Razor2,Razor3,Razor4,Razor5,Razor6,Razor7,Razor8,Razor9,Razor10,Razor11,Razor12,Razor13,Razor14,Razor17,Razor18,Razor23,Razor24}]{,@value}"
exclude: []
requires: [editor-razor-library]
verified: diff
---

# Waterfalls, smoke, animals and corpses in the editor palette

A new "Natural" folder at the end of the palette (PC only). It holds placeable waterfalls,
river waves, smoke, fire and a sandstorm, the campaign's wild and farm animals, corpses, a target
dummy and the two factions' HQ doormen.

## How

`ingameeditor/object_inventory.xml`: one top-level `<Directory Id="Directory_Natural"
Display="Natural" PcOnly="1">` (commented `Animals, Smoke & WaterFall`) is added whole
(`Directory[+9]`, one change). 32 entries:

- **Particle effects**, 17 entries from the custom library
  ([`editor-razor-library`](editor-razor-library.md)), each drawn in the editor with the proxy mesh
  `graphics\editor\particles.xbg`:
  - sub-folder "Natural" (`WaterFall`): `Custom.RaZoR_Object.ParticlesEffect2`..`7`, labelled
    "Waterfall Crash (S)", "(M)", "(L)" and "Waterfall (L)", "(M)", "(S)";
  - sub-folder "Natural": `ParticlesEffect8`..`10`, "Waves (S)", "(M)", "(L)";
  - sub-folder "Other" (`Dark Smoke, Big Fire & Dust Storm`): `ParticlesEffect11`..`14`, `17`..`20`
    ("Dark Smoke (S) 01", "Light Smoke (M)", "Fire (M)", "Dark Smoke (S) 02", "Sandstorm", "Light
    Smoke (S)", "Dark Smoke (S) 03", "Dark Smoke (L)"), plus the vanilla template's
    `IGE_Archetypes.AutoPrefab.NewParticlesEffect_31`.
- **Animals**, eight `Animals.Quadrupeds.*` with `IsFlock="1"`: `Chicken`, `ChickenA`, `ChickenB`,
  `CapeBuffalo`, `Goat`, `Zebra`, `Wildebeest`, `Gazelle`.
- **Characters**: `enemy_archetypes.PartnerMissions.AmericanCorpse`,
  `enemy_archetypes.Corpses.ArmsMerchantCorpse` and `Corpse`, the dummy
  `enemy_archetypes.MercTest.Target_Practice`, and `buddies.APR.APR_HQDoorman` and
  `buddies.UFLL.UFLL_HQDoorman` (both mislabelled `Display="OilPuddle_01"`).

The 17 `RazorN` label strings for the effects are this page's. The animals and characters are in the
template world's vanilla library.

## Depends on

- [`editor-razor-library`](editor-razor-library.md) for the 17 effects. Most of their particle
  systems (waterfalls, river foam) are campaign systems the template world's vanilla particle
  library lacks. [`editor-particles`](editor-particles.md) supplies them to the template world as
  well.
- The animals and the doormen are AI-driven. Placed in a map, they probably act only with the
  single-player AI services that [`ai-editor-mode-services`](ai-editor-mode-services.md) adds to
  the editor mode.

## Uncertain

- How a flock entry (`IsFlock`) places animals, one or a group, has not been checked.
