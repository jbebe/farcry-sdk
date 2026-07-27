#pragma once

#include <string>

namespace FCSE {

// Resolves which loaded module a given return address belongs to, and returns its module name
// with the directory and ".dll"/".exe" extension stripped (e.g. "example_plugin"). Used to tag
// log lines and to name the owner in Hook()/Patch()/AddFunctionCB() conflict reports - all
// without requiring plugins to pass their own identity into any API call, which would just be
// another thing a plugin author could get wrong.
//
// Falls back to a hex-formatted address string (e.g. "0xdeadbeef") if the address doesn't resolve
// to any loaded module, which should not happen in practice for addresses captured via
// _ReturnAddress() at a real API call site.
std::string ResolveCallerModuleName(void* returnAddress);

} // namespace FCSE
