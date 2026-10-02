# Flashlight

A flashlight for Far Cry 2: a spot light at the player's head, switched by a new **Flashlight**
control (L by default, rebindable in Options > Controls), with a switch click and an icon on the HUD.
There is no battery. At night the light gives you away: guards see you from as far off as by day,
and one you shine it on notices you even with his back turned.

The light is one of the engine's own scene lights, made the way the player's vehicle makes its
headlight and placed every frame from the render camera. Light never reaches the AI in this engine,
so while it is on the plugin applies two of the engine's own sight rules to the player. He counts as
having just fired, which lifts the night's shorter sight. A guard inside the beam sees all around
him, as one sitting in a vehicle does. Both are in
[AI: Seeing](../../docs/docs/engine-internals/ai.md#seeing). Everything that can be data is data in
`layer\`; the FCSE plugin in `src\` does only what data cannot reach.

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
  engine\                    the game's own seams: the frame, the signal dispatcher, the light and
                             camera, the guards' sight, the sound system, the HUD's Magma objects
layer\mods\
  config\                    the toggle_flashlight control and its binding
  languages\                 the control's label, in every language
  soundbinary\00fc0a00.spk   the switch click
  ui\                        the HUD icon: textures, hud.mgb per variant, the UI dependency lists
layer\plugins\flashlight\    the built plugin, staged here by build.ps1
build\                       local and untracked: the scripts that make the HUD files, the icon
                             textures and the click bank from retail files and assets\
```

## Building

```
.\build.ps1                                  # x86 release -> layer\plugins\flashlight\Flashlight.dll
.\build.ps1 -Install "C:\Games\Far Cry 2\bin"   # and build the layer into the game
```

How the HUD icon is added to `hud.mgb` is in
[Adding an icon to the HUD](../../docs/docs/magma-ui/patterns.md#adding-an-icon-to-the-hud).
