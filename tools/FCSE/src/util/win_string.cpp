#include "util/win_string.h"

#include <windows.h>

namespace FCSE {

namespace {
    std::string Convert(UINT codePage, const wchar_t* text, size_t length) {
        if (length == 0) {
            return "";
        }
        int len = WideCharToMultiByte(codePage, 0, text, static_cast<int>(length), nullptr, 0,
                                      nullptr, nullptr);
        if (len <= 0) {
            return "";
        }
        std::string result(len, '\0');
        WideCharToMultiByte(codePage, 0, text, static_cast<int>(length), result.data(), len,
                            nullptr, nullptr);
        return result;
    }
}

std::string Narrow(const std::wstring& wide) {
    return Convert(CP_ACP, wide.c_str(), wide.size());
}

std::string Utf8(const wchar_t* text, size_t length) {
    return Convert(CP_UTF8, text, length);
}

}
