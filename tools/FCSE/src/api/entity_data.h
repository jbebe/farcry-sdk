#pragma once

#include "fcse_api.h"

namespace FCSE {

// The table behind FCSE_PluginAPI::EntityData, over engine/entity_data_component.h.
class EntityDataApi {
public:
    static const FCSE_EntityDataAPI* Table();
};

}
