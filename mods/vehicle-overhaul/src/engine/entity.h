// Entities, reached from one of their components.
#pragma once

#include <cstdint>

namespace VehicleOverhaul::Entity {

// False, and logged, when this build lacks the calls that move an entity or find its skeleton.
bool Install();

// The entity `component` belongs to, or null once it is gone. Null in, null out.
void* Of(void* component);

// The world transform: 4x4 row-major, rows 0-2 the basis (Y forward, Z up), row 3 the position.
const float* Matrix(void* entity);

// Moves and turns the entity, after its pending job. Angles are pitch, roll and yaw in radians.
void Place(void* entity, const float* position, const float* angles);

// Where the bone of the entity's skeleton named by the CStringID `bone` is in the world, and which
// way its Y axis points. False when the entity has no skeleton or no such bone.
bool Bone(void* entity, uint32_t bone, float* position, float* forward);

}
