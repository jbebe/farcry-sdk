// Releasing a COM pointer and forgetting it, which is what every teardown here does.
#pragma once

namespace SkyOverhaul {

template <class T>
void Release(T*& object) {
    if (object != nullptr) {
        object->Release();
        object = nullptr;
    }
}

}
