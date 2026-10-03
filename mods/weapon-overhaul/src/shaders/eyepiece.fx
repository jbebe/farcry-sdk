// The gun's depth copied into a float target, which unlike the depth texture can be read back.

// The weapon's hardware depth at half resolution, one where the weapon is not.
sampler2D WeaponDepth : register(s3);

float4 DepthPS(float2 uv : TEXCOORD0) : COLOR0 {
    return tex2D(WeaponDepth, uv).r;
}
