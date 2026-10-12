// One lamp's glow over the finished frame: the lit lens as a disc, foreshortened to an oval when seen
// off its axis, and the glare round it.

// x  the quad's half size, y  the lens's radius, z  the glare's radius, all in pixels
// w  the lens's brightness
float4 Shape : register(c0);
// rgb  the lamp's colour, a  the glare's brightness
float4 Colour : register(c1);
// xy  the lamp's axis across the screen, z  how far the lens is foreshortened along it
float4 Lens : register(c2);

float4 MainPS(float2 uv : TEXCOORD0) : COLOR0
{
    float2 p = (uv * 2.0f - 1.0f) * Shape.x;
    float along = dot(p, Lens.xy) / Lens.z;
    float across = dot(p, float2(-Lens.y, Lens.x));
    float r = sqrt(along * along + across * across);

    // A disc with a pixel and a half of soft edge, so a lens a pixel across still shows whole.
    float lens = saturate(Shape.y + 0.75f - r) * Shape.w;

    float spread = r / max(Shape.z, 0.001f);
    float glare = 0.6f * exp(-2.0f * spread * spread) + 0.4f / (1.0f + 6.0f * spread * spread);
    glare *= Colour.a * saturate((Shape.x - length(p)) / (0.25f * Shape.x));

    return float4(Colour.rgb * (lens + glare), 1.0f);
}
