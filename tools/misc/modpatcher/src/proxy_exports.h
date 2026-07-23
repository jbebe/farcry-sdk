#pragma once

namespace LooseMods {

// Resolves all 6 real DINPUT8.dll exports from the actual system DLL (via GetSystemDirectoryW,
// WOW64-redirected to the correct 32-bit copy automatically) so the naked JMP thunks in
// proxy_exports.cpp have somewhere to jump. Must succeed before the game calls any DirectInput
// function - call from DLL_PROCESS_ATTACH.
bool LoadRealDinput8();

} // namespace LooseMods
