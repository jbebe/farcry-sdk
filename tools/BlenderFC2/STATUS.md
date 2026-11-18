# Blender add-on

Purpose: Edit Far Cry 2 models, materials and animations in Blender through JackAll's .fc2model packs, holding no Dunia format code itself.
Stage: active

## Features

- [done] Import a .fc2model pack - parts, LODs, UVs, colours, normals, armature, rigid parts on pivots, skin weights
- [done] Generic shader rebuilt as a node graph preview, alpha wired
- [done] Export geometry back into the pack - untouched round trip byte-exact, topology may change, bounds refitted
- [done] Add as New Part - lives only at the LOD it was added to
- [done] Load Far Cry 2 Animation with the body the clips pose, First frame only - clip choice by root tag (36538e1f) not in the changelog
- [done] Write Animation into the bank it came from - only the clip that fits the rig, Only selected bones
- [done] Retime Animation - confirmed in game on the AK-47's reload
- [done] Auto IK drags an arm or a leg
- [done] Eye view from the player's Camera bone
- [done] Sight picture for SCOPE_HI
- [done] Check panel - format rules, silent on retail
- [done] Motion table (Measure motion)
- [done] Material editor in the Far Cry 2 panel, Clean twins set along, Copy to a New Material - 0.4.0 unreleased; new path untested in game
- [done] Textures written back, outside images added as new textures - 0.4.0 unreleased
- [done] build.ps1 builds the extension zip with Blender; open_model.cmd opens a pack and a clip in the UI

## To validate

## Approvals

- played: d04a2176 2026-11-18 - "I read all texts and played all mods and validated all lines"
- page text: d04a2176 2026-11-18 - "I read all texts and played all mods and validated all lines"

## Checks

- simplify: aa85c23 2026-11-15 - since blenderfc2-0.3.3

## Release order

- after jackall-1.7.0

## Collisions
