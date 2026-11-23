# Changelog

Notable changes to Sky Overhaul, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added
- **Weather.** Clear skies with more or less cumulus and cirrus, hazy dry days, fog, overcast,
  drizzle and steady rain, wind, and storms that always come behind a dark warning sky, gather,
  break and clear. Rain falls when the weather rains, light or heavy, and the fog, wind and the
  game's storm light follow the weather. Morning fog lies on calm mornings after a clear night, more
  in the jungle, never in the desert, and mist rises after rain. Showers and storms gather in
  places and drift over with the wind, most in the jungle and in the afternoon, few over the
  desert: one shows far off as thicker, darker cloud with an anvil of ice ahead of it, and passing
  over it darkens the sky, rains and clears. Clouds hide the moon: a clear night is moonlit, a
  cloudy one dimmer, an overcast or stormy one close to black, and only the clouds overhead catch
  the moonlight, dimmer the more cloud there is, the far ones fading into the dark. The weather follows the game's clock and saves
  with it.
  Switched with Weather in the Mod Configuration Menu.
- **Rain you can see.** Thousands of fine streaks fall round you in place of the game's dozen,
  sparse and short in drizzle, long and driven slant by the wind in a downpour, streaking as you run
  and turn, and none under a roof. They come with any rain the game has, the mod's weather or not. Rain in the Mod Configuration Menu
  gives the game its own drops back.
- **Lightning and thunder.** Storms flash: the clouds light up from inside, and half the strikes
  reach the ground as jagged bolts with rounded turns that come out of the cloud, a step thinner at
  each branch, their branches tapering to a point where they stop in the air. A strike lights the
  land through the game's own light, by how near it is: a strobe close by and a faint glow far
  off, most of all at night. A storm overhead strikes every eight seconds or so in a downpour, most
  of it within three kilometres, while storms far off flash only now and then. The thunder
  follows as fast as sound travels: a crack when it is
  close, a roll from a few kilometres, a low rumble from far off.
- **A rainbow after rain.** When the rain where you stand stops and the sun breaks through low in
  the morning or late afternoon, a faint rainbow stands opposite it, red outside, rising from its
  left or right end, over the top and fading out on the way down the other side. It comes up over a
  few minutes and fades within half an hour of game time.
- **A wet world.** In rain the dust turns to dark mud, walls and rocks darken, and by day every
  surface shines wet through its own lighting: planks, stone and cars catch the light, and metal that
  already shone stays near its own. Puddles fill level ground in a long rain, never a slope, mirror
  the walls, trees and sky round them and ring with sharp, short-lived rings; the game's splashes
  on the ground are gone with its drops. Lakes and rivers ring with quick, sharp drops too, and a downpour dulls
  their mirror. Under a roof it stays dry, while the rain comes through the trees, and after the
  rain it all dries over a few hours.

### Changed
- **A smaller setting sun.** The sun swells less as it sets, to about two thirds of the size it
  grew to before.
- **The sun stays a disc through dusk.** From the late afternoon to sunset the sky round the sun
  carries less haze and the glare stays as weak as a setting sun's, so the sun shows in a soft glow
  instead of a white blot over half the sky.
- **Moonlight once the moon is up.** On clear and jungle nights the moon lights the land in full
  from about 22:40, when it rises, instead of an hour later.
- **Darker clear nights.** No night is brighter than one under thick, light cumulus: a clear night
  has under half the moonlight and two thirds of the ambient light it had.
- **The moon's face shows.** The moon is no longer a white disc: it shines at half its old
  brightness with only a faint corona round it, so its maria and craters always show.
- **Fainter stars low down and in haze.** The stars and the Milky Way are gone below fifteen degrees
  and fade in up to forty, and a milky, hazy sky hides more of them.
- **Faster.** Sky Overhaul costs about a third less GPU time: the sky's air is worked out once a
  frame instead of for every pixel, the clouds are drawn at half resolution, and the heat haze reads
  its noise less often. Thin cloud edges are a touch grainier; the rest looks as it did. The extra
  depth the heat haze and the wet ground read is drawn only while they show something.
- **No shadows drawn in the pitch dark.** On a night too dark for the moon to cast a shadow, before
  it rises or under overcast or a storm, the game draws no shadows at all: a stormy night runs about
  a third faster. Moonlit nights keep their shadows as far out as by day.

### Removed
- **The afterimage.** Staring at the sun and looking away no longer leaves a dark spot, a negative
  of the view or a haze over it. Looking toward the sun still washes the picture out, raises its
  contrast and drains its colour. Its values leave `sky-overhaul.ini` on the next launch.

### Fixed
- **Even frame pacing.** Looking at a still sky could make every tenth frame or so take twice as
  long. That stutter is gone.
- **No stripes in far cirrus.** A cirrus sheet near the horizon blurs into an even haze instead of
  repeating in stripes.
- **The mod's sky all night.** Once the night was full the game drew its own sky in its place,
  every star at full brightness down to the horizon and no land past the drawn world's edge. The
  mod's sky now stays until dawn.
- **The game's depth image in half floats.** It held depth in three eight-bit channels, rounded in
  centimetre steps at fixed distances; in rain those steps drew lines across grass. It now keeps the
  same depth without the steps, for about 40 MB more video memory at 1080p with 4× multisampling.
  Third Person needs its next release alongside, which reads the new format.
- **All grass left out of the ground's depth.** Grass drawn through some of the game's shaders, such
  as bent clumps, unshadowed patches or other graphics settings, counted as solid ground in the depth
  ambient occlusion reads. It is now told apart by how it turns to face you, in any setting.

## [1.6.0] - 2026-11-17

### Changed
- **Needs FCSE 1.5.0.** Built for its plugin API 10; an older FCSE leaves this mod unloaded and says
  so in `fcse.log`.
- **No rim light.** The bluish glow along the edges of people and plants is gone by day too, and
  in every weather, not only on clear, cloudy and jungle nights.
- **Ambient occlusion and Flatten on by default.** A new install starts with both on. An upgrade
  keeps what `fcse.ini` already holds, so turn them on in the Mod Configuration menu.
- **Fainter stars.** The stars and the Milky Way shine at about half their old brightness.
- **Far land through the day.** The land painted past the edge of the drawn world follows the
  hour: darker around dawn and late at night, a little brighter through the day.

### Removed
- **Rock shading.** Rocks and cliffs are the game's own again: the faceted look read as
  low-poly rather than stone. The **Rocks** switch and the Rock tab are gone with it.

### Fixed
- **The sun sets behind the far land.** Where land is painted past the edge of the drawn world,
  the setting sun stayed in front of it, glare and light shafts included. It now sinks behind it.
- **Works under dgVoodoo and DLSS injectors.** Under dgVoodoo, which the DLSS 5 injector needs to
  run Far Cry 2, Sky Overhaul drew nothing and the game's own sky stayed. It draws through the game
  itself instead of hooking Direct3D, so its sky is right there too, with ReShade and a DLSS 5
  injector on top, and beside Aiming Overhaul and Vehicle Overhaul, which hook the same parts of
  the game.

## [1.5.0] - 2026-10-16

### Added
- **Heat haze.** On a sunny day the hot air over the ground shimmers in thin waves that drift
  sideways. The farther something is, the harder the air pushes and smudges it, so distant land
  swims while nearby things stay steady. It hugs the low ground: the foot of a hill shimmers, its
  top stays clear. It follows the sun's height and is gone at night and in storms, and through a
  scope the waves keep their size on screen. **Heat haze** in the Mod Configuration menu turns it
  off; its sliders are under **Post FX** in the DevTools window.
- **Mirages.** Bare, level ground far off shows the sky mirrored in it, softly, like a road
  shimmering wet in the heat. Grass, slopes and rocks are left alone. Part of the heat haze, with
  its own strength, slope and start distance sliders.
- **Land to the horizon.** Where the game stops drawing the world short of the horizon, the gap
  below it used to show sky; it is now filled with far, hazy land. The **Horizon land fill**
  sliders, under **Sky**, turn it off or move its edge.
- **Flatten**, off by default. Distant land is drawn flatter, so the world reads bigger and farther
  off than it is. Only the picture changes, so with it on, a shot at something beyond about 250 m
  lands slightly off from where it appears. **Flatten** in the Mod Configuration menu turns it on;
  its sliders are on the new **World** tab.

## [1.4.0] - 2026-10-15

### Fixed
- **The sky is never left without clouds.** With Clouds on Overhaul, the game's own clouds now stay
  until the mod's are actually drawn, so another Direct3D tool keeping the frame from the mod, such
  as an upscaler injector, no longer leaves an empty sky. `fcse.log` says when that happens.
- **Occlusion fades into fog.** It used to darken the fog itself, so in thick fog the creases of
  distant buildings and rocks showed through it. It now weakens as the fog thickens, and is gone
  where the fog hides everything.
- **Far rocks are no longer faceted.** Rock turns smoothly back into the game's own look with
  distance, so distant low-detail rock no longer shows big flat faces. **Rock flat distance**, on
  the Rock tab, sets where: 69 m by default, fading from about 35 m to 104 m.

## [1.3.1] - 2026-10-09

### Changed
- **Needs FCSE 1.4.0**, rebuilt for its new plugin interface. Older FCSE no longer loads it.

## [1.3.0] - 2026-10-01

### Added
- **Film grain**, faint and grey; **Grain** in the Mod Configuration menu turns it off. Its
  strength, size and colour are under **Post FX** in the DevTools window, the tab that was **Grade**.

## [1.2.0] - 2026-09-28

Upgrading: delete `bin\sky-overhaul.ini` once. A file kept from 1.1.0 holds the old values, so the
new glare and grade below would not take effect.

### Added
- **Grass lit by the sun**, with dark roots, a sheen across the blades and light glowing through
  them, in place of the engine's flat grass lighting.
- **Tree leaves lit one by one**: a crown shades its own far side from the sun, each leaf tilts its
  own way, and leaves glint in the sun and glow with it behind them.
- **Rocks and cliffs** shaded as the faceted stone their meshes are, with a fine detail texture from
  Poly Haven's CC0 rock scan and a tight glint off their faces.
- **Sun shadows** that follow a low sun further toward the horizon and reach further from the
  player, turned with the sun in half-degree steps so thin shadows on buildings hold still.
- **Dusk fog**: the land's fog darkens as the sun sets.

### Changed
- **Clear days are no longer blown out.** Once the sun is well up, the sky is drawn at half its
  brightness and sunlit clouds at a quarter, so the sky reads blue instead of clipped cyan and the
  clouds keep their form. Dawn, dusk and storms are unchanged.
- **Shade is lighter on clear days**: up to 1.7 times the light at noon, so sunlit ground is about
  4.5 times as bright as shade instead of 7. Jungle, cloudy and storm weather and the night are
  unchanged.
- **The sun's glare stays around the sun.** The whole view washes out, loses contrast and drains of
  colour only when looking straight at it; with the sun off to one side, the rest of the view keeps
  its tones.
- **Retuned defaults**: a stronger contrast and lighter shadows in the grade, stronger ambient
  occlusion, softer cloud shadows.
- **A smaller moon.**

### Fixed
- Distant land is no longer darkened all day, only at dusk.

## [1.1.0] - 2026-09-15

### Added
- **Ambient occlusion**, darkening the corners and creases the light doesn't reach, from a depth of
  the world drawn a second time at half resolution, without its grass, leaves or the player's own
  weapon, blurred and multiplied into the world at the sky pass alongside the cloud shadows. Switched
  on its own in the Mod Configuration menu.

## [1.0.0] - 2026-09-14

### Added
- **The sky**, worked out from the sun's position in place of the engine's painted dome: blue at
  noon, orange and purple sunsets, a lingering twilight and stars that stay out all night. The
  land's distant fog takes the sky's horizon colour.
- **Volumetric clouds** with silver linings and dark bases, a high cirrus sheet above them, and
  aircraft trails.
- **A sky that follows the game's clock.** The clouds drift with it, so loading a save shows the sky
  that was left, and time spent or slept through moves it on. Every midnight rolls a new set of one
  to three aircraft trails.
- **Storms** thicken the clouds, close the cirrus over the whole sky and grey the light, while the
  land keeps the clear weather's fog.
- **The sun**: light shafts that stop where the clouds stand, a glare over the view when looking
  toward the sun that the clouds cover, and an afterimage after staring at it.
- **Cloud shadows** on the ground, which need depth pass quality high or above.
- **The night**: a smaller moon, unhidden by fog and lighting the clouds, darker blue-grey nights
  with moonlight that throws shadows once the moon is up, and the world's colour draining where it
  is dark while fires and lamps keep theirs.
- **A neutral colour grade** in place of the game's yellow cast, tuned by saturation, contrast,
  brightness, warmth and tint.
- **Every part switchable** in the Mod Configuration menu, every value in `bin\sky-overhaul.ini`, and
  a window of sliders in the DevTools overlay when DevTools is installed.

### Known issues
- The night's colour drain also applies indoors.
