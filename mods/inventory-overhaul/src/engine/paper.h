// The notebook's pages in the world. build-ui.ps1 draws the Magma page between two marker images;
// while the notebook is open, what is drawn between them goes into a texture instead of the screen,
// and that texture is bound wherever the map's "maptop" texture is, which the dossier's pages draw
// with. Magma draws after the world, so the pages show the frame before's page.
#pragma once

namespace InventoryOverhaul::Paper {

// Logs whether it hooked; when it did not, the page is drawn over the screen.
void Install();

// A HUD was loaded, so its marker images have textures of their own to be found again. Any thread.
void NewHud();

}
