# Sky Overhaul

An FCSE plugin for the sun as something the eye cannot look at: a glare that washes over the finished
frame as the player looks toward the sun, and a retinal afterimage left behind when they look away.

**The layer ships two data fragments per campaign world.** The world descriptor's `<Environment>`
halves the moon's size and points the storm's fog at the clear preset, and is otherwise retail. The
preset library in `managers.fcb` changes only the night in the clear, jungle, storm and cloudy
lighting: a dimmer blue-grey ambient, no rim light, and a brighter moon that lights the ground only
once it has risen, so moonlit ground has shading and shadows. It replaces the whole library, so it
collides with any other mod that edits a preset. Nothing here changes how the sun itself looks.

## What it does

- **The sun's direction**, read out of the renderer's scene state by hooking the cloud-layer
  submission, which receives a pointer into it at every hour. `src/engine/cloud_layer.cpp`.
- **The angle to the sun**, from that direction and the view-projection matrix read back off the
  Direct3D device at the sky pass.
- **Cover**, from a hardware occlusion query: a patch drawn where the sun is with depth testing on
  and colour writes off, through a shader that discards its pixels as far as our clouds cover them,
  counted against the same patch with depth testing off so the multisample factor cancels.
  `src/engine/sun_occlusion.cpp`, and `CoverPS` in `src/shaders/clouds.fx`.
- **The glare**, a wash that brightens toward the sun, raises contrast and drains colour, drawn over
  the world's composite so it lands under the heads-up display. It weakens with a low sun, at
  night, and with anything standing between the player and the sun, our clouds included.
- **The afterimage.** While the eye is dazzled the view accumulates into a burn texture and the light
  that fell on it accumulates into a bleach mask. Looking away brings up a dark tinted core where the
  sun's image sat, a faint desaturated negative of the whole view, and a haze over everything else.
  It arms only after a held stare, deepens with how long that stare lasted, and fades over twice as
  long as it took to build.
- **Light shafts through our clouds.** The engine still submits its clouds to the god-ray mask pass,
  and that one draw is replaced by our clouds' cover, blended as the engine's own, so the rays stop
  where our clouds stand. `MaskPS` in `src/shaders/clouds.fx`, and `src/engine/dome_draw.cpp`.
- **The world at night as the eye sees it.** Once the sun is well down, the world's colour drains
  where it is dark toward the blue-grey of rod vision, reds going dark first, while fires, lamps
  and headlights keep theirs. A high, uncovered moon lets it keep more; a storm takes that away.
  Drawn at the sky pass through a quad at the far plane that passes the depth test only where the
  world drew, so the sky is never touched. Optional faint grain in the darkest parts.
  `src/night.cpp` and `src/shaders/night.fx`.
- **The moon without the engine's fog.** The engine fogs the moon's sprite by its height, which hid
  all but a high moon in the night's near-black fog. Its one draw is recognised in the sky pass and
  drawn with the fog amount at zero. `src/engine/dome_draw.cpp`.
- **Cloud shadows.** Ground under our clouds loses sunlight as they drift over it. Each patch of
  ground marches up through the cloud layer toward the sun, through the same density the clouds are
  drawn from, so a shadow is where its cloud is and moves with it. Needs Clouds on Overhaul. Worked
  out at half resolution from the engine's own linear depth, a texture its water and soft particles
  read, which is found by recognising those draws' shaders. Blurred along surfaces and multiplied
  into the world at the sky pass, never nearer than a metre, so the player's weapon stays clean.
  `src/shadows.cpp`, `ShadowPS` in `src/shaders/clouds.fx`, `src/engine/depth_texture.cpp`.
- **The colour grade.** The final pass's saturation, per-channel powers and contrast curve drawn with
  values of our own, recognised by its shader and put back after its draw. Replaces the weather
  preset's grade in every weather. `src/grade.cpp`.
- **Staying out of menus.** The cloud-layer hook reports whether the engine drew a world at all
  this frame, at every hour of the night too; a frame without one gets nothing. No guessing at game
  state.
- **Switching each part.** The mod menu, and the `[SkyOverhaul]` group in `fcse.ini`, holds only
  which parts are on: Sky (Engine or Overhaul), Clouds (Engine, Off or Overhaul), Sun (Engine, or
  Overhaul for the glare and the afterimage), Night, Shadows and Grade (each Engine or Overhaul).
  The last two start on Engine.
- **Tuning.** Every effect's values are kept in `bin\sky-overhaul.ini`, beside `fcse.ini`, which is
  written on the first launch. The sky has none: it follows the sun alone. `src/tuning.cpp`.
- **A window in DevTools' overlay.** With [DevTools](../DevTools) installed, Home shows a Sky Overhaul
  window that edits that file in Sun, Clouds, Night, Shadows and Grade tabs of sliders,
  saving each edit when it is let go.
  Its Sky tab holds a tab per key moment of the sun's day - Night, Dawn, Sunrise, Morning, Noon,
  Afternoon, Sunset and Dusk - and picking one sets the game's clock to its hour, from which the day
  runs on. Without DevTools, `fcse.log` says so once and the file is the only way in.

## How it is put together

Three seams, on two threads. The cloud-layer submission runs on the game thread and only publishes,
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

`DepthPassQuality` high or above for the cloud shadows: below it the engine writes no linear depth,
and `fcse.log` says so once.

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

The first configure clones the Dear ImGui that `mods/DevTools/include/devtools_imgui.cmake` pins, so
it needs `git`, a network connection, and `mods/DevTools` beside this folder.

## Background

The sky is documented rather than duplicated here:

- [The sky and cloud system](../../docs/docs/engine-internals/sky-and-clouds.md) — what draws what,
  the scene state, and what a shader is and is not given.
- [Presenting a frame](../../docs/docs/engine-internals/presentation-and-input.md) — why `EndScene`,
  and what is missing by the time a frame is finished.
- [`shadersobj`](../../docs/docs/file-formats/shader-objects.md) and
  [replacing a shader](../../docs/docs/modding/replacing-a-shader.md) — the compiled shaders, which
  this mod no longer touches but which the sky's own effects are made of.
