// The gun out of focus down the sights, the eye focused on the front sight. See src/blur.cpp for the
// passes.

// x: turns a difference in the weapon's stored depth into its blur, as a share of the largest.
// y: how far the eye has settled into the sights.
float4 Lens : register(c0);
// xy: how far around the screen's centre the front sight is looked for, in texture coordinates.
float4 Focus : register(c1);
// xy: the blur's step along its axis, in texture coordinates.
float4 Step : register(c2);

// The scene as the pass left it, or the half-resolution image being blurred.
sampler2D Source : register(s0);
// The half-resolution scene blurred, its alpha the blur amount blurred with it.
sampler2D Blurred : register(s1);
// The half-resolution scene, its alpha the blur amount.
sampler2D Down : register(s2);
// The weapon's hardware depth at half resolution, one where the weapon is not.
sampler2D WeaponDepth : register(s3);

// The farthest of the gun around the aim point, which is the front sight beyond the rear one; one
// where the gun is not there at all.
float FocusDepth() {
    static const float2 kTaps[13] = {
        float2(0.0f, 0.0f),
        float2(1.0f, 0.0f), float2(0.5f, 0.866f), float2(-0.5f, 0.866f),
        float2(-1.0f, 0.0f), float2(-0.5f, -0.866f), float2(0.5f, -0.866f),
        float2(0.433f, 0.25f), float2(0.0f, 0.5f), float2(-0.433f, 0.25f),
        float2(-0.433f, -0.25f), float2(0.0f, -0.5f), float2(0.433f, -0.25f),
    };
    float focus = 0.0f;
    for (int i = 0; i < 13; i++) {
        float stored = tex2D(WeaponDepth, 0.5f + kTaps[i] * Focus.xy).r;
        focus = stored < 1.0f ? max(focus, stored) : focus;
    }
    return focus > 0.0f ? focus : 1.0f;
}

float4 DownPS(float2 uv : TEXCOORD0) : COLOR0 {
    float stored = tex2D(WeaponDepth, uv).r;
    float amount = stored < 1.0f ? saturate(Lens.x * abs(stored - FocusDepth())) : 0.0f;
    return float4(tex2D(Source, uv).rgb, amount);
}

float4 BlurPS(float2 uv : TEXCOORD0) : COLOR0 {
    // A Gaussian over six steps either side, sigma three steps.
    static const float kWeights[7] = {0.1370f, 0.1296f, 0.1097f, 0.0831f, 0.0563f, 0.0342f, 0.0185f};
    float4 sum = tex2D(Source, uv) * kWeights[0];
    for (int i = 1; i < 7; i++) {
        sum += (tex2D(Source, uv + Step.xy * i) + tex2D(Source, uv - Step.xy * i)) * kWeights[i];
    }
    return sum;
}

float4 CompositePS(float2 uv : TEXCOORD0) : COLOR0 {
    float4 blurred = tex2D(Blurred, uv);
    // The gun keeps its own amount, so a sharp front sight stays sharp beside a blurred rear one;
    // past the gun's edge the blur spreads over what is behind it.
    float amount = tex2D(WeaponDepth, uv).r < 1.0f ? tex2D(Down, uv).a : saturate(2.0f * blurred.a);
    amount = smoothstep(0.0f, 1.0f, amount) * Lens.y;
    clip(amount - 1.0f / 256.0f);
    return float4(lerp(tex2D(Source, uv).rgb, blurred.rgb, amount), 1.0f);
}
