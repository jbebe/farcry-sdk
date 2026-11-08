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

## Approvals

- played: f8ab839 2026-10-25 - implied by the blenderfc2-0.3.3 release
- page text: f8ab839 2026-10-25 - implied by the blenderfc2-0.3.3 release; the BBCode predates 0.4.0 and lists a new material as impossible

## Checks

## Release order

- after jackall-1.7.0

## Collisions
