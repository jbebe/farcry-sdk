# Aiming Overhaul

An FCSE plugin, with weapon and player data and controls, for how the player aims and moves: sway
and a blurred gun down the iron sights; a scope that shadows, is seen from its eyepiece, and has the
world around it at the eye's own field of view; tracers; leaning, on two new controls; a gun carried
low in a sprint; a landing the knees take; and a player who takes a moment to get going. It owns
everything from the player's input to the shot leaving the gun, and nothing after: no damage, no AI.
Its weapon data sets only tracer and scope-effect fields, and gives each weapon entity data of the
plugin's own; its player data sets only how quickly a walk and a sprint get going. JackAll merges
both field by field with other mods' edits, reporting any clash.

## What it does

- **Sway.** The hands drift with a breath and a slow wander. Each breath draws in for 1.5 s,
  lifting the aim, lets out for 2 s, and rests for 1.5 s at the bottom, where the wander calms to
  under half: the moment to time a shot in. Down the iron sights the eye drifts a couple of
  millimetres off the gun with them. The rear sight is nearer the eye than the front one, so it
  moves further and the two drift out of line. Shots still go where the engine sends them: down the
  view, which is where the barrel points. Sights that have drifted out of line are wrong by the same
  angle as on a real gun. The breath and the wander also move the aim itself, view, gun and shot
  together, by about 0.2° down the iron sights, 0.35° through a holographic sight and 0.7° through
  a magnifying scope; the mouse corrects it as it would a real hand. While a scope's own sight
  picture is up the eye does not drift off the gun, because that picture is a miniature right at
  the eye, but the aim still wanders under the reticle.
- **Heart rate.** The heart beats at 70 a minute at rest and up to 170. It climbs over a few
  seconds as stamina runs down, higher while sprinting, and comes back down as the stamina does,
  at rest soon after it is full. It jumps with a wound by as much as the health lost and while a
  blast nearby shakes the camera, and that settles back over about 20 s. Each beat jolts the aim up
  a little, more as the heart races, and a racing heart breathes up to twice as fast with a quarter
  of the rest, sways up to 60% wider, and holds the breath for as little as half as long. Down a
  magnifying scope a heart past 130 a minute is heard faintly, and it fades out as the heart slows,
  silent below 80; iron sights and the holographic sights never hear it.
- **Stance.** Crouched and still, the sway, heart jolts included, is half what it is standing still;
  moving, it is 1.4 times crouched and 1.8 times standing, eased in from walking pace with a creep
  still counting as still. Crouched and still a wound's or a blast's share of the heart settles
  twice as fast, crouched and moving it holds where it is, and walking upright keeps the heart a
  little up. A lean keeps the stance it leans
  from, and nothing counts as resting the gun.
- **Hold breath.** With a magnifying scope, on the Dart Rifle, the M1903, the Dragunov and the AS50,
  holding sprint down the scope holds the breath instead of sprinting: the breath settles to the
  bottom of its rest within half a second and stays there, and the wander eases to a sixth of
  itself, for 4 s with the heart at rest and 2 s with it racing. Held longer, the hands shake
  harder, up to two and a half times the wander, until it is let go. After a hold the sway is up to
  half again as large while the breath comes back, which takes 3 s from empty, and the next hold
  waits until half of it is back. The breath is drawn in as the hold begins and let out as it ends
  or the breath runs out, whichever comes first, lightly with more than half the stamina left and
  harder below, and while it is in each beat of the heart is heard. Down those scopes the sprint key
  does nothing else; down any other gun's sights it does what the game does.
- **Lean.** Lean left or Lean right slides the upper body 10 cm to that side and bends
  it 15° from the lower back, over a quarter of a second, so the eye moves out about 25 cm. The
  head, the view, the arms and the gun go with it, and shots leave from where the eye now is. Held, the key leans for as long as it is held;
  tapped, let go within a quarter of a second, it leans until it is tapped again. The other key
  leans the other way instead. Sprinting ends a lean.
- **Gun near walls.** A gun that would reach into a wall, within 0.8 m of the eye, swings down out
  of it, by up to 34° as the room ahead shrinks by another 0.5 m, while the view holds still.
  Swung most of the way down, past nine tenths, it lets go of the sights and does not fire, until it
  is back above seven tenths; then the sights come back by themselves if the aim key is still held.
  It comes back up only once there has been 10 cm more room than it went down for, for 0.4 s, and
  then over 0.3 s, so a trunk or a branch swaying at the edge of its reach does not shake it.
- **Vertical inertia.** The game slides the arms a little sideways behind a turn, never up or down.
  Here they also slide down a little as the look pitches up, and up as it pitches down, at the same
  rate, smoothing and size as the game's own: 1.1 cm at 126° a second, the most at 185°. Down the
  sights there is none, as there is none sideways.
- **Landing.** A landing drops the eye as the knees take it, by how fast it came down, about 5 cm
  from a standing jump, and it is back up in about a third of a second.
- **Gun up after a sprint.** A sprint carries the gun lower than the game does, and after it the gun
  takes 0.25 s and 0.03 s more for each kilogram it weighs to come back up: 0.38 s for an AK-47,
  half a second for a PKM. Until it is up it neither fires nor comes to the sights, and the sights
  come up by themselves then if the aim key is still held.
- **Getting going.** A walk takes half a second to reach its pace, not a seventh of one, and a
  sprint a second and a half to reach its top, not half a second, before it falls back to a run as
  the game's does. Walking out of a sprint, or into the sights' slower pace, slows over a moment.
  The top speeds and the jump are the game's. Letting go of every key still stops the player at
  once.
- **Recoil that stays.** Each shot kicks the aim up and a little to one side, and the aim stays
  there until you pull it back down. The game's own climb drops back to where the burst began once
  the shots stop; here it never does. The kick is the game's own for the gun, its wear and the
  sights: a new AK-47 climbs 0.6° a shot down the sights and 1.1° from the hip. The first two kicks
  of a burst are a quarter of that, so its first three shots land together. Shotguns, launchers and
  mounted guns kick and come back as in the game.
- **Full auto spreads.** From the fourth shot of a burst each shot strays further, by a quarter of
  the gun's kick more each time, up to eight shots' worth: down a new AK-47's sights, up to 1.2°
  more than the game's 0.4°. A burst is shots less than a quarter of a second apart, so slower taps
  each start fresh.
- **Gun blur.** Down the iron sights the eye focuses on the front sight, and the rest of the gun
  goes out of focus by how far it is from it, as through an 8 mm pupil: the rear sight most, and the
  blur spills a little past the gun's edge. The focus holds on the front sight while a fast turn
  swings the gun. It fades in as the eye settles into the sights. A gun held far from the eye, like
  a pistol, has its sights too close together in dioptres for a blur you could see, and gets none,
  as a real eye nearly does.
- **Scope shadow.** As the look turns, a soft dark crescent comes in from the eyepiece's rim on the
  side turned toward: hardly any on a slow move, a slight one on a fast flick, gone when the look is
  still or the scope comes down. It needs the eyepiece, whose opening it shades.
- **Eyepiece.** The engine draws a scope as a tube seen down its length, with the reticle and the
  zoomed view far at its end. Here the tube is not drawn. Every scope looks the same size instead:
  a pitch-black, out-of-focus body whose outer edge spans 80% of the screen's height, with a black
  mount below it down to the screen's foot, and the zoomed view filling its opening. The sniper
  scopes are a plain ring, thin on the Dart Rifle and the M1903 and thicker on the Dragunov and the
  AS50; the AR-16's and the MGL-140's, which are not round, also show their eyepiece's own shape.
  The reticle is a new one, an image with its centre on the point of aim and its edge on the lens's
  rim: a hunting duplex on the Dart Rifle and the M1903, a PSO-1 with its rangefinder on the
  Dragunov, a red tactical mil-scale on the AS50, and a holographic chevron on the AR-16 and the
  MGL-140. The holographic one is lit: its strokes burn toward a hotter shade of their colour inside
  and glow past their edges, adding light rather than covering. The magnification is unchanged. The view inside is seen through
  glass: lines bow slightly toward the rim, colours part there the more the scope magnifies, the
  rim darkens, and the coating tints it faintly. The scope is laid over the finished frame, after
  the tone mapping, and trails the look a little as it turns. Each round fired kicks it back toward
  the eye for a moment, as the stock comes back onto the shoulder. A scope on a weapon without the
  plugin's scope data, or whose mesh has no `SCOPE_HI` part, is drawn as the game draws it, and
  named in `fcse.log`.
- **Surroundings.** Around the eyepiece the world is seen at the field of view the eye has without
  a scope, as with both eyes open, while the eyepiece keeps the scope's magnified view. With the
  eyepiece on too, the view does not zoom as the scope comes up: the gun comes to the eye, and the
  magnification is there at once inside the eyepiece, so the world around it never changes. The
  surroundings are evenly out of focus, as the other eye sees past a scope, and the game's own
  radial blur toward the screen's edges is left out. They are a second, cheaper view drawn by the
  engine's water reflection renderer, at half the screen's size with less detail; the magnified
  view inside stays the engine's own.
- **Tracers.** Only the M249, the PKM, the Dragunov, the AS50 and the mounted M249s and M2s fire
  them, at the rate the game gives each, the player's own shots included, which the game never
  draws. A tracer flies at 350 m/s with a streak short enough to be seen crossing even a near
  shot, and some ricochet off where they land, anywhere from level to straight up and to either
  side, never back. The streak is drawn by a shader of the plugin's own: an orange glow with a
  hotter core, brighter than white so that it blooms, smooth at any angle, and a few pixels wide
  however far off it is.

The eye is moved through the camera's positional offset, the one the recoil kicks, and a scope's
zoom held back until its sight picture is up, in `src/engine/aim.cpp`. The aim's wander is added
to the look the input builds each frame, in `src/engine/look.cpp`; the breath is `src/breath.cpp`,
and the sprint it holds is refused in the input listener, in `src/engine/sprint.cpp`. A lean is
`src/lean.cpp`, bending the body's bones each time the engine poses them, in `src/body.cpp` through
`src/engine/skeleton.cpp`, and rolling the view and moving the eye with the head in
`src/engine/aim.cpp`; a shot is moved with the eye in `src/engine/shot.cpp`. The gun near walls is
`src/wall.cpp`, casting a physics ray in `src/engine/ray.cpp`, lowering the look in
`src/engine/look.cpp` and refusing the shot in `src/engine/fire.cpp`. The gun after a sprint is
`src/ready.cpp`, refused the same way and held low by the same pass that bends the lean, which
turns the arms' bones; how long it takes is by the weight `src/weight.cpp` reads. The arms' slide
behind the look's pitch, `src/pitch_slide.cpp`, moves them in the same pass. A landing is
`src/landing.cpp`, dropping the eye through the camera's positional offset. The recoil is
`src/recoil.cpp`: each of the player's kicks is taken from the game before the game blends it in,
in `src/engine/kick.cpp`, added to the look in `src/engine/look.cpp`, and a burst's shots stray in
`src/engine/shot.cpp`. The guns that keep the game's recoil have `AimingOverhaul.RecoilReturns` on
their archetype, written by `data\fragments.py`. The blur draws the gun's own
depth pass a second time into a depth texture of its own, in `src/engine/weapon_draws.cpp`, and the
blur and the scope shadow draw at the end of the pass the gun's colour is drawn in, before the
bloom, in `src/blur.cpp` and `src/scope_shadow.cpp`.
The eyepiece drops the engine's scope and draws its own over the finished frame, in
`src/eyepiece.cpp`. What it draws for a scope is its weapon's archetype's entity data, read in
`src/scopes.cpp`, and which of the weapon's draws are the scope is read from the engine's own
mesh, in `src/engine/weapon_mesh.cpp`. The reticles are plain PNGs beside the plugin,
`layer\plugins\aiming-overhaul\reticles\*.png`, installed into `bin\plugins\aiming-overhaul\reticles\`
and read in `src/engine/image_file.cpp`; replacing one replaces what that scope shows. The second
view is drawn in `src/engine/second_view.cpp` and laid down as the gun's colour pass begins, in
`src/surroundings.cpp`. The player's own tracers, a streak's length against its shot, and the
ricochets are in `src/tracers.cpp`, and the streak is drawn in `src/streak.cpp` with
`src/shaders/tracer.fx`, in the draws that bind the Direct3D texture the engine's tracer texture
resource holds. Which guns fire tracers, their speed and their streaks' longest length, and the
scopes the plugin draws losing the engine's iron-sight effect and its random spread down the sights, so a scoped shot goes where the reticle is, are weapon data: `WeaponProperties`
fragments under `layer\mods\`. So is each scope's reticle, eyepiece shape, rim, lens centre, raise
and size: `AimingOverhaul.Scope*` keys in a `CFCSEDataComponent` on its `weapons` archetype, the
reticle as an image path under `bin\plugins` with `ScopeReticleLit` for one lit from within. A gun
whose breath can be held, and whose scope sways harder, has `AimingOverhaul.HoldBreath` set to 1
there, and every weapon has its weight in kilograms, `AimingOverhaul.Weight`; a weapon without one
weighs 3.5. All of it is written by `data\fragments.py`. How quickly a walk speeds up and slows down
is the player archetypes' `fWalkingAcceleration` and `fWalkingDeceleration`, and a sprint's speed
over time is `Curves.Locomotion.Sprint`, both fragments under `layer\mods\` written by
`data\movement.py`. The breaths are the game's own stamina breathing,
cut into inhales and exhales by `data\breath.py` into four banks, `layer\mods\soundbinary\00fc0b*.spk`,
played in `src/breath.cpp` through `src/engine/sound.cpp`. The engine side is in
[first-person aiming](../../docs/docs/engine-internals/first-person-aiming.md),
[bullet tracers](../../docs/docs/engine-internals/bullet-tracers.md) and
[presenting a frame](../../docs/docs/engine-internals/presentation-and-input.md).

## Requirements

FCSE 1.4.0 or later.

## Controls

Two new controls, rebindable in Options > Controls under Movements: **Lean left** on Q and **Lean
right** on E. To free those keys the defaults move: interact to F, throw grenade to G and next
throwable to T. E and Q stay as the game had them where the game never lets them be
rebound: leaving a vehicle or a mounted gun, a buddy rescue, the machete finish and the watch.

## Settings

On the plugin's page in the Mod Configuration Menu, stored under `[AimingOverhaul]` in
`bin\fcse.ini`:

| Setting | Values | Default |
|---|---|---|
| Sway | Yes / No | Yes |
| Hold breath | Yes / No | Yes |
| Gun near walls | Yes / No | Yes |
| Vertical inertia | Yes / No | Yes |
| Gun up after sprint | Yes / No | Yes |
| Landing | Yes / No | Yes |
| Recoil that stays | Yes / No | Yes |
| Gun blur | Yes / No | Yes |
| Iron sight zoom | Yes / No | Yes |
| Scope shadow | Yes / No | Yes |
| Scope eyepiece | Yes / No | Yes |
| Scope surroundings | Yes / No | Yes |
| Tracers | Yes / No | Yes |

With Sway off neither the eye nor the aim drifts, and there is nothing for the breath to steady.
With Hold breath off, sprint down the sights does what the game does with it. With Vertical
inertia off, the arms keep still as the look pitches, as in the game. With Gun up after sprint off, the gun fires and aims as soon as the sprint ends, as in the game. With Landing off,
the eye does not drop. With Recoil that stays off, the game's recoil comes back as it was and a
burst no longer spreads. How quickly the player gets going is the player data's, and stays. With
Iron sight zoom
off, the view keeps its field of view down a gun's iron sights; a scope still
magnifies. With Tracers off, the player's shots leave none again, none ricochets, and they are drawn as the game
draws them. Which guns fire them and how fast they fly are the weapon data's, and stay; so does the
scopes' lost iron-sight effect, whatever the scope settings.

## Building

```
.\build.ps1                                     # x86 release -> layer\plugins\aiming-overhaul\
.\build.ps1 -Install "C:\Games\Far Cry 2\bin"   # and build the layer into the game with jackall-cli
python data\fragments.py world1.xml world2.xml dlc1.xml   # rewrite the weapon data, see the script
python data\movement.py world1.xml world2.xml            # rewrite the player data, see the script
python data\breath.py <worlds soundbinary folder>        # rebuild the breath banks, see the script
```

`fxc`, from the Windows SDK, compiles the shaders at build time. `-Install` builds the game's patch
from this layer alone, dropping any other layer built into it before.
