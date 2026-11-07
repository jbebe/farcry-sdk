// The player's shadow: one soft circle on the ground under the feet, multiplied into the world from the
// engine's linear depth. See src/shadow.cpp.

// In metres: darkest out to INNER from the feet and gone by OUTER, and fading out above or below the
// feet's level by HEIGHT. Then what its middle takes from the light.
#define INNER 0.15f
#define OUTER 0.55f
#define HEIGHT 0.5f
#define DARKNESS 0.7f

sampler2D SceneDepth : register(s0);

// xy: half the view's width and height one metre ahead. z: what the linear depth is a fraction of.
float4 Lens : register(c0);
float4 CameraRight : register(c1);
float4 CameraUp : register(c2);
float4 CameraForward : register(c3);
// xyz: where the feet stand, from the eye.
float4 Feet : register(c4);

// Colour the world is multiplied by, and in alpha what it loses, which the alpha test skips at nought.
float4 MainPS(float2 uv : TEXCOORD0) : COLOR0 {
    float z = dot(tex2D(SceneDepth, uv).rgb, float3(1.0f, 1.0f / 256.0f, 1.0f / 65536.0f)) * Lens.z;
    float3 p = (CameraForward.xyz + CameraRight.xyz * ((uv.x * 2.0f - 1.0f) * Lens.x) +
                CameraUp.xyz * ((1.0f - uv.y * 2.0f) * Lens.y)) * z;
    float3 fromFeet = p - Feet.xyz;
    float circle = smoothstep(OUTER, INNER, length(fromFeet.xy)) *
                   smoothstep(HEIGHT, 0.0f, abs(fromFeet.z));

    // Nothing on the weapon, which the depth holds at nought.
    float lost = DARKNESS * circle * step(0.05f, z);
    return float4(1.0f - lost, 1.0f - lost, 1.0f - lost, lost);
}
