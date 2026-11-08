#include "engine/bin_folder.h"

#include <algorithm>
#include <iterator>
#include <windows.h>
#include <psapi.h>

namespace DevTools::BinFolder {

std::filesystem::path File(const wchar_t* name) {
    std::wstring exe(32768, L'\0');
    exe.resize(GetModuleFileNameW(nullptr, exe.data(), static_cast<DWORD>(exe.size())));
    return std::filesystem::path(exe).parent_path() / name;
}

const std::string& Plugins() {
    static const std::string plugins = [] {
        std::string joined;
        HMODULE modules[1024];
        DWORD bytes = 0;
        if (!EnumProcessModules(GetCurrentProcess(), modules, sizeof(modules), &bytes)) {
            return joined;
        }

        const std::wstring folder = File(L"plugins\\").wstring();
        const size_t count = std::min<size_t>(bytes / sizeof(HMODULE), std::size(modules));
        for (size_t index = 0; index < count; ++index) {
            wchar_t path[MAX_PATH];
            const std::wstring_view file(path, GetModuleFileNameW(modules[index], path, MAX_PATH));
            if (file.size() > folder.size() &&
                _wcsnicmp(file.data(), folder.c_str(), folder.size()) == 0 &&
                GetProcAddress(modules[index], "FCSE_Load") != nullptr) {
                joined.append(joined.empty() ? "" : ", ")
                    .append(std::filesystem::path(file).filename().string());
            }
        }
        return joined;
    }();
    return plugins;
}

}
