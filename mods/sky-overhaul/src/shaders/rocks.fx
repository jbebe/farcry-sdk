// Rock and cliff, drawn in place of the engine's Generic pixel shaders: every triangle flat, lit by
// the sun and the sky from its own facing, with a detail map laid over it.
//
// The engine draws rock through five of them, which differ in where their registers sit, what
// arrives interpolated and how they shadow. Each has an entry point here, with the engine's
// registers as uniform parameters; the position arrives from the engine's own vertex shader,
// patched to hand it on.

// The camera's position in the world.
float4 CameraPosition : register(c110);

// Toward the light the engine lights this draw by, in the world.
float4 ToLight : register(c111);

// How far rock is lit back toward the engine's smooth, normal-mapped surface from flat triangles,
// how bright the sun's glint off the triangles is, and how tight.
float4 Shading : register(c112);

// The detail map's repeats per metre, and how strong its relief, grain and cavity are.
float4 DetailSettings : register(c113);

// Relief as a normal's x and y in red and green, cavity in blue and grain in alpha, the last two
// as half their ratio to the average. See rocks/scripts/make_detail.py.
sampler2D DetailMap : register(s7);

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

float4 Fogged(float3 colour, float alpha, float4 fog)
{
    return float4(colour * fog.w + fog.rgb, alpha);
}

// A triangle's own facing, turned toward the eye, from how far the camera-relative position moves
// across one pixel either way.
float3 Facing(float3 position, float3 dx, float3 dy)
{
    float3 geometric = normalize(cross(dx, dy));
    return dot(geometric, position) > 0 ? -geometric : geometric;
}

// A detail texel's relief laid onto `face`, both in one projection's tangent frame.
float3 Whiteout(float4 texel, float3 face)
{
    float2 relief = (texel.rg * 2 - 1) * DetailSettings.y;
    return float3(relief + face.xy, face.z * sqrt(saturate(1 - dot(relief, relief))));
}

// A value that drifts slowly and without period across the world, the same from every projection.
float Wander(float3 world)
{
    float3 p = world * (1.0 / 6.0);
    return sin(dot(p, float3(1.0, 0.61, 0.83))) + sin(dot(p, float3(0.57, 1.13, 0.71))) +
           sin(dot(p, float3(0.67, 0.79, 0.93)));
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
float4 Unrepeated(float2 uv, float2 dx, float2 dy, Stretch s)
{
    float4 here = tex2Dgrad(DetailMap, uv + s.here, dx, dy);
    float4 next = tex2Dgrad(DetailMap, uv + s.next, dx, dy);
    return lerp(here, next, smoothstep(0.2, 0.8, s.toward - 0.1 * (here.a - next.a)));
}

struct Detail
{
    float3 normal;
    float grain;
    float cavity;
};

// The detail map projected along the world's three axes, each weighted by how squarely the
// triangle faces it, with its relief laid onto the triangle's facing. The facing is flat, so a
// projection it barely faces is skipped by the whole triangle alike.
Detail DetailOn(float3 world, float3 face, float3 dx, float3 dy)
{
    float3 weights = pow(abs(face), 4);
    weights /= dot(weights, 1);
    weights *= weights > 0.02;
    weights /= dot(weights, 1);

    float3 uv = world * DetailSettings.x;
    float3 uvDx = dx * DetailSettings.x;
    float3 uvDy = dy * DetailSettings.x;
    Stretch stretch = StretchAt(world);
    float3 normal = 0;
    float2 ratios = 0;
    [branch] if (weights.x > 0)
    {
        float4 texel = Unrepeated(uv.yz, uvDx.yz, uvDy.yz, stretch);
        normal += Whiteout(texel, face.yzx).zxy * weights.x;
        ratios += texel.ba * weights.x;
    }
    [branch] if (weights.y > 0)
    {
        float4 texel = Unrepeated(uv.xz, uvDx.xz, uvDy.xz, stretch);
        normal += Whiteout(texel, face.xzy).xzy * weights.y;
        ratios += texel.ba * weights.y;
    }
    [branch] if (weights.z > 0)
    {
        float4 texel = Unrepeated(uv.xy, uvDx.xy, uvDy.xy, stretch);
        normal += Whiteout(texel, face) * weights.z;
        ratios += texel.ba * weights.z;
    }

    Detail d;
    d.normal = normalize(normal);
    d.cavity = lerp(1, ratios.x * 2, DetailSettings.w);
    d.grain = lerp(1, ratios.y * 2, DetailSettings.z);
    return d;
}

// Everything ours lights rock by.
struct Rock
{
    float3 position;
    // The engine's normal map, read only while rock is lit toward its smooth surface, and its sun
    // and world up in the mesh's tangent space.
    float2 normalUv;
    float normalStrength;
    float3 tangentLight;
    float3 tangentUp;
    float3 lightColour;
    float shadow;
    float3 sky;
    float3 ground;
    float height;
    float occlusion;
};

Rock MakeRock(float3 position, float2 normalUv, float normalStrength, float3 tangentLight,
              float4 ambientVector, float3 lightColour, float shadow, float3 sky, float3 ground,
              float occlusion)
{
    Rock r;
    r.position = position;
    r.normalUv = normalUv;
    r.normalStrength = normalStrength;
    r.tangentLight = tangentLight;
    r.tangentUp = ambientVector.xyz;
    r.lightColour = lightColour;
    r.shadow = shadow;
    r.sky = sky;
    r.ground = ground;
    r.height = ambientVector.w;
    r.occlusion = occlusion;
    return r;
}

// Each triangle lit flat by the sun, by the sky over it and by the ground under it as far as it
// faces up, with the detail map's relief, grain and cavity, blended toward the engine's smooth
// surface by Shading.x. No highlight but a small, tight glint of the sun off the triangles.
float3 RockLight(Rock r, float3 albedo, sampler2D normalMap)
{
    float3 dx = ddx(r.position);
    float3 dy = ddy(r.position);
    float3 face = Facing(r.position, dx, dy);
    Detail detail = DetailOn(r.position + CameraPosition.xyz, face, dx, dy);

    float facing = dot(detail.normal, ToLight.xyz);
    float up = detail.normal.z;
    float2 normalDx = ddx(r.normalUv);
    float2 normalDy = ddy(r.normalUv);
    [branch] if (Shading.x > 0)
    {
        float4 texel = tex2Dgrad(normalMap, r.normalUv, normalDx, normalDy);
        float3 tangentNormal = DecodeNormal(texel, r.normalStrength);
        float3 flattened = normalize(tangentNormal * float3(2, 2, 1));
        facing = lerp(facing, dot(tangentNormal, -r.tangentLight), Shading.x);
        up = lerp(up, dot(flattened, r.tangentUp), Shading.x);
    }
    float skyward = saturate(up * 0.5 + 0.5 + r.height);
    float3 ambient = lerp(r.ground, r.sky, skyward) * r.occlusion * detail.cavity;
    float3 sun = saturate(facing) * r.lightColour * r.shadow;

    float3 halfway = normalize(normalize(-r.position) + ToLight.xyz);
    float glint = facing > 0 ? pow(saturate(dot(detail.normal, halfway)), Shading.z) : 0;
    return albedo * detail.grain * (ambient + sun) + glint * Shading.y * r.lightColour * r.shadow;
}

// The cascaded, single-slice and river shaders' interpolators; the tangent-space half vector at
// TEXCOORD8 is replaced by the position.
struct HemisphereInput
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

float4 CascadedPS(HemisphereInput i,
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
    Rock r = MakeRock(i.position, Tiled(i.uv, normalTiling), mask.b, i.light, i.ambientVector,
                      lightColour.rgb, lerp(lit, FakeShadow(i.sky), isLastSlice * i.ambientHalf.w),
                      i.sky, tex2D(hemisphereMap, i.hemisphere).rgb, i.mask.a);
    float4 albedo = Albedo(tex2D(diffuse1, Tiled(i.uv, diffuse1Tiling)),
                           tex2D(diffuse2, Tiled(i.uv, diffuse2Tiling)).rgb, colourBase.rgb,
                           colour1.rgb, colour2.rgb, mask);
    return Fogged(RockLight(r, albedo.rgb, normalMap), albedo.a, i.fog);
}

float4 SinglePS(HemisphereInput i,
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
    Rock r = MakeRock(i.position, Tiled(i.uv, normalTiling), mask.b, i.light, i.ambientVector,
                      lightColour.rgb, lerp(lit, FakeShadow(i.sky), i.ambientHalf.w), i.sky,
                      tex2D(hemisphereMap, i.hemisphere).rgb, i.mask.a);
    float4 albedo = Albedo(tex2D(diffuse1, Tiled(i.uv, diffuse1Tiling)),
                           tex2D(diffuse2, Tiled(i.uv, diffuse2Tiling)).rgb, colourBase.rgb,
                           colour1.rgb, colour2.rgb, mask);
    return Fogged(RockLight(r, albedo.rgb, normalMap), albedo.a, i.fog);
}

// No second diffuse map: one sampler and every register after the first colour pair move down.
float4 RiverPS(HemisphereInput i,
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
    Rock r = MakeRock(i.position, Tiled(i.uv, normalTiling), mask.b, i.light, i.ambientVector,
                      lightColour.rgb, lerp(lit, FakeShadow(i.sky), i.ambientHalf.w), i.sky,
                      tex2D(hemisphereMap, i.hemisphere).rgb, i.mask.a);
    float4 albedo = Albedo(tex2D(diffuse1, Tiled(i.uv, diffuse1Tiling)), 0, colourBase.rgb,
                           colour1.rgb, 0, mask);
    return Fogged(RockLight(r, albedo.rgb, normalMap), albedo.a, i.fog);
}

struct VertexAmbientInput
{
    float4 uv : TEXCOORD0;
    float3 mask : TEXCOORD1_centroid;
    float3 shadowCoords : TEXCOORD2_centroid;
    float4 fog : TEXCOORD3_centroid;
    float4 ambient : TEXCOORD4_centroid;
    float3 light : TEXCOORD5_centroid;
    float3 position : TEXCOORD7;
};

// The ambient arrives lit per vertex, occlusion and all, so it stands for both sky and ground; past
// the shadow map the sun is unshadowed.
float4 VertexAmbientPS(VertexAmbientInput i,
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
    float lit = SinglePcf(shadowMap, i.shadowCoords, mapSize, texelScale);
    Rock r = MakeRock(i.position, Tiled(i.uv, normalTiling), mask.b, i.light, 0, lightColour.rgb,
                      lerp(lit, 1, i.ambient.w), i.ambient.rgb, i.ambient.rgb, 1);
    float4 albedo = Albedo(tex2D(diffuse1, Tiled(i.uv, diffuse1Tiling)),
                           tex2D(diffuse2, Tiled(i.uv, diffuse2Tiling)).rgb, colourBase.rgb,
                           colour1.rgb, colour2.rgb, mask);
    return Fogged(RockLight(r, albedo.rgb, normalMap), albedo.a, i.fog);
}

struct ShadowlessInput
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

float4 ShadowlessPS(ShadowlessInput i,
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
    Rock r = MakeRock(i.position, Tiled(i.uv, normalTiling), mask.b, i.light, i.ambientVector,
                      lightColour.rgb, FakeShadow(i.sky), i.sky,
                      tex2D(hemisphereMap, i.hemisphere).rgb, i.mask.a);
    float4 albedo = Albedo(tex2D(diffuse1, Tiled(i.uv, diffuse1Tiling)),
                           tex2D(diffuse2, Tiled(i.uv, diffuse2Tiling)).rgb, colourBase.rgb,
                           colour1.rgb, colour2.rgb, mask);
    return Fogged(RockLight(r, albedo.rgb, normalMap), albedo.a, i.fog);
}
