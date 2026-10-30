#include "engine/bin_folder.h"

#include <string>
#include <windows.h>

namespace DevTools::BinFolder {

std::filesystem::path File(const wchar_t* name) {
    std::wstring exe(32768, L'\0');
    exe.resize(GetModuleFileNameW(nullptr, exe.data(), static_cast<DWORD>(exe.size())));
    return std::filesystem::path(exe).parent_path() / name;
}

}
