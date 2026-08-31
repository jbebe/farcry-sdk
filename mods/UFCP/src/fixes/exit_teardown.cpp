// Quitting the game from the menu crashes to desktop.
//
// The symptom: choosing Exit Game faults instead of closing cleanly. Harmless in the sense that the
// process was leaving anyway, but it is the reason "Far Cry 2 crashes on exit" is folklore, and it
// buries any real crash in a crash log under a guaranteed false positive.
//
// Not FCSE's own doing, which was worth ruling out: FCSE loads fcse.mgb into the same magma engine
// and never unloads it, so the stale entry could have been its. Settled by disabling that load and
// quitting - the guard still fires with the package never loaded, so the object is the game's.
//
// A magma registry teardown (Steam 0x10AD40D0, Retail 0x10AC3FD0) walks a vector of (owner, object)
// pairs and hands each to 0x10AD3FB0 (Retail 0x10AC30C0), which walks the object and then destroys
// it. Some of those objects are already gone, destroyed through another path, so it works on freed
// memory. What it faults on depends only on how the dead allocation has decayed, which is why this
// tests the object rather than any one field. Three modes seen on one machine:
//
//     10ad3feb  8b 45 28   mov eax, [ebp+28h]   ; page unmapped
//     10ad4095  8b 50 0c   mov edx, [eax+0Ch]   ; page reused, vtable zeroed, eax is 0
//     10ad409c  ff d2      call edx             ; vtable readable, destructor slot is 0
//
// The engine's own null check at 0x10AD4090 waves all three through: the pointer is non-null and the
// object behind it is not there.
//
// The teardown is not exit-only. 0x10AD40D0 is also reached from magma's list-widget
// remove-all-items path, so this runs on every UI list repopulation - menu-event rate, not frame
// rate, which is what makes a VirtualQuery per object affordable.
//
// Probing rather than catching is the point: catching the fault shuts the game down cleanly too,
// but the access violation still happens, and a first-chance exception is what a crash handler
// reports. Probing raises nothing, so a guarded exit is silent. The __except is a backstop for a
// decay mode not listed above - most likely a dead child, since the engine dereferences each of
// those too and IsLive does not walk them.
#include "fcse_api.h"

#include <cstdint>
#include <cstdio>

#include <windows.h>

namespace {
    // __thiscall: the registry in ECX, the pair's owner and object on the stack, callee-cleaned.
    using TeardownFn = void(__fastcall*)(void* self, void* unused, void* owner, void* object);

    FCSE::Relocation<TeardownFn> g_teardown{FCSE::Uplay(0x00AD3FB0)};

    TeardownFn g_original = nullptr;
    bool g_reported = false;

    // Why an object was rejected, so the log can name it. The class identity of whatever is being
    // destroyed twice is the one thing a real fix would need, and this is where it surfaces.
    enum class Decay { Live, Unmapped, NoVtable, NoDestructor };

    bool IsReadable(const void* address, SIZE_T size) {
        MEMORY_BASIC_INFORMATION region;
        if (VirtualQuery(address, &region, sizeof(region)) != sizeof(region)) {
            return false;
        }
        if (region.State != MEM_COMMIT || (region.Protect & (PAGE_NOACCESS | PAGE_GUARD)) != 0) {
            return false;
        }

        const auto start = static_cast<const uint8_t*>(address);
        const auto regionEnd = static_cast<const uint8_t*>(region.BaseAddress) + region.RegionSize;
        return start + size <= regionEnd;
    }

    // Never cache the region across calls. This fix exists because pages are decommitted and reused
    // between them, so a remembered answer is exactly the stale one that must not be trusted.
    //
    // The teardown reads the object's vector at +0x28, then calls the destructor in vtable slot
    // +0x0C. Both have to be there for any of it to mean anything.
    Decay Inspect(const void* object, const void* const** vtableOut) {
        if (!IsReadable(object, 0x30)) {
            return Decay::Unmapped;
        }

        const void* const* vtable = *static_cast<const void* const* const*>(object);
        if (vtable == nullptr || !IsReadable(vtable, 0x10)) {
            return Decay::NoVtable;
        }

        *vtableOut = vtable;
        return vtable[3] == nullptr ? Decay::NoDestructor : Decay::Live;
    }

    void ReportOnce(const FCSE_PluginAPI* api, Decay decay, const void* const* vtable) {
        if (g_reported) {
            return;
        }
        g_reported = true;

        char line[160];
        if (decay == Decay::NoDestructor) {
            std::snprintf(line, sizeof(line),
                          "exit teardown: skipped a destroyed object, vtable Dunia+0x%tX",
                          reinterpret_cast<uintptr_t>(vtable) - api->duniaBase);
        } else {
            std::snprintf(line, sizeof(line), "exit teardown: skipped a destroyed object (%s)",
                          decay == Decay::Unmapped ? "unmapped" : "no vtable");
        }
        api->Log(line);
    }

    void __fastcall Teardown(void* self, void* unused, void* owner, void* object) {
        const void* const* vtable = nullptr;
        const Decay decay = Inspect(object, &vtable);
        if (decay != Decay::Live) {
            ReportOnce(FCSE::ApiPointer(), decay, vtable);
            return;
        }

        // Only an access violation means a dead object; anything else is someone else's and is
        // allowed past, so the message below stays true when it does fire.
        __try {
            g_original(self, unused, owner, object);
        } __except (GetExceptionCode() == EXCEPTION_ACCESS_VIOLATION ? EXCEPTION_EXECUTE_HANDLER
                                                                     : EXCEPTION_CONTINUE_SEARCH) {
            FCSE::ApiPointer()->Log(
                "exit teardown: a dead object got past the liveness check - the crash logged above "
                "is this fix's own, and the check needs widening");
        }
    }
}

void ApplyExitTeardownFix() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_teardown) {
        api->Log("exit teardown: the registry teardown was not found in this build - not fixed");
        return;
    }

    if (api->Hook(reinterpret_cast<void*>(g_teardown.get()), reinterpret_cast<void*>(&Teardown),
                  reinterpret_cast<void**>(&g_original))) {
        api->Log("exit teardown: guarded the registry teardown against already-destroyed objects");
    }
}
