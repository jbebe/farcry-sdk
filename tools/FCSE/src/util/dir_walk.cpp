#include "util/dir_walk.h"

#include <vector>
#include <windows.h>

namespace FCSE {

void WalkDirectory(
    const std::wstring& directory,
    const std::function<void(const std::wstring& fullPath, const std::wstring& name)>& onFile,
    const std::function<DirAction(const std::wstring& fullPath, const std::wstring& name)>&
        onDirectory) {
    WIN32_FIND_DATAW entry;
    HANDLE search = FindFirstFileW((directory + L"*").c_str(), &entry);
    if (search == INVALID_HANDLE_VALUE) {
        return;
    }

    std::vector<std::wstring> subdirectories;
    do {
        std::wstring name = entry.cFileName;
        if (name == L"." || name == L"..") {
            continue;
        }
        if (entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
            std::wstring folder = directory + name + L"\\";
            if (!onDirectory || onDirectory(folder, name) == DirAction::Recurse) {
                subdirectories.push_back(folder);
            }
            continue;
        }
        onFile(directory + name, name);
    } while (FindNextFileW(search, &entry));

    FindClose(search);

    for (const std::wstring& subdirectory : subdirectories) {
        WalkDirectory(subdirectory, onFile, onDirectory);
    }
}

bool HasExtensionI(const std::wstring& name, const wchar_t* dotExtension) {
    const size_t length = wcslen(dotExtension);
    return name.size() > length &&
           _wcsicmp(name.c_str() + name.size() - length, dotExtension) == 0;
}

}
