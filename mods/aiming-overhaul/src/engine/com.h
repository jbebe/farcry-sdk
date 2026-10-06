// Releasing a COM pointer and forgetting it, which is what every teardown here does.
#pragma once

namespace AimingOverhaul {

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
