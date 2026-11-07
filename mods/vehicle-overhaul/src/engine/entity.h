// Entities, reached from one of their components.
#pragma once

#include <cstdint>

namespace VehicleOverhaul::Entity {

// False, and logged, when this build lacks the calls that find an entity's skeleton.
bool Install();

// The entity `component` belongs to, or null once it is gone. Null in, null out.
void* Of(void* component);

// Where the bone of the entity's skeleton named by the CStringID `bone` is in the world, and which
// way its Y axis points. False when the entity has no skeleton or no such bone.
bool Bone(void* entity, uint32_t bone, float* position, float* forward);

}
