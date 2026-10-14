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
| Mission scripts | `domino\user\*.lua` in `worlds.dat` | the missions' internal names |

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
- **The predecessor's side story.** Its script, `sidemissions.carvertapes_missions`, is in world 1's
  dependency list (`world1_depload.xml`). Whether every part of it is reachable in a retail game,
  with its briefings (`5124037`, `5124224`) and the invitation to Sehlakalase Island (`5123973`), has
  not been checked in play.
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
