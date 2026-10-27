// Holding COM pointers.
#pragma once

namespace InventoryOverhaul {

// Releases the pointer and forgets it.
template <class T>
void Release(T*& object) {
    if (object != nullptr) {
        object->Release();
        object = nullptr;
    }
}

}
