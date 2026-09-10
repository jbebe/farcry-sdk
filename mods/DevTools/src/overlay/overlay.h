// The developer overlay on Home: the command catalog, and any window another plugin adds through
// include/devtools_api.h.
#pragma once

namespace DevTools::Overlay {

// Puts the overlay on the game's frames. Call once from FCSE_Load. Everything it needs beyond that
// - the window, the device - arrives with the first frame, so a failure here is only ever about the
// drawing itself, and is logged.
bool Install();

}
