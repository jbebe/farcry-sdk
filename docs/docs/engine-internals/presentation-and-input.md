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

### The scene's depth is gone by the time the frame is

The other half of the same point. By the composite stage — the pass with the back buffer bound, and
`Present` after it — **no depth-stencil surface is attached at all**: `GetDepthStencilSurface`
returns nothing. So anything drawn there is drawn over a flat image. A screen-space effect that wants
to be occluded by the world cannot be depth-tested where it is drawn, and setting `D3DRS_ZENABLE` for
it is silently a no-op rather than an error.

Anything needing the scene's depth has to run while a pass that owns it is still open. Hooking one of
the engine's own draws is the way in, and the sky's draws are a convenient place to stand for
anything sun-related — see [the sky and cloud system](./sky-and-clouds.md). An occlusion query issued
there reads back a few frames later, by which time the composite stage can use the answer without
ever having needed the depth buffer itself.

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

## Unknowns

- Whether the `d3d10` platform is reachable in retail at all, and what resolves it if so. No D3D10 or
  DXGI import exists, and nothing has been traced loading one.
- Whether the game ever presents through a swap chain rather than `IDirect3DDevice9::Present`. The
  device-level hook fires, so if a second path exists it is not the one in use.
- Which cooperative-level flags the mouse is actually acquired with. The behaviour observed is
  exclusive, but the `SetCooperativeLevel` call itself has not been read.
