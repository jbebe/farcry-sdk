// Tree leaves lit by the sun, in place of the engine's leaf vertex shaders. Placement, fog and
// shadow are the engine's operation for operation; the light is ours: the crown shades itself away
// from the sun, and every leaf tilts its own way. See docs/docs/file-formats/shader-objects.md.

#include "foliage.inc.fx"

float4x4 ViewRotProjectionMatrix : register(c0);
float3 ViewPoint : register(c47);
float3 FogColor : register(c49);
float3 FogColorRange : register(c50);
float BloomAdaptationFactor : register(c58);
float4 CurvedHorizonFactors : register(c60);
// Per scale equation: the distances it holds between, its slope and its offset.
float4 LeavesEquations[6] : register(c71);

// x  how much of the sunlight the crown takes from its far side
// y  how thick the crown is to that light
// z  how far each leaf tilts its own way
// w  the glint off leaves facing the sun
float4 Leaves : register(c110);
// x  the sunlight through leaves the sun stands behind
float4 LeavesGlow : register(c111);

struct Vertex
{
    // xy  which corner of the leaf, zw  its uv
    float4 corner : PSIZE;
    // xyz  the leaf's centre to this corner, w  which scale equation
    float4 spoke : FOG;
    // x  how far the corner is from the centre
    float4 reach : TEXCOORD0;
    // rg  how hidden the leaf is inside the tree
    float4 colour : COLOR0;
    // xyz  the leaf's centre, w  its growth
    float4 position : POSITION;
};

// A tree drawn many times over, whose place and turn come with each copy.
struct Copy
{
    float3 position : TEXCOORD2;
    // The turn's sine and cosine.
    float2 turn : TEXCOORD3;
    float4 ground : TEXCOORD4;
    float4 sky : TEXCOORD5;
};

// What the engine hands one draw of leaves.
struct Tree
{
    float4x3 world;
    bool curved;
    // xyz  the crown's centre, w  its radius
    float4 crown;
    float3 lightColor;
    float3 lightDirection;
    float meshHeight;
    float4 compression;
    float fakeSpecularPower;
    float sssHighLight;
    float sssStrength;
    float occlusionIntensity;
    float4 morph;
    // The light from the ground and from the sky, before SkyColor.
    float3 ground;
    float3 sky;
    float3 skyColor;
};

struct Shadow
{
    float4x3 projection;
    // Cascades pick their slice by the point's distance from the map's centre.
    bool cascaded;
    // zw  how a point's distance from the map's edge fades its shadow
    float4 edgeFade;
    // See ShadowShown.
    float4 fade[2];
};

struct Light
{
    // rgb  the fog, w  what is left of the leaf through it
    float4 fog;
    // w  how hidden the leaf is inside the tree
    float4 ambient;
    // Shadowed by the pixel shader. w  the glint
    float4 sun;
    float3 through;
    float3 normal;
};

// The engine lowers what stands far off, so the ground curves away over the horizon.
float CurvedDrop(float distance)
{
    float x = saturate((distance - CurvedHorizonFactors.x) * CurvedHorizonFactors.y);
    return (x * CurvedHorizonFactors.z + CurvedHorizonFactors.w) * (x * x);
}

float4x3 Turned(Copy copy)
{
    float4x3 world;
    world._m00_m10_m20_m30 = float4(copy.turn.y, -copy.turn.x, 0.0f, copy.position.x);
    world._m01_m11_m21_m31 = float4(copy.turn.x, copy.turn.y, 0.0f, copy.position.y);
    world._m02_m12_m22_m32 = float4(0.0f, 0.0f, 1.0f, copy.position.z);
    return world;
}

// A leaf as the engine places it: turned to face the eye up close, set along its spoke far off.
float3 Place(Vertex v, inout Tree t, out float3 local, out float3 centre)
{
    if (t.curved)
    {
        float3 origin = float3(t.world._m30, t.world._m31, t.world._m32);
        t.world._m32 -= CurvedDrop(length(CameraPosition.xyz - origin));
    }

    float c = t.world._m00;
    float s = -t.world._m01;
    float4x3 toLocal;
    toLocal._m00_m10_m20_m30 = float4(c, -s, 0.0f, (-t.world._m30 * c) + (-t.world._m31 * (-s)));
    toLocal._m01_m11_m21_m31 = float4(s, c, 0.0f, (-t.world._m30 * s) + (-t.world._m31 * c));
    toLocal._m02_m12_m22_m32 = float4(0.0f, 0.0f, 1.0f, -t.world._m32);
    float3 toEye = mul(float4(ViewPoint, 1.0f), toLocal);
    float eyeDistance = length(toEye);
    toEye /= eyeDistance;
    float treeDistance = eyeDistance * CameraPosition.w;

    float growth = v.position.w;
    float3 spoke = growth * v.reach.x * (v.spoke.xyz * 2.0f - 1.0f);
    float index = v.spoke.w * 255.0f + 0.5f;
    float facing = saturate(treeDistance * t.morph.x + t.morph.y);
    if (index > 99.0f)
    {
        index = index - 100.0f;
        facing = 0.0f;
    }

    float3 across = float3(-toEye.y, toEye.x, 0.0f);
    float2 diagonal = v.corner.xy * 2.0f - 1.0f;
    float3 corner = diagonal.x * normalize(float3(across.x, across.y, across.z + diagonal.y));

    float4 equation = LeavesEquations[index];
    float scale = growth * (clamp(treeDistance, equation.x, equation.y) * equation.z + equation.w);
    centre = v.position.xyz * t.compression.y + t.compression.x;
    local = lerp(scale * spoke, scale * v.reach.x * corner, facing) + centre;
    return mul(float4(local, 1.0f), t.world);
}

Light Shade(Vertex v, Tree t, float3 world, float3 local, float3 centre)
{
    Light o;
    float3 toEye = CameraPosition.xyz - world;
    float eyeDistance = length(toEye);
    float3 eye = toEye / eyeDistance;

    float fog = FogAmount(eyeDistance, world.z);
    o.fog = float4((FogRampPlace(toEye) * FogColorRange + FogColor) * fog, 1.0f - fog) *
            BloomAdaptationFactor;

    // The engine's own measure of how hidden a leaf is: its vertex colour, its depth in the crown
    // and which way its spoke points.
    float3 fromCentre = local - t.crown.xyz;
    float deep = min(dot(fromCentre, fromCentre) / (t.crown.w * t.crown.w), 1.0f);
    float hidden = (deep * deep + v.colour.x + v.colour.y) * 0.33f + v.spoke.z * 3.0f;
    float occlusion = lerp(1.0f, saturate(hidden - 1.36f), t.occlusionIntensity);

    // Where the leaf sits in the crown, one at its rim. Its own tilt comes from a hash of its
    // centre, which every corner of the leaf shares.
    float3 offset = mul(fromCentre, (float3x3)t.world) / t.crown.w;
    float3 outward = normalize(offset);
    float3 tilt = frac(sin(dot(centre, float3(12.9898f, 78.233f, 37.719f))) *
                       float3(43758.5453f, 22578.1459f, 19642.349f)) * 2.0f - 1.0f;
    o.normal = normalize(outward + tilt * Leaves.z);

    // How much crown the sunlight crosses to reach the leaf, in radii.
    float3 toSun = -normalize(t.lightDirection);
    float along = dot(offset, toSun);
    float crossed = max(-along + sqrt(max(along * along - dot(offset, offset) + 1.0f, 0.0f)), 0.0f);
    float crown = lerp(1.0f, exp(-crossed * Leaves.y), Leaves.x);

    float height = min(saturate(centre.z / t.meshHeight), 0.8f) * 0.9f - 0.6f;
    float upward = saturate(normalize(o.normal * float3(2.0f, 2.0f, 1.0f)).z * 0.5f + 0.5f + height);
    float3 ground = t.skyColor * t.ground * 2.1875f;
    float3 sky = t.skyColor * t.sky * occlusion;
    float inside = lerp(1.0f - Leaves.x * 0.5f, 1.0f, saturate(length(offset)));
    o.ambient = float4(lerp(ground, sky, upward) * inside, occlusion);

    // Wrapped, since light reaches round a leaf's edge.
    float direct = saturate((dot(o.normal, toSun) + 0.3f) / 1.3f) * crown;
    float near = min(exp((20.0f - eyeDistance) * 0.004f), 1.0f);
    float glint = pow(saturate(dot(normalize(eye + toSun), o.normal)), t.fakeSpecularPower);
    o.sun = float4(direct * t.lightColor * occlusion, glint * near * Leaves.w * crown * occlusion);

    float edgeOn = 1.0f - abs(dot(-eye, outward));
    float behind = pow(saturate(dot(normalize(t.lightDirection), eye)), t.sssHighLight) * edgeOn;
    float glowFade = min(exp((20.0f - eyeDistance) * 0.01f), 1.0f);
    o.through = behind * (t.lightColor + sky) * glowFade * t.sssStrength * LeavesGlow.x;
    return o;
}

float4 ToScreen(float3 world)
{
    return mul(float4(world - CameraPosition.xyz, 1.0f), ViewRotProjectionMatrix);
}

// Shadowed through one shadow map slice, by the pixel shader the engine pairs with it.
struct ShadowedLit
{
    float4 position : POSITION;
    float4 shadow : TEXCOORD0;
    float4 fog : TEXCOORD1;
    float4 ambient : TEXCOORD2;
    float4 sun : TEXCOORD3;
    // w  how much of the shadow shows
    float4 through : TEXCOORD4;
    float3 normal : TEXCOORD5;
    float2 uv : TEXCOORD6;
};

ShadowedLit Shadowed(Vertex v, Tree t, Shadow s)
{
    float3 local;
    float3 centre;
    float3 world = Place(v, t, local, centre);
    Light light = Shade(v, t, world, local, centre);
    ShadowedLit o;
    o.position = ToScreen(world);
    float3 projected = mul(float4(world, 1.0f), s.projection);
    o.fog = light.fog;
    o.ambient = light.ambient;
    o.sun = light.sun;
    float centred;
    float shown = ShadowShown(projected, s.cascaded, s.edgeFade, s.fade[0], s.fade[1], centred);
    o.shadow = float4(projected, s.cascaded ? centred : 1.0f);
    o.through = float4(light.through, shown);
    o.normal = light.normal;
    o.uv = v.corner.zw;
    return o;
}

// Unshadowed, by the pixel shader the engine pairs with it.
struct UnshadowedLit
{
    float4 position : POSITION;
    float4 fog : TEXCOORD0;
    float4 ambient : TEXCOORD1;
    float4 sun : TEXCOORD2;
    float3 through : TEXCOORD3;
    float3 normal : TEXCOORD4;
    float2 uv : TEXCOORD5;
};

UnshadowedLit Unshadowed(Vertex v, Tree t)
{
    float3 local;
    float3 centre;
    float3 world = Place(v, t, local, centre);
    Light light = Shade(v, t, world, local, centre);
    UnshadowedLit o;
    o.position = ToScreen(world);
    o.fog = light.fog;
    o.ambient = light.ambient;
    o.sun = light.sun;
    o.through = light.through;
    o.normal = light.normal;
    o.uv = v.corner.zw;
    return o;
}

// The registers of the shadowed shaders for one tree, through one slice and through cascades alike.
#define ONE_TREE_SHADOWED                                                                          \
    uniform float4x3 projection : register(c77), uniform float4x3 world : register(c80),           \
    uniform float4 crown : register(c83), uniform float3 lightColor : register(c84),               \
    uniform float3 lightDirection : register(c85), uniform float4 edgeFade : register(c86),        \
    uniform float4 fade0 : register(c87), uniform float4 fade1 : register(c88),                    \
    uniform float meshHeight : register(c89), uniform float4 compression : register(c90),          \
    uniform float fakeSpecularPower : register(c91), uniform float sssHighLight : register(c92),   \
    uniform float sssStrength : register(c93), uniform float occlusionIntensity : register(c94),   \
    uniform float4 morph : register(c95), uniform float3 ground : register(c96),                   \
    uniform float3 sky : register(c97), uniform float3 skyColor : register(c98)

#define DRAW_ONE_TREE_SHADOWED(cascaded)                                                           \
    Tree t = {world, true, crown, lightColor, lightDirection, meshHeight, compression,            \
              fakeSpecularPower, sssHighLight, sssStrength, occlusionIntensity, morph, ground, sky, \
              skyColor};                                                                           \
    Shadow s = {projection, cascaded, edgeFade, {fade0, fade1}};                                   \
    return Shadowed(v, t, s)

// The same for copies of a tree.
#define COPIES_SHADOWED                                                                            \
    uniform float4x3 projection : register(c77), uniform float4 crown : register(c80),             \
    uniform float3 lightColor : register(c81), uniform float3 lightDirection : register(c82),      \
    uniform float4 edgeFade : register(c83), uniform float4 fade0 : register(c84),                 \
    uniform float4 fade1 : register(c85), uniform float meshHeight : register(c86),                \
    uniform float4 compression : register(c87), uniform float fakeSpecularPower : register(c88),   \
    uniform float sssHighLight : register(c89), uniform float sssStrength : register(c90),         \
    uniform float occlusionIntensity : register(c91), uniform float4 morph : register(c92),        \
    uniform float3 skyColor : register(c93)

#define DRAW_COPIES_SHADOWED(cascaded)                                                             \
    Tree t = {Turned(copy), false, crown, lightColor, lightDirection, meshHeight, compression,     \
              fakeSpecularPower, sssHighLight, sssStrength, occlusionIntensity, morph,             \
              copy.ground.rgb, copy.sky.rgb, skyColor};                                            \
    Shadow s = {projection, cascaded, edgeFade, {fade0, fade1}};                                   \
    return Shadowed(v, t, s)

ShadowedLit ShadowedVS(Vertex v, ONE_TREE_SHADOWED)
{
    DRAW_ONE_TREE_SHADOWED(false);
}

ShadowedLit CascadedVS(Vertex v, ONE_TREE_SHADOWED)
{
    DRAW_ONE_TREE_SHADOWED(true);
}

ShadowedLit ShadowedCopiesVS(Vertex v, Copy copy, COPIES_SHADOWED)
{
    DRAW_COPIES_SHADOWED(false);
}

ShadowedLit CascadedCopiesVS(Vertex v, Copy copy, COPIES_SHADOWED)
{
    DRAW_COPIES_SHADOWED(true);
}

UnshadowedLit UnshadowedVS(Vertex v,
                           uniform float4x3 world : register(c77),
                           uniform float4 crown : register(c80),
                           uniform float3 lightColor : register(c81),
                           uniform float3 lightDirection : register(c82),
                           uniform float meshHeight : register(c83),
                           uniform float4 compression : register(c84),
                           uniform float fakeSpecularPower : register(c85),
                           uniform float sssHighLight : register(c86),
                           uniform float sssStrength : register(c87),
                           uniform float occlusionIntensity : register(c88),
                           uniform float4 morph : register(c89),
                           uniform float3 ground : register(c90),
                           uniform float3 sky : register(c91),
                           uniform float3 skyColor : register(c92))
{
    Tree t = {world, true, crown, lightColor, lightDirection, meshHeight, compression,
              fakeSpecularPower, sssHighLight, sssStrength, occlusionIntensity, morph, ground, sky,
              skyColor};
    return Unshadowed(v, t);
}

UnshadowedLit UnshadowedCopiesVS(Vertex v, Copy copy,
                                 uniform float4 crown : register(c77),
                                 uniform float3 lightColor : register(c78),
                                 uniform float3 lightDirection : register(c79),
                                 uniform float meshHeight : register(c80),
                                 uniform float4 compression : register(c81),
                                 uniform float fakeSpecularPower : register(c82),
                                 uniform float sssHighLight : register(c83),
                                 uniform float sssStrength : register(c84),
                                 uniform float occlusionIntensity : register(c85),
                                 uniform float4 morph : register(c86),
                                 uniform float3 skyColor : register(c87))
{
    Tree t = {Turned(copy), true, crown, lightColor, lightDirection, meshHeight, compression,
              fakeSpecularPower, sssHighLight, sssStrength, occlusionIntensity, morph,
              copy.ground.rgb, copy.sky.rgb, skyColor};
    return Unshadowed(v, t);
}
