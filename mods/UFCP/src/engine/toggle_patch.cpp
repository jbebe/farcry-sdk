// A byte patch an option can turn on and off.
//
// Ported from FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) - source/core/common.ixx `raw_mem`.
//
// UFCP's fixes patch unconditionally and never look back, so nothing here existed before options
// started patching bytes. FCSE's Patch() permits a plugin to overwrite a range it patched itself,
// which is what makes restoring the original bytes legal rather than a conflict.
#include "engine/toggle_patch.h"

#include "fcse_api.h"

#include <cstring>

namespace UFCP {

bool TogglePatch::Resolve(uintptr_t address) {
    if (address == 0) {
        return false;
    }

    std::memcpy(m_original, reinterpret_cast<const void*>(address), m_size);
    m_address = address;
    return true;
}

void TogglePatch::Set(bool on) {
    if (m_address == 0) {
        return;
    }

    FCSE::ApiPointer()->Patch(reinterpret_cast<void*>(m_address), on ? m_patched : m_original,
                              m_size);
}

}
