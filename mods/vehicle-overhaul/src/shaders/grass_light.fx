// The player's headlight on grass, drawn as a second, additive pass over each draw of grass lit by
// the sun. Placement is the engine's operation for operation, as in Sky Overhaul's grass.fx, so it
// lands on exactly the depth the engine's draw left. See docs/docs/file-formats/shader-objects.md.

float4x4 ViewProjectionMatrix : register(c4);
// The camera's heading, which every clump is turned by.
float3x3 GrassCylindricalBillboardMatrix : register(c32);
float4 CameraPosition : register(c45);
float3 FogValues : register(c51);
float4 FogHeightValues : register(c52);
float4 WindSimParamsX : register(c65);
float4 WindSimParamsY : register(c66);
// x  position offset, y  position scale, z  uv offset, w  uv scale
float4 MeshDecompression : register(c74);
float2 DiffuseTiling1 : register(c81);

// xyz  where the light is, w  its range
float4 HeadlightPosition : register(c112);
// xyz  which way it points
float4 HeadlightDirection : register(c113);
// rgb  its colour times its intensity
float4 HeadlightColour : register(c114);
// x  the cosine of half its inner cone, y  of half its outer cone
float4 HeadlightCone : register(c115);

// The exposure the whole frame is scaled by, which the engine's grass pixel shaders apply.
float4 Exposure : register(c58);
sampler2D Diffuse : register(s1);

// The box instance positions are packed into.
static const float3 WORLD_BOX = float3(5120.0f, 5120.0f, 256.0f);

struct Vertex
{
    float4 position : POSITION;
    float2 uv : TEXCOORD0;
    float4 colour : COLOR0;
    // xyz  the clump's place in the world's box, w  the ground's normal x
    float4 clump : TEXCOORD2;
    // w  the clump's height
    float4 clumpColour : COLOR1;
    float4 windX : TEXCOORD3;
    float4 windY : TEXCOORD6;
    // w  the ground's normal y
    float4 ground : FOG;
};

struct Lit
{
    float4 position : POSITION;
    float2 uv : TEXCOORD0;
    // rgb  the light the clump reflects, with the fog's share taken out
    float3 light : TEXCOORD1;
};

Lit MainVS(Vertex v)
{
    float3 model = v.position.xyz * MeshDecompression.y + MeshDecompression.x;
    float4 windX = v.windX * 2.0f - 1.0f;
    float2 wind = float2(dot(windX, WindSimParamsX),
                         dot(float4(v.windY.xy * 2.0f - 1.0f, windX.zw), WindSimParamsY));
    float3 turned = mul(model, GrassCylindricalBillboardMatrix);
    turned.z *= v.clumpColour.w;
    float weight = saturate(turned.z * 0.5f - 0.1f);
    wind *= weight;

    float3 clump = v.clump.xyz * 0.5f + 0.5f;
    float distance = length(CameraPosition.xyz - (clump * WORLD_BOX + turned));
    float far = saturate((distance - 25.0f) * 0.002325f);
    float3 offset = float3(wind * (far * 3.5f + 1.0f), 0.0f);
    float3 swayed = turned + offset;
    float3 moved = swayed * sqrt(dot(turned, turned) / dot(swayed, swayed)) + offset;
    float3 world = clump * WORLD_BOX + moved;

    Lit o;
    o.position = mul(float4(world, 1.0f), ViewProjectionMatrix);
    o.uv = (v.uv * MeshDecompression.w + MeshDecompression.z) * DiffuseTiling1;

    float fog = saturate(distance * FogValues.x + FogValues.y) * FogValues.z *
                (saturate(world.z * FogHeightValues.x + FogHeightValues.y) * FogHeightValues.z +
                 FogHeightValues.w);

    float3 toLight = HeadlightPosition.xyz - world;
    float reach = length(toLight);
    toLight /= max(reach, 0.001f);
    // As the engine falls its lights off: a square law reaching nothing at the range.
    float falloff = saturate(1.0f - reach * reach / (HeadlightPosition.w * HeadlightPosition.w));
    float cone = saturate((dot(-toLight, HeadlightDirection.xyz) - HeadlightCone.y) /
                          max(HeadlightCone.x - HeadlightCone.y, 0.001f));

    // The clump as a round tuft, as Sky Overhaul lights it by the sun, and wrapped all the way, since
    // a headlight skims across the blades rather than falling on them from above.
    float2 groundXY = float2(v.clump.w, v.ground.w * 2.0f - 1.0f);
    float3 ground = float3(groundXY, sqrt(1.0f - min(dot(groundXY, groundXY), 1.0f)));
    float spread = length(turned.xy);
    float2 side = turned.xy / max(spread, 0.001f) * saturate(spread * 2.0f);
    float3 tuft = normalize(ground + float3(side, 0.0f));
    float facing = saturate((dot(tuft, toLight) + 1.0f) * 0.5f);

    float3 colour = v.clumpColour.rgb * v.colour.rgb * 4.0f;
    o.light = HeadlightColour.rgb * falloff * cone * facing * colour * (1.0f - fog);
    return o;
}

float4 MainPS(Lit i) : COLOR0
{
    return float4(tex2D(Diffuse, i.uv).rgb * i.light * Exposure.x, 1.0f);
}
