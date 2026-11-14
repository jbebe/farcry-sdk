# Flashlight

A flashlight for Far Cry 2: a spot light at the player's head, switched by a new **Flashlight**
control (L by default, rebindable in Options > Controls), with a switch click and an icon on the HUD.
There is no battery. The light changes nothing about the guards by itself: light never reaches the
AI in this engine ([AI: Seeing](../../docs/docs/engine-internals/ai.md#seeing)). It sends its beam
on FCSE's `flashlight.beam` channel instead, for a mod that models the guards' sight. Difficulty
Overhaul does: with it, a guard looking your way at night sees the light from up to 60 m, and one you
shine it on notices you even with his back turned.

The light is one of the engine's own scene lights, made the way the player's vehicle makes its
headlight and placed every frame from the render camera. Everything that can be data is data in
`layer\`; the FCSE plugin in `src\` does only what data cannot reach.

## The beam channel

While the light is on, the plugin sends one message on FCSE's `flashlight.beam` channel every frame,
and one more when it goes off or its world goes. The message is 40 bytes:

| Field | Type | Meaning |
|---|---|---|
| version | uint32 | 1 |
| lit | uint32 | 1 while the light is on, 0 in the last message |
| position | float[3] | the lamp, in world coordinates |
| direction | float[3] | where it points, a unit vector |
| range | float | how far it reaches, in metres |
| coneAngle | float | its full outer angle, in radians |

## Settings

On the plugin's page in the Mod Configuration Menu, stored under `[Flashlight]` in `bin\fcse.ini`:

| Setting | Values | Default |
|---|---|---|
| Cone | Small (30° / 12°), Medium (45° / 20°), Large (60° / 30°), outer / inner | Medium |
| Shadows | Yes / No - whether the light casts shadows | Yes |

Both apply to a light that is on.

## Layout

```
src\                         the FCSE plugin
  flashlight.cpp             the toggle, the click-then-light timing, the HUD icon's timing
  beam.cpp                   the beam sent on the flashlight.beam channel
  engine\                    the game's own seams: the frame, the signal dispatcher, the light and
                             camera, the sound system, the HUD's Magma objects
layer\mods\
  config\                    the toggle_flashlight control and its binding
  languages\                 the control's label, in every language
  soundbinary\00fa0000.spk   the switch click
  ui\                        the HUD icon: textures, the parts of hud.mgb it changes per variant,
                             the UI dependency lists
layer\plugins\flashlight\    the built plugin, staged here by build.ps1
hud-icon\                    the HUD icon's art and the texture headers its textures are built with
build\                       local and untracked: the scripts that make the HUD files, the icon
                             textures and the click bank from retail files and hud-icon\
```

## Building

```
.\build.ps1                                  # x86 release -> layer\plugins\flashlight\Flashlight.dll
.\build.ps1 -Install "C:\Games\Far Cry 2\bin"   # and build the layer into the game
```

How the HUD icon is added to `hud.mgb` is in
[Adding an icon to the HUD](../../docs/docs/magma-ui/patterns.md#adding-an-icon-to-the-hud).
