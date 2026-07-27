#pragma once

#include <cstdint>
#include <string>
#include <windows.h>

// Resolves the 3 Dunia.dll exports FCSE.exe needs to reimplement FarCry2.exe's own WinMain, plus
// the module's base/size for plugins that want their own RVA-based tier-2/3 access. All 3 are
// plain exports (confirmed live via list_exports against the Steam v1.03 build in
// docs/docs/engine-internals/overview.md's Ghidra project) - RunGame is C++-mangled
// ("?RunGame@@YA_NPAUHINSTANCE__@@PBD@Z"), RegisterGameFunctionProvider and AddFunctionCB are
// plain undecorated C exports. Resolving by name means this loader has no hardcoded-RVA
// dependency on a specific Dunia.dll build the way tools/misc/modpatcher's VFS hook does.
namespace FCSE {

// bool __cdecl RunGame(HINSTANCE hInstance, const char* cmdLine) - the game's actual entry
// point/main loop, reached from FarCry2.exe's WinMain exactly like this in the stock exe. The
// mangled export name (PAUHINSTANCE__@@) demangles to a single pointer-to-HINSTANCE__ - i.e. one
// plain HINSTANCE handle, not a pointer-to-HINSTANCE - matching WinMain's own hInstance parameter
// exactly, no address-of needed at the call site.
using RunGameFn = bool(__cdecl*)(HINSTANCE, const char*);

// void __cdecl RegisterGameFunctionProvider(void* providerCallback) - stashes a no-argument
// callback pointer into Dunia.dll's own g_pGameFunctionProvider global; RunGame invokes it once,
// later, after InitDuniaEngine succeeds.
using RegisterGameFunctionProviderFn = void(__cdecl*)(void*);

// void __cdecl AddFunctionCB(void* fn, const char* name) - inserts (fn, CRC32(name)) into
// Dunia.dll's function registry. First registrant for a given name wins; a second registration of
// an already-present name is a silent no-op inside Dunia.dll itself (confirmed via decompile of
// FunctionRegistry_Insert, 0x10299430) - see function_registry.h for how FCSE turns that into a
// loud, logged rejection instead.
using AddFunctionCBFn = void(__cdecl*)(void*, const char*);

class DuniaApi {
public:
    // Loads Dunia.dll from `directory` (expected: the loader's own directory, i.e. bin\) and
    // resolves all 3 exports above. Returns false and logs the specific failure (file missing,
    // load failure, or a missing export - e.g. a build so old/new none of these 3 names exist) if
    // any step fails; no partial state is left behind either way.
    static bool Load(const std::wstring& directory);

    static HMODULE Module();
    static uintptr_t Base();
    static size_t Size();

    static RunGameFn RunGame();
    static RegisterGameFunctionProviderFn RegisterGameFunctionProvider();
    static AddFunctionCBFn AddFunctionCB();
};

} // namespace FCSE
