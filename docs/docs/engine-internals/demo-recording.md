---
sidebar_position: 21
---

# Demo recording and playback

:::info[Verified via reverse engineering]
The recorder, its key binding and the three file layouts are traced in `Dunia.dll` and checked
against the two demos `FC2BenchmarkTool.exe` ships. See [the overview](./overview.md) for binary
identification.
:::

:::warning[Not yet tested in a running game]
Neither recording with <kbd>Numpad 8</kbd> nor replaying a demo through `-benchmark playback` has been
tried on a retail install.
:::

The benchmark's two "Playback" demos, *Action Scene* and *Demo Ranch*, are not camera paths. They are
input recordings made in ordinary gameplay by a recorder that is still in the retail engine: a save
of the starting state, the player's input actions frame by frame, and the player's transform frame by
frame.

## Recording

The input signal `toggle_recording` (CRC32 `0x72543291`) starts and stops the recorder. Retail data
binds it in the `common_system` action map of `config\inputactionmapcommon.xml`, next to the QA dump
keys:

```xml
<Binding input="kb:numpad8" action="press" signal="toggle_recording"/>
```

The input handler (at `0x1071b485`) compares the signal against that hash, passes two further
checks that are not identified, and calls the toggle `FUN_1067b6f0`. The toggle does nothing unless
the byte `DAT_10f9a2b8` is set; it is `1` in the shipped image, and only playback mode clears it.

Starting a recording shows `Recording Playback...` on screen and writes three files with one base
name, `Playback YYYY-MM-DD HH-MM-SS`, under `<dir>\Benchmarks\Playbacks\`, where `<dir>` is the path
string at `DAT_10ff0e94 + 0x94`. Playback resolves demo names through
`GameFileUtils::GenerateRelativeFileName` mode `1`, `Benchmarks\Playbacks\`, the same function that
puts saves under `Documents\My Games\Far Cry 2\`, so both are expected to meet in
`Documents\My Games\Far Cry 2\Benchmarks\Playbacks\`:

| File | Written | Contents |
|---|---|---|
| `.bsav` | once, at the start | a save of the current game state |
| `.npl` | every frame | the input actions fired that frame |
| `.ppl` | every frame | the player's position and two angle triples |

Pressing the key again stops the recording, and that press is itself recorded as the last entry in
the `.npl`. The recorder also stops itself from the game-mode update and from the per-frame recording
handler, under conditions that are not identified.

## Playback

`-benchmark playback` loads a demo by its base name:

```
FarCry2.exe -benchmark playback -world world1 -benchmarkinputname "<name>"
```

`-world` is required only to pass the harness's usage check. The other
[benchmark flags](./command-line-args.md#benchmark-harness-createbenchmarknode-entered-whenever--benchmark-is-present)
(`-benchmarkloop`, `-benchmarkfixedframerate`, `-benchmarkdisableai`) apply as usual.
`FC2BenchmarkTool.exe` names its demos `Demo_CPU` and `Playback_Demo`, and carries both as embedded
.NET resources (`Benchmark.Demo_CPU.bsav` and so on).

Playback setup (`FUN_1067ba60`) clears the recording gate, opens the `.ppl` for reading and hands the
`.npl` to its reader. On each playback event the per-frame handler (`FUN_1067be90`) reads one `.ppl`
record and writes it back onto the player: position through the entity, the two angle triples into
the pawn's data at `+0x38` and `+0x44`. The player is therefore held on the recorded path, while the
`.npl` supplies what the player did.

## `.bsav`

The first two sections of a [`.sav`](../file-formats/savegame.md), followed directly by the
embedded `.fcb` (`FCbn` at `0x57` in both shipped demos). There is no thumbnail.

| Offset | Size | Value in both shipped demos |
|---|---|---|
| `0x00` | 4 | u32 `10` |
| `0x04` | 4 | u32 `0x63af73f3` |
| `0x08` | 12 | 3 floats, the start position (matches the first `.ppl` record) |
| `0x14` | 4+6 | length-prefixed `"world1"` |
| `0x1E` | 4+13 | length-prefixed `"Marty_Alencar"` |
| `0x2F` | 12 | 3 × u32: `1`, `0`, `1` |

## `.npl`

```
u32    frameCount      rewritten when the recording stops
f32    unknown         449.94 in Demo_CPU, 1372.31 in Playback_Demo
frame[frameCount]:
  u32  entryCount      0 for a frame with no input
  entry[entryCount]:
    u32  signal        CRC32 of the action-map signal name
    u32  type          1 = one f32 follows, 2 = two f32 follow
    f32  value[type]
```

Both shipped demos parse to the last byte with this layout, and their frame counts match the header.
The signals they contain:

| Signal | `Demo_CPU` | `Playback_Demo` |
|---|---|---|
| `look` (type 2) | 606 | 748 |
| `move` (type 2) | 58 | 46 |
| `startshooting` / `stopshooting` | – | 2 / 2 |
| `cycleweapon` | – | 2 |
| `throw_grenade` | 2 | 2 |
| `toggle_recording` | 1 | 1 |

## `.ppl`

A flat array of 36-byte records, one per frame, no header: `frameCount - 1` records in both shipped
demos.

| Offset | Size | Field |
|---|---|---|
| `0x00` | 12 | position (3 floats) |
| `0x0C` | 12 | angle triple, pawn data `+0x38` (`+0x54` when bit `4` of its flags is set) |
| `0x18` | 12 | angle triple, pawn data `+0x44` |

## Unknowns

- Whether <kbd>Numpad 8</kbd> reaches the handler in retail, since the `common_system` map may not be
  active, and what the handler's two extra checks test.
- How faithfully AI, physics and other randomness replay. Only the player is held to the recording.
- The `.npl` header float, the `.bsav` u32 at `0x04`, and what separates the two angle triples.
