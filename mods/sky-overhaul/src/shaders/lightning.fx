// Lightning: a bolt's channel and its glow. See src/lightning.cpp.

// rgb: the bolt's colour at full brightness, already scaled by the frame's exposure.
float4 BoltColour : register(c0);

// x: across the ribbon, -1 to 1; y: how bright this piece is; z: how sharply it falls off across.
float4 BoltPS(float4 shape : TEXCOORD0) : COLOR {
    const float across = saturate(1.0f - shape.x * shape.x);
    return float4(BoltColour.rgb * shape.y * pow(across, shape.z), 0.0f);
}
