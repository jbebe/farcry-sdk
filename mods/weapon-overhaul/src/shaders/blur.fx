// The gun out of focus down the sights, the eye focused on the front sight. See src/blur.cpp for the
// passes.

// x: turns a difference in the weapon's stored depth into its blur, as a share of the largest.
// y: how far the eye has settled into the sights. z: how far the focus moves toward this frame's.
float4 Lens : register(c0);
// xy: the blur's step along its axis, in texture coordinates.
float4 Step : register(c1);
// xy: one half-resolution texel.
float4 Texel : register(c2);

// The scene as the pass left it, or the half-resolution image being blurred.
sampler2D Source : register(s0);
// The gun's half-resolution colour blurred a little and a lot, premultiplied by its blur amount.
sampler2D Small : register(s1);
sampler2D Large : register(s2);
// The weapon's hardware depth at half resolution, one where the weapon is not.
sampler2D WeaponDepth : register(s3);
// The gun's half-resolution colour premultiplied by its blur amount, which is its alpha.
sampler2D Down : register(s4);
// The stored depth focused on, nought before there is one.
sampler2D Focus : register(s5);

static const float2 kCentre = float2(0.5f, 0.5f);

// The farthest of the gun on the front sight's line, just below the aim point, which is the front
// sight beyond the rear one. The eye eases toward it, and keeps its focus while the gun is not there.
float4 FocusPS(float2 uv : TEXCOORD0) : COLOR0 {
    float farthest = 0.0f;
    for (int x = -2; x <= 2; x++) {
        for (int y = 0; y < 5; y++) {
            float stored = tex2D(WeaponDepth, kCentre + float2(x * 3.0f, 1.0f + y * 4.0f) * Texel.xy).r;
            farthest = stored < 1.0f ? max(farthest, stored) : farthest;
        }
    }
    float previous = tex2D(Focus, kCentre).r;
    float eased = previous > 0.0f ? lerp(previous, farthest, Lens.z) : farthest;
    return float4(farthest > 0.0f ? eased : previous, 0.0f, 0.0f, 1.0f);
}

float4 DownPS(float2 uv : TEXCOORD0) : COLOR0 {
    float stored = tex2D(WeaponDepth, uv).r;
    float focus = tex2D(Focus, kCentre).x;
    float amount = stored < 1.0f ? saturate(Lens.x * abs(stored - (focus > 0.0f ? focus : 1.0f)))
                                 : 0.0f;
    return float4(tex2D(Source, uv).rgb * amount, amount);
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
    float3 sharp = tex2D(Source, uv).rgb;
    float4 small = tex2D(Small, uv);
    float4 large = tex2D(Large, uv);
    float3 smallColour = small.rgb / max(small.a, 0.0001f);
    float3 largeColour = large.rgb / max(large.a, 0.0001f);

    // The gun goes from sharp through a little blur to a lot by its own amount, from its own colour
    // alone; past its edge, its largest blur spreads over what is behind it.
    float own = tex2D(Down, uv).a;
    float3 gun = own < 0.5f ? lerp(sharp, smallColour, own * 2.0f)
                            : lerp(smallColour, largeColour, own * 2.0f - 1.0f);
    float spread = saturate(2.0f * large.a);
    bool onGun = tex2D(WeaponDepth, uv).r < 1.0f;
    float3 blurred = onGun ? gun : lerp(sharp, largeColour, spread);

    clip((onGun ? own : spread) * Lens.y - 1.0f / 256.0f);
    return float4(lerp(sharp, blurred, Lens.y), 1.0f);
}
