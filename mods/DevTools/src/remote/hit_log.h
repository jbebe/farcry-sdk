// Every bullet, blast and blade that hits the player, and every one the player lands on a soldier,
// sent on the `combat` channel with the health it took; and a soldier's death by anything else.
#pragma once

namespace DevTools::HitLog {

void Install();

}
