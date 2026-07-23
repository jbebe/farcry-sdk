#include "loose_index.h"
#include "log.h"

#include <windows.h>

#include <array>
#include <cctype>
#include <cstring>
#include <unordered_map>

namespace LooseMods {

namespace {

// hash -> absolute loose-file path. Built once at load time (single-threaded under the loader
// lock), read-only thereafter, so lookups from arbitrary engine threads need no locking.
std::unordered_map<uint32_t, std::string> g_index;

const std::array<uint32_t, 256>& Crc32Table() {
    static const std::array<uint32_t, 256> table = [] {
        std::array<uint32_t, 256> t{};
        for (uint32_t i = 0; i < 256; ++i) {
            uint32_t c = i;
            for (int k = 0; k < 8; ++k) {
                c = (c & 1) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
            }
            t[i] = c;
        }
        return t;
    }();
    return table;
}

std::string WideToNarrow(const std::wstring& wide) {
    if (wide.empty()) {
        return std::string();
    }
    int len = WideCharToMultiByte(CP_ACP, 0, wide.c_str(), -1, nullptr, 0, nullptr, nullptr);
    if (len <= 0) {
        return std::string();
    }
    std::string narrow(static_cast<size_t>(len) - 1, '\0');
    WideCharToMultiByte(CP_ACP, 0, wide.c_str(), -1, narrow.data(), len, nullptr, nullptr);
    return narrow;
}

// "<installRoot>Data_Win32\Loose\" - installRoot is the parent of bin\ (where this DLL lives),
// matching where a modder already sees patch.dat/worlds\ after unpacking with Gibbed's tools.
// Same "go up one directory, no hardcoded 'bin' literal" logic hook_vfs.cpp uses for the string
// path, kept independent here to avoid disturbing that proven code.
std::wstring ComputeLooseRootWide() {
    const std::wstring& moduleDir = Log::ModuleDirectory(); // "...\bin\" (trailing slash included)
    if (moduleDir.empty()) {
        return std::wstring();
    }
    std::wstring dir = moduleDir;
    if (!dir.empty() && dir.back() == L'\\') {
        dir.pop_back();
    }
    size_t slash = dir.find_last_of(L'\\');
    if (slash == std::wstring::npos) {
        return std::wstring();
    }
    return dir.substr(0, slash + 1) + L"Data_Win32\\Loose\\";
}

void WalkDir(const std::wstring& dir, size_t rootPrefixLen) {
    WIN32_FIND_DATAW fd;
    HANDLE find = FindFirstFileW((dir + L"*").c_str(), &fd);
    if (find == INVALID_HANDLE_VALUE) {
        return;
    }
    do {
        const wchar_t* name = fd.cFileName;
        if (name[0] == L'.' && (name[1] == L'\0' || (name[1] == L'.' && name[2] == L'\0'))) {
            continue; // "." / ".."
        }
        std::wstring full = dir + name;
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
            WalkDir(full + L"\\", rootPrefixLen);
        } else {
            std::wstring relWide = full.substr(rootPrefixLen);
            std::string relNarrow = WideToNarrow(relWide);
            uint32_t hash = Crc32Path(relNarrow.c_str());
            g_index[hash] = WideToNarrow(full);

            wchar_t hex[16];
            swprintf_s(hex, L"0x%08X", hash);
            Log::Info(std::wstring(L"[HASH] indexed ") + hex + L"  " + relWide);
        }
    } while (FindNextFileW(find, &fd));
    FindClose(find);
}

} // namespace

std::string NormalizePath(const char* path) {
    std::string out;
    if (!path) {
        return out;
    }
    out.reserve(std::strlen(path));
    bool prevSep = false;
    for (const char* p = path; *p; ++p) {
        char c = *p;
        if (c == '/' || c == '\\') {
            if (prevSep) {
                continue; // collapse consecutive separators
            }
            prevSep = true;
            out.push_back('\\');
        } else {
            prevSep = false;
            out.push_back(static_cast<char>(std::tolower(static_cast<unsigned char>(c))));
        }
    }
    // Strip a single leading separator (parity with FUN_102356d0's leading-'\' handling; loose
    // relative paths never actually start with one, but keep the behavior identical).
    if (!out.empty() && out.front() == '\\') {
        out.erase(out.begin());
    }
    return out;
}

uint32_t Crc32Path(const char* path) {
    const std::array<uint32_t, 256>& table = Crc32Table();
    std::string s = NormalizePath(path);
    uint32_t crc = 0xFFFFFFFFu;
    for (unsigned char ch : s) {
        crc = (crc >> 8) ^ table[(crc ^ ch) & 0xFF];
    }
    return ~crc;
}

void BuildLooseIndex() {
    std::wstring root = ComputeLooseRootWide();
    if (root.empty()) {
        Log::Warn(L"[HASH] could not resolve the Loose root directory - hash index disabled.");
        return;
    }

    DWORD attrs = GetFileAttributesW(root.c_str());
    if (attrs == INVALID_FILE_ATTRIBUTES || !(attrs & FILE_ATTRIBUTE_DIRECTORY)) {
        Log::Info(L"[HASH] no Loose folder at " + root + L" - hash index empty (nothing to do).");
        return;
    }

    WalkDir(root, root.size());
    Log::Info(L"[HASH] index built: " + std::to_wstring(g_index.size()) + L" file(s) under " +
               root);
}

const std::string* LookupLooseByHash(uint32_t hash) {
    auto it = g_index.find(hash);
    return it == g_index.end() ? nullptr : &it->second;
}

} // namespace LooseMods
