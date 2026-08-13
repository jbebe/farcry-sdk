#pragma once

#include <cstddef>

namespace FCSE {

// Locates an RCDATA resource in FCSE.exe's own image - the settings-page layouts, the Lua runtime
// and the address table are all embedded that way, so the loader installs as a single file.
//
// Nothing needs freeing: the bytes point into the mapped image and stay valid for the process.
bool FindRcData(const wchar_t* name, const void** data, size_t* size);

}
