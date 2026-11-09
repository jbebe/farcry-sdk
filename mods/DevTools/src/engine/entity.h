// Entities, and holding onto one safely.
//
// The engine hands out entities through reference holders rather than pointers, because an entity
// can be destroyed by a level load while something still names it. Anything that keeps an entity
// across frames has to hold a reference, and give it back.
#pragma once

#include <cstddef>
#include <cstdint>
#include <optional>

namespace DevTools::Entity {

constexpr float kRadiansPerDegree = 0.0174532925f;

inline float Degrees(float radians) { return radians / kRadiansPerDegree; }

// CFCXCountersComponent's health CCounter, and in it the value and its maximum.
constexpr ptrdiff_t kCountersHealth = 0x44;
constexpr ptrdiff_t kCounterValue = 0x10;
constexpr ptrdiff_t kCounterMax = 0x14;

void Install();

// The entity with id `id`, as a holder the caller owns a reference on, or null. Pass it back to
// ReleaseRef.
void* AcquireRef(uint64_t id);

// The entity a camera manager is focused on, as AcquireRef gives it. Null when the manager has no focus.
void* AcquireFocusRef(void* manager);

// Drops one reference and clears `ref`, destroying the holder if it was the last.
void ReleaseRef(void*& ref);

// The entity a holder names, or null once it has gone away - which is how a level load is noticed.
void* Of(void* ref);

// Adds a reference for a callee that takes a holder by value and drops one of its own.
void AddRef(void* ref);

// The entity's world transform: 4x4 row-major, rows 0-2 the basis, row 3 the position.
const float* Matrix(void* entity);

// The yaw SetEuler takes for the way the entity faces now, in radians.
float Yaw(void* entity);

void SetPosition(void* entity, float x, float y, float z);
void SetEuler(void* entity, float pitch, float roll, float yaw);

// The name an entity has from its archetype, as `weapons.Primary.AK47`, or "" for no entity.
const char* Name(const void* entity);

// The entity's component of the class `classId`, or null.
void* Component(void* entity, const void* classId);

// The entity's component of the class named `className`, or of a class derived from it, or null.
void* Component(void* entity, const char* className);

struct Health {
    float value;
    float max;
};

// The health in the entity's CFCXCountersComponent, the player's and a soldier's alike, if it has one.
std::optional<Health> HealthOf(void* entity);

// The entity's physics component, registering its class tag first, as the engine does on every
// fetch. Null if the entity has none.
void* PhysicsComponent(void* entity);

// Collision and gravity. Off is what lets something fly through the world.
void SetPhysicsEnabled(void* physics, bool enabled);

}
