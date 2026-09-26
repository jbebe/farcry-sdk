---
sidebar_position: 22
---

# Bark banks — `.bank`, `.pfe` and bark `.spk`

:::info[Verified via reverse engineering and against the retail files]
Traced via GhidraMCP in the symbolized `FarCry2_server`, which carries the same bark code as
`Dunia.dll` under readable names (`CFCXBarkManagerService`, `CBarkResourceContainer`); addresses are
that binary's. Counts are over the retail files: 862 `.bank`, 860 `.pfe`, 860 bark `.spk` and the
863-entry bank list.
:::

NPC speech — generic mercenary chatter, buddy lines, mission dialogue — plays from **bark banks**. A
bank is three files sharing one decimal number `N`:

| File | Archive | Holds |
| --- | --- | --- |
| `scripts\game\BarkData\<N>.bank` | `worlds.dat` | the bark table, an [FCB](./fcb.md) |
| `scripts\game\BarkData\<N>.pfe` | `worlds.dat` | facial animation, one entry per line |
| `scripts\game\BarkData\loc\<N>.spk` | `worlds_english.dat` | the recorded lines, an ordinary [sound bank](./spk.md) |

Only the `.spk` is localized. These are the only `.spk` files in the game not named by a hex sound id.
`scripts\game\BarkData\SPBarkData.banklist`, in `common.dat`, is an FCB listing every bank number as a
`Bark` object with one `Bank` value. `jackall-cli fcb decode` reads the `.bank` and the `.banklist` as
they are.

## The bank number

`N` packs four fields (`CFCXBarkManagerService::GetBankIndex`; `GetBankInfoFromIndex` is its inverse):

```
N = type × 42900 + character × 825 + mission × 5 + variant
```

| Field | Range | Engine enum |
| --- | --- | --- |
| type | 0–44 | `EBarkBankType` — see [bank types](#bank-types) |
| character | 0–51 | `ESpecialCharacterType` — see [characters](#characters) |
| mission | 0–164 | `EMission` — see [missions](#missions) |
| variant | 0–4 | — see [variants](#variants) |

Every retail bank number decodes to in-range fields. `1436645` is type 33, character 25
(`Nasreen_Davar`), mission 64 (`A2LM08_SE02`), variant 0.

## Bank types

Types come in threes. A base type `t` is loaded together with `t+1` while the player's infamy is low
or `t+2` while it is high. `CFCXCountersComponentPlayerSP::IsInHighInfamy` calls infamy high from the
midpoint of the current act's infamy range up. Retail ships the infamy pair for only some groups.

| Types | Retail banks | Loaded for | Speakers | Example event tags |
| --- | --- | --- | --- | --- |
| 0 / 1 / 2 | 3 / 3 / 3 | mental state 0–1 | `GAP` | `EIN`, `ENO`, `ENM`, `DL1` |
| 3 / 4 / 5 | 1 / 0 / 0 | always | `GAA` | `DTH`, `PI2`, `ST1`, `BN1` |
| 6 / 7 / 8 | 16 / 0 / 0 | buddies | buddies | `BDP`, `BDD`, `BDX`, `BDH` |
| 9 / 10 / 11 | 36 / 0 / 0 | buddies | buddies | `BRN`, `BW1`–`BW8` |
| 12 / 13 / 14 | 12 / 0 / 0 | buddies | buddies | `BWH`, `BWW`, `BWS`, `PH01` |
| 15 / 16 / 17 | 5 / 5 / 5 | mental state 0–1 | `GAP`, `GAK`, `GAZ` | `DGI`, `DFE`, `DMB`, `DTS` |
| 18 / 19 / 20 | 3 / 3 / 3 | always | `GMD`, `GAP` | `AWE`, `AKR`, `HFX`, `HFA` |
| 21 / 22 / 23 | 2 / 2 / 2 | always | `GMD`, `GAP` | `AKR`, `ADF`, `PCL4`, `WAL4` |
| 24 / 25 / 26 | 3 / 3 / 3 | mental state 2–4 | `GAP` | `EIN`, `ENO`, `DCD1` |
| 27 / 28 / 29 | 1 / 1 / 1 | always, [external loading](#external-loading-types-2729) | `GAA` | `CVSUBV`, `CVTASK`, `CVBIOM` |
| 30 / 31 / 32 | 2 / 2 / 2 | mental state 2–4 | `GAP` | `PBL2`, `PCL3`, `DEL3` |
| 33 / 34 / 35 | 589 / 35 / 41 | every speaker, per mission | mission characters, buddies | `GREET`, `DIALA`, `EXITA`, `BECKN` |
| 36 / 37 / 38 | 3 / 0 / 0 | mental state 2–4 | `GAA`, `GAB` | `ILG` |
| 39 / 40 / 41 | 36 / 0 / 0 | buddies | buddies | `BCTR`, `BCTD`, `BCQE`, `BCGW` |
| 42 / 43 / 44 | 36 / 0 / 0 | buddies | buddies | `BFW` |

Type 33 is the bulk: dialogue for one character in one mission.

## When a bank is loaded

`UpdateBarkBankLoading` (`0x087b6660`) rebuilds the set of resident banks on every update:

1. **Always:** types 3, 18, 21 and 27, each with its infamy pair, as character 0, mission 0.
2. **Per registered speaker** with character `c`: type 33, plus 6, 9, 12, 39 and 42 when `c` is a buddy
   (18–29), plus the mental-state types for that speaker's own state, each with its infamy pair. Each
   type loads once per active mission and, when `c` is not 0, once for mission 0.
3. **Crowd chatter:** one mental state stands for all nearby mercenaries: the first of 0, 1 or 2 that
   at least two mercs within range share, otherwise the nearest merc's. Its types load as character
   0, mission 0. With no speaker registered the state is 4.

The mental-state types (`AddMentalStateBaseBanks`) are 0 and 15 for states 0–1, and 24, 30 and 36
for states 2–4. `CPawnAgent::GetBarkMentalState` (`0x08574b80`) derives the state from the agent's
army-member state; 6 means dead, and states 5 and 6 never become the crowd state.

A mission becomes active through the Lua call `LoadMissionBarkBank(tag)` (see
[Lua API surface](../engine-internals/lua-api-surface.md)). `GetMissionFromTag` turns the tag into
its `EMission` number from the [table below](#missions).

### Variants

The variant is an alternative recording set of the same bank. Per-speaker banks always load variant
0. The always-loaded and crowd banks load their type's current variant, and `UpdateVariationBanks`
(`0x087b6240`) rotates it: once the type's duration (`GetVariationBankDuration`) runs out, the
variant moves to the next number if that bank exists (inferred: the manager's bank list, read from
`SPBarkData.banklist`), otherwise back to 0, and the bank's played flags are reset. Retail ships 1,
2, 3 or 5 variants of each always-loaded and mental-state type.

## `.bank` — the bark table

An [FCB](./fcb.md) whose root holds `Bark` objects. Tags are hashes; each has a
[`text_` twin](./fcb.md#member-names-and-their-text_-twins) carrying the readable tag.

```
Bark
  BarkEventTag, SourceActorTag, TargetActorTag
  IsGeneric
  EnvironmentStates, SourceActorStates, TargetActorStates
    State { StateTag, StateValueTag }
  BarkVersions
    { HasPlayed, BarkLines }
      Line { SpeakerName, LookAt, GestureTarget, FacialEmotion, Gesture,
             VariationBank, ApplyLipSync, BarkSoundIDs [ SoundID ] }
```

What the 5,141 retail barks contain:

- **Speakers.** `SourceActorTag` and `SpeakerName` are three-letter codes, one per
  [character](#characters). The target is mostly the player, `PLY`.
- **Conditions.** A `MIS` state tests a [mission](#missions) tag. The most common other tags are
  `TK`, `AIS` and `TL` on the speaker and `IL` and `STR` on the target. All 15 environment tags carry
  the value `NA`.
- **Performance.** `FacialEmotion` names an expression (`relaxed`, `mefiant`, `happy`, …) and `Gesture`
  an animation (`bodywhawha_gestlayer_a0_intlow`, `pawn_gesture_wave2`, …).
- **Lines.** A bark has 1–7 versions (88% have one), and a version 1–12 lines. `BarkSoundIDs` hold
  13,281 ids across the banks. Apart from 102 `-1` placeholders, which the loader skips, every one is a
  sound event in the same-numbered `.spk`.

How the manager picks a version, and what several SoundIDs on one line are for, is not traced.

## `.spk` — the recorded lines

The container is the ordinary [`.spk`](./spk.md): sound events, the resources they play and the
audio. `CBarkResourceContainer::SetBankIndex` (`0x09193140`) loads the whole file as a
`CSoundResource`, so every line registers when the bank does. `jackall-cli spk list` reads them
unchanged. A line used by several banks is stored in each: 729 sound ids appear in more than one bark
`.spk`.

### External loading: types 27–29

For types 27, 28 and 29, `GetBarkResource` (`0x087b2c20`) puts the bank in external loading mode.
`SetBankIndex` then loads neither `.spk` nor `.pfe`, which is why banks `1158300`, `1201200` and
`1244100` ship neither. `LoadBarkResource` (`0x09191570`) adds each line's SoundID one at a time
instead (`CSoundResource::AddFromSoundId`, `CFaceAnimResource::AddFaceAnim`) **(inferred: its flag
argument is the external mode)**. Of their 1,225 SoundIDs, 946 exist only in hash-named `soundbinary`
banks, 277 are inside other bark `.spk` files, and 2 are in no retail file.

## `.pfe` — facial animation

`SetBankIndex` loads it as a `CFaceAnimResource`. It starts with `01 00 65 46`, then a `u32` count
and that many `u32` keys. The count equals the number of sound events in the same-numbered `.spk` in
all 860 pairs. The keys are not the SoundIDs, and the data after the key table is not decoded.

## Missing from extractions: `1436645.bank`

The bank list names `1436645`, and its `.spk` and `.pfe` ship, but its `.bank` path has the CRC32
`4A724578`, the same as `levels\ige_map\generated\sdat\sd10_shadow.xbt`
([the collision](../engine-internals/asset-reachability.md#what-would-change-these-numbers)).
One hash is one archive entry, and the `worlds.dat` entry holds the bank: it starts with FCB magic
`nbCF`, not `TBX`. An extractor that names files from a list that includes the texture path writes the
bank out as `sd10_shadow.xbt`.

## Characters

The name is what `GetSpecialCharacterName` returns. The speaker tag is what the retail banks use for
that character.

| # | `ESpecialCharacterType` | Speaker tag | Banks |
| --- | --- | --- | --- |
| 0 | `None` | generic: `GAP`, `GAA`, `GAK`, `GAZ`, `GAB`, `GMD`, `GAW`–`GAY` | 61 |
| 1 | `Player` | — | — |
| 2 | `UFLL_Joaquin_Carbonell` | `RL2` | 10 |
| 3 | `UFLL_Leon_Gakumba` | `RC1` | 5 |
| 4 | `APR_Nick_Greaves` | `BL3` | 5 |
| 5 | `Jackal` | `JKL` | 1 |
| 6 | `UFLL_Anto_Kankaras` | `RL1` | 10 |
| 7 | `APR_Prosper_Kouassi` | `BC1` | 5 |
| 8 | `UFLL_Addi_Mbantuwe` | `RW1` | 2 |
| 9 | `APR_Walton_Purefoy` | `BL2` | 10 |
| 10 | `APR_Arturo_Quiepo` | `BL1` | 10 |
| 11 | `Reuben_Oluwagembi` | `JRN` | 7 |
| 12 | `APR_Oliver_Tambossa` | `BW1` | 2 |
| 13 | `UFLL_Hector_Voorhees` | `RL3` | 5 |
| 14 | `PrisonGuard1` | `MPG1` | 1 |
| 15 | `PrisonGuard2` | — | — |
| 16 | `PrisonPhysician` | — | — |
| 17 | `DJLordHawHaw` | `MDJ` | 3 |
| 18 | `Andre_Hyppolite` | `BAH` | 49 |
| 19 | `Flora_Guillen` | `BFG` | 48 |
| 20 | `Frank_Bilders` | `BFB` | 52 |
| 21 | `Hakim_Echebbi` | `BHE` | 49 |
| 22 | `Josip_Idromeno` | `BJI` | 51 |
| 23 | `Marty_Alencar` | `BMA` | 82 |
| 24 | `Michele_Dachss` | `BMD` | 48 |
| 25 | `Nasreen_Davar` | `BND` | 48 |
| 26 | `Paul_Ferenc` | `BPF` | 48 |
| 27 | `Quarbani_Singh` | `BQS` | 48 |
| 28 | `Warren_Clyde` | `BWC` | 50 |
| 29 | `Xianyong_Bai` | `BXB` | 50 |
| 30 | `World1_ArmsMerchant` | `AM1` | 4 |
| 31 | `World2_ArmsMerchant` | `AM2` | 4 |
| 32 | `GRIN_OperativeA` | `GOA` | 12 |
| 33 | `GRIN_RefugeeA` | `GRA` | 2 |
| 34 | `GRIN_Doctor_Obua` | `GL2` | 4 |
| 35 | `GRIN_Father_Maliya` | `GL1` | 5 |
| 36 | `APR_Courier` | `MAC` | 3 |
| 37 | `Chief_Police` | `MCP` | 3 |
| 38 | `Chief_Brother` | `MCB` | 3 |
| 39 | `UFLL_Politician` | `MUP` | 3 |
| 40 | `Guard` | `GRD` | 25 |
| 41 | `RadioVoice` | — | — |
| 42 | `APR_Informant` | `MAI` | 3 |
| 43 | `King_Nnyere` | `MKN` | 1 |
| 44 | `Prince_Oeduard` | `MPO` | 3 |
| 45 | `Propaganda_Minister` | `MPM` | 3 |
| 46 | `Seth_Unyia` | `MSU` | 6 |
| 47 | `Yabek` | `MYK` | 3 |
| 48 | `GRIN_OperativeB` | `GOB` | 12 |
| 49 | `GRIN_RefugeeB` | `GRB` | 2 |
| 50 | `Arms_Bazaar_Tout` | — | — |
| 51 | `Border_Patrol_Commandant` | `BPC` | 1 |

## Missions

The tag-to-number map `GetMissionFromTag` builds. Mission 0 is no mission.

<details>
<summary>All 165 mission numbers</summary>

| # | Tag | # | Tag | # | Tag | # | Tag |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | none | 42 | `A2BSQA_SE01` | 84 | `A2SM08_SE03B` | 126 | `WAG_BJI_LOS` |
| 1 | `A1BSQA_SE01` | 43 | `A2BSQA_SE02` | 85 | `A2SM09_SE02A` | 127 | `WAG_BJI_ABN` |
| 2 | `A1BSQA_SE02` | 44 | `A2BSQB_SE01` | 86 | `A2SM09_SE02B` | 128 | `WAG_BMA_SRV` |
| 3 | `A1BSQB_SE01` | 45 | `A2BSQB_SE02` | 87 | `A2SM09_SE05A` | 129 | `WAG_BMA_LOS` |
| 4 | `A1BSQB_SE02` | 46 | `A2BU06_SE02` | 88 | `A2SM09_SE05B` | 130 | `WAG_BMA_ABN` |
| 5 | `A1BU0X_SE03` | 47 | `A2BU07_SE02` | 89 | `A3SM10_SE03` | 131 | `WAG_BMD_SRV` |
| 6 | `A1BU01_SE04` | 48 | `A2CV05_SE01` | 90 | `A3SM11_SE02A` | 132 | `WAG_BMD_LOS` |
| 7 | `A1BU02_SE04` | 49 | `A2CV06_SE01` | 91 | `A3SM11_SE02B` | 133 | `WAG_BMD_ABN` |
| 8 | `A1BU03_SE04` | 50 | `A2CV07_SE01` | 92 | `A3SM13_SE01` | 134 | `WAG_BPF_SRV` |
| 9 | `A1BU04_SE04` | 51 | `A2CV08_SE01` | 93 | `A3SM14_SE01` | 135 | `WAG_BPF_LOS` |
| 10 | `A1CV01_SE01` | 52 | `A2GM02_SE01B` | 94 | `A3SM15_SE01` | 136 | `WAG_BPF_ABN` |
| 11 | `A1CV02_SE01` | 53 | `A2GM03_SE01` | 95 | `A3SM15_SE02A` | 137 | `WAG_BQS_SRV` |
| 12 | `A1CV03_SE01` | 54 | `A2GM04_SE01A` | 96 | `A3SM15_SE02B` | 138 | `WAG_BQS_LOS` |
| 13 | `A1CV04_SE01` | 55 | `A2GM04_SE01B` | 97 | `A3SMXX_SE01H` | 139 | `WAG_BQS_ABN` |
| 14 | `A1GM00_SE01A` | 56 | `A2GM04_SE02` | 98 | `A3SMXX_SE01M` | 140 | `WAG_BWC_SRV` |
| 15 | `A1GM00_SE01B` | 57 | `A2GM04_SE03` | 99 | `A3SMXX_SE02` | 141 | `WAG_BWC_LOS` |
| 16 | `A1GM00_SE02` | 58 | `A2GM04_SE04` | 100 | `A3SMXX_SE03` | 142 | `WAG_BWC_ABN` |
| 17 | `A1GM01_SE01` | 59 | `A2JT03_SE01` | 101 | `A3SMYY_SE01H` | 143 | `WAG_SLF_SRV` |
| 18 | `A1GM02_SE01A` | 60 | `A2JT04_SE01` | 102 | `A3SMYY_SE01M` | 144 | `WAG_BAH_SRV` |
| 19 | `A1GM02_SE02` | 61 | `A2LM07_SE02` | 103 | `A3SMYY_SE02` | 145 | `WAG_BAH_LOS` |
| 20 | `A1GM02_SE03` | 62 | `A2LM07_SE03` | 104 | `A3SMYY_SE03` | 146 | `WAG_BAH_ABN` |
| 21 | `A1JT01_SE01` | 63 | `A2LM07_SE04` | 105 | `ABW1` | 147 | `WAG_BFB_SRV` |
| 22 | `A1JT02_SE01` | 64 | `A2LM08_SE02` | 106 | `ABW2` | 148 | `WAG_BFB_LOS` |
| 23 | `A1LM01_SE02` | 65 | `A2LM09_SE02` | 107 | `BU0X_SE02` | 149 | `WAG_BFB_ABN` |
| 24 | `A1LM01_SE03` | 66 | `A2LM09_SE05` | 108 | `BU0Y_SE02` | 150 | `WAG_BHE_SRV` |
| 25 | `A1LM01_SE04` | 67 | `A2LM10_SE02` | 109 | `HQMR` | 151 | `WAG_BHE_LOS` |
| 26 | `A1LM02_SE02` | 68 | `A2LM10_SE04` | 110 | `HQWS` | 152 | `WAG_BHE_ABN` |
| 27 | `A1LM02_SE03` | 69 | `A2LM11_SE02` | 111 | `A1BU0X_SE01` | 153 | `WAG_BND_SRV` |
| 28 | `A1LM03_SE02` | 70 | `A2LM11_SE03` | 112 | `A2BU05_SE01` | 154 | `WAG_BND_LOS` |
| 29 | `A1LM04_SE02` | 71 | `A2LM11_SE05` | 113 | `A3SM12_SE01` | 155 | `WAG_BND_ABN` |
| 30 | `A1LM04_SE03` | 72 | `A2LM12_SE02` | 114 | `A3SM13_SE03` | 156 | `WAG_BXB_SRV` |
| 31 | `A1LM05_SE02` | 73 | `A2LM12_SE03` | 115 | `A3SMAA_SE02A` | 157 | `WAG_BXB_LOS` |
| 32 | `A1LM05_SE03` | 74 | `A2LM12_SE05` | 116 | `A3SMAA_SE02B` | 158 | `WAG_BXB_ABN` |
| 33 | `A1LM06_SE02` | 75 | `A2LMXX_SE01` | 117 | `A3SMUU_SE02C` | 159 | `A1GM02_SE01` |
| 34 | `A1LM06_SE03` | 76 | `A2LMYY_SE01` | 118 | `A3SMUU_SE02D` | 160 | `A2GM04_SE01` |
| 35 | `A1LM06_SE04` | 77 | `A2SM05_SE02` | 119 | `UDLM00_SE02` | 161 | `A1CV00_SE01` |
| 36 | `A1LMXX_SE01` | 78 | `A2SM06_SE02` | 120 | `A3SM11_PH01` | 162 | `A3SM16_SURV` |
| 37 | `A1LMYY_SE01` | 79 | `A2SM06_SE03` | 121 | `A1BU0X_PH01` | 163 | `A1GMXX_SE01` |
| 38 | `A1SM02_SE02` | 80 | `A2SM07_SE01` | 122 | `WAG_BFG_SRV` | 164 | `A2GMXX_SE01` |
| 39 | `A1SM03_SE01` | 81 | `A2SM08_SE02A` | 123 | `WAG_BFG_LOS` | | |
| 40 | `A1SM03_SE02A` | 82 | `A2SM08_SE02B` | 124 | `WAG_BFG_ABN` | | |
| 41 | `A1SM03_SE02B` | 83 | `A2SM08_SE03A` | 125 | `WAG_BJI_SRV` | | |

</details>

## Unknowns

- The `.pfe` layout past the key table, and how a key maps to a line.
- How a bark picks one of its versions, and what several SoundIDs on one line are for.
- What a line's `VariationBank` selects. It equals its bank's variant in 8,459 of 9,303 lines; the rest
  are `0` inside a variant 1–4 bank.
- Buddy banks (types 6, 9, 39, 42) and types 34/35 ship variants 1–2, but the loading traced above
  only ever asks a per-speaker bank for variant 0.
- What the event tags stand for.
