// Wildlife Overhaul - more varied and more dangerous wildlife for Far Cry 2. See README.md.
#include "knockdown.h"
#include "fcse_api.h"

#include <filesystem>
#include <fstream>
#include <sstream>
#include <windows.h>

extern "C" IMAGE_DOS_HEADER __ImageBase;

namespace {
    // knockout.lua, from the folder this plugin is in. Empty when it is missing.
    std::string ReadScript() {
        wchar_t module[MAX_PATH] = {};
        GetModuleFileNameW(reinterpret_cast<HMODULE>(&__ImageBase), module, MAX_PATH);
        std::ifstream file(std::filesystem::path(module).parent_path() / "knockout.lua");
        std::ostringstream text;
        text << file.rdbuf();
        return text.str();
    }
}

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION || !FCSE::Bind(api)) {
        return false;
    }
    std::string script = ReadScript();
    if (script.empty()) {
        api->Log("knockout.lua was not found beside the plugin");
        return false;
    }
    return WildlifeOverhaul::Knockdown::Install(std::move(script));
}
