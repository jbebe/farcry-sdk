---
sidebar_position: 8
---

# `.sbao` — Sound Binary Audio Object

:::info[Verified via direct binary analysis and reverse engineering]
The Ogg-backed layout is derived by direct byte-analysis of 54 real music `.sbao` files (from
`sound_english.dat`), cross-checked against the community workflow in the [FarCry2Crew Steam
guide](https://steamcommunity.com/groups/FarCry2Crew/discussions/6/3182361055544934985/). Tooling
lives in `tools/sbao/sbao_tool.py`. The short-SFX codec is identified via GhidraMCP against
`Dunia.dll` — see [the `.spk` page](./spk.md#the-audio-codec-ima-adpcm) for the full writeup, since
it's the same underlying DARE data reached through a different container path.
:::

`.sbao` = "sound binary audio object", a standalone (non-bank) DARE resource file — the same
"atomic"/"streamed" object model documented on [the `.spk` page](./spk.md), just saved to its own
file (`<id:08x>.sbao` or `.bao`) instead of packed into a bank. Two layouts exist:

## Long audio (music, dialogue): `[40-byte header][verbatim Ogg Vorbis bitstream]`

An **Ogg Vorbis** stream in a thin wrapper. The header is byte-identical across every music file
except a 16-byte field at `0x08`:

| Offset | Size | Value (retail) | Meaning |
|---|---|---|---|
| `0x00` | 4 | `02 1F 00 10` | constant type/magic marker — the same one `.spk` records use |
| `0x04` | 4 (u32 LE) | `28 00 00 00` = 40 | **offset to the Ogg payload** (= header length) |
| `0x08` | 16 | *varies per file* | asset GUID — **not** a content hash (verified: ≠ MD5 of payload/whole-file/first-page) |
| `0x18` | 8 | zero | — |
| `0x20` | 4 | `00 00 00 50` | constant (unidentified; possibly a flags/type field) |
| `0x24` | 4 | `02 00 00 00` = 2 | constant here (channel count / stereo) |

The Ogg payload is a **complete, standard Ogg Vorbis bitstream** — first page carries the Vorbis
identification header, last page has the EOS flag set. Nothing about the audio (length, sample rate,
sample count) is duplicated in the wrapper; it all lives in the Vorbis stream itself.

**Consequences for modding:**
- **Decode** = carve bytes `[40:]` to a `.ogg`.
- **Encode/replace** = `original_header[:40] + new_ogg`. No size field, no rate field, and no checksum
  to recompute — reusing the original 40-byte header (GUID and all) is safe because the GUID isn't
  derived from the content.
- A robust reader should still *read* the offset at `0x04` rather than hardcode 40 (all retail files
  use 40, but the field exists for a reason).

**The one real constraint: replacement audio must be 48000 Hz stereo.** Every retail music file's
embedded Vorbis header is 48000 Hz, 2 channels; Far Cry 2 plays music at 48 kHz, so a replacement Ogg
at a different rate plays at the wrong speed. This is the actual explanation for the Steam guide's
"reduce the track speed by −8.120% (×0.919)" instruction for menu music: `44100 / 48000 = 0.91875`.
People were exporting 44.1 kHz Ogg from Audacity (played too fast at 48 kHz) and compensating by
pre-slowing the audio. The correct fix is simply to **export at 48 kHz** — then no speed adjustment
is needed. `sbao_tool.py repack` refuses a non-48 kHz Ogg for exactly this reason.

## Short SFX: IMA-ADPCM

No `OggS` signature (sample: `tools/misc/format-samples/004ae237.sbao`, starts `8d 06 08 02` — notably
*not* the `02 1F 00 10` magic the long-audio layout and `.spk` records share, so this sub-type's outer
envelope hasn't been directly confirmed to match `.spk`'s `FlatCopy` record shape byte-for-byte).

The codec itself is confirmed: **standard IMA-ADPCM**, the same DARE `TImaAdpcm` data as `.spk`'s
`FlatCopy` records, found via direct byte-search of `Dunia.dll` for the canonical IMA-ADPCM tables and
verified by decoding real payloads end-to-end into playable audio. Full writeup — table addresses,
the two decoder functions, the 28-byte per-stream header layout, and the verification — is on
[the `.spk` page](./spk.md#the-audio-codec-ima-adpcm).

This is a different, unrelated codec family from Ubisoft's older in-house "Ubi Sound Tools" ADPCM
dialects (`ubi_v3`/`v5`/`v6`/interleaved, decodable by the third-party tool `Ubitunedec` and used in
older titles like *XIII* and *Splinter Cell*) — confirmed by running `Ubitunedec` directly against a
real short-SFX sample: every one of its structural decoders rejects the file on its own signature
check, and its buffer-scan mode finds no recognizable chunk anywhere in it (while correctly finding
the `OggS` chunk in a real long-audio sample, confirming the tool itself works). Genuinely just two
unrelated codecs that both happen to be "an ADPCM."

## Known file id

- `004b177b.sbao` = English main-menu theme (confirmed: matches the Steam guide's stated id and the
  user-labelled `main_theme_004b177b.sbao`). Archive-relative path is `soundbinary\004b177b.sbao`
  (consistent with the `soundbinary\<hash>.spk` entries in the community filelists).

## Loose-file override path

Because sound objects are requested through the engine's VFS by relative path (observed in
`modpatcher.log`: `[VFS] passthrough SoundBinary\...`), a repacked file dropped at
`Data_Win32\Loose\soundbinary\004b177b.sbao` is overridden by ModPatcher's Phase 1
`VFS_ResolvePath` hook (and, if requested by hash, the Phase 2 `ArchiveEntry_FindAndOpen` hook) — the
identical resolver `.spk` bank loading uses, confirmed by tracing both code paths. See
[the archives format page](./archives-fat-dat.md).
