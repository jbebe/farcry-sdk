// The sheet held level while the map is out: once the player's body is posed, the spine and every bone
// below it but the view's own are turned back about the eye by the view's pitch, so the arms and the sheet
// in the left hand keep their place against a level view while the view looks up and down.
// See docs/docs/design/map.md.
#pragma once

namespace MapOverhaul::Level {

bool Install();

}
