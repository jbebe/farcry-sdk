#pragma once

#include <string>

namespace FCSE {

// Wide to narrow in the active code page, for the paths and module names that end up in the log.
// An unconvertible string comes back empty rather than partially converted.
std::string Narrow(const std::wstring& wide);

// Wide to UTF-8, for text the engine produced. Same failure behaviour as Narrow.
std::string Utf8(const wchar_t* text, size_t length);

}
