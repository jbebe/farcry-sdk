// Grass lit by the sun, in place of the vertex shader the engine builds for it. Placement, sway,
// fog and shadow are the engine's operation for operation; the light is ours, built only from what
// a clump's turn toward the camera leaves alone. See docs/docs/file-formats/shader-objects.md.

float4x4 ViewProjectionMatrix : register(c4);
// The camera's heading, which every clump is turned by.
float3x3 GrassCylindricalBillboardMatrix : register(c32);
float4 CameraPosition : register(c45);
float3 FogColorVector : register(c48);
float3 FogValues : register(c51);
float4 FogHeightValues : register(c52);
float4 WindSimParamsX : register(c65);
float4 WindSimParamsY : register(c66);
float4x3 ShadowProjectionMatrix : register(c71);
// x  position offset, y  position scale, z  uv offset, w  uv scale
float4 MeshDecompression : register(c74);
float3 LightColor : register(c75);
float3 LightDirectionWS : register(c76);
// zw  how a point's distance from the shadow map's edge fades its shadow
float4 ShadowEdgeFade : register(c77);
// A single slice keeps the map's extent in [0] and the fade over depth in [1]; cascades keep the
// fade over depth in [0] and their extent, which differs on each side of the centre, in [1].
float4 ShadowFade[2] : register(c78);
float3 SkyColor : register(c80);
float2 DiffuseTiling1 : register(c81);

// x  the light the root of a blade keeps, against its tip
// y  how far a clump's sides turn toward or away from the sun
// z  the shine along blades
// w  the sunlight through a blade the sun stands behind
float4 Grass : register(c110);
// x  how narrow the shine along blades is
float4 GrassSheen : register(c111);

// The box instance positions are packed into.
static const float3 WORLD_BOX = float3(5120.0f, 5120.0f, 256.0f);

struct Vertex
{
    float4 position : POSITION;
    float2 uv : TEXCOORD0;
    // a  how far the engine lets a blade's wind show
    float4 colour : COLOR0;
    // xyz  the clump's place in the world's box, w  the ground's normal x
    float4 clump : TEXCOORD2;
    // w  the clump's height
    float4 clumpColour : COLOR1;
    float4 windX : TEXCOORD3;
    float4 windY : TEXCOORD6;
    // How much sky the clump sees.
    float4 sky : TEXCOORD5;
    // w  the ground's normal y
    float4 ground : FOG;
};

struct Lit
{
    float4 position : POSITION;
    // xyz  the shadow map, w  what the cascades pick their slice by
    float4 shadow : TEXCOORD0;
    // w  where the fog's colour ramp stands
    float4 ambient : TEXCOORD1;
    // Shadowed by the pixel shader. w  how much fog
    float4 sun : TEXCOORD2;
    // z  how much of the shadow shows
    float3 uvFade : TEXCOORD3;
};

// A clump as the engine places it, and what its placing leaves behind for the light.
struct Placed
{
    float3 world;
    float3 model;
    float3 turned;
    float3 offset;
    float weight;
    float distance;
    float far;
};

Placed Place(Vertex v)
{
    Placed p;
    p.model = v.position.xyz * MeshDecompression.y + MeshDecompression.x;
    float4 windX = v.windX * 2.0f - 1.0f;
    float2 wind = float2(dot(windX, WindSimParamsX),
                         dot(float4(v.windY.xy * 2.0f - 1.0f, windX.zw), WindSimParamsY));
    p.turned = mul(p.model, GrassCylindricalBillboardMatrix);
    p.turned.z *= v.clumpColour.w;
    p.weight = saturate(p.turned.z * 0.5f - 0.1f);
    wind *= p.weight;

    float3 clump = v.clump.xyz * 0.5f + 0.5f;
    p.distance = length(CameraPosition.xyz - (clump * WORLD_BOX + p.turned));
    p.far = saturate((p.distance - 25.0f) * 0.002325f);
    p.offset = float3(wind * (p.far * 3.5f + 1.0f), 0.0f);
    float3 swayed = p.turned + p.offset;
    float3 moved = swayed * sqrt(dot(p.turned, p.turned) / dot(swayed, swayed)) + p.offset;
    p.world = clump * WORLD_BOX + moved;
    return p;
}

// The engine's brightening of blades the wind is bending, which is what patches a field.
float Wave(Placed p, Vertex v)
{
    float lean = saturate(p.offset.y * 5.0f - p.offset.x + 0.25f) * min(p.weight, 0.25f);
    float near = 1.0f - max((25.0f - p.distance) * 0.04f, 0.0f);
    float shown = lerp(v.colour.a, 1.0f, near * near);
    float reach = shown * shown * 14.44f / (1.33329999f + 0.0025f * p.far + 0.03f * p.distance);
    return lean * shown * reach;
}

Lit Light(Vertex v, Placed p)
{
    Lit o;
    o.position = mul(float4(p.world, 1.0f), ViewProjectionMatrix);

    float3 toEye = CameraPosition.xyz - p.world;
    float2 heading = normalize(toEye.xy);
    float facing = dot(-heading, FogColorVector.xy);
    float rampPlace = (facing * -0.0675179511f + 0.5f) * sqrt(max(1.0f - facing, 0.0f));
    float fog = saturate(p.distance * FogValues.x + FogValues.y) * FogValues.z *
                (saturate(p.world.z * FogHeightValues.x + FogHeightValues.y) * FogHeightValues.z +
                 FogHeightValues.w);

    float3 toSun = -LightDirectionWS;
    toEye = normalize(toEye);
    float2 groundXY = float2(v.clump.w, v.ground.w * 2.0f - 1.0f);
    float3 ground = float3(groundXY, sqrt(1.0f - min(dot(groundXY, groundXY), 1.0f)));

    // The clump as a round tuft: a vertex's offset from its axis is the side it stands on. The
    // clump faces the eye, so its left edge is always the tuft's left side, whatever the heading.
    float spread = length(p.turned.xy);
    float2 side = p.turned.xy / max(spread, 0.001f) * saturate(spread * 2.0f) * Grass.y;
    float3 tuft = normalize(ground + float3(side, 0.0f));
    // Wrapped, since a blade is thin enough for light to reach round it.
    float direct = saturate((dot(tuft, toSun) + 0.5f) / 1.5f);

    // Height up the blade, a metre-tall mesh reaching one at its tips.
    float rise = saturate(p.model.z);
    float shade = lerp(Grass.x, 1.0f, rise);

    // Along a blade, which leans with the wind, the shine is widest where the sun and the eye meet
    // it at the same slant: a low sun, and a blade seen across.
    float3 blade = normalize(float3(p.offset.xy, 1.0f));
    float along = dot(blade, normalize(toSun + toEye + float3(0.0f, 0.0f, 0.0001f)));
    float sheen = pow(saturate(1.0f - along * along), GrassSheen.x) * Grass.z * rise;
    float glow = pow(saturate(dot(-toEye, toSun)), 4.0f) * Grass.w * rise;

    float brighten = 1.0f + Wave(p, v);
    float3 colour = v.clumpColour.rgb * v.colour.rgb * 4.0f;
    float3 ambient = (v.clumpColour.rgb * 2.5f + v.sky.rgb) * SkyColor * 0.5f;
    o.ambient = float4(ambient * shade * brighten * colour, rampPlace);
    o.sun = float4(LightColor * (direct * shade + sheen + glow) * brighten * colour, fog);

    o.shadow = float4(mul(float4(p.world, 1.0f), ShadowProjectionMatrix), 1.0f);
    o.uvFade = float3((v.uv * MeshDecompression.w + MeshDecompression.z) * DiffuseTiling1, 0.0f);
    return o;
}

// How much of the shadow shows at a point `edge` from the map's centre, in extents.
float ShadowShown(float edge, float depthFade, float4 depth)
{
    return 1.0f - saturate(depth.x * depthFade + depth.y) *
                  saturate(edge * ShadowEdgeFade.z + ShadowEdgeFade.w);
}

// One shadow map slice.
Lit MainVS(Vertex v)
{
    Lit o = Light(v, Place(v));
    float2 edge = o.shadow.xy * ShadowFade[0].xy + ShadowFade[0].zw;
    o.uvFade.z = ShadowShown(max(abs(edge.x), abs(edge.y)), o.shadow.z, ShadowFade[1]);
    return o;
}

// Cascades, which pick their slice by the point's distance from the centre.
Lit CascadedVS(Vertex v)
{
    Lit o = Light(v, Place(v));
    float2 edge = o.shadow.xy * lerp(ShadowFade[1].zw, ShadowFade[1].xy, o.shadow.xy >= 0.0f);
    o.shadow.w = max(abs(edge.x), abs(edge.y));
    o.uvFade.z = ShadowShown(o.shadow.w, o.shadow.z, ShadowFade[0]);
    return o;
}
