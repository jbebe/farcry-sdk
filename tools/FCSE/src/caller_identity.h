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

// Makes ResolveCallerModuleName report `name` instead of resolving the address, for as long as this
// object is alive.
//
// Exists for Lua scripts. Every script reaches Hook()/Patch()/RegisterSettings through the same C
// shim compiled into FCSE.exe, so address-based resolution names all of them "fcse" - which would
// both mistag their log lines and, worse, defeat the per-owner conflict checks in HookManager and
// PatchManager: two scripts patching the same bytes would look like one owner patching twice, which
// is explicitly allowed. Naming the script restores the distinction the compiled-plugin case gets
// for free.
//
// Scoped rather than a setter so an early return or a longjmp out of Lua cannot leave the override
// stuck on. Nests correctly: the previous value is restored, not cleared.
class ScopedCallerIdentity {
public:
    explicit ScopedCallerIdentity(const std::string& name);
    ~ScopedCallerIdentity();

    ScopedCallerIdentity(const ScopedCallerIdentity&) = delete;
    ScopedCallerIdentity& operator=(const ScopedCallerIdentity&) = delete;

private:
    std::string previous_;
    bool hadPrevious_;
};

} // namespace FCSE
