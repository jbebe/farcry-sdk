#include "util/win_string.h"

#include <windows.h>

namespace FCSE {

std::string Narrow(const std::wstring& wide) {
    if (wide.empty()) {
        return "";
    }
    int len = WideCharToMultiByte(CP_ACP, 0, wide.c_str(), static_cast<int>(wide.size()), nullptr, 0,
                                  nullptr, nullptr);
    if (len <= 0) {
        return "";
    }
    std::string result(len, '\0');
    WideCharToMultiByte(CP_ACP, 0, wide.c_str(), static_cast<int>(wide.size()), result.data(), len,
                        nullptr, nullptr);
    return result;
}

}
