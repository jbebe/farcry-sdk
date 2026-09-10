# Sky Overhaul

An FCSE plugin for the sun as something the eye cannot look at: a glare that washes over the finished
frame as the player looks toward the sun, and a retinal afterimage left behind when they look away.

**The layer ships one data fragment per campaign world.** The world descriptor's `<Environment>`
halves the moon's size and is otherwise retail. Nothing here changes how the sun itself looks.

## What it does

- **The sun's direction**, read out of the renderer's scene state by hooking the sun-disc
  submission, which receives a pointer into it. `src/engine/sky_state.cpp`.
- **The angle to the sun**, from that direction and the view-projection matrix read back off the
  Direct3D device at the sky pass.
- **Cover**, from a hardware occlusion query: a patch drawn where the sun is with depth testing on
  and colour writes off, counted against the same patch with depth testing off so the multisample
  factor cancels. `src/engine/sun_occlusion.cpp`.
- **The glare**, a wash that brightens toward the sun, raises contrast and drains colour, drawn over
  the world's composite so it lands under the heads-up display. It weakens with a low sun, at
  night, and with anything standing between the player and the sun.
- **The afterimage.** While the eye is dazzled the view accumulates into a burn texture and the light
  that fell on it accumulates into a bleach mask. Looking away brings up a dark tinted core where the
  sun's image sat, a faint desaturated negative of the whole view, and a haze over everything else.
  It arms only after a held stare, deepens with how long that stare lasted, and fades over twice as
  long as it took to build.
- **Staying out of menus.** The cloud-layer hook reports whether the engine drew a world at all
  this frame, at every hour of the night too; a frame without one gets nothing. No guessing at game
  state.
- **Live tuning.** Fourteen sliders in the mod menu, from glare strength through to the afterimage's
  colour, so tuning never needs a rebuild.

## How it is put together

Three seams, on two threads. The sun-disc submission runs on the game thread and only publishes,
through a seqlock; every Direct3D call is made from `EndScene` on the render thread; and the engine's
own device teardown is hooked so the plugin surrenders what it holds before a reset. It refuses to
install unless it has all three, because a plugin holding a render target through a `Reset` breaks
the reset itself.

`src/engine/` is the part that knows about the engine: following the frame and classifying its
passes, publishing the sun, measuring cover, and putting back every piece of device state a
screen-space draw disturbs. `src/dazzle.cpp` and `src/shaders/dazzle.fx` are the effect, and know
about none of it.

## Requirements

Bloom on, in Options → Video or on the `<quality>` row in
`Documents\My Games\Far Cry 2\GamerProfile.xml` matching the profile's `Quality`. The glare works
without it, but the sun it sits beside is a flat disc until bloom runs.

`Present` is hooked by DevTools, and FCSE gives an address to one plugin only, which is why this
draws from `EndScene` instead. The two coexist.

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
