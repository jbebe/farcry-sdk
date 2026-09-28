// Grass and tree leaves lit by the sun through vertex shaders of ours, in place of the engine's.
// The engine's pixel shaders still shadow, texture and fog them.
#pragma once

namespace SkyOverhaul::Foliage {

void SetGrass(bool enabled);
void SetLeaves(bool enabled);

void ReleaseDeviceObjects();

}
