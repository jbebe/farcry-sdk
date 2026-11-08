# Aiming Overhaul

How the player aims and moves in Far Cry 2, from the hands to the shot leaving the gun: sway driven
by the breath and the heart, holding the breath on a sniper scope, leaning, a gun that swings down
at walls and comes up slowly after a sprint, recoil that stays in the aim, a view that kicks with
each shot and reels when you are hit, a blurred gun down the iron sights, scopes seen from the
eyepiece with the world around them, and tracers. It changes no damage and no AI. Its data merges
with other mods field by field: the weapon data sets tracers, how the mod draws each scope, which
guns can hold the breath, what each weapon weighs and which keep the game's recoil, the player's data
how quickly a walk and a sprint get going, and the HUD data takes away the marks pointing to whoever
hit you.

## Aiming

- **Sway.** The breath and a slow wander move the aim, the view, the gun and the shot together, by
  about 0.1° down iron sights, 0.18° through a holographic sight and 0.35° through a magnifying
  scope, standing still; the mouse corrects it as it would a real hand. Each breath draws in, lets
  out and rests calmly at the bottom: the moment to time a shot. Down the iron sights the eye also
  drifts a couple of millimetres off the gun, so the rear and front sights drift out of line as on a
  real gun.
- **Heart rate**, from 70 a minute at rest up to 170. It climbs as stamina runs down and jumps with
  a wound or a blast nearby. Each beat jolts the aim, and a racing heart breathes faster, sways
  wider and holds the breath for less. Down a magnifying scope a racing heart is heard faintly.
- **Stance.** Crouched and still, the sway is half what it is standing; moving widens it.
- **Hold breath** on the Dart Rifle, the M1903, the Dragunov and the AS50: hold sprint down the
  scope and the breath settles and the aim steadies, for 4 s with a calm heart and 2 s with a racing
  one, heard drawn in and let out. Held longer, the hands shake. Those four scopes sway twice as far
  when the breath is not held.
- **Lean** on Q and E. The upper body slides out and bends to that side, so the eye comes about
  25 cm past a corner, with the view, the arms and the gun, and shots leave from where the eye now
  is. Hold the key to lean while it is held, or tap it to lean until it is tapped again. Sprinting
  ends a lean.
- **Recoil that stays.** Each shot kicks the aim up by the game's own amount for the gun, its wear
  and the sights, and the aim stays there until you pull it back down. The first two kicks of a
  burst are light, so its first three shots land together. Shotguns, launchers and mounted guns
  kick and come back as in the game.
- **Full auto spreads.** From the fourth shot of a burst each shot strays a little further, up to
  eight shots' worth. Shots more than a quarter of a second apart each start fresh.
- **The operation manual tightens the iron sights.** With a gun's manual from the arms dealer, its
  random spread down the iron sights is half the game's standing and a quarter crouched: an AK-47's
  0.2° and 0.095° instead of 0.4° and 0.38°. Without it, and from the hip, it is the game's.

## Moving

- **Getting going.** A walk takes half a second to reach its pace and a sprint a second and a half
  to reach its top. The top speeds and the jump are the game's, and letting go of every key still
  stops you at once.
- **Gun up after a sprint.** The gun is carried lower in a sprint and takes a moment to come back
  up after it, longer the heavier it is: 0.38 s for an AK-47, half a second for a PKM. Until it is
  up it neither fires nor comes to the sights.
- **Gun near walls.** A gun that would reach into a wall swings down out of it, the view staying
  where it is, and does not fire until it is back up. It waits for the way to stay clear before it
  comes back up, so a trunk swaying at the edge of its reach doesn't shake it.
- **Vertical inertia.** The game's arms slide a little sideways behind a turn but never up or down;
  now they also slide a little down as you look up and up as you look down, just as much.
- **Landing.** A landing drops your view a few centimetres as your knees take it.

## What you see

- **Shot shake.** Each shot kicks the view up and it springs straight back, harder for a heavier
  round: about 0.4° for an AK-47, 2° for a SPAS-12. Firing full auto, the kick fades into a quick
  roll clockwise and back, which stops when the rounds do. Only the view moves: the gun and where
  the shots go stay as they were, and a scope's sight picture does not shake.
- **Being hit.** A hit on the body punches your view back and tips it down, and the edges go dark
  for a couple of seconds. A hit on the head cuts the view to black, then fades it back in double,
  the second image fading slowly. The harder the hit, the more of it. The game's red marks pointing
  to whoever hit you are gone; its hit sound and its red flash around the edges stay.
- **Dirt in the eye.** A round landing in the dirt, sand, stone or wood within 3 m of your face, your
  own included, throws dirt in your eye: dark, blurry blobs on that side with the view blurred
  around them, gone in a few seconds.
- **Gun blur.** Down the iron sights the eye focuses on the front sight and the rest of the gun goes
  out of focus by how far it is from it, the rear sight most. A pistol's sights are too close
  together to blur.
- **Scope eyepiece.** Instead of a long tube, every scope is a black, out-of-focus eyepiece spanning
  80% of the screen, with the zoomed view filling its opening. The view inside is seen through
  glass, and the scope trails the look a little and kicks back with each shot. The AR-16's and the
  MGL-140's scopes lose the game's random spread, so a scoped shot goes where the reticle is, as on
  the sniper rifles.
- **Reticles**, new in every scope: a hunting duplex on the Dart Rifle and the M1903, a PSO-1 with
  its rangefinder on the Dragunov, a red tactical mil-scale on the AS50, and a lit holographic
  chevron on the AR-16 and the MGL-140. They are plain PNG files in
  `bin\plugins\aiming-overhaul\reticles\`, so you can swap in your own.
- **Scope surroundings.** Around the eyepiece the world stays at your normal field of view, slightly
  out of focus, as with both eyes open. The magnification is there at once inside the eyepiece, so
  the view no longer zooms as the scope comes up.
- **Scope shadow.** A soft dark crescent comes in from the eyepiece's rim on a fast turn.
- **Tracers** from the M249, the PKM, the Dragunov, the AS50 and the mounted machine guns, your own
  shots included: bright orange streaks at 350 m/s, visible however far off. One in six glances off
  where it lands, up into the air.

## Controls

Two new controls, rebindable in Options > Controls under Movements: **Lean left** on Q and **Lean
right** on E. To free those keys the defaults move: interact to F, throw grenade to G and next
throwable to T. E and Q stay as the game had them where the game never lets them be rebound:
leaving a vehicle or a mounted gun, a buddy rescue, the machete finish and the watch.

## Settings

In the **Mod Configuration** menu, and under `[AimingOverhaul]` in `bin\fcse.ini`, each effect has
a switch, all on by default:

| Setting | With it off |
|---|---|
| Sway | neither the eye nor the aim drifts, and there is nothing for the breath to steady |
| Hold breath | sprint down the sights does what the game does with it |
| Gun near walls | the gun goes into walls, as in the game |
| Vertical inertia | the arms keep still as the look pitches |
| Gun up after sprint | the gun fires and aims as soon as the sprint ends |
| Landing | the eye does not drop |
| Recoil that stays | the game's recoil, and a burst no longer spreads |
| Shot shake | the view holds still as the gun fires |
| Dirt in the eye | no round throws dirt |
| Being hit | a hit shows only what the game shows, without its marks pointing to the shooter |
| Gun blur | the gun stays sharp down the iron sights |
| Iron sight zoom | the view doesn't zoom in down iron sights; scopes still magnify |
| Scope shadow | no crescent; it needs the eyepiece, whose opening it shades |
| Scope eyepiece | the game's own scopes |
| Scope surroundings | the game's view around a scope |
| Tracers | your own shots leave none and none ricochets |

How much sway is left down the sights, the shot shake for each weapon, the dirt and the look of a
hit are values in `bin\aiming-overhaul.ini`, written with the defaults on the first start. With
DevTools installed, its overlay (Home) has an **Aiming Overhaul** window that sets them as you play,
and can throw dirt or show a hit to try them.

## Requirements

- **FCSE 1.4.0 or later**, on Far Cry 2 1.03 (Steam or GOG).
- **JackAll 1.4.1 or the Vortex extension 0.4.0, or later**, to install it.
- **DirectX 9**, set in Options > Display > DirectX Version. On DirectX 10 you get the game's own
  scopes, without the eyepiece, reticles, surroundings or shadow, and the gun blur, the dirt and the
  dark edges of a hit don't show.

## Installing

This archive is a JackAll layer: `mods\` at its root is the weapon and player data, the controls,
the HUD, the text and the breath sounds, `plugins\` is the FCSE plugin, and both installers read
that same shape.

- **Vortex** — with the Far Cry 2 extension installed, drop the zip in and enable it.
- **JackAll** — add the zip in the app, or from the command line:

```
jackall-cli mod build   --game "C:\Games\Far Cry 2" --layer aiming-overhaul.zip
jackall-cli mod restore --game "C:\Games\Far Cry 2"
```

`mod restore` is the uninstall, and removes the plugin too.

## Compatibility

- **Mods that change weapons' stats or data** merge with it field by field.
- **Mods that change the same fields** — a weapon's tracer settings, its iron-sight effect or its
  spread down a scope — clash on those fields, and the installer reports it.
- **Mods that change the controls**, such as Flashlight, combine with it control by control. One
  that rebinds interact, throw grenade or next throwable too clashes on that control, and the
  installer reports it.
- **Mods that change the HUD** combine with it part by part. One that ships the whole HUD still
  works, with the marks pointing to whoever hit you taken away on top of it.
- **Mods that change the operation manuals' accuracy** in `gamemodesconfig.xml` change what the
  game does, and the mod tells the manual apart by its 20 %: a manual given less than 15 % no longer
  tightens the iron sights.
