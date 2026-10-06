// Holding COM pointers: releasing one and forgetting it, and using one without keeping it.
#pragma once

namespace AimingOverhaul {

// Releases the pointer and forgets it.
template <class T>
void Release(T*& object) {
    if (object != nullptr) {
        object->Release();
        object = nullptr;
    }
}

// A COM pointer without the reference its getter added, for comparing or using in passing.
template <class T>
T* Borrowed(T* object) {
    if (object != nullptr) {
        object->Release();
    }
    return object;
}

}
