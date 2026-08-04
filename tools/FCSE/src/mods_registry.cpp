#include "mods_registry.h"

#include "caller_identity.h"
#include "log.h"

namespace FCSE {

namespace {
    std::vector<ModsRegistry::Page> g_pages;

    // Backing storage for FCSE's own built-in dummy setting - deliberately does nothing, it exists
    // purely so the "Mods" tab always has at least one row and the whole pipeline (registration ->
    // row rendering -> click -> toggle -> onChanged) can be smoke-tested with zero plugins
    // installed.
    bool g_builtInDummyValue = false;
}

bool ModsRegistry::RegisterConfigPage(const char* pluginName, const FCSE_ConfigBool* fields,
                                       size_t fieldCount) {
    if (pluginName == nullptr || fields == nullptr || fieldCount == 0) {
        Log::FromCaller(_ReturnAddress(),
                         "RegisterConfigPage() called with a null/empty argument, rejected");
        return false;
    }

    Page page;
    page.pluginName = pluginName;
    page.fields.assign(fields, fields + fieldCount);
    g_pages.push_back(std::move(page));

    Log::FromCaller(_ReturnAddress(), "RegisterConfigPage(\"" + std::string(pluginName) +
                                           "\") registered " + std::to_string(fieldCount) +
                                           " bool(s)");
    return true;
}

const std::vector<ModsRegistry::Page>& ModsRegistry::Pages() { return g_pages; }

void ModsRegistry::RegisterBuiltIn() {
    static FCSE_ConfigBool field{};
    field.label = "Dummy setting";
    field.value = &g_builtInDummyValue;
    field.onChanged = nullptr;
    field.userdata = nullptr;

    RegisterConfigPage("FCSE", &field, 1);
}

} // namespace FCSE
