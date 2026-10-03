// The vertex shader ScreenDraw::FullQuad draws through: a quad already in clip space.

float4 MainVS(float4 position : POSITION0) : POSITION0 {
    return position;
}
