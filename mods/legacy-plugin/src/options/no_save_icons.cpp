// No save icons on map: save points lose their save icon on the map and the GPS.
//
// From Realism Plus, legacy/realism-plus/features/nav-no-save-icons.md. The patch blanks the
// SaveDisk and SaveDiskGPS archetype names, which the engine initialises at start-up, so a change
// may only apply on the next launch.
//
// Not implemented yet.
#include "fcse_api.h"

void InstallNoSaveIcons() {
    FCSE::Logf("no save icons on map: not implemented yet");
}

void __cdecl OnNoSaveIconsChanged(const FCSE_SettingValue* /*value*/, void* /*userdata*/) {}
