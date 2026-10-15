// Three values in [0, 1) per lattice point. No sine, which loses precision at the large arguments a
// lattice across the screen gives it.
float3 Hash(float3 p)
{
    p = frac(p * float3(0.1031f, 0.1030f, 0.0973f));
    p += dot(p, p.yxz + 33.33f);
    return frac((p.xxy + p.yxx) * p.zyx);
}

// A different value in [0, 1) per pixel, spread so neighbours differ as much as they can.
float Dither(float2 screen)
{
    return frac(52.9829189f * frac(dot(screen, float2(0.06711056f, 0.00583715f))));
}
