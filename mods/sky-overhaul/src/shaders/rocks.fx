// Rock and cliff, drawn in place of the engine's Generic pixel shaders.
//
// The engine draws rock through five of them, which differ in where their registers sit, what
// arrives interpolated and how they shadow. Each has an entry point here per purpose, with the
// engine's registers as uniform parameters.

// The colour a census paints a rock draw, by which of the engine's shaders it came through.
float4 CensusTint : register(c110);

// The camera's position in the world, for the patched vertex shaders' camera-relative positions.
float4 CameraPosition : register(c110);

// The light the engine lights this draw by, as the direction its light travels in the world.
float4 LightDirection : register(c111);

// How far rock is lit back toward the engine's smooth, normal-mapped surface from flat triangles,
// and how bright and how tight the sun's glint off the triangles is.
float4 Shading : register(c112);

// The detail map's repeats per metre, and how strong its relief, grain and cavity are.
float4 DetailSettings : register(c113);

// Relief as a normal's x and y in red and green, cavity in blue and grain in alpha, the last two
// as half their ratio to the average. See rocks/scripts/make_detail.py.
sampler2D DetailMap : register(s7);

float4 CensusPS() : COLOR0
{
    return CensusTint;
}

float2 Tiled(float4 uv, float4 tiling)
{
    return uv.xy * tiling.xy + uv.zw * tiling.zw;
}

// A normal map's tangent-space normal, with X in alpha and Y in green, flattened toward straight
// out by one minus `strength`.
float3 DecodeNormal(float4 texel, float strength)
{
    float2 xy = texel.wy * 2 - 1;
    return lerp(float3(0, 0, 1), float3(xy, sqrt(abs(1 - dot(xy, xy)))), strength);
}

// Two diffuse layers: the first tinted from DiffuseColorBase toward DiffuseColor1 by the mask's
// blue, the second blended over it by the mask's green.
float4 Albedo(float4 layer1, float3 layer2, float3 colourBase, float3 colour1, float3 colour2,
              float3 mask)
{
    float3 tinted = layer1.rgb * lerp(colourBase, colour1, mask.b);
    return float4(lerp(tinted, layer2 * colour2, mask.g), layer1.a);
}

float3 SpecularColour(float3 texel, float3 colourBase, float3 colour1, float strength)
{
    return lerp(colourBase, texel * colour1, strength);
}

// Eight compared taps of the shadow map around `uv`, `spacing` apart.
float Pcf(sampler2D shadowMap, float2 uv, float depth, float2 spacing)
{
    const float2 offsets[8] = {
        float2(1, -1), float2(-1, -1), float2(1, 1), float2(-1, 1),
        float2(-2, 0), float2(2, 0), float2(0, -2), float2(0, 2),
    };
    float lit = 0;
    for (int i = 0; i < 8; i++)
    {
        lit += tex2Dproj(shadowMap, float4(uv + offsets[i] * spacing, depth, 1)).x;
    }
    return lit * 0.125;
}

// The cascade a point falls in is the smallest whose range holds it, so the mask is cumulative;
// the first two slices also have to hold its depth.
float CascadedPcf(sampler2D shadowMap, float3 coords, float4 sliceScaleOffset[3], float4 mapSize,
                  float4 ranges, float4 scale, float4 depthScales, float4 depthOffsets,
                  float4 depthRanges, out float isLastSlice)
{
    float2 k = float2(coords.x >= 0 ? scale.x : scale.z, coords.y >= 0 ? scale.y : scale.w);
    float2 edge = abs(k * coords.xy);
    float4 mask = ranges.yzwx - max(edge.x, edge.y) >= 0;
    mask.x *= (depthRanges.y - coords.z >= 0) * (depthRanges.x - coords.z < 0);
    mask.y *= (depthRanges.w - coords.z >= 0) * (depthRanges.z - coords.z < 0);

    float4 slice = mask.x * sliceScaleOffset[0] + mask.y * sliceScaleOffset[1] +
                   mask.z * sliceScaleOffset[2];
    float depth = saturate(coords.z * dot(mask.xyz, depthScales.xyz) +
                           dot(mask.xyz, depthOffsets.xyz));
    float spread = dot(mask, float4(0.5, 1.0 / 6.0, 1.0 / 3.0, 1));
    isLastSlice = 1 - mask.y;
    return Pcf(shadowMap, slice.xy * coords.xy + slice.zw, depth, spread * mapSize.zw);
}

float SinglePcf(sampler2D shadowMap, float3 coords, float4 mapSize, float4 texelScale)
{
    float lit = Pcf(shadowMap, coords.xy, saturate(coords.z), texelScale.x * mapSize.zw);
    return coords.x - texelScale.y >= 0 ? lit : 1;
}

// Beyond the shadow map, the sun reaches as far as the sky is bright.
float FakeShadow(float3 sky)
{
    return saturate(dot(sky - 0.07, float3(1.5, 2.95, 0.55)));
}

// Everything the engine's lighting reads, in the tangent space it reads it in.
struct Retail
{
    float3 normal;
    float4 ambientVector;
    float3 ambientHalf;
    float3 sky;
    float3 ground;
    float occlusion;
    float3 light;
    float3 halfVector;
    float3 lightColour;
    float specularPower;
    float shadow;
};

float Glint(float3 halfVector, float3 normal, float power)
{
    return pow(saturate(dot(normalize(halfVector), normal)), power);
}

// Sun and hemisphere as the engine lights them, the sky's own highlight included, and the
// colour that falls on the albedo and the specular colour's own share.
void RetailLight(Retail r, float3 specularColour, out float3 onAlbedo, out float3 specular)
{
    float3 flattened = normalize(r.normal * float3(2, 2, 1));
    float skyward = saturate(dot(flattened, r.ambientVector.xyz) * 0.5 + r.ambientVector.w + 0.5);
    float3 ambient = lerp(r.ground, r.sky, skyward) * r.occlusion;
    float3 skySheen = r.sky * saturate(0.75 + r.ambientVector.w) *
                      Glint(r.ambientHalf, r.normal, r.specularPower) * specularColour;

    float facing = dot(r.normal, -r.light);
    float backFade = facing <= 0 ? saturate(facing * 5 + 1) : 1;
    onAlbedo = ambient + skySheen + saturate(facing) * r.lightColour * r.shadow;
    specular = Glint(r.halfVector, r.normal, r.specularPower) * r.lightColour * backFade *
               r.shadow * specularColour;
}

float4 Fogged(float3 colour, float alpha, float4 fog)
{
    return float4(colour * fog.w + fog.rgb, alpha);
}

struct CascadedInput
{
    float4 uv : TEXCOORD0_centroid;
    float4 mask : TEXCOORD1_centroid;
    float3 shadowCoords : TEXCOORD2_centroid;
    float4 ambientVector : TEXCOORD3_centroid;
    float4 fog : TEXCOORD4_centroid;
    float4 ambientHalf : TEXCOORD5_centroid;
    float3 sky : TEXCOORD6_centroid;
    float3 light : TEXCOORD7_centroid;
    float3 halfVector : TEXCOORD8_centroid;
    float2 hemisphere : TEXCOORD9_centroid;
};

float4 CascadedParityPS(CascadedInput i,
                        uniform sampler2D diffuse1 : register(s0),
                        uniform sampler2D diffuse2 : register(s1),
                        uniform sampler2D specularMap : register(s2),
                        uniform sampler2D normalMap : register(s3),
                        uniform sampler2D shadowMap : register(s4),
                        uniform sampler2D hemisphereMap : register(s5),
                        uniform float4 sliceScaleOffset[3] : register(c71),
                        uniform float4 colourBase : register(c74),
                        uniform float4 colour1 : register(c75),
                        uniform float4 colour2 : register(c76),
                        uniform float4 specularBase : register(c77),
                        uniform float4 specular1 : register(c78),
                        uniform float4 specularPower : register(c79),
                        uniform float4 diffuse1Tiling : register(c80),
                        uniform float4 diffuse2Tiling : register(c81),
                        uniform float4 normalTiling : register(c82),
                        uniform float4 specularTiling : register(c83),
                        uniform float4 lightColour : register(c84),
                        uniform float4 mapSize : register(c85),
                        uniform float4 ranges : register(c86),
                        uniform float4 scale : register(c87),
                        uniform float4 depthScales : register(c88),
                        uniform float4 depthOffsets : register(c89),
                        uniform float4 depthRanges : register(c90)) : COLOR0
{
    float3 mask = saturate(i.mask.rgb);
    float isLastSlice;
    float lit = CascadedPcf(shadowMap, i.shadowCoords, sliceScaleOffset, mapSize, ranges, scale,
                            depthScales, depthOffsets, depthRanges, isLastSlice);

    Retail r;
    r.normal = DecodeNormal(tex2D(normalMap, Tiled(i.uv, normalTiling)), mask.b);
    r.ambientVector = i.ambientVector;
    r.ambientHalf = i.ambientHalf.xyz;
    r.sky = i.sky;
    r.ground = tex2D(hemisphereMap, i.hemisphere).rgb;
    r.occlusion = i.mask.a;
    r.light = i.light;
    r.halfVector = i.halfVector;
    r.lightColour = lightColour.rgb;
    r.specularPower = specularPower.x;
    r.shadow = lerp(lit, FakeShadow(i.sky), isLastSlice * i.ambientHalf.w);

    float3 specularColour = SpecularColour(tex2D(specularMap, Tiled(i.uv, specularTiling)).rgb,
                                           specularBase.rgb, specular1.rgb, mask.r);
    float4 albedo = Albedo(tex2D(diffuse1, Tiled(i.uv, diffuse1Tiling)),
                           tex2D(diffuse2, Tiled(i.uv, diffuse2Tiling)).rgb, colourBase.rgb,
                           colour1.rgb, colour2.rgb, mask);
    float3 onAlbedo, specular;
    RetailLight(r, specularColour, onAlbedo, specular);
    return Fogged(albedo.rgb * onAlbedo + specular, albedo.a, i.fog);
}

float4 SingleParityPS(CascadedInput i,
                      uniform sampler2D diffuse1 : register(s0),
                      uniform sampler2D diffuse2 : register(s1),
                      uniform sampler2D specularMap : register(s2),
                      uniform sampler2D normalMap : register(s3),
                      uniform sampler2D shadowMap : register(s4),
                      uniform sampler2D hemisphereMap : register(s5),
                      uniform float4 colourBase : register(c71),
                      uniform float4 colour1 : register(c72),
                      uniform float4 colour2 : register(c73),
                      uniform float4 specularBase : register(c74),
                      uniform float4 specular1 : register(c75),
                      uniform float4 specularPower : register(c76),
                      uniform float4 diffuse1Tiling : register(c77),
                      uniform float4 diffuse2Tiling : register(c78),
                      uniform float4 normalTiling : register(c79),
                      uniform float4 specularTiling : register(c80),
                      uniform float4 lightColour : register(c81),
                      uniform float4 mapSize : register(c82),
                      uniform float4 texelScale : register(c83)) : COLOR0
{
    float3 mask = saturate(i.mask.rgb);
    float lit = SinglePcf(shadowMap, i.shadowCoords, mapSize, texelScale);

    Retail r;
    r.normal = DecodeNormal(tex2D(normalMap, Tiled(i.uv, normalTiling)), mask.b);
    r.ambientVector = i.ambientVector;
    r.ambientHalf = i.ambientHalf.xyz;
    r.sky = i.sky;
    r.ground = tex2D(hemisphereMap, i.hemisphere).rgb;
    r.occlusion = i.mask.a;
    r.light = i.light;
    r.halfVector = i.halfVector;
    r.lightColour = lightColour.rgb;
    r.specularPower = specularPower.x;
    r.shadow = lerp(lit, FakeShadow(i.sky), i.ambientHalf.w);

    float3 specularColour = SpecularColour(tex2D(specularMap, Tiled(i.uv, specularTiling)).rgb,
                                           specularBase.rgb, specular1.rgb, mask.r);
    float4 albedo = Albedo(tex2D(diffuse1, Tiled(i.uv, diffuse1Tiling)),
                           tex2D(diffuse2, Tiled(i.uv, diffuse2Tiling)).rgb, colourBase.rgb,
                           colour1.rgb, colour2.rgb, mask);
    float3 onAlbedo, specular;
    RetailLight(r, specularColour, onAlbedo, specular);
    return Fogged(albedo.rgb * onAlbedo + specular, albedo.a, i.fog);
}

// No second diffuse map: one sampler and every register after the first colour pair move down.
float4 RiverParityPS(CascadedInput i,
                     uniform sampler2D diffuse1 : register(s0),
                     uniform sampler2D specularMap : register(s1),
                     uniform sampler2D normalMap : register(s2),
                     uniform sampler2D shadowMap : register(s3),
                     uniform sampler2D hemisphereMap : register(s4),
                     uniform float4 colourBase : register(c71),
                     uniform float4 colour1 : register(c72),
                     uniform float4 specularBase : register(c73),
                     uniform float4 specular1 : register(c74),
                     uniform float4 specularPower : register(c75),
                     uniform float4 diffuse1Tiling : register(c76),
                     uniform float4 normalTiling : register(c77),
                     uniform float4 specularTiling : register(c78),
                     uniform float4 lightColour : register(c79),
                     uniform float4 mapSize : register(c80),
                     uniform float4 texelScale : register(c81)) : COLOR0
{
    float3 mask = float3(saturate(i.mask.r), 0, saturate(i.mask.b));
    float lit = SinglePcf(shadowMap, i.shadowCoords, mapSize, texelScale);

    Retail r;
    r.normal = DecodeNormal(tex2D(normalMap, Tiled(i.uv, normalTiling)), mask.b);
    r.ambientVector = i.ambientVector;
    r.ambientHalf = i.ambientHalf.xyz;
    r.sky = i.sky;
    r.ground = tex2D(hemisphereMap, i.hemisphere).rgb;
    r.occlusion = i.mask.a;
    r.light = i.light;
    r.halfVector = i.halfVector;
    r.lightColour = lightColour.rgb;
    r.specularPower = specularPower.x;
    r.shadow = lerp(lit, FakeShadow(i.sky), i.ambientHalf.w);

    float3 specularColour = SpecularColour(tex2D(specularMap, Tiled(i.uv, specularTiling)).rgb,
                                           specularBase.rgb, specular1.rgb, mask.r);
    float4 albedo = Albedo(tex2D(diffuse1, Tiled(i.uv, diffuse1Tiling)), 0, colourBase.rgb,
                           colour1.rgb, 0, mask);
    float3 onAlbedo, specular;
    RetailLight(r, specularColour, onAlbedo, specular);
    return Fogged(albedo.rgb * onAlbedo + specular, albedo.a, i.fog);
}

struct VertexAmbientInput
{
    float4 uv : TEXCOORD0_centroid;
    float3 mask : TEXCOORD1_centroid;
    float3 shadowCoords : TEXCOORD2_centroid;
    float4 fog : TEXCOORD3_centroid;
    float4 ambient : TEXCOORD4_centroid;
    float3 light : TEXCOORD5_centroid;
    float3 halfVector : TEXCOORD6_centroid;
};

// The ambient arrives lit per vertex, occlusion and all, and past the shadow map the sun is unshadowed.
float4 VertexAmbientParityPS(VertexAmbientInput i,
                             uniform sampler2D diffuse1 : register(s0),
                             uniform sampler2D diffuse2 : register(s1),
                             uniform sampler2D specularMap : register(s2),
                             uniform sampler2D normalMap : register(s3),
                             uniform sampler2D shadowMap : register(s4),
                             uniform float4 colourBase : register(c71),
                             uniform float4 colour1 : register(c72),
                             uniform float4 colour2 : register(c73),
                             uniform float4 specularBase : register(c74),
                             uniform float4 specular1 : register(c75),
                             uniform float4 specularPower : register(c76),
                             uniform float4 diffuse1Tiling : register(c77),
                             uniform float4 diffuse2Tiling : register(c78),
                             uniform float4 normalTiling : register(c79),
                             uniform float4 specularTiling : register(c80),
                             uniform float4 lightColour : register(c81),
                             uniform float4 mapSize : register(c82),
                             uniform float4 texelScale : register(c83)) : COLOR0
{
    float3 mask = saturate(i.mask);
    float shadow = lerp(SinglePcf(shadowMap, i.shadowCoords, mapSize, texelScale), 1, i.ambient.w);
    float3 normal = DecodeNormal(tex2D(normalMap, Tiled(i.uv, normalTiling)), mask.b);

    float facing = dot(normal, -i.light);
    float backFade = facing <= 0 ? saturate(facing * 5 + 1) : 1;
    float3 onAlbedo = i.ambient.rgb + saturate(facing) * lightColour.rgb * shadow;
    float3 specularColour = SpecularColour(tex2D(specularMap, Tiled(i.uv, specularTiling)).rgb,
                                           specularBase.rgb, specular1.rgb, mask.r);
    float3 specular = Glint(i.halfVector, normal, specularPower.x) * lightColour.rgb * backFade *
                      shadow * specularColour;
    float4 albedo = Albedo(tex2D(diffuse1, Tiled(i.uv, diffuse1Tiling)),
                           tex2D(diffuse2, Tiled(i.uv, diffuse2Tiling)).rgb, colourBase.rgb,
                           colour1.rgb, colour2.rgb, mask);
    return Fogged(albedo.rgb * onAlbedo + specular, albedo.a, i.fog);
}

struct ShadowlessInput
{
    float4 uv : TEXCOORD0_centroid;
    float4 mask : TEXCOORD1_centroid;
    float4 ambientVector : TEXCOORD2_centroid;
    float4 fog : TEXCOORD3_centroid;
    float3 ambientHalf : TEXCOORD4_centroid;
    float3 sky : TEXCOORD5_centroid;
    float3 light : TEXCOORD6_centroid;
    float3 halfVector : TEXCOORD7_centroid;
    float2 hemisphere : TEXCOORD8_centroid;
};

float4 ShadowlessParityPS(ShadowlessInput i,
                          uniform sampler2D diffuse1 : register(s0),
                          uniform sampler2D diffuse2 : register(s1),
                          uniform sampler2D specularMap : register(s2),
                          uniform sampler2D normalMap : register(s3),
                          uniform sampler2D hemisphereMap : register(s4),
                          uniform float4 colourBase : register(c71),
                          uniform float4 colour1 : register(c72),
                          uniform float4 colour2 : register(c73),
                          uniform float4 specularBase : register(c74),
                          uniform float4 specular1 : register(c75),
                          uniform float4 specularPower : register(c76),
                          uniform float4 diffuse1Tiling : register(c77),
                          uniform float4 diffuse2Tiling : register(c78),
                          uniform float4 normalTiling : register(c79),
                          uniform float4 specularTiling : register(c80),
                          uniform float4 lightColour : register(c81)) : COLOR0
{
    float3 mask = saturate(i.mask.rgb);

    Retail r;
    r.normal = DecodeNormal(tex2D(normalMap, Tiled(i.uv, normalTiling)), mask.b);
    r.ambientVector = i.ambientVector;
    r.ambientHalf = i.ambientHalf;
    r.sky = i.sky;
    r.ground = tex2D(hemisphereMap, i.hemisphere).rgb;
    r.occlusion = i.mask.a;
    r.light = i.light;
    r.halfVector = i.halfVector;
    r.lightColour = lightColour.rgb;
    r.specularPower = specularPower.x;
    r.shadow = FakeShadow(i.sky);

    float3 specularColour = SpecularColour(tex2D(specularMap, Tiled(i.uv, specularTiling)).rgb,
                                           specularBase.rgb, specular1.rgb, mask.r);
    float4 albedo = Albedo(tex2D(diffuse1, Tiled(i.uv, diffuse1Tiling)),
                           tex2D(diffuse2, Tiled(i.uv, diffuse2Tiling)).rgb, colourBase.rgb,
                           colour1.rgb, colour2.rgb, mask);
    float3 onAlbedo, specular;
    RetailLight(r, specularColour, onAlbedo, specular);
    return Fogged(albedo.rgb * onAlbedo + specular, albedo.a, i.fog);
}

// A triangle's own facing, from how the camera-relative position changes across the screen, and
// which way round the screen's derivatives made it: -1 where it had to be turned to face the eye.
float3 Facing(float3 position, out float turned)
{
    float3 geometric = normalize(cross(ddx(position), ddy(position)));
    turned = dot(geometric, position) > 0 ? -1 : 1;
    return geometric * turned;
}

// Each triangle in the colour of its own facing, under world-locked lines a metre apart. Both hold
// still as the camera moves only if the patched vertex shader's position is right.
float4 Grid(float3 position)
{
    float turned;
    float3 facing = Facing(position, turned);
    float3 world = position + CameraPosition.xyz;
    float3 distance = abs(frac(world + 0.5) - 0.5) / max(fwidth(world), 1e-4);
    float edge = 1 - saturate(min(distance.x, min(distance.y, distance.z)) - 0.5);
    return float4(lerp((facing * 0.5 + 0.5) * 0.6, 1, edge), 1);
}

float4 Grid7PS(float3 position : TEXCOORD7) : COLOR0
{
    return Grid(position);
}

float4 Grid8PS(float3 position : TEXCOORD8) : COLOR0
{
    return Grid(position);
}

float4 Grid9PS(float3 position : TEXCOORD9) : COLOR0
{
    return Grid(position);
}

// Everything ours lights rock by.
struct Rock
{
    float3 position;
    // The engine's normal-mapped normal, and its sun and world up, in the mesh's tangent space.
    float3 tangentNormal;
    float3 tangentLight;
    float3 tangentUp;
    float3 lightColour;
    float shadow;
    // The engine's ambient where it arrives already lit per vertex; black to light it here.
    float3 ambient;
    float3 sky;
    float3 ground;
    float height;
    float occlusion;
};

// How much light bare stone reflects straight back, whatever the material's own specular
// colours say; about what the engine gives the desert's.
static const float kStoneReflectance = 0.05;

// A detail texel's relief laid onto `face`, both in one projection's tangent frame.
float3 Whiteout(float4 texel, float3 face)
{
    float2 relief = (texel.rg * 2 - 1) * DetailSettings.y;
    return float3(relief + face.xy, face.z * sqrt(saturate(1 - dot(relief, relief))));
}

struct Detail
{
    float3 normal;
    float grain;
    float cavity;
};

// A value that drifts slowly and without period across the world, the same from every projection.
float Wander(float3 world)
{
    float3 p = world * (1.0 / 6.0);
    return sin(p.x + 1.7 * sin(p.y * 0.61 + p.z * 0.83)) +
           sin(p.y * 1.13 + 1.3 * sin(p.z * 0.71 + p.x * 0.57)) +
           sin(p.z * 0.93 + 1.1 * sin(p.x * 0.67 + p.y * 0.79));
}

// Where in the detail map a stretch of rock reads from, and the next stretch's place with how far
// toward it this point is, so the map never shows the same patch twice side by side.
struct Stretch
{
    float2 here;
    float2 next;
    float toward;
};

Stretch StretchAt(float3 world)
{
    float index = Wander(world) * 1.5;
    float stretch = floor(index);
    // The plastic number's sequence, which puts successive stretches far apart in the map.
    const float2 step = float2(0.7548777, 0.5698403);
    Stretch s;
    s.here = frac(stretch * step);
    s.next = frac((stretch + 1) * step);
    s.toward = index - stretch;
    return s;
}

// The detail map at `uv`, blended from the two places the stretch reads, along the grain of what
// each shows rather than straight across.
float4 Unrepeated(float2 uv, Stretch s)
{
    float2 dx = ddx(uv);
    float2 dy = ddy(uv);
    float4 here = tex2Dgrad(DetailMap, uv + s.here, dx, dy);
    float4 next = tex2Dgrad(DetailMap, uv + s.next, dx, dy);
    return lerp(here, next, smoothstep(0.2, 0.8, s.toward - 0.1 * (here.a - next.a)));
}

// The detail map projected along the world's three axes, each weighted by how squarely the
// triangle faces it, with its relief laid onto the triangle's facing.
Detail DetailOn(float3 world, float3 face)
{
    float3 weights = pow(abs(face), 4);
    weights /= dot(weights, 1);
    float3 uv = world * DetailSettings.x;
    Stretch stretch = StretchAt(world);
    float4 alongX = Unrepeated(uv.yz, stretch);
    float4 alongY = Unrepeated(uv.xz, stretch);
    float4 alongZ = Unrepeated(uv.xy, stretch);

    Detail d;
    d.normal = normalize(Whiteout(alongX, face.yzx).zxy * weights.x +
                         Whiteout(alongY, face.xzy).xzy * weights.y +
                         Whiteout(alongZ, face) * weights.z);
    float2 ratios = (alongX.ba * weights.x + alongY.ba * weights.y + alongZ.ba * weights.z) * 2;
    d.cavity = lerp(1, ratios.x, DetailSettings.w);
    d.grain = lerp(1, ratios.y, DetailSettings.z);
    return d;
}

// Each triangle lit flat by the sun, by the sky over it and by the ground under it as far as it
// faces up, with the detail map's relief, grain and cavity, blended toward the engine's smooth
// surface by Shading.x. No highlight but a small, tight glint of the sun off the triangles.
float3 RockLight(Rock r, float3 albedo)
{
    float turned;
    float3 face = Facing(r.position, turned);
    Detail detail = DetailOn(r.position + CameraPosition.xyz, face);
    float3 toLight = -normalize(LightDirection.xyz);
    float smoothness = Shading.x;

    float facing =
        lerp(dot(detail.normal, toLight), dot(r.tangentNormal, -r.tangentLight), smoothness);
    float3 flattened = normalize(r.tangentNormal * float3(2, 2, 1));
    float up = lerp(detail.normal.z, dot(flattened, r.tangentUp), smoothness);
    float skyward = saturate(up * 0.5 + 0.5 + r.height);
    float3 ambient = any(r.ambient) ? r.ambient : lerp(r.ground, r.sky, skyward) * r.occlusion;
    float3 sun = saturate(facing) * r.lightColour * r.shadow;

    float3 halfway = normalize(normalize(-r.position) + toLight);
    float glint =
        facing > 0 ? pow(saturate(dot(detail.normal, halfway)), Shading.z) * (1 - smoothness) : 0;
    return albedo * detail.grain * (ambient * detail.cavity + sun) +
           glint * Shading.y * kStoneReflectance * r.lightColour * r.shadow;
}

struct CascadedRockInput
{
    float4 uv : TEXCOORD0;
    float4 mask : TEXCOORD1_centroid;
    float3 shadowCoords : TEXCOORD2_centroid;
    float4 ambientVector : TEXCOORD3_centroid;
    float4 fog : TEXCOORD4_centroid;
    float4 ambientHalf : TEXCOORD5_centroid;
    float3 sky : TEXCOORD6_centroid;
    float3 light : TEXCOORD7_centroid;
    float3 position : TEXCOORD8;
    float2 hemisphere : TEXCOORD9_centroid;
};

float4 CascadedPS(CascadedRockInput i,
                  uniform sampler2D diffuse1 : register(s0),
                  uniform sampler2D diffuse2 : register(s1),
                  uniform sampler2D normalMap : register(s3),
                  uniform sampler2D shadowMap : register(s4),
                  uniform sampler2D hemisphereMap : register(s5),
                  uniform float4 sliceScaleOffset[3] : register(c71),
                  uniform float4 colourBase : register(c74),
                  uniform float4 colour1 : register(c75),
                  uniform float4 colour2 : register(c76),
                  uniform float4 diffuse1Tiling : register(c80),
                  uniform float4 diffuse2Tiling : register(c81),
                  uniform float4 normalTiling : register(c82),
                  uniform float4 lightColour : register(c84),
                  uniform float4 mapSize : register(c85),
                  uniform float4 ranges : register(c86),
                  uniform float4 scale : register(c87),
                  uniform float4 depthScales : register(c88),
                  uniform float4 depthOffsets : register(c89),
                  uniform float4 depthRanges : register(c90)) : COLOR0
{
    float3 mask = saturate(i.mask.rgb);
    float isLastSlice;
    float lit = CascadedPcf(shadowMap, i.shadowCoords, sliceScaleOffset, mapSize, ranges, scale,
                            depthScales, depthOffsets, depthRanges, isLastSlice);

    Rock r;
    r.position = i.position;
    r.tangentNormal = DecodeNormal(tex2D(normalMap, Tiled(i.uv, normalTiling)), mask.b);
    r.tangentLight = i.light;
    r.tangentUp = i.ambientVector.xyz;
    r.lightColour = lightColour.rgb;
    r.shadow = lerp(lit, FakeShadow(i.sky), isLastSlice * i.ambientHalf.w);
    r.ambient = 0;
    r.sky = i.sky;
    r.ground = tex2D(hemisphereMap, i.hemisphere).rgb;
    r.height = i.ambientVector.w;
    r.occlusion = i.mask.a;
    float4 albedo = Albedo(tex2D(diffuse1, Tiled(i.uv, diffuse1Tiling)),
                           tex2D(diffuse2, Tiled(i.uv, diffuse2Tiling)).rgb, colourBase.rgb,
                           colour1.rgb, colour2.rgb, mask);
    return Fogged(RockLight(r, albedo.rgb), albedo.a, i.fog);
}

float4 SinglePS(CascadedRockInput i,
                uniform sampler2D diffuse1 : register(s0),
                uniform sampler2D diffuse2 : register(s1),
                uniform sampler2D normalMap : register(s3),
                uniform sampler2D shadowMap : register(s4),
                uniform sampler2D hemisphereMap : register(s5),
                uniform float4 colourBase : register(c71),
                uniform float4 colour1 : register(c72),
                uniform float4 colour2 : register(c73),
                uniform float4 diffuse1Tiling : register(c77),
                uniform float4 diffuse2Tiling : register(c78),
                uniform float4 normalTiling : register(c79),
                uniform float4 lightColour : register(c81),
                uniform float4 mapSize : register(c82),
                uniform float4 texelScale : register(c83)) : COLOR0
{
    float3 mask = saturate(i.mask.rgb);
    float lit = SinglePcf(shadowMap, i.shadowCoords, mapSize, texelScale);

    Rock r;
    r.position = i.position;
    r.tangentNormal = DecodeNormal(tex2D(normalMap, Tiled(i.uv, normalTiling)), mask.b);
    r.tangentLight = i.light;
    r.tangentUp = i.ambientVector.xyz;
    r.lightColour = lightColour.rgb;
    r.shadow = lerp(lit, FakeShadow(i.sky), i.ambientHalf.w);
    r.ambient = 0;
    r.sky = i.sky;
    r.ground = tex2D(hemisphereMap, i.hemisphere).rgb;
    r.height = i.ambientVector.w;
    r.occlusion = i.mask.a;
    float4 albedo = Albedo(tex2D(diffuse1, Tiled(i.uv, diffuse1Tiling)),
                           tex2D(diffuse2, Tiled(i.uv, diffuse2Tiling)).rgb, colourBase.rgb,
                           colour1.rgb, colour2.rgb, mask);
    return Fogged(RockLight(r, albedo.rgb), albedo.a, i.fog);
}

float4 RiverPS(CascadedRockInput i,
               uniform sampler2D diffuse1 : register(s0),
               uniform sampler2D normalMap : register(s2),
               uniform sampler2D shadowMap : register(s3),
               uniform sampler2D hemisphereMap : register(s4),
               uniform float4 colourBase : register(c71),
               uniform float4 colour1 : register(c72),
               uniform float4 diffuse1Tiling : register(c76),
               uniform float4 normalTiling : register(c77),
               uniform float4 lightColour : register(c79),
               uniform float4 mapSize : register(c80),
               uniform float4 texelScale : register(c81)) : COLOR0
{
    float3 mask = float3(saturate(i.mask.r), 0, saturate(i.mask.b));
    float lit = SinglePcf(shadowMap, i.shadowCoords, mapSize, texelScale);

    Rock r;
    r.position = i.position;
    r.tangentNormal = DecodeNormal(tex2D(normalMap, Tiled(i.uv, normalTiling)), mask.b);
    r.tangentLight = i.light;
    r.tangentUp = i.ambientVector.xyz;
    r.lightColour = lightColour.rgb;
    r.shadow = lerp(lit, FakeShadow(i.sky), i.ambientHalf.w);
    r.ambient = 0;
    r.sky = i.sky;
    r.ground = tex2D(hemisphereMap, i.hemisphere).rgb;
    r.height = i.ambientVector.w;
    r.occlusion = i.mask.a;
    float4 albedo = Albedo(tex2D(diffuse1, Tiled(i.uv, diffuse1Tiling)), 0, colourBase.rgb,
                           colour1.rgb, 0, mask);
    return Fogged(RockLight(r, albedo.rgb), albedo.a, i.fog);
}

struct VertexAmbientRockInput
{
    float4 uv : TEXCOORD0;
    float3 mask : TEXCOORD1_centroid;
    float3 shadowCoords : TEXCOORD2_centroid;
    float4 fog : TEXCOORD3_centroid;
    float4 ambient : TEXCOORD4_centroid;
    float3 light : TEXCOORD5_centroid;
    float3 position : TEXCOORD7;
};

float4 VertexAmbientPS(VertexAmbientRockInput i,
                       uniform sampler2D diffuse1 : register(s0),
                       uniform sampler2D diffuse2 : register(s1),
                       uniform sampler2D normalMap : register(s3),
                       uniform sampler2D shadowMap : register(s4),
                       uniform float4 colourBase : register(c71),
                       uniform float4 colour1 : register(c72),
                       uniform float4 colour2 : register(c73),
                       uniform float4 diffuse1Tiling : register(c77),
                       uniform float4 diffuse2Tiling : register(c78),
                       uniform float4 normalTiling : register(c79),
                       uniform float4 lightColour : register(c81),
                       uniform float4 mapSize : register(c82),
                       uniform float4 texelScale : register(c83)) : COLOR0
{
    float3 mask = saturate(i.mask);

    Rock r;
    r.position = i.position;
    r.tangentNormal = DecodeNormal(tex2D(normalMap, Tiled(i.uv, normalTiling)), mask.b);
    r.tangentLight = i.light;
    r.tangentUp = 0;
    r.lightColour = lightColour.rgb;
    r.shadow = lerp(SinglePcf(shadowMap, i.shadowCoords, mapSize, texelScale), 1, i.ambient.w);
    r.ambient = i.ambient.rgb;
    r.sky = 0;
    r.ground = 0;
    r.height = 0;
    r.occlusion = 1;
    float4 albedo = Albedo(tex2D(diffuse1, Tiled(i.uv, diffuse1Tiling)),
                           tex2D(diffuse2, Tiled(i.uv, diffuse2Tiling)).rgb, colourBase.rgb,
                           colour1.rgb, colour2.rgb, mask);
    return Fogged(RockLight(r, albedo.rgb), albedo.a, i.fog);
}

struct ShadowlessRockInput
{
    float4 uv : TEXCOORD0;
    float4 mask : TEXCOORD1_centroid;
    float4 ambientVector : TEXCOORD2_centroid;
    float4 fog : TEXCOORD3_centroid;
    float3 sky : TEXCOORD5_centroid;
    float3 light : TEXCOORD6_centroid;
    float2 hemisphere : TEXCOORD8_centroid;
    float3 position : TEXCOORD9;
};

float4 ShadowlessPS(ShadowlessRockInput i,
                    uniform sampler2D diffuse1 : register(s0),
                    uniform sampler2D diffuse2 : register(s1),
                    uniform sampler2D normalMap : register(s3),
                    uniform sampler2D hemisphereMap : register(s4),
                    uniform float4 colourBase : register(c71),
                    uniform float4 colour1 : register(c72),
                    uniform float4 colour2 : register(c73),
                    uniform float4 diffuse1Tiling : register(c77),
                    uniform float4 diffuse2Tiling : register(c78),
                    uniform float4 normalTiling : register(c79),
                    uniform float4 lightColour : register(c81)) : COLOR0
{
    float3 mask = saturate(i.mask.rgb);

    Rock r;
    r.position = i.position;
    r.tangentNormal = DecodeNormal(tex2D(normalMap, Tiled(i.uv, normalTiling)), mask.b);
    r.tangentLight = i.light;
    r.tangentUp = i.ambientVector.xyz;
    r.lightColour = lightColour.rgb;
    r.shadow = FakeShadow(i.sky);
    r.ambient = 0;
    r.sky = i.sky;
    r.ground = tex2D(hemisphereMap, i.hemisphere).rgb;
    r.height = i.ambientVector.w;
    r.occlusion = i.mask.a;
    float4 albedo = Albedo(tex2D(diffuse1, Tiled(i.uv, diffuse1Tiling)),
                           tex2D(diffuse2, Tiled(i.uv, diffuse2Tiling)).rgb, colourBase.rgb,
                           colour1.rgb, colour2.rgb, mask);
    return Fogged(RockLight(r, albedo.rgb), albedo.a, i.fog);
}
