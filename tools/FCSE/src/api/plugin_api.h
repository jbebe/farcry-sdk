#pragma once

#include "engine/build_id.h"
#include "fcse_api.h"

// Assembles the FCSE_PluginAPI that every plugin's FCSE_Load receives. include/fcse_api.h is the
// contract; this is the loader's side of it.
namespace FCSE {

class PluginApi {
public:
    // Built once and kept for the life of the process: plugins hold the pointer, and the provider
    // callback hands the same one back at FCSE_OnRegisterFunctions time. Call after the address
    // library and the settings registry are up, since the struct quotes both.
    static const FCSE_PluginAPI* Build(const BuildInfo& build);
};

}
