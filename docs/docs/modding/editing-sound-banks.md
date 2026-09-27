---
sidebar_position: 7.5
---

# Editing sound banks

A sound field in an `.fcb` holds an **event id**. The game plays it by opening
`soundbinary\<id:08x>.spk` and running the event of that id, which plays a resource, which ends in
audio. `jackall-cli spk` turns a bank into an XML document you can edit and build back. With it you
can add variations, make a sound random, change gains and falloff, or author a bank from scratch. The
byte layout behind all of it is on the [`.spk` page](../file-formats/spk.md).

:::info[What has been heard in game]
Banks named after a new event id, their own rolloff curves and multi-events were all built this way
for `mods/sound-overhaul` and heard (2026-09-26/27). A random container built by the tool has **not**
been heard yet.
:::

## The round trip

```
jackall-cli spk decode soundbinary\00448bd2.spk -o passby      # passby\00448bd2.xml + audio files
jackall-cli spk encode passby\00448bd2.xml -o 00448bd2.spk     # checks, then builds
jackall-cli spk verify soundbinary\00448bd2.spk               # decode + encode, byte for byte
```

Every retail bank (7,513 of them) rebuilds byte for byte, so an unedited decode changes nothing. The
built bank goes in your layer at `mods/soundbinary/<id>.spk`.

## What is in a bank

One element per record, in file order:

| Element | Record | What you edit |
| --- | --- | --- |
| `<Play>` | event type 1 | `sound` (the resource it plays), `rolloff` (the distance curve; none means unpositioned) |
| `<MultiEvent>` | event type 12 | `<Child event>` per event it starts, all at once |
| `<SwitchEvent>` | event type 11 | `group`, `default`, `<Case value event>` per switch value |
| `<StopNGo>` | event type 4 | `stop`, `play` |
| `<SetReverb>` | event type 8 | `effect` |
| `<Event type>` | any other event type | `target` |
| `<Sample>` | resource kind 1 | `audio`, `gainDb`, `loop` |
| `<Random>` | resource kind 4 | `<Choice resource weight>` per variation, `silence` |
| `<Switch>` | resource kind 3 | `group`, `default`, `<Case value resource>` per switch value |
| `<Multilayer>` | resource kind 7 | `<Layer resource>`, each with `<Curve target parameter>` of `<Point x y>` |
| `<Resource kind>` | kinds 2, 6, 8 | not identified; kept as they are |
| `<Audio>` | the audio stream | `file`: `.ogg` stored as is, `.wav` (16-bit PCM) encoded to IMA-ADPCM, `.ima` a raw stream that needs `rate` |
| `<Rolloff>` | a distance curve | `<Point m db>`, distances increasing |

Every other sub-header word appears as `wN="0x…"` where it differs from the usual value. Those words
are not identified; copy them from a retail record that behaves like yours. `key` holds 16
unidentified bytes. Leave it out on a new record and the tool derives one from the id. `preamble`
lists the bank's own id and the banks that pull it in.

You never write a length, a count or an offset. On encode the tool derives:
- each sample's byte and frame lengths, rate, channels and codec from its audio file;
- every child count;
- every tail offset.

That includes the playback length the game [cuts the audio at](../file-formats/spk.md#playback-length-comes-from-the-descriptor).
A `wN` on one of those words overrides the derivation. Decode writes them only where a retail bank
disagrees with it.

## Making a sound random

A `<Random>` picks one `<Choice>` each time it plays:

```xml
<Random id="0x00fc0201" silence="1">
    <Choice resource="0x00fc0202" />
    <Choice resource="0x00fc0204" weight="2" />
</Random>
```

- Weights are relative. Here the two choices and `silence` weigh 1, 2 and 1: a quarter of the time
  the first plays, half the time the second, and a quarter of the time nothing plays. Weights
  written as probabilities that sum to 1 are read as they stand. Choices without a weight weigh 1.
- **The choice that played last is skipped** on the next play, so two variations already alternate.
  A `<Choice repeat="true">` may play twice in a row.
- **Silence never comes twice in a row**, unless the container has `repeatSilence="true"`.
- **`sequence="true"` plays the choices in order** instead, from a random start per emitter, and
  ignores the weights and silence.

These rules are traced in `Dunia.dll`; see [how a random container picks](../file-formats/spk.md#how-a-random-container-picks).

The quickest route is `spk new`, which scaffolds an event over one clip, or a random container when
given several:

```
jackall-cli spk new 0x00fc0200 shot_a.wav shot_b.wav shot_c.wav --rolloff 0x00442c37 ^
    --like soundbinary\00455359.spk
jackall-cli spk encode 00fc0200\00fc0200.xml
```

Ids run on from the event's: the container, then a sample and its audio per clip. `--like` copies the
unidentified words of a retail event and sample, which is how a weapon shot should start. The audio
is copied in beside the XML, so the folder is the source you keep.

To randomise a retail sound instead, decode its bank. Add a `<Sample>` and an `<Audio>` per new
variation. Then put a `<Random>` between the event and the samples, or add `<Choice>`s to the one
that is already there. Retail impacts are a material `<Switch>` over random containers, which is
where a ricochet variant goes (`004565a3.spk`).

## What `spk encode` checks

- **Errors:** a reference to a record of the wrong kind (an event where a resource belongs), an
  `.ima` without a `rate`, audio that is neither Ogg nor IMA-ADPCM, and a rolloff whose distances
  go backwards.
- **Warnings:**
  - a container with no children;
  - an event bank with no record of the id it is named after;
  - a `<Play>` with a rolloff over a stereo sample. Stereo plays unpositioned, so the curve is
    ignored **(heard, 2026-09-27)**.
- **Notes:** ids the bank points at but does not hold. Another loaded bank has to supply them.

## How the game finds a bank

- **The file is named after the event the `.fcb` field names.** Every retail bank that holds events
  contains a record with its own id **(seen in data)**. A bank of samples or curves is named by a
  variant id instead.
- **A new bank named after a new event id loads without a `depload` entry** when an `.fcb` field
  names that id. The G3KA4's shots in `mods/sound-overhaul` are such banks **(heard, 2026-09-26)**.
- **Keep everything a new event reaches in its own bank.** An id reached only from inside another
  bank must already be loaded, and the lookup has no load-on-miss path (see
  [`depload`](../file-formats/spk.md#what-loads-the-child-bank-depload)).
- **Rolloff curves can live in your own bank** beside the event that uses them **(heard,
  2026-09-27)**. The retail curves live in `common/soundbinary/2fffffff.spk`.
