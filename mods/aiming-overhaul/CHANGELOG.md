# Changelog

Notable changes to Aiming Overhaul, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.3.0] - 2026-11-17

### Added
- **Lean**, on two new controls in Options > Controls under Movements: Lean left on Q and Lean
  right on E. The upper body slides out and bends to that side, so the eye comes about 25 cm past a
  corner, with the view, the arms and the gun, and shots leave from where the eye now is. Hold the
  key to lean while it is held, or tap it to lean until it is tapped again. Sprinting ends a lean,
  and a load, a vehicle or a mounted gun ends it at once.
- **The sway moves the aim.** The breath and a slow wander now move the view, the gun and the shot
  together, by about 0.2° down iron sights, 0.35° through a holographic sight and 0.7° through a
  magnifying scope, and the mouse corrects it as it would a real hand. Each breath draws in, lets
  out and rests calmly at the bottom: the moment to time a shot. Standing, the front sight wanders
  further off the mark than crouched.
- **Heart rate**, from 70 a minute at rest up to 170. It climbs as stamina runs down and jumps with
  a wound or a blast nearby. Each beat jolts the aim, and a racing heart breathes faster, sways
  wider and holds the breath for less. Down a magnifying scope a racing heart is heard faintly.
- **Stance.** Crouched and still, the sway is half what it is standing still. On the move it grows
  and runs faster: crouched as much as standing still, standing twice that.
- **Hold breath** on the Dart Rifle, the M1903, the Dragunov and the AS50: hold sprint down the
  scope and the breath settles and the aim steadies, for 4 s with a calm heart and 2 s with a racing
  one, heard drawn in and let out. Those four scopes sway twice as far when the breath is not held.
- **Gun near walls.** A gun that would reach into a wall swings down out of it, the view staying
  where it is, and does not fire until it is back up. The sights come back by themselves if the aim
  key is still held. It waits for the way to stay clear before it comes back up, so something
  swaying at the edge of its reach, like a tree trunk in the wind, doesn't shake it up and down.
- **Inertia.** The arms slide and point behind the look as it turns, sideways and up and down, twice
  as far as the game's own sideways slide, which this replaces, and come back as slowly as the gun
  is heavy, slower still after a hard twitch. Down the iron sights the gun turns about its own
  middle instead, the muzzle leading the turn and the stock trailing. A scope's eyepiece lags the
  same way.
- **Gun up after a sprint.** The gun is carried lower in a sprint and takes a moment to come back
  up after it, longer the heavier it is; until then it doesn't fire or aim.
- **Getting going.** A walk takes half a second to reach its pace and a sprint a second and a half
  to reach its top. The top speeds and the jump are the game's.
- **Recoil that stays.** Each shot kicks the aim up by the game's own amount, and it stays there
  until you pull it down, instead of dropping back once you stop firing. The first two kicks of a
  burst are light, so its first three shots land together. Shotguns, launchers and mounted guns are
  as they were.
- **Full auto shakes the gun.** From the fourth shot of a burst the aim no longer climbs: the gun
  turns about its own middle at random, as two hands fighting to hold it, snapping a new way with
  each round at the gun's own rate of fire, and the shots go where it points. It is worst from the
  eighth shot until you let go or the magazine runs dry, most on the move, half that standing still
  and least crouched.
- **Full auto spreads.** From the fourth shot of a burst each shot strays a little further, up to
  eight shots' worth.
- **Shot shake.** Each shot kicks the view up and it springs straight back, as hard as the gun's
  real round pushes back, from the round Difficulty Overhaul gives each gun, or from the game's own
  kick without it. Firing full auto, the kick fades into a roll clockwise and back, which stops when
  the rounds do. Only the view moves. A scope's sight picture does not shake. The kick, the roll and
  the full-auto shake are set for each weapon in Aiming Overhaul's window in DevTools' overlay.
- **Steadier down the sights.** Half the sway is left down the sights, standing and crouched, each
  set in the same window.
- **The operation manual tightens the iron sights.** With a gun's manual its random spread down the
  iron sights is half the game's standing and a quarter crouched; without it, the game's.
- **Dirt in the eye.** A round landing in the dirt, sand, stone or wood by your face, your own
  included, throws dirt in your eye: big, dark, blurry blobs on that side, darker where they
  overlap, with the view blurred around them, gone in a few seconds. How big, dark and blurred, how
  long it lasts and how near it has to land are set in the same window, which can also throw some
  to try it.
- **Being hit.** A hit on the body punches your view back and tips it down, and the edges go dark
  for a couple of seconds, fading in smoothly from a third of the way out. A hit on the head
  darkens the edges as hard as the hardest body hit, and at once you see double, the second image
  fading slowly. The harder the hit, the more of it. How far and how long each lasts is set in the
  same window, which can also show a hit to try it.
- **Health in the picture.** Three full health bars still count as healthy. Into the third bar the
  picture loses some of its colour; into the second, more of it, with light grain and a slight
  glow; into the last, it is black and white, grainy and glowing. Each fades into the next over a
  second, and back as you heal. Its looks are set in the same window, which can hold a level to try
  it.
- **Scopes on the Carl Gustaf and the crossbow.** The Carl Gustaf's sight shows its M4 range and
  lead marks in a thick eyecup, with no black mount below it; the launcher's barrel shows right of
  it instead, black and out of focus. The DLC crossbow gets the hunting duplex, and its breath can
  be held like the sniper rifles'.
- **Tracers by cartridge.** Soviet rounds' tracers, from the PKM, burn green; NATO rounds' burn
  orange-red. The core keeps the round's colour instead of washing out to pale yellow. A ricochet
  keeps its round's colour. A modded gun picks green with `AimingOverhaul.SovietTracer` in its
  weapon data.
- **A muzzle light that reaches.** A shot lights the walls and ground up to 5 m away, two and a half
  times as far as the game's.
- **A muzzle flash per gun.** Each gun's flash, yours and the soldiers', is sized to its round, its
  barrel and its muzzle: flash hiders break it into prongs, short barrels and big rounds throw a
  bigger ball, and the AS50 and M2 the largest. The Dragunov and M1903 now flash. Every flash lasts
  as long as the game's.
- **On the test range too.** Every gun change the mod makes reaches the guns of DevTools' test range.
- **The window** is in DevTools' overlay when DevTools is installed. Its values are kept in
  `bin\aiming-overhaul.ini`, which can be edited without it.
- **Hold breath**, **Gun near walls**, **Inertia**, **Gun up after sprint**, **Recoil that stays**,
  **Shot shake**, **Dirt in the eye**, **Being hit** and **Health in the picture** switches in the
  Mod Configuration menu.

### Changed
- **Needs JackAll 1.8.0 or the Vortex extension 0.7.0**, which merge the HUD's parts, the game
  modes config and the particle effects with other mods'.
- **Needs FCSE 1.5.0.** Built for its plugin API 10; an older FCSE leaves this mod unloaded and says
  so in `fcse.log`.
- **Interact moves to F, throw grenade to G and next throwable to T** by default, to free Q and E
  for the lean. All three can be rebound. Leaving a vehicle or a mounted gun and healing a downed
  buddy go to F with them. The machete finish and the watch stay on E and Q, which the game never
  lets be rebound.
- **Tracers as real belts are linked.** One round in five from the M249 and the mounted M249s and
  M2s, one in four from the PKM, where every third and second round were tracers. The Dragunov and
  the AS50 fire none: a marksman loads plain rounds.
- **No random spread down the AR-16's and the MGL-140's scopes**, so a scoped shot goes where the
  reticle is, as it already did on the sniper rifles.
- **No red marks pointing to whoever hit you, and no red flash around the edges.**
- **The scope lags behind the look as it turns,** and its shadow shows on slow turns too, deeper on
  fast ones.
- **The view around a scope eases out of focus** as the scope comes up, instead of at once.
- **The breath and heartbeat on sound ids of their own, from 0x00FB0000,** out of Sound Overhaul's
  block. They sound the same.

### Fixed
- **Works under dgVoodoo and DLSS injectors.** Under dgVoodoo, which the DLSS 5 injector needs to
  run Far Cry 2, the scope picture, eyepiece, scope shadow, gun blur and the tracers' look were
  missing. Aiming Overhaul draws through the game itself instead of hooking Direct3D, so they are
  there too.

## [1.2.0] - 2026-10-15

### Added
- **Iron sight zoom** in the Mod Configuration menu. Switched off, the view no longer zooms in
  down a gun's iron sights; scopes still magnify. On by default, as the game has it.

## [1.1.0] - 2026-10-09

### Changed
- **Needs FCSE 1.4.0**, rebuilt for its new plugin interface. Older FCSE no longer loads it.
- **The reticles are PNG files** beside the plugin, in `bin\plugins\aiming-overhaul\reticles\`,
  so one can be replaced by overwriting it, and a weapon's data can name an image of another mod's.
- **Scopes are weapon data.** How each scope is drawn, its reticle, rim, lens and how far the eye
  comes to it, is now set on the weapon in the mod's data, and the mod finds the scope in the
  weapon's own mesh instead of recognising each rifle's model. A weapon mod can give its own gun a
  scope the same way.

## [1.0.0] - 2026-10-06

### Added
- **Sway down the iron sights.** The eye drifts a couple of millimetres off the gun and rises and
  falls with a slow breath, so the rear and front sights drift out of line as on a real gun. Shots
  still go where the game sends them.
- **A blurred gun down the iron sights.** The eye focuses on the front sight and the rest of the
  gun goes out of focus by how far it is from it, the rear sight most. It holds through fast turns.
  A pistol's sights are too close together to blur.
- **Scopes seen from the eyepiece.** Instead of a long tube, every scope is a black, out-of-focus
  eyepiece spanning 80% of the screen, with the zoomed view filling its opening. The view inside is
  seen through glass, bowing and fringing slightly toward the rim, and the scope trails the look a
  little and kicks back with each shot.
- **New reticles**: a hunting duplex on the Dart Rifle and the M1903, a PSO-1 with its rangefinder
  on the Dragunov, a red tactical mil-scale on the AS50, and a lit holographic chevron on the AR-16
  and the MGL-140 that burns brighter inside its strokes and glows past them.
- **The world around a scope**, at your normal field of view and slightly out of focus, as with both
  eyes open. The magnification is there at once inside the eyepiece, so the view no longer zooms as
  the scope comes up, and the game's radial blur around scopes is left out.
- **A scope shadow**: a soft dark crescent comes in from the eyepiece's rim on a fast turn.
- **Tracers** from the M249, the PKM, the Dragunov, the AS50 and the mounted machine guns, your own
  shots included: bright orange streaks at 350 m/s, smooth at any angle and visible however far
  off. One in six glances off where it lands, up into the air.
- **A switch for each** in the Mod Configuration menu.
- **Weapon data that merges.** Which guns fire tracers, how fast they fly and the scopes' iron-sight
  effect are set as weapon data, so other weapon mods merge with it field by field, and a clash on
  the same field is reported by the installer.
