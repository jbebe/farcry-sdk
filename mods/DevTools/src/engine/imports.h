// A module's import table, for redirecting one function it calls in another DLL.
#pragma once

namespace DevTools::Imports {

// Redirects one import of `module`, leaving every other caller in the process alone. `function` is
// a name, or an ordinal passed through MAKEINTRESOURCEA. `original` receives what the slot held.
bool Hook(void* module, const char* fromModule, const char* function, void* detour, void** original);

}
