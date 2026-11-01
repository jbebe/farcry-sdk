// The notebook's pages, the Magma page build-ui.ps1 adds to the HUD: Items, Docs, Missions and
// Buddies, each with a post-it for the row selected.
#pragma once

namespace InventoryOverhaul::Page {

// False when the HUD's Magma calls were not found in this build.
bool Install();

// Writes the open page again if what it shows has changed. Game thread, with the player's
// CInventoryViewPawn and inventory, while the notebook is open.
void Update(const void* view, void* inventory);

// Moves the selection `steps` rows down, or up when negative, the next time the page is written.
// Any thread.
void Scroll(int steps);

// Turns to the next page, after Buddies back to Items, the next time the page is written. Any
// thread.
void Turn();

// Uses the row selected the next time the page is written: reads the paper selected on Docs whole,
// or puts it back, and while one is read Scroll turns its sheets; plays or stops a tape; on Items,
// stows gun 1 or 2, or opens a stowed gun's menu, in which Scroll moves and Use runs the action. Any
// thread.
void Use();

// Puts back the paper being read and closes a stowed gun's menu; false when neither was open. Any
// thread.
bool Close();

// Whether a click heals instead of being used: Syrettes is selected on Items. Any thread.
bool Heals();

// Drops the row selected on Items the next time the page is written. Any thread.
void Drop();

// Shows the page while the notebook is open and hides it otherwise, a HUD loaded since included.
// Game thread, once a frame.
void Frame();

}
