---
sidebar_position: 19
---

# `Dunia.dll` — Presenting a Frame, and Who Owns the Mouse

:::info[Verified in a running game]
Confirmed against retail Far Cry 2 (GOG v1.03, `fc2_103_retail`). The addresses are Steam v1.03;
see [the overview](./overview.md) for binary identification.
:::

Far Cry 2 draws with Direct3D 9 and reads the player with DirectInput 8. Neither is negotiable from
outside: both are chosen in `Dunia.dll` before any plugin gets a say.

## Direct3D 9, and a second renderer that is not there

`Direct3DCreate9` is a **static import** of `Dunia.dll`, so the D3D9 path is linked in and always
present. `d3d10` is selectable — `GamerProfile.xml`'s `RenderProfile` carries
`Platform="d3d9"` and the quality block has a `customd3d10` sibling — but no D3D10 or DXGI symbol is
imported anywhere in the DLL, so that path is reached, if at all, through code that resolves it
itself.

Two windows exist before the game's own, and neither is the one to draw into:

| Window class | Built by | What it is |
|---|---|---|
| `NomadSplash` | `0x10006130` | The startup splash, a layered window fed a PNG through GDI+. FCSE recolours it. |
| `NomadTool9` | `0x1037c1a0` | A hidden 1×1 window created only to hold a throwaway device. |

`NomadTool9` is the same trick an overlay wants. The engine
`LoadLibraryA("d3d9.dll")`, `GetProcAddress`es `Direct3DCreate9`, creates a device against that 1×1
window, and asks it what the hardware can do. Anyone who needs `IDirect3DDevice9`'s vtable can do
precisely this and release the device again: every device in the process shares d3d9.dll's vtable, so
one device of your own names the functions the game's device will call.

### EndScene is not once a frame

Far Cry 2 renders through offscreen passes and composites them — HDR,
bloom, tone mapping — so `EndScene` is called **several times per frame**, once per pass, and only
some of those have the back buffer bound.

Drawing from an `EndScene` hook therefore draws into whatever pass happens to be ending. In practice
the overlay appears correctly *and* a second time, doubled in size and blended through whatever the
post chain does to that surface, which reads as a multiply. It is absent in menus, because the menu
does not run the same pipeline — which is a good way to misdiagnose it as a UI-layer problem.

`Present` is called once, after all of it. Drawing there, with the back buffer fetched via
`GetBackBuffer` and bound explicitly, is the only placement that lands on the final image exactly
once. Present is outside the engine's scene, so the drawing needs a `BeginScene`/`EndScene` of its
own, and the render target the game had bound has to be put back afterwards.

### What a frame actually looks like from EndScene

:::info[Verified in a running game]
Counted by logging every `EndScene` of selected frames from an FCSE plugin, retail GOG v1.03, at
1280×720 with HDR and bloom on. Frame ordinals matched live frames exactly over a
300-frame span, so the sequence below is one whole frame.
:::

| Order | Calls | Render target | Depth attached |
|---|---|---|---|
| World | 14–16 | offscreen, back-buffer size, `D3DMULTISAMPLE_4_SAMPLES` | yes, same size |
| Bloom and luminance chain | ~16 | offscreen, 320×180 halving to 1×1, `A16B16G16R16F` | no |
| Composite | 1 | **the back buffer** | **no** |
| `Render2DView` (HUD, menus) | 2 | the back buffer | yes, its own surface |

Two offscreen world targets alternate: an `A8R8G8B8` one and, once HDR is on, an
`A16B16G16R16F` one. Both are multisampled and carry the scene's depth.

**The composite is the only back-buffer pass with no depth surface**, and that is the reliable way
to recognise it. `Render2DView` allocates a `DepthStencil` of its own (see
[the sky and cloud system](./sky-and-clouds.md) for where the world's own passes come from), so
"back buffer plus depth" means the interface, not the world. Recognising a pass this way puts an
effect under the HUD rather than over it.

:::warning[The shader constants at EndScene are stale]
Do not try to tell passes apart by what is bound at `c4`/`c8`. Constants persist until something
sets them, so at `EndScene` they hold whatever the pass's last draw left. The interface's passes
report the *world's* projection, identical to the world's own passes, and a menu frame reports
zeroes. Render-target identity, size and depth attachment are the only dependable discriminators.
:::

Only the **last** of the world passes has the whole scene's depth behind it, which matters to
anything issuing an occlusion query. It can be recognised while it is happening, by its viewport:

:::info[Verified in a running game]
The sky pass is the only world pass drawn through a viewport squeezed against the far plane. Its
`MinZ` measures 0.999 to 1.000 where every other pass reports 0. Testing `MinZ >= 0.9` picks it out
of the frame with no false positives over a 300-frame span, and it is the pass the engine draws the
sun in, so the world's opaque depth is complete and the camera transform is the one it drew with.
:::

That has a consequence for anything drawn there. A screen-space quad inherits the pass's depth
range, so its own vertex depth is remapped into the last thousandth before the far plane unless the
viewport is reset to `MinZ = 0`, `MaxZ = 1` for the draw and restored afterwards.

### The world's passes in order

:::info[Verified in a running game]
Retail GOG v1.03 at 1920×1080 with four-sample multisampling, one whole frame recorded from an FCSE
plugin: each pass's target, depth surface and viewport at `EndScene`, and the render states of every
draw inside it. A frame in play held 51 to 64 passes up to the composite.
:::

The world half of the frame runs in this order:

1. **Shadow map cascades**, three pairs of solid then alpha-tested draws into a `NULL`-format
   6144×2048 target with a depth surface of their own.
2. **The water reflection**, at 1024×1024 with its own multisampled depth.
3. **A near-plane depth pass** of about 10 to 17 draws through a viewport of `MinZ` 0 to 0.001,
   which is the first-person weapon. The stencil is cleared here.
4. **The depth prepass**, solid draws then alpha-tested ones, writing depth and the linear depth
   with the stencil off.
5. **The main colour pass for solid geometry**, depth-tested against that depth without writing it.
6. **The foliage colour pass**, every draw with `ZFUNC` equal, so foliage is coloured only where its
   own depth landed. Taking foliage out of the depth prepass would take it out of the frame.
7. **Further colour passes and 12 or so blended draws**, some of them reading the linear depth.
   Every draw in passes 5 to 7 runs with the stencil enabled.
8. **The sky pass.**
9. **About 18 blended draws**, then a stencil clear and a handful of solid draws, most likely the
   weapon's colour.
10. **Bloom, luminance and the composite.**

The world's depth-stencil surface is the same one from the near-plane pass through the sky pass.

### Whether there is a render thread

`CThreadingConfig` has a `RENDER_THREAD` entry, `engine\settings\defaultthreadingconfig.xml` ships
it enabled (`ThreadCnt="1"`), and `Dunia.dll:0x103430A0` reads it and builds a `RenderThread`
(`0x103B20A0`) when it is nonzero.

Measured, `EndScene` nonetheless runs on **the same thread** as the game's own update: a hook on the
sky's submission and a hook on `EndScene` report the same thread id. So on
this build and machine the frame graph is executed inline and a plugin needs no cross-thread
handling for Direct3D. Do not assume that holds everywhere — publish state across the boundary
anyway if it is cheap, since the configuration that separates them plainly exists.

### The scene's depth is gone by the time the frame is

The other half of the same point. By the composite stage — the pass with the back buffer bound, and
`Present` after it — **no depth-stencil surface is attached at all**: `GetDepthStencilSurface`
returns nothing. So anything drawn there is drawn over a flat image. A screen-space effect that wants
to be occluded by the world cannot be depth-tested where it is drawn, and setting `D3DRS_ZENABLE` for
it is silently a no-op rather than an error.

Anything needing the scene's depth has to run while a pass that owns it is still open, which means
one of the world passes in the table above — recognised as an offscreen back-buffer-sized target
with depth attached. An occlusion query issued there reads back a frame or two later, by which time
the composite stage can use the answer without ever having needed the depth buffer itself.

:::warning[The scene's depth cannot be read as a texture either]
Depth testing against it works; sampling it does not. The world passes are multisampled four ways
and their depth surface matches, and Direct3D 9 offers no way to bind a multisampled depth surface
as a texture. The usual escape — creating an `INTZ` depth-format texture and binding it as the
depth-stencil target so it can also be sampled — is defined only for single-sampled surfaces.

So a screen-space effect that needs per-pixel depth, ambient occlusion being the obvious one, cannot
simply read the depth-stencil surface. It has to read the engine's own linear depth instead, below,
or intercept the creation of the depth surface and substitute a single-sampled readable one, which
costs the game its antialiasing. An occlusion query needs none of this, because it asks the hardware
to count against depth rather than to hand it over.
:::

### The engine keeps a readable linear depth of its own

:::info[Verified via reverse engineering]
Read from `Dunia.dll` (Steam v1.03) and the shipped shader objects.
:::

`CSceneRenderer::PrepareFrameGraph` (`0x10347040`) creates a render target named `"Linear depth"`,
an `A8R8G8B8` texture at the scene's size, whenever the renderer config's `+0x4A0` is zero. With
multisampling on it also creates `"LinearDepthMSAA"`, a multisampled surface of the same size and
format, for the depth pass to draw into before the texture is filled from it. `"Linear depth SS"`
belongs to the other renderer, `CSceneRendererD3D10::PrepareFrameGraph` (`0x10356BC0`).

That config value is most likely the depth pass mode, which was not traced to its register call. In
the `DepthPass` group of `engine\settings\defaultrenderconfig.xml`, `high`, `veryhigh` and `ultrahigh`
are `DepthPassMode="full"` with `DepthPassNoPixel="0"`; `low` and `medium` are partial passes with
`DepthPassNoPixel="1"`, which would write no colour.

The texture reaches shaders as `DepthVPSampler`, one of the viewport's global parameters registered
in `0x103788F0`. Seventy-four pixel shaders in the D3D9 `shadersobj` tree bind it, all at `s0`.
The prototype's `depth.inc.fx` gives the packing: view depth along `CameraDirection`, divided by the
view distance, split across red, green and blue as 24 bits. `UncompressDepthWeights` at `c56` turns a
texel back into that fraction, `UncompressDepthWeightsWS` at `c57` into world units, and
`CameraDistances` at `c40` holds near, far, view distance and its inverse.

No seam hands the texture over. It can be found by recognising one of those 74 shaders as it
draws, by the CRC-32 of its bytecode, and reading what is bound at `s0`. Sky Overhaul's
`src/engine/depth_texture.cpp` does that.

:::info[Verified in a running game]
Retail GOG v1.03 at 1920×1080 with four-sample multisampling, read back from an FCSE plugin at the
sky pass.
:::

- **It is complete and in place by the sky pass**, and one texture for the whole session.
- **`c56` holds (1, 1/256, 1/65536)** and `c57` the same times the view distance, 999.9 in the world
  and 1023.75 while loading.
- **Where nothing was drawn it holds white**, which decodes to just past the view distance: 1003.8 m.
- **The first-person weapon leaves it at zero.**

### How far neighbouring texels can be trusted

:::info[Verified in a running game]
Retail GOG v1.03 at 1920×1080 with four-sample multisampling, one column of the texture read back
from an FCSE plugin at the sky pass, looking down at open ground.
:::

The prototype's `CompressDepthValue` hands each byte over as a multiple of 1/256, and the channel
stores 255ths, so every byte is rounded to the nearest 255th on the way in.

- **The middle byte holds 127 across two of its steps**, halfway through every step of the top byte:
  at 1.95 m, 5.86 m, 9.77 m and so on. The lowest byte wraps underneath it, so the depth there reads
  1.5 cm nearer than the texel before. At 5.87 m, on rows stepping 1.24 cm apart, three rows read
  (1, 127, 100), (1, 127, 53) and (1, 128, 6): 5.8724, 5.8696 and 5.8821 m.
- **Past each step of the top byte the depth reads a further 1.5 cm long**, since `c56` weighs the
  bytes as 256ths: 38 cm too far by 100 m. This follows from the packing; one step read back at
  3.93 m jumped 2.4 cm from the row before it.
- **Lone texels read about a centimetre off the surface around them.** On rows stepping 1.24 cm
  apart, one step was 2.44 cm and the next 0.14 cm. They are the storage too: the same frame held
  in half floats has none, below.
- **Texels half a top-byte step out are rare.** A repair for them changed 8 texels in a column read
  back every two seconds for a minute. Averaging the bytes of multisamples that lie either side of a
  step would make those, so the resolve does not do it often.

:::warning[A surface normal taken from neighbouring texels breaks on all of these]
A 1.5 cm step is inside the tolerance any depth comparison already allows. A normal is not, because
it is taken from depth differences one pixel apart. Sky Overhaul built ambient occlusion on this
texture and removed it again. In game:

- Taking each axis from the neighbour nearer in depth, the usual guard against silhouettes, picks
  the neighbour across a backward step wherever rows lie more than 0.77 cm apart. It drew a faint
  dark line across the ground at 5.87 m that turned with the camera.
- Taking each axis from the side whose next two texels run straight on through the pixel removed
  the lines, and left dark dots at the lone texels that ran along the terrain as the camera moved.
- Taking each texel as the median of the three by three around it first was the last thing tried,
  and what it did was not recorded.
:::

### Storing it in half floats

:::info[Verified in a running game]
Retail GOG v1.03 at 1920×1080 with four-sample multisampling. The format was switched from an FCSE
plugin while the player stood still, so both formats were read back over the same frame content.
:::

`CSceneRenderer::PrepareFrameGraph` passes each target's format as a raw `D3DFORMAT` pushed onto the
stack, `0x15` for both `"Linear depth"` (Steam `0x10347659`) and `"LinearDepthMSAA"` (Steam
`0x10347b8d`). Writing `0x71`, `D3DFMT_A16B16G16R16F`, over both bytes stores the same packing
exactly. The two have to change together, since the resolve needs them to agree.

- **The renderer takes it.** The targets are requested by name and format every frame, so the change
  applies on the next frame, no restart. Water, soft particles, fire and Sky Overhaul's cloud shadows
  all read the texture and looked unchanged.
- **Every artefact above goes.** Over 3.5 to 8 m of ground, the 8-bit texture had three backward
  steps of about 15 mm and four lone texels 8 to 12 mm off. The half-float one had none of them.
- **What remains is the ground.** Deviations of 3 to 20 mm sit on the same rows with the same size
  in both formats, so they are relief the renderer really drew.
- **The 8-bit decode reads short** by the same weighting error: 3.2 cm at 8.3 m.
- **Normals from it are clean.** On bare ground, 98% of texels turn by less than 2° from one row to
  the next at a one-pixel baseline, and the count grows with a wider baseline, which is what real
  bumps do and noise does not.

Half floats cost twice the memory of both targets: about 41 MB more at 1080p with four samples.

### Cut-out materials

:::info[Verified via reverse engineering]
Read from `Dunia.dll` (Steam v1.03).
:::

Around `0x1040E9CC` the renderer sets up a cut-out material. With alpha-to-coverage it goes through
the drivers' own switches, `D3DRS_ADAPTIVETESS_Y` set to `'ATOC'` on NVIDIA and `D3DRS_POINTSIZE` set
to `'A2M1'` on AMD, and back to zero or `'A2M0'` for everything else. Either way it then sets
`D3DRS_ALPHATESTENABLE`, followed by the material's `D3DRS_ALPHAREF`.

:::info[Verified in a running game]
Retail GOG v1.03 at 1920×1080 with four-sample multisampling, standing in a grass field, counted
from an FCSE plugin that stencil-marked draws and counted the marked samples at the sky pass.
:::

Neither state names the grass. Draws with alpha-to-coverage on covered about 5% of the frame's
samples, the detailed blades nearest the camera. Draws with alpha test on covered about 7%. The grass
carpet that fills the rest of the field is instanced: draws with `D3DSTREAMSOURCE_INDEXEDDATA` set
on stream 0 covered 25 to 45%. Two-sided draws covered 30 to 50%, and blended or depth-equal ones
none.

The engine's `.rs` render-state files use only the stencil's top bit. Marking draws with `0x40`
collides with nothing, and the marks are still there at the sky pass.

### Marking foliage in the stencil

:::info[Verified in a running game]
Retail GOG v1.03 at 1920×1080 with four-sample multisampling, marks drawn from an FCSE plugin and
shown by drawing through the stencil at the sky pass.
:::

- **Mark in the depth prepass.** Every draw in the colour passes already runs with the stencil
  enabled, so a mark that waits for draws with the stencil off never happens there. The depth prepass
  draws with it off, and nothing clears the stencil between it and the sky pass.
- **Clear the bit on every other depth-prepass draw.** Only foliage marks, but a solid draw in front
  of foliage has to take the mark away again.
- **Restrict it to the world's depth surface.** The shadow cascades and the water reflection write
  depth too, into surfaces of their own.
- **Put back every stencil state changed.** The engine filters redundant state, so a stencil function
  left behind is what its next stencilled draw runs under.
- **Neither render state names foliage.** Marking instanced and alpha-tested draws also marks the
  rocks the collection system scatters, cut-out road signs, and tree trunks in the distance.
- **Its vertex shader does.** The grass material's shaders are the only ones that bind the wind and
  mesh decompression without a world matrix, and the leaf shaders bind the leaf morph or
  level-of-detail constants. Matching the bound vertex shader's CRC-32 against those objects marks
  grass and leaves and nothing else. See
  [shader objects](../file-formats/shader-objects.md#finding-an-object-by-what-it-binds).

A shader cannot read the stencil. To use the marks as a texture, draw white through them into a
render target at the world's multisampling, which can share the world's depth-stencil surface, and
`StretchRect` it into a plain texture. What arrives is each pixel's share of foliage samples.

### Ambient occlusion on this frame

:::info[Verified in a running game]
Retail GOG v1.03 at 1920×1080 with four-sample multisampling, from an FCSE plugin that was built,
tested and then removed from Sky Overhaul.
:::

Ground-truth ambient occlusion works on the half-float linear depth. It ran at half resolution,
with 2 slices and 6 steps per side and a fixed 4×4 pattern cancelled by a depth-aware blur. It was
drawn at the sky pass, so most blended draws and the weapon come after it. Flat ground stayed
white, corners darkened, and nothing moved with the camera.

- **Foliage has to be left out twice.** As a receiver it shades every blade. As an occluder it
  darkens the ground behind and between the grass.
- **A pixel only partly covered by a blade counts as foliage.** Its resolved depth lies between the
  blade and the ground, which the occlusion reads as a small occluder floating in front. Those move
  with the wind, and the ground behind grass flickered until any coverage at all was excluded.
- **The ambient term is separable only in source.** The prototype's `aaa.fx` adds a hemisphere
  ambient after the direct light has been shadowed, so one multiply there would touch nothing else.
  Retail ships compiled shaders, so a plugin multiplies the finished colour instead.
- **Ubisoft's own attempt came later.** The Far Cry 3 Blood Dragon debug package in
  `tools/third-party` carries a port of NVIDIA's HBAO as a Direct3D 11 compute shader over a linear
  depth texture. Its Direct3D 9 blur variant samples an encoded depth through the same
  `DepthVPSampler` state.

It was not taken further, in favour of other work.

:::danger[Hooking one of the engine's own sky functions is not a way in]
The obvious move — detour the sun's draw and issue the query from inside it — does not work, and
fails silently rather than loudly. Those functions **submit packets and return**; the Direct3D calls
happen later, when the frame graph is executed. A hook there runs before any of the frame's world
passes, against whatever target and depth buffer the *previous* work left bound, so a query issued
from it counts nothing and a draw lands somewhere invisible. See
[the sky and cloud system](./sky-and-clouds.md#the-sky-draws-nothing-it-submits-packets).
:::

## Drawing into the world's frame

What the device does to anything drawn into a pass, all of it measured with the sun-glare effect in
`mods/sky-overhaul`.

### The device is reset, not recreated

`Dunia.dll:0x104225E0` calls `TestCooperativeLevel` and classifies `D3DERR_DEVICELOST` and
`D3DERR_DEVICENOTRESET` separately, so an alt-tab or a resolution change goes through
`IDirect3DDevice9::Reset` and the device pointer survives. Any `D3DPOOL_DEFAULT` resource still
outstanding makes that `Reset` fail, and a failed reset is the game breaking rather than the plugin
misbehaving, so a plugin holding a render target must release it first.

The engine brackets its own reset with a teardown and a restore, each `__thiscall` taking only
`this`. Detouring the teardown is the seam that lets a plugin let go in time.

| Routine | GOG / retail v1.03 | Steam / Ubisoft Connect v1.03 |
|---|---|---|
| Device teardown, before `Reset` | `0x104168A0` | `0x104246E0` |
| Device restore, after `Reset` | `0x10416F50` | `0x10424D90` |

Detecting the change afterwards and rebuilding is not an alternative. The outstanding resource is
exactly what stops the reset from happening, so there is nothing to detect.

### An occlusion query counts samples, not pixels

:::info[Verified in a running game]
A 32×32 patch drawn into a world pass returns 4096, not 1024 — four samples per pixel, matching the
`D3DMULTISAMPLE_4_SAMPLES` in the frame table. Readings of `0/4096` behind a wall, `4096/4096` in
the open and fractional values through foliage confirm the counting works as expected once the
patch is drawn in the right pass.
:::

The multiplier is a property of whichever surface the query ran against, and nothing publishes it.
Issuing a second query over the same patch with depth testing off gives the total, and the ratio of
the two cancels the multiplier whatever it is. That also survives a build or a preset that
multisamples differently.

### Eight-bit targets cannot accumulate small weights

The world composites at back-buffer format, so a plugin's own accumulation targets usually are too.
Blending a frame in at weight `w` moves the destination by `w` times the difference, and below about
one part in 255 that product rounds to zero and the target never moves at all.

This is not a corner case here. Measured frame rates reach roughly 490 per second at 1280×720 facing
open sky, so a running mean over a five-second window has a per-frame weight near 0.0004 and stalls
completely: the target keeps whatever the first frame wrote. The symptom is an accumulation that
looks correct for a fraction of a second and then freezes, with nothing in any log. Either use a
blend that does not depend on small increments, such as `D3DBLENDOP_MAX`, or give the target a
format with room to accumulate.

### What a mid-frame draw has to put back

A draw at `Present` is after everything and can be careless. A draw inside a pass the engine still
owns cannot, because the engine's redundant-state filtering assumes nothing else touched the device.
The full list one screen-space quad disturbs is in `mods/sky-overhaul/src/engine/screen_draw.cpp`.
Two entries on it are not obvious:

- **Stream zero.** `DrawPrimitiveUP` leaves it unbound. An engine that filters redundant
  `SetStreamSource` calls then re-binds nothing and draws nothing for the rest of the frame.
- **The vertex declaration, not the vertex format.** With a declaration bound, `GetFVF` returns 0,
  so restoring the format alone restores nothing.

Shader constant registers are the asymmetric case. A plugin that reads the camera out of `c4`, `c8`
or `c46` during a world pass — see [what a shader is given](./sky-and-clouds.md#what-a-shader-is-given-and-what-it-is-not)
for the full map — depends on state that a second plugin drawing into the same frame could
legitimately overwrite. Nothing in the loader arbitrates device state; it arbitrates addresses only.

### Getting the camera out of a pass, and the wrong way to do it

A screen-space effect that reaches into the world needs where the camera is and where each pixel
looks. Both are in the constants the viewport provider leaves bound, and all of them are live in
the sky pass: `CameraPosition` at `c45`, `CameraDirection` at `c46`, `CameraRight` at `c53`,
`CameraUp` at `c54`, and the projection at `c8`, whose first two diagonal entries are how far off
centre the frustum's edges sit. The direction through a corner of the viewport is then

```
corner = direction + right * (±1 / projection[0][0]) + up * (±1 / projection[1][1])
```

:::warning[Inverting the view-projection is accurate enough for the position and not for the rays]
The obvious alternative is to invert `ViewProjectionMatrix` at `c4` and unproject the corners.
Measured against the registers above in a running game, that recovers the camera **position** to
within 1.5 mm — but the **directions** only to within about 0.01 in unit-vector terms, which is
half a degree, and half a degree is 35 m of error at a kilometre.

The reason is the subtraction it ends with. A corner's direction comes out as a far-plane point
minus a near-plane point, both unprojected from a transform built around world coordinates in the
thousands, and the near plane here is 0.1 m away. The error is small against the world's scale and
large against the tenth of a metre being recovered.

It reads in game as a rigid layer stepping around a fixed position whenever the view moves, and
holding still the moment it stops — not as blur or noise, which is what makes it easy to
misdiagnose as aliasing.
:::

### The sky pass's state

Measured from a plugin logging its own draws, retail GOG v1.03 at 1280×720:

- The world's colour target **alternates between `A16B16G16R16F` and `A8R8G8B8`** frame to frame,
  both `D3DMULTISAMPLE_4_SAMPLES`, with a `D24S8` depth surface multisampled to match.
- **A frame can hold more than one pass with the sky's viewport.** Usually one, but two while the
  menu is open. Anything that blends has to draw into one of them only, or it composites over
  itself.
- **Depth bias, multisample mask, sRGB write, stencil and both alpha-to-coverage tokens are all at
  their neutral values** in that pass, so a blended draw inherits nothing that would spoil it.
- The **far plane is about 1000 m**, and the world's distance fog is fully saturated by 420 m
  (`FogValues.x` is 1/400). An effect further out than that cannot use the distance fog the terrain
  uses; the sky's own fog is a function of height and heading instead.
- Drawing at depth **exactly 1.0 with `D3DCMP_LESSEQUAL`** occludes correctly at any distance,
  because the sky writes no depth and the cleared far value is what remains wherever the world drew
  nothing. A value merely close to one stops occluding roughly `near / (1 - z)` metres out.

### Bringing a shader of your own

A plugin's own pixel shader needs no D3DX and no DirectX SDK. `fxc.exe` ships with the Windows SDK
the MSVC toolchain already requires, and a developer prompt puts it on `PATH`, so the shader can be
compiled at build time straight into a header the plugin embeds and hands to `CreatePixelShader`.
The generated array is bytes; `CreatePixelShader` wants whole tokens, so it has to be copied into an
aligned buffer on the way through.

Target `ps_2_0` unless the instruction count forces `ps_2_b`. Both pair with the fixed-function
vertex pipeline and a `D3DFVF_XYZRHW | D3DFVF_TEX1` quad, which is the documented Direct3D 9
post-process pairing and needs no vertex shader at all.

`ps_3_0` is the second working shape, and worth the machinery once an effect needs loops, volume
textures or screen-space derivatives. It requires a matching `vs_3_0`, which in turn requires a
vertex declaration rather than an FVF, and a quad in clip space rather than a pretransformed one.
That pairing is what carries a per-corner direction into the pixel shader: put the ray in the
vertex, give every corner `w = 1` so the interpolation stays linear, and take **no** half-pixel
offset — that rule is for reading a texture by screen position, and a direction belonging at the
viewport's edge is already where a rasteriser interpolating to a pixel centre expects it.

### Which device vtable slots the repo's plugins hold

`IDirect3DDevice9`'s vtable is shared by every device in the process, so a throwaway device created
at load is enough to read a slot from. No `Dunia.dll` address is involved and none of it is
build-specific.

| Slot | Function | Held by |
|---|---|---|
| 16 | `Reset` | DevTools |
| 17 | `Present` | DevTools |
| 42 | `EndScene` | Sky Overhaul |
| 82 | `DrawIndexedPrimitive` | Sky Overhaul |
| 94 | `SetVertexShaderConstantF` | Sky Overhaul |
| 109 | `SetPixelShaderConstantF` | Sky Overhaul |

Slot 82 is where the sky dome is recognised, by the signature in
[the sky and cloud system](./sky-and-clouds.md#the-domes-draw-call). The two constant setters see
every upload of the fog colour at `c49`–`c50`.

FCSE gives an address to one plugin and does not chain detours, so these six are the whole
contention surface. Everything else on that vtable is free, including `TestCooperativeLevel`, which
the engine calls before it resets and which is therefore a workable third release seam.

## DirectInput 8, and the mouse messages that never arrive

Input comes up in `0x102afff0`, which calls `DirectInput8Create` with the IID at `0x10eda224` —
`{BF798030-483A-4DA2-AA99-5D64ED369700}`, which is **`IID_IDirectInput8A`**. The ANSI interface,
matching the rest of the engine's Win32 use (`RegisterClassExA`, `CreateWindowExA`,
`PeekMessageA`). Only the ANSI device vtable is ever created, so only `IDirectInputDevice8A` needs
attention.

The mouse is held **exclusively**, and that has a consequence nothing in the API announces:

:::info[Verified in a running game]
Windows delivers **no mouse button or movement messages** to a window whose mouse is held
exclusively through DirectInput. `WM_LBUTTONDOWN` and `WM_MOUSEMOVE` simply never arrive, so an
overlay subclassing the window sees a pointer that hovers — `GetCursorPos` still tracks, so
position-driven behaviour works — and a mouse that can never click. Buttons have to be read
directly, from `GetAsyncKeyState` or from the DirectInput data itself.

What *does* still arrive: `WM_KEYDOWN`, `WM_CHAR` and, unexpectedly, `WM_MOUSEWHEEL`. The keyboard is
not suppressed the way the mouse is, and the wheel follows keyboard focus rather than the mouse
capture. So a subclassed window is enough for typing, hotkeys and scrolling, and short by exactly the
buttons and the movement.
:::

Taking input away from the game means answering both halves. Window messages stop at a subclassed
`WndProc`. Everything that moves the player comes through `IDirectInputDevice8A::GetDeviceState`
(vtable slot 9) and `GetDeviceData` (slot 10) instead, where a message hook has no reach — those are
answered with an all-zero state, which the engine reads as an idle device. Failing them instead is
wrong: a refused read looks like a lost device and sends the engine off to reacquire it. A buffered
`GetDeviceData` still has to be drained and then reported as empty, or the events queue up and all
arrive at once when the overlay closes.

## What this is enough to build

DevTools' overlay is exactly the above and nothing else: a throwaway device for the vtable, `Present`
and `Reset` detoured through FCSE's own hook API, the back buffer bound explicitly for the draw, the
window subclassed, and the two DirectInput reads answered while it has the input. No address in
`Dunia.dll` is involved, so none of it is build-specific — see `mods/DevTools/src/engine/` and
`mods/DevTools/src/overlay/`.

Sky Overhaul is the other shape: an effect that belongs *inside* the world's frame rather than over
the finished one. It follows `EndScene`, classifies each pass, measures the sun against the scene's
depth during the sky pass, and paints over the composite so the result lands under the interface.
`mods/sky-overhaul/src/engine/` holds those three concerns — following the frame, guarding device
state, and letting go before a reset — in modules that know nothing about the effect itself.

## Unknowns

- Whether the `d3d10` platform is reachable in retail at all, and what resolves it if so. No D3D10 or
  DXGI import exists, and nothing has been traced loading one.
- Whether the game ever presents through a swap chain rather than `IDirect3DDevice9::Present`. The
  device-level hook fires, so if a second path exists it is not the one in use.
- Which cooperative-level flags the mouse is actually acquired with. The behaviour observed is
  exclusive, but the `SetCooperativeLevel` call itself has not been read.
- Whether substituting a single-sampled `INTZ` depth surface at creation actually yields readable
  scene depth here. The multisampling that rules out the direct route is measured; the substitution
  is untried, and it would also have to force the colour targets to match.
- Whether water surfaces write `"Linear depth"`, and whether the resolve is the engine's own
  `StretchRect` or a shader of its own.
- Whether the colour passes' stencil write mask covers bit `0x40`. Marks made in the depth prepass
  survive to the sky pass, so it does not clear them, but it has not been read.
- What the dozen blended draws before the sky pass are.
- Where the engine stores the result of its own flare visibility query. The readback wrapper is
  known; the functions around it are undefined code in the Ghidra project.
