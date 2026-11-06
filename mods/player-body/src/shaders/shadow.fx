// The player's shadow: a column of soft spheres from the feet to the head, each casting along the light
// onto whatever the engine's linear depth shows, multiplied into the world. See src/shadow.cpp.

#define SPHERES 7
// In metres: the blur at the body, and what it grows by over each metre from it.
#define BLUR 0.12f
#define SPREAD 0.25f

sampler2D SceneDepth : register(s0);

// xy: half the view's width and height one metre ahead. z: what the linear depth is a fraction of.
float4 Lens : register(c0);
float4 CameraRight : register(c1);
float4 CameraUp : register(c2);
float4 CameraForward : register(c3);
// From the eye: the lowest sphere's centre and every sphere's radius, then the highest's centre and
// how much light a full cover takes away.
float4 Feet : register(c4);
float4 Head : register(c5);
// xyz: toward the light.
float4 Light : register(c6);

// Colour the world is multiplied by, and in alpha what it loses, which the alpha test skips at nought.
float4 MainPS(float2 uv : TEXCOORD0) : COLOR0 {
    float share = dot(tex2D(SceneDepth, uv).rgb, float3(1.0f, 1.0f / 256.0f, 1.0f / 65536.0f));
    float z = share * Lens.z;
    float3 p = (CameraForward.xyz + CameraRight.xyz * ((uv.x * 2.0f - 1.0f) * Lens.x) +
                CameraUp.xyz * ((1.0f - uv.y * 2.0f) * Lens.y)) * z;
    float radius = Feet.w;
    float3 axis = Head.xyz - Feet.xyz;

    // The most any sphere takes from the light. A blurred sphere spreads what it takes over more
    // ground, and starts fading on the light's side of its centre.
    float cover = 0.0f;
    [unroll] for (int i = 0; i < SPHERES; i++) {
        float3 toCentre = Feet.xyz + axis * (i / (SPHERES - 1.0f)) - p;
        float along = dot(toCentre, Light.xyz);
        float miss = length(toCentre - Light.xyz * along);
        float blur = BLUR + SPREAD * abs(along);
        float edge = smoothstep(radius + blur, radius - blur, miss);
        float spread = saturate(2.0f * radius / (radius + blur));
        cover = max(cover, edge * spread * saturate(1.0f + along / blur));
    }

    // Nothing on the weapon, which the depth holds at nought, or on the sky, which it holds white.
    float lost = Head.w * cover * step(0.05f, z) * step(share, 0.995f);
    return float4(1.0f - lost, 1.0f - lost, 1.0f - lost, lost);
}
