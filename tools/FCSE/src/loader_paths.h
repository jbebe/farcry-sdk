#pragma once

#include <string>
#include <windows.h>

// Where FCSE.exe was launched from. Everything the loader reads or writes - fcse.log, fcse.ini,
// plugins\, Dunia.dll itself - is named relative to that one directory rather than to the working
// directory, which the game is free to change.
namespace FCSE {

class LoaderPaths {
public:
    // Resolves the directory from the loader's own module handle. Call first, before anything that
    // needs a path; a path that cannot be resolved leaves Directory() empty.
    static void Init(HMODULE loaderModule);

    // With a trailing backslash, or empty if Init failed or never ran.
    static const std::wstring& Directory();

    // A file or folder inside it.
    static std::wstring In(const wchar_t* name);
};

}
