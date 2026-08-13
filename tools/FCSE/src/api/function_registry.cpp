#include "api/function_registry.h"

#include "caller_identity.h"
#include "engine/dunia_api.h"
#include "log.h"

#include <intrin.h>
#include <string>
#include <unordered_map>

namespace FCSE {

namespace {
    std::unordered_map<std::string, std::string> g_owners; // registered name -> owning module
}

bool FunctionRegistry::Register(void* fn, const char* name) {
    const std::string caller = ResolveCallerModuleName(_ReturnAddress());
    const std::string key = name != nullptr ? name : "";

    auto existing = g_owners.find(key);
    if (existing != g_owners.end()) {
        Log::Write(caller, "AddFunctionCB(\"" + key + "\") conflict: name already claimed by '" +
                               existing->second + "', rejected");
        return false;
    }

    DuniaApi::AddFunctionCB()(fn, name);
    g_owners[key] = caller;
    Log::Write(caller, "AddFunctionCB(\"" + key + "\") registered");
    return true;
}

} // namespace FCSE
