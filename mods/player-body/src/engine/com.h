// COM pointers, released and forgotten.
#pragma once

#include <d3d9.h>

namespace PlayerBody {

template <class T>
void Release(T*& object) {
    if (object != nullptr) {
        object->Release();
        object = nullptr;
    }
}

// A surface the device handed over, released at the end of the scope.
struct Surface {
    IDirect3DSurface9* surface = nullptr;

    ~Surface() {
        Release(surface);
    }
};

}
