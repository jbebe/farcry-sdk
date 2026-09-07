---
sidebar_position: 19
---

# `Dunia.dll` — Presenting a Frame, and Who Owns the Mouse

:::info[Verified in a running game]
Established while building DevTools' overlay and confirmed against retail Far Cry 2 (GOG v1.03,
`fc2_103_retail`) on 2026-09-05. The addresses are Steam v1.03; see [the overview](./overview.md) for
binary identification. What follows is what anyone drawing over this game, or taking input from it,
runs into first.
:::

Far Cry 2 draws with Direct3D 9 and reads the player with DirectInput 8. Neither is negotiable from
outside: both are chosen in `Dunia.dll` before any plugin gets a say. This page is the shape of both,
because an overlay that ignores either produces exactly the artefacts described below.

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

`NomadTool9` is worth knowing about because it is the same trick an overlay wants. The engine
`LoadLibraryA("d3d9.dll")`, `GetProcAddress`es `Direct3DCreate9`, creates a device against that 1×1
window, and asks it what the hardware can do. Anyone who needs `IDirect3DDevice9`'s vtable can do
precisely this and release the device again: every device in the process shares d3d9.dll's vtable, so
one device of your own names the functions the game's device will call.

### EndScene is not once a frame

The load-bearing detail. Far Cry 2 renders through offscreen passes and composites them — HDR,
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
Counted by logging every `EndScene` of selected frames from an FCSE plugin, retail GOG v1.03 on
2026-09-06, at 1280×720 with HDR and bloom on. Frame ordinals matched live frames exactly over a
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

### Whether there is a render thread

`CThreadingConfig` has a `RENDER_THREAD` entry, `engine\settings\defaultthreadingconfig.xml` ships
it enabled (`ThreadCnt="1"`), and `Dunia.dll:0x103430A0` reads it and builds a `RenderThread`
(`0x103B20A0`) when it is nonzero.

Measured, `EndScene` nonetheless runs on **the same thread** as the game's own update: a hook on the
sky's submission and a hook on `EndScene` report the same thread id, twice over on two runs. So on
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
simply read what the engine already has. It has to intercept the creation of the depth surface and
substitute a single-sampled readable one, which costs the game its antialiasing. An occlusion query
needs none of this, because it asks the hardware to count against depth rather than to hand it over.
:::

:::danger[Hooking one of the engine's own sky functions is not a way in]
The obvious move — detour the sun's draw and issue the query from inside it — does not work, and
fails silently rather than loudly. Those functions **submit packets and return**; the Direct3D calls
happen later, when the frame graph is executed. A hook there runs before any of the frame's world
passes, against whatever target and depth buffer the *previous* work left bound, so a query issued
from it counts nothing and a draw lands somewhere invisible. See
[the sky and cloud system](./sky-and-clouds.md#the-sky-draws-nothing-it-submits-packets).
:::

## Drawing into the world's frame

Everything above describes where a pass is. This describes what the device does to anything drawn
into one, all of it measured while building the sun-glare effect in `mods/sky-overhaul`.

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

### Bringing a shader of your own

A plugin's own pixel shader needs no D3DX and no DirectX SDK. `fxc.exe` ships with the Windows SDK
the MSVC toolchain already requires, and a developer prompt puts it on `PATH`, so the shader can be
compiled at build time straight into a header the plugin embeds and hands to `CreatePixelShader`.
The generated array is bytes; `CreatePixelShader` wants whole tokens, so it has to be copied into an
aligned buffer on the way through.

Target `ps_2_0` unless the instruction count forces `ps_2_b`. Both pair with the fixed-function
vertex pipeline and a `D3DFVF_XYZRHW | D3DFVF_TEX1` quad, which is the documented Direct3D 9
post-process pairing and needs no vertex shader at all. `ps_3_0` does not: it requires a matching
`vs_3_0` and a vertex declaration, which is a great deal of machinery for a full-screen quad.

### Which device vtable slots the repo's plugins hold

`IDirect3DDevice9`'s vtable is shared by every device in the process, so a throwaway device created
at load is enough to read a slot from. No `Dunia.dll` address is involved and none of it is
build-specific.

| Slot | Function | Held by |
|---|---|---|
| 16 | `Reset` | DevTools |
| 17 | `Present` | DevTools |
| 42 | `EndScene` | Sky Overhaul |

FCSE gives an address to one plugin and does not chain detours, so these three are the whole
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
- Where the engine stores the result of its own flare visibility query. The readback wrapper is
  known; the functions around it are undefined code in the Ghidra project.
