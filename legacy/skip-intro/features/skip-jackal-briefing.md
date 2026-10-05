---
title: Skip the Jackal's briefing
kind: component
bundle: skip-intro
claims:
  - "Skip the Jackal's briefing in the hotel room"
status: located
systems: [missions]
match:
  - "domino/user/a1sm01_townescape.a1sm01_mission.lua@L{1784,1785}"
exclude: []
requires: []
verified: diff
---

# Skip the Jackal's briefing

The town escape opens on the player lying in the hotel bed while the Jackal talks for about a minute
and a half. With this change the Jackal is gone and the screen whites out at once, into the
player waking up.

## How

`domino/user/a1sm01_townescape.a1sm01_mission.lua`, in `f_146_Started`. Box `146` is the PlayAnim
that starts the Jackal's `sm01_se01_jackc_brief_all`. Its `Started` starts Delay `150` and Delay `151`.

- `@L1785`: Delay `150` goes from 96 s to 0.1 s. When it elapses, `f_150_TimeElapsed` enables
  `whitescreenfx` and moves on to the wake-up (`f_149_Out`).
- `@L1784`: four inserted lines, unconditional. They run before the rest of the handler and remove
  two entities of sector 3160 (`levels/w1_c_3`, Pala):
  - `RemoveEntity("2057061720366558035")` removes `teh_jackal_6`, the Jackal himself
    (`buddies.Jackal.Jackal_SpecialCharacter`).
  - `RemoveEntity("2057061720393821051")` removes `World1_HotelRoom.HotelRoom_SingleSheet_4`.
  - Two calls are left commented out: the Jackal's dossier `Props.JackalDossier_20` and the handgun
    `World1_HotelRoom.HotelRoom_HandGun_5`, which stay in the room.

The two hunks go together. The removal alone leaves 96 s of an empty room. The shorter wait alone
leaves the Jackal standing in the room mid-animation until the scene is cleared.

`jackall-cli mod lint` warns that the inserted lines are `unrepresented-statement`s, meaning the
graph view does not show them, and that the `.debug.lua` twin is stale. Neither matters at runtime.

## Uncertain

- Delay `151` (89 s) is unchanged. It and the Delay `170` (8 s) it starts still play two ceiling
  fan cues (`0x004e1f93`, `0x004e1f95` on `SoundPoint_A1SM01_SE01_fan`) well after the briefing they
  were timed to. The music started with the briefing is unchanged too.
- Vanilla clears the scene itself once the wake-up's Delay `166` elapses. It turns off the
  `Missions/StoryMissions/A1SM01/A1SM01_SE01_Actors` layer (see `hotel-wake-up-sooner`), so the
  removal probably only keeps the Jackal out of the moments before that. Whether `teh_jackal_6` is
  in that layer has not been checked.
