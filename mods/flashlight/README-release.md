# Flashlight

A flashlight for Far Cry 2, made to feel like part of the game. There is no battery to manage; it
is a convenience.

- **Its own control**, Flashlight, on L by default and rebindable in Options > Controls.
- **A real light.** The engine's own spot light, the kind the vehicles' headlights use, sits at your
  head and follows where you look. It is the headlights' warm white, reaches 25 m and casts shadows.
- **A switch click**, with the light coming up just after it.
- **A HUD icon** above the diamond counter, showing on or off. It fades in when you toggle and out
  again with the rest of the HUD.

## Settings

In the **Mod Configuration** menu, and under `[Flashlight]` in `bin\fcse.ini`:

| Setting | Values | Default |
|---|---|---|
| Cone | Small, Medium, Large | Medium |
| Shadows | On / Off | On |

Both apply to a light that is already on.

## Requirements

- **FCSE 1.3.0 or later**, on Far Cry 2 1.03 (Steam or GOG).

## Installing

This archive is a JackAll layer: `mods\` at its root is game data, `plugins\` is the FCSE plugin, and
both installers read that same shape.

- **Vortex** — with the Far Cry 2 extension installed, drop the zip in and enable it.
- **JackAll** — add the zip in the app, or from the command line:

```
jackall-cli mod build   --game "C:\Games\Far Cry 2" --layer flashlight.zip
jackall-cli mod restore --game "C:\Games\Far Cry 2"
```

`mod restore` is the uninstall, and removes the plugin too.

## Compatibility

- **Mods that change the controls**, `config\inputactionmapcommon.xml` or
  `config\defaultusercontrols.xml`, conflict. Whichever loads last wins; if the other mod wins, there
  is no Flashlight control.
- **Mods that replace the HUD**, `ui\localized\...\hud.mgb`, conflict the same way. If the other mod
  wins, the flashlight works without its icon.
