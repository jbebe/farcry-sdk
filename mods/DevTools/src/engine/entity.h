// Entities, and holding onto one safely.
//
// The engine hands out entities through reference holders rather than pointers, because an entity
// can be destroyed by a level load while something still names it. Anything that keeps an entity
// across frames has to hold a reference, and give it back.
#pragma once

namespace DevTools::Entity {

void Install();

// The entity a camera manager is focused on, as a holder the caller owns a reference on. Null when
// the manager has no focus. Pass it back to ReleaseRef.
void* AcquireFocusRef(void* manager);

// Drops one reference and clears `ref`, destroying the holder if it was the last.
void ReleaseRef(void*& ref);

// The entity a holder names, or null once it has gone away - which is how a level load is noticed.
void* Of(void* ref);

// Adds a reference for a callee that takes a holder by value and drops one of its own.
void AddRef(void* ref);

// The entity's world transform: 4x4 row-major, rows 0-2 the basis, row 3 the position.
const float* Matrix(void* entity);

void SetPosition(void* entity, float x, float y, float z);
void SetEuler(void* entity, float pitch, float roll, float yaw);

// The entity's physics component, registering its class tag first, as the engine does on every
// fetch. Null if the entity has none.
void* PhysicsComponent(void* entity);

// Collision and gravity. Off is what lets something fly through the world.
void SetPhysicsEnabled(void* physics, bool enabled);

}
