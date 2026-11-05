// Each bullet's impact on a surface, as the engine spawns its puff.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Impact {

// Where the bullet landed in the world, and the logic material it hit, as its `disId` in
// databases\materials\logicmaterials.xml.
using ImpactFn = void (*)(const float point[3], uint8_t material);

// False, and logged, when the impacts cannot be hooked.
bool Install(ImpactFn onImpact);

}
