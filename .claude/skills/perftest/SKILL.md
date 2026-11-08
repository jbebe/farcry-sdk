---
name: perftest
description: Use when asked to measure what a mod costs in performance (frame rate, CPU, GPU, memory, VRAM) - resets the GOG install to FCSE, UFCP and DevTools, has the user play a session without the mod and one with it, and compares DevTools' session logs
---

# Perftest

Measures one mod's cost. The GOG install (`C:\Games\Far Cry 2`) is reset to FCSE, UFCP and DevTools;
the user plays a session; the mod is added; the user plays the same again; and the two
[Session logs](../../../mods/DevTools/README.md) are compared.

`perftest.ps1` makes every change to the install and `compare.py` does the analysis; both sit beside
this file. The user plays the sessions: never launch the game for one yourself.

## 1. The mod

- The argument is a folder under `mods/`; ask if it is ambiguous. It needs a `layer/`. A mod without
  one (`legacy-plugin`) cannot be tested this way: say so and stop.
- Read its README for mods it needs, such as Tales of Africa reading in Inventory Overhaul's notebook.
  Those go into both sessions with `-With`, so the difference is the mod alone.
- `git status -- mods/<mod>`: name any uncommitted work, since that is what gets measured.
- The game must be closed.
- If the user says how long a session will be, that is its length in play; otherwise 30 s, the
  length the user plays by design. The comparison scales its blocks to it. A run counts as short only
  below three quarters of its length; one run per side is enough for a small change.

## 2. Build

The script builds nothing, so every layer must be current. For DevTools, UFCP, each `-With` mod and
the mod, run its `build.ps1` with no arguments, plus any layer step its README names
(`build-layer.ps1`, `build_layer.py`). **Never pass `-Install`**: for a layer mod it rebuilds the
patch from that one layer and drops the others (`/deploy` step 2 lists which mods). Sky Overhaul
stages its DLL into the layer only under `-Install`, so copy it yourself as `/deploy` says. Log each
build to a file in the scratchpad and read the tail.

## 3. Reset

```
powershell -File .claude\skills\perftest\perftest.ps1 -Step reset -Mod <mod> [-With <mod>,<mod>] [-Length <seconds>]
```

- Backs up the patch and `bin\plugins` into a new run folder under `C:\Games\Far Cry 2 - perftest\`.
- Deploys DevTools and UFCP, and any `-With` mods, and removes every other plugin. The backup keeps
  them.
- Turns DevTools' Session log on.
- Refuses if the last run was never restored. Ask the user whether to restore it first or pass
  `-Force`.

It prints UFCP's Maximum frame rate and the game's vsync. If either caps the frame rate, tell the
user before the session:
- While the game sits at the cap, frame time cannot show a cost. Only GPU time and engine CPU per
  frame can.
- Recommend Unlocked, on UFCP's page in the Mod Configuration Menu, and vsync off. UFCP caps at 60 Hz
  because physics misbehave above it, which matters when testing a vehicle mod.
- Leave the choice to them; whatever they pick has to hold for both sessions.

## 4. Session without the mod

Hand over, then stop and wait:
- Launch the game through FCSE and load a save.
- Play the session's length of what the mod touches: drive for a vehicle mod, fight for a combat mod,
  look around for a graphics mod. It has to be repeatable: the same save, route and actions both
  times.
- Set Storm and Rain to Off in the DevTools overlay (Home), on the Environment tab. A storm in one
  session and not the other moves the frame rate more than most mods do.
- Keep the same graphics settings and window size in both sessions. Quit to the desktop at the end.

When the user is done, run `-Step collect`. It moves the log into the run folder and prints its time
in play. If there is no log, or it is marked short, say so and have them play again. If there
is no log, check `Session log = true` under `[DevTools]` in `bin\fcse.ini` and `DevTools loaded` in
`bin\fcse.log`.

## 5. Session with the mod

Run `-Step mod` and check that the printed plugins include the mod's DLL, if it has one. Then hand
over the same instructions: the same thing as before. Wait, then `-Step collect`.

## 6. Evaluate

```
python -I .claude\skills\perftest\compare.py "<run folder>"
```

Read its output and look at `report.png`; both are saved in the run folder. For each resource, tell
the user what the mod costs and whether that is more than noise:
- **Frame rate and frame time**, and what bounds the frame: the GPU, the engine thread, or a cap.
- **GPU** time per frame.
- **CPU**:
  - Engine thread CPU per frame is the main thread's work in ms. It compares even when the two frame
    rates differ.
  - Process CPU covers every thread: sound, streaming, physics jobs.
- **Stutter**: the slowest 1% and hitches per minute. Neither has an interval, and small counts are
  noisy.
- **Memory**:
  - Committed and private peaks.
  - The lowest largest free block. The 32-bit process fails an allocation that no block fits, so
    below 256 MB is a warning and below 128 MB a danger.
  - The VRAM peak.

  Peaks follow where the session went, so a few tens of MB either way is not the mod.

Read the segment the mod works in, such as "in a vehicle" for a vehicle mod. "All play" mixes on foot
and driving, so its interval is wider. Pass on every warning the script printed: uneven time in a
segment, different settings, an unexpected plugin difference. Give each change in ms per frame and in
percent.

If a change that matters comes out as noise with a wide interval, offer another round:
1. `-Step switch -State baseline`, a session, `-Step collect`.
2. `-Step switch -State mod`, a session, `-Step collect`.
3. Compare again. It reads every `baseline-*.csv` and `mod-*.csv` in the folder.

## 7. Put the install back

Ask whether to restore the setup from before the reset. `-Step restore` puts back the patch, every
plugin and the Session log setting. A run folder holds three copies of the patch, about 150 MB each:
say so, and leave deleting old ones to the user.
