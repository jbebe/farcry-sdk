// Loose-file asset loader for Far Cry 2 - Project 1.
//
// Ships as dinput8.dll, dropped next to FarCry2.exe. Windows loads it because Dunia.dll
// statically imports DINPUT8.dll (confirmed via its import-name string table, see
// reverse/dunia/archive_loading.md and tools/modpatcher/README.md) - that means our DllMain
// runs before any of Dunia.dll's own code, including InitDuniaEngine's file-system bootstrap.
// Every real DINPUT8.dll export is forwarded (via naked JMP thunks, see proxy_exports.cpp) to the
// real system DLL, so input handling is completely unaffected.
//
// What this DLL does on its own: build a hash index of Data_Win32\Loose\, then install two
// complementary loose-file hooks - VFS_ResolvePath (string-path asset requests) and
// ArchiveEntry_FindAndOpen (the hash-based lookup that the world/level streamer uses, bypassing
// VFS_ResolvePath). See reverse/dunia/archive_loading.md.

#include "hook_findandopen.h"
#include "hook_vfs.h"
#include "log.h"
#include "loose_index.h"
#include "proxy_exports.h"

#include <windows.h>

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reasonForCall, LPVOID /*reserved*/) {
    switch (reasonForCall) {
    case DLL_PROCESS_ATTACH:
        // We don't care about per-thread notifications, and skipping them avoids extra work
        // under the loader lock.
        DisableThreadLibraryCalls(hModule);
        LooseMods::Log::Init(hModule);
        LooseMods::LoadRealDinput8();
        // Build the hash index before installing the hash hook, so it's ready the first time the
        // hook can fire. Safe to do here single-threaded: no engine code runs until after DllMain.
        LooseMods::BuildLooseIndex();
        LooseMods::InstallVfsHook();
        LooseMods::InstallFindAndOpenHook();
        break;

    case DLL_PROCESS_DETACH:
        LooseMods::RemoveFindAndOpenHook();
        LooseMods::RemoveVfsHook();
        LooseMods::Log::Shutdown();
        break;
    }

    return TRUE;
}
