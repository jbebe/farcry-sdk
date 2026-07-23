---
sidebar_position: 8
---

# `.sbao` — Sound Binary Audio Object

:::info[Verified via direct binary analysis]
Derived by direct byte-analysis of 54 real music `.sbao` files (from `sound_english.dat`),
cross-checked against the decoder logic in `tools/Ubitunedec` (the `ldeon/Ubitunedec` source) and
the community workflow in the [FarCry2Crew Steam
guide](https://steamcommunity.com/groups/FarCry2Crew/discussions/6/3182361055544934985/). This is
empirical binary analysis, not Ghidra disassembly — see [Getting Started](../modding/getting-started.md)
for how that differs from the community-sourced pages elsewhere in this section. Tooling lives in
`tools/sbao/sbao_tool.py`.
:::

## There are (at least) two `.sbao` sub-types

`.sbao` = "sound binary audio object", the DARE (Ubisoft's proprietary audio middleware — config in
`Data_Win32/SoundBinary/DARE.INI`, paired with `bin/eax.dll`) container. Two layouts seen:

1. **Long audio (music, dialogue)** — an **Ogg Vorbis** stream in a thin wrapper. This is the one
   documented here and supported by `sbao_tool.py`.
2. **Short SFX** — no `OggS` signature. (Sample: `tools/misc/format-samples/004ae237.sbao`, starts
   `8d 06 08 02`.) **Correction:** an earlier pass through this file assumed this was a Ubisoft ADPCM
   codec (`ubi_v3`/`v5`/`v6`/interleaved) decodable by Ubitunedec, matching the "first byte is the
   stream-version number" signature scheme documented on [the `.spk`
   page](./spk.md#a-side-investigation-does-ubitunedec-know-more-than-we-do). That assumption doesn't
   survive contact with the actual tool: running `UbitunedecCMD.exe`'s structural validators
   (`ubi_v3`/`v5`/`v6`, `ubi_iv2`, `ubi_6or4`) against this real sample rejects it as every one of them
   ("File does not have the correct signature..."), and `-S`/`--scan` (which walks the whole buffer
   looking for a recognized chunk anywhere, not just at offset 0) finds zero matches — while the same
   scan correctly finds the `OggS` chunk in a real Ogg-backed `.sbao` at its documented offset 40,
   confirming the tool itself works. This format is **not** Ubitunedec-decodable and remains
   undocumented; not covered here.

## The Ogg-backed layout: `[40-byte header][verbatim Ogg Vorbis bitstream]`

The header is **byte-identical across every music file except a 16-byte field at 0x08**:

| Offset | Size | Value (retail) | Meaning |
|---|---|---|---|
| 0x00 | 4 | `02 1F 00 10` | constant type/magic marker |
| 0x04 | 4 (u32 LE) | `28 00 00 00` = 40 | **offset to the Ogg payload** (= header length) |
| 0x08 | 16 | *varies per file* | asset GUID — **not** a content hash (verified: ≠ MD5 of payload/whole-file/first-page) |
| 0x18 | 8 | zero | — |
| 0x20 | 4 | `00 00 00 50` | constant (unidentified; possibly a flags/type field) |
| 0x24 | 4 | `02 00 00 00` = 2 | constant here (channel count / stereo) |

The Ogg payload is a **complete, standard Ogg Vorbis bitstream** — first page carries the Vorbis
identification header, last page has the EOS flag set. Nothing about the audio (length, sample rate,
sample count) is duplicated in the wrapper; it all lives in the Vorbis stream itself.

**Consequences for modding:**
- **Decode** = carve bytes `[40:]` to a `.ogg`.
- **Encode/replace** = `original_header[:40] + new_ogg`. No size field, no rate field, and no checksum
  to recompute — reusing the original 40-byte header (GUID and all) is safe because the GUID isn't
  derived from the content.
- A robust reader should still *read* the offset at 0x04 rather than hardcode 40 (all retail files
  use 40, but the field exists for a reason).

## The one real constraint: replacement audio must be 48000 Hz stereo

Every retail music file's embedded Vorbis header is **48000 Hz, 2 channels**. Far Cry 2 plays music
at 48 kHz, so a replacement Ogg at a different rate plays at the wrong speed.

This is the actual explanation for the Steam guide's "reduce the track speed by −8.120% (×0.919)"
instruction for menu music: `44100 / 48000 = 0.91875`. People were exporting **44.1 kHz** Ogg from
Audacity (played too fast at 48 kHz) and compensating by pre-slowing the audio. The correct fix is
simply to **export at 48 kHz** — then no speed adjustment is needed. (The "Jackal tapes need ×2" note
is the same class of bug in the other direction.) `sbao_tool.py repack` refuses a non-48 kHz Ogg for
exactly this reason.

## Known file id

- `004b177b.sbao` = English main-menu theme (confirmed: matches the Steam guide's stated id and the
  user-labelled `main_theme_004b177b.sbao`). Archive-relative path is `soundbinary\004b177b.sbao`
  (consistent with the `soundbinary\<hash>.spk` entries in the community filelists).

## Loose-file override path

Because sound objects are requested through the engine's VFS by relative path (observed in
`modpatcher.log`: `[VFS] passthrough SoundBinary\...`), a repacked file dropped at
`Data_Win32\Loose\soundbinary\004b177b.sbao` is overridden by ModPatcher's Phase 1
`VFS_ResolvePath` hook (and, if requested by hash, the Phase 2 `ArchiveEntry_FindAndOpen` hook). See
[the archives format page](./archives-fat-dat.md).
