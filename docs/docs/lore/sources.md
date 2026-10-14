---
sidebar_position: 1
---

# Sources and citations

The lore pages rebuild Far Cry 2's world from the game's own text: what characters say, what the
player's journal says, and what the map prints. Wikis were used only to cross-check, and where a wiki
claim has no basis in the game's text, the pages say so.

## Where the text comes from

| Source | File | What it holds |
| --- | --- | --- |
| String table | `languages\english\oasisstrings.rml` in `patch.dat` | 11,394 strings in 61 sections. `jackall-cli rml decode` reads it. |
| Subtitles | the table's `Subtitles` section | 6,334 spoken lines, keyed by sound id |
| Bark banks | `scripts\game\BarkData\<N>.bank` in `worlds.dat` | who says each line, in which mission, at which moment; see [Bark banks](../file-formats/bark-banks.md) |
| Map pages | `levels\w1_*\mapcompass.xbt`, `levels\w2_*\mapcompass.xbt` in `worlds.dat` | the paper map, 5×5 pages per world, with place names printed on it |
| Mission scripts | `domino\user\*.lua` in `worlds.dat` | every mission's logic: what it asks, how it branches and how it ends; see [Domino scripts](../engine-internals/domino-scripts.md) |
| Mission table | `engine\gamemodes\gamemodesconfig.xml` in `patch.dat` | every mission's id, internal name, act, faction and reward, with comments on cut ones |
| Scene animations | `worlds\<world>\generated\<world>_depload.xml` in `worlds.dat` | which choreographed scene plays each line, so whose line it is |

The bark banks attribute 5,222 of the 6,334 subtitle lines to a speaker and a mission. The other 1,112
are phone calls, scripted scenes, radio broadcasts and multiplayer announcements; for those the pages
name the speaker only when the line itself does, and otherwise mark the speaker as inferred.

The patched table differs from the 1.00 one (`common.dat`) in typo fixes, multiplayer strings and two
added Liberation Radio broadcasts (`5119680`, `5160431`); no story text changed.

The Jackal tapes have no text beyond their titles and one-line descriptions: the recordings are not
subtitled.

## How claims are cited

Each claim ends with where it comes from:

- `4561963` — a subtitle, by its sound id, with the speaker the bark banks record for it:
  (`4561963`, Purefoy). A mission tag such as `A3SM13_SE01` is added when it matters. A speaker
  followed by `?` — (`4577714`, Jackal?) — is a line in no bark bank, whose speaker was identified
  from the scene: the line names the person addressed, or its neighbouring sound ids belong to a
  scene whose speaker is known.
- `Objectives/A1LM06_03` — another string-table entry, as `<section>/<key>`. `LoadScreens` holds the
  player's diary-style chapter synopses, `Objectives` the journal, `Buddies` and `MainMenu` the
  character dossiers, `JackalTapes` the tape descriptions, `Coda` the closing text.
- `map w2_c_3` — a printed label on that map page.
- `a1lm01_copkiller.a1lm01_mission:f_9_Out` — a mission script and the handler in it that does the
  thing: the file `domino\user\a1lm01_copkiller.a1lm01_mission.lua`, function `f_9_Out`. What a
  script does is stated; what it was meant for is implied.
- `scene sm01_se01_jackc_brief_all` — the choreographed animation that plays a line. Its name gives
  the mission, the scene and often the character (`jackc` is the Jackal), so a line it plays has a
  stated speaker even when no bark bank lists it.

Many strings are templates. `FIRST_LT`, `WINNING_FACTION_BOSS`, `PRIMARY_BUDDY_NAME` and the like are
filled in from the player's choices, so a sentence such as "WINNING_FACTION_BOSS betrayed me" is true
for whichever faction the player sided with. The pages keep the template and say what it stands for.

## Stated and implied

Every page separates two kinds of claim:

- **The game states** — a string says it outright. A claim made by a character is reported as that
  character's claim: "Gakumba says the UFLL brought peace" is stated; that the UFLL brought peace is
  not.
- **Implied** — a conclusion drawn from what the strings say, from how the files are organised, or
  from the map. Each implied claim says what it rests on.

## Open questions

- **The Jackal tapes' words.** The recordings are not subtitled. A transcript needs the map from tape
  to sound, which `CJackalTapeManager` holds (`world1.managers.fcb`, 50 resources) and nobody has
  traced, and then a transcription of the audio.
- **The 1,112 unattributed lines.** Their speakers are inferred from the scene. Phone calls take
  their sound from the Domino `PhoneCall` box's `PhonecallSound` input, wired in the mission layers.
  Tracing those wires would turn many inferred speakers into recorded ones.
- **Who sent the player.** The orders cite "the Joint Signatory Framework" (`4577324`). No string
  names the employer, the earlier hunter, or the voice that offers "target of opportunity" contracts
  (`5175655`).
- **The predecessor's side story.** It runs in the `partnerspecialmissions` scripts once a password
  is entered under Additional Content (see [Side missions](./missions/side-missions.md#the-predecessors-trail)).
  The unlock was traced on the Linux server binary only, and the trail has not been played through.
- **The mission scripts.** The pages under [Missions](./missions/overview.md) were read from the scripts,
  not played. What the engine decides on its own, such as which faction wins an act, which buddy it
  assigns, and whether two phone calls issued together both play, is not traced.
- **Reuben's dead interviewee.** "This man I interviewed here, he died a few months ago. These are
  his famous last words" (`4911521`) does not fit the living Jackal.
- **Names.**
  - Is "Kgosi" a family name or a title?
  - Why is the capital "Mosate Selao" on the map and "Port Selao" in speech?
  - What does the Underground's internal name, GRIN, stand for?
- **The "party leader".** Gakumba answers to an unnamed "party leader" (`4558591`). Mbantuwe is the
  likely man, but nothing names him.
- **Buddy outcomes.** The meanings of the `WAG_*_SRV`, `_LOS` and `_ABN` banks, and of the `IL=ILL`
  and `IL=ILH` gates (low and high reputation), are read from the lines' content. The code that sets
  them is not traced.
- **Other languages.** Only the English table was read. The other nine may name or phrase things
  differently.
- **Leftovers.** `Objectives/UbidaysLongDemo_*` is a demo version of the Weelegol radio and pipeline
  jobs. The multiplayer line "Good! Now capture their canister!" (`5172698`) points to an older
  objective than the diamond case.
