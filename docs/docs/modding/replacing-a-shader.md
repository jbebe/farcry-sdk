---
sidebar_position: 12
---

# Replacing a shader

:::info[Verified via reverse engineering]
The loop below was validated by recompiling a shipped shader from HLSL and rebuilding it to a file
of the same size, differing from the original only where the compiler chose the other operand order
for two commutative multiplies. See [`shadersobj`](../file-formats/shader-objects.md) for the
formats involved.
:::

Shaders are compiled objects addressed by hash, so replacing one is not like replacing a texture:
you have to find the object first, and you have to bind your parameters to the registers the engine
already feeds. Nothing here needs the game's original HLSL.

## 1. Find the object

`shader index` resolves a shader's no-option permutation through the tree's own index tables:

```
jackall-cli shader index <game>/engine/shaders/obj --shader celestialbody
```

```
key fb20db90  (celestialbody, no options)
  pso  82718c80  h00\shadernumber_82718c80.pso
  vso  84118c11  h11\shadernumber_84118c11.vso
  rs   6fc67631  h31\shadernumber_6fc67631.rs
```

Permutations compiled **with** options cannot be addressed by name yet. Find those by their
parameter tables instead: extract candidates and look for the samplers only that shader declares.
Only four objects in the whole D3D9 tree bind `CelestialBodySampler`, and disassembling them tells
you which is which — the one multiplying by `BloomAdaptationFactor` is the `ADDITIVE` variant, the
one applying fog is not, and a trailing `add -1` / `mul 0.125` pair is `FAKEHDR`.

## 2. Extract it

```
jackall-cli shader extract shadernumber_3ffcc3dd.pso -n names.txt
```

You get `.bin` (the bytecode) and `.xml` (the binding table). Disassemble the bytecode with the
Windows SDK compiler:

```
fxc /nologo /dumpbin shadernumber_3ffcc3dd.bin
```

`-n` is a newline-separated list of candidate parameter names; anything whose CRC32 matches gets
named in the XML. Without it the table still round-trips, it just reads as hashes.

## 3. Write the replacement

Write a standalone `.fx` that declares **only** what it uses, at the registers the extracted table
names. Do not try to rebuild the original include chain — the engine binds by register, so the
register is the whole contract:

```hlsl
sampler2D CelestialBodySampler  : register(s0);
sampler2D TimeOfDayColorSampler : register(s1);

float4 Params                : register(c71);
float  BloomAdaptationFactor : register(c58);

float4 MainPS(centroid float2 texCoord : TEXCOORD0) : COLOR0
{
    ...
}
```

Compile it to the profile the original declares (`shader extract` reports it):

```
fxc /nologo /T ps_3_0 /E MainPS /Fo replacement.bin replacement.fx
```

Two rules the engine will not forgive:

- **Keep the interpolators.** The vertex shader is a separate object; if you change what the pixel
  shader reads from `TEXCOORD0`, you have to change the vertex shader too.
- **Keep the register bindings.** A parameter at the wrong register silently reads whatever the
  renderer last left there.

## 4. Rebuild and install

```
jackall-cli shader build replacement.bin shadernumber_3ffcc3dd.xml -o shadernumber_3ffcc3dd.pso
```

The binding table comes from the original, so the engine keeps feeding the same values. `build` drops
the constant table `fxc` writes, which no shipped object carries.

Stage the result in a mod layer at its archive path and build:

```
layer/mods/engine/shaders/obj/h5d/shadernumber_3ffcc3dd.pso
```

```
jackall-cli mod build --game "C:\Games\Far Cry 2" --layer layer
```

`patch.dat` is searched before `shadersobj.dat`, so the staged file wins.

## Render states need no compiler

A `.rs` is plain text. To stop a sky element being clipped by terrain, stage a copy of its render
state with one line added:

```
AlphaBlendEnable=true
BlendOp=Add
ZWriteEnable=false
CullMode=None
ZEnable=false
```

States the file does not name are inherited from whatever the renderer set last, so adding a line can
change behaviour that the original never controlled.

## Verifying

Confirm the pipeline before changing any behaviour: recompile the shader to do exactly what it
already does, install it, and check the game looks unchanged. Then make one visible change — a
magenta tint is the usual canary — so a black screen and a working override are distinguishable. Only
then write the effect you actually want.
