#include "loader_paths.h"

namespace FCSE {

namespace {
    std::wstring g_directory;
}

void LoaderPaths::Init(HMODULE loaderModule) {
    wchar_t path[MAX_PATH];
    DWORD length = GetModuleFileNameW(loaderModule, path, MAX_PATH);
    if (length == 0 || length == MAX_PATH) {
        return;
    }

    const std::wstring modulePath(path, length);
    const size_t slash = modulePath.find_last_of(L"\\/");
    g_directory = slash == std::wstring::npos ? L"" : modulePath.substr(0, slash + 1);
}

const std::wstring& LoaderPaths::Directory() { return g_directory; }

std::wstring LoaderPaths::In(const wchar_t* name) { return g_directory + name; }

}
