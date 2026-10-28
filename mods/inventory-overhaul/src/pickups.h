// Mission pickups that are the notebook's items: one whose archetype names an item under the entity
// data key "InventoryOverhaul.Item" puts that item in Items when it is picked up.
#pragma once

namespace InventoryOverhaul::Pickups {

// Logs whether it hooked; when it did not, such pickups only vanish.
bool Install();

}
