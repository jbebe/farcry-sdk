#pragma once

#include <string>

namespace FCSE {

// Wide to narrow in the active code page, for the paths and module names that end up in the log.
// An unconvertible string comes back empty rather than partially converted.
std::string Narrow(const std::wstring& wide);

}
