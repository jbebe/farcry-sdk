# Sky Overhaul

An FCSE plugin for the sun's glare: a wash over the finished frame that brightens as the player looks
toward the sun and fades as they look away.

**Nothing here is finished, and the mod ships no game files.** The layer is empty by design — the
sun, the sky textures and every world's `<Sky>` data are the engine's own. What is in the tree is a
working skeleton: the plugin builds, installs and runs, and the one behaviour it does not yet get
right is called out below.

## What works

- **The sun's direction**, read out of the renderer's scene state by hooking the sun-disc draw, which
  receives a pointer into it. `src/engine/sky_state.cpp`.
- **The angle to the sun**, from that direction and the view-projection matrix read back off the
  Direct3D device.
- **The glare itself**, a gradient fan centred on the sun's projected screen position, drawn
  additively from an `EndScene` hook. `src/veil.cpp`.
- **Staying out of menus.** The sun-disc hook reports whether the engine drew a sky at all this
  frame; a frame without one gets nothing. No guessing at game state.
- **Live tuning.** Strength and spread are FCSE settings, so they are sliders in the mod menu rather
  than a rebuild.

## What does not

**Cover.** The glare is not yet blocked by walls. `src/engine/sun_occlusion.cpp` measures the sun
with a hardware occlusion query during the sky pass, and the measurement comes back empty; the counts
it reads are logged each second as `veil: cos ... query R/T visible ...`, which is where to start.
The two things already ruled out are worth not repeating:

- The `SunOcclusionFactor` global is not this value. It scales water specular and reads 1.0
  throughout play.
- The glare cannot simply be depth-tested where it is drawn. No depth-stencil is attached that late
  in the frame.

Both are written up in [the sky and cloud system](../../docs/docs/engine-internals/sky-and-clouds.md)
and [presenting a frame](../../docs/docs/engine-internals/presentation-and-input.md).

## Requirements

Bloom on, in Options → Video or on the `<quality>` row in
`Documents\My Games\Far Cry 2\GamerProfile.xml` matching the profile's `Quality`. The glare works
without it, but the sun it sits beside is a flat disc until bloom runs.

`Present` is hooked by DevTools, and FCSE gives an address to one plugin only, which is why this draws
from `EndScene` instead. The two coexist.

## Building

```
.\build.ps1
python ..\..\scripts\verify_patterns.py
jackall-cli mod build --game "C:\Games\Far Cry 2" --layer mods\sky-overhaul\layer
```

The layer carries the built DLL under `layer\plugins\`, which `mod build` syncs into `bin\plugins`.
`build.ps1 -Install "<game>\bin"` copies it directly instead, for a faster loop.

## Background

The sky is documented rather than duplicated here:

- [The sky and cloud system](../../docs/docs/engine-internals/sky-and-clouds.md) — what draws what,
  the scene state, and what a shader is and is not given.
- [Presenting a frame](../../docs/docs/engine-internals/presentation-and-input.md) — why `EndScene`,
  and what is missing by the time a frame is finished.
- [`shadersobj`](../../docs/docs/file-formats/shader-objects.md) and
  [replacing a shader](../../docs/docs/modding/replacing-a-shader.md) — the compiled shaders, which
  this mod no longer touches but which the sky's own effects are made of.
