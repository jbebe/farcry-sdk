# Doom Eternal Super Shotgun

Replaces the DLC1 sawed-off shotgun's art. **The first custom weapon in this repo to reach the
game.** It is art only — no archetype fragment — which makes it the smaller of the two worked
examples; `mods/vss-vintorez` is the complete one. The method both share is
[texturing a replaced weapon](../../docs/docs/modding/texturing-a-weapon.md), which supersedes the
summary below where the two disagree.

Released through 1.0.0 → 1.5.0. The procedure is written up in
[replacing an existing weapon](../../docs/docs/modding/replacing-a-weapon.md) and
[adding a weapon § replacing a weapon's art](../../docs/docs/modding/adding-a-weapon.md#replacing-a-weapons-art).

## What it changes

Eight files, all overrides of existing paths — no new asset paths, so no hashlist entry and no
`depload` work. It is **art only**: no archetype fragment, so nothing about the weapon's behaviour
changes.

```
mods/graphics/weapons/dlc/sawed_off_shotgun/dlc1_sawedoff_shotgun.xbg   all six LODs
mods/graphics/weapons/dlc/sawed_off_shotgun/sawed_off_shotgun_state0{1,2}.xbt   + _mip0 companions
mods/graphics/_materials/SDORE2-M-2008091137450636.xbm                  three materials
mods/graphics/_materials/SDORE2-M-2008091138277853.xbm
mods/graphics/_materials/SDORE2-M-2008091861652784.xbm
```

`patch.dat` overriding a file whose home is a DLC archive is the load-bearing assumption here, and it
holds.

## Install

```
jackall-cli mod build   --game "C:\Games\Far Cry 2" --layer mods\doom-super-shotgun\layer
jackall-cli mod restore --game "C:\Games\Far Cry 2"
```

The sawed-off is a world pickup at ten DLC crate sites. Under Scubrah's Patch those spawns are
commented out and it is sold in the weapon bazaar instead (secondary, 25 diamonds).

## Why it matters beyond itself

The `Weapon` shader **has no albedo slot**. Colour comes from two shared, game-wide tiling maps
blended by a per-model mask, so a donated colour texture has nowhere to go until the `.xbm` is
rewritten to give it one. The whole method is
[texturing a replaced weapon](../../docs/docs/modding/texturing-a-weapon.md); the rewrite is:

- `DiffuseTexture1` → a texture the weapon already owns, `DiffuseTiling1` set to `1,1` so it lands on
  the model's own UVs instead of tiling.
- `MaskTexture1` / `MaskTextureBroken` → a control map with green at 0 so the second tiling layer
  never blends, and blue at 1 so the tint weight is full.
- `DiffuseColorBase` / `DiffuseColor1` and their Clean and Broken variants set so no weapon condition
  re-tints the texture.

:::note
Those last two are what this mod does, and they cost it the degraded look — green is the weight on
`DiffuseTexture2`, the game's own rust map, and pinning both mask slots to one green-zero map means
the weapon never grimes however much it is used. The VSS gives `MaskTextureBroken` a second control
map with rust painted into green, and splits the `Broken` tints. A third texture path is not the
obstacle it looks like: one at an invented path loads with no hashlist and no `depload` entry.
:::

With blue held at 1, `DiffuseColor1` becomes a plain per-material multiplier — the texture carries
wear and detail, the material carries the colour.

Five things that went wrong. Four are worth inheriting; the first is worth inheriting as a *method*,
not as constants.

- **A physically based albedo is often far too dark for this shader**, because its metal gets
  brightness from a metalness map this shader has no equivalent for. This source measured 0.05–0.12
  luma and read as black plastic. **Fit the band to your own source's percentiles rather than reusing
  the constants here** — the VSS's source arrived at 0.124–0.534, already inside the band, and this
  mod's fixed lift would have crushed a 4.3:1 range to 1.4:1.
- **Fix it in the texture, not the material.** Reaching a metal level from 0.05 needs about 8×, and
  DXT1 gives red five bits — multiplying through `DiffuseColor1` afterwards bands it badly. Do it in
  float before quantisation.
- **A gamma lift is the wrong curve.** It compresses the top, burning pale areas to white under the
  game's sun while barely moving the darkest metal. Fit the albedo into a band with a floor and a
  ceiling instead.
- **Never split one visual surface across two clusters with different materials.** Assign triangles
  by edge loop, not by count, or the boundary interleaves triangle by triangle and reads as saw teeth
  the moment the two materials differ.
- **`SpecularColorBase` is what every texel gets where the mask's red is low.** This mod ships 0.6,
  which suits a chrome shotgun; retail weapons run 0.043 with `SpecularPower` 30. Copy 0.6 onto an
  ordinary gun and the whole thing reads as one polished surface — and no offline gate shows it, because
  the add-on's preview models neither `SpecularColorBase` nor the mask's red.

## Credit

This work is based on **"DOOM eternal super shotgun"**
(https://sketchfab.com/3d-models/doom-eternal-super-shotgun-697778b664c1423692dfcde19a19b64e)
by **DJ_Nugget** (https://sketchfab.com/DJ_Nugget) licensed under
**CC-BY-4.0** (http://creativecommons.org/licenses/by/4.0/).
