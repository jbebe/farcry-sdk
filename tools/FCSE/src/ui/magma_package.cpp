#include "ui/magma_package.h"

#include "engine/dunia_api.h"
#include "api/hook.h"
#include "log.h"
#include "ui/page_assets.h"

#include <cstdint>
#include <windows.h>

namespace FCSE {

namespace {
    // Dunia.dll (Steam v1.03) RVAs against the preferred image base, same convention as
    // mods_tab.cpp. Every one of these was read off the MSVC build directly - see
    // PLAN-own-page.md work item 3 (git history, removed in cf13c2b), which records how each was
    // confirmed.
    constexpr uintptr_t kDuniaPreferredBase = 0x10000000;

    // magma::CFileNameNomad - 0x24 bytes: vtable, CPathID at +0x04, the path string at +0x08
    // (its inline character buffer at +0x0c, size +0x1c, capacity +0x20).
    constexpr uintptr_t kFileNameCtorRva = 0x105E8800;
    constexpr uintptr_t kFileNameSetIdentifierRva = 0x105E8CE0; // SetIdentifier(char const*)
    constexpr size_t kFileNameSize = 0x24;

    // The path string object begins at +0x08, and its first member is the *allocator* - the ELF
    // type is std::basic_string<char, char_traits<char>, magma::Allocator<char,0>>, whose allocator
    // is stateful, so it is not empty-base-optimised away. The character data follows at +0x0c: the
    // inline buffer while capacity < 0x10, otherwise a pointer. Same shape as CUIPageBase's page
    // name (allocator +0x28, data +0x2c). CFileNameNomad::GetFileType calls
    // magma::StringUtil::FindExtension on +0x08, i.e. on the string object, not on the characters.
    constexpr ptrdiff_t kPathAllocatorOffset = 0x08;
    constexpr ptrdiff_t kPathBufferOffset = 0x0c;
    constexpr ptrdiff_t kPathSizeOffset = 0x1c;
    constexpr ptrdiff_t kPathCapacityOffset = 0x20;

    // NOT destroying the CFileNameNomad. 0x105E93F0 was previously used for this and is NOT the
    // destructor - both of the call sites it was inferred from pass a *different* object than the
    // FileName they had just constructed, and calling it access-violated every run. The real
    // destructor has not been identified; the vtable's slot 0 is a candidate but MSVC's slot 0 is
    // the scalar *deleting* destructor, which is the wrong thing for stack storage.
    //
    // The cost of skipping it is one leaked path string per session, and only when the path is long
    // enough to have spilled out of the string's inline buffer. That is strictly better than
    // calling an unidentified function on a live engine object.

    // The magma engine singleton *pointer*. Confirmed by EngineRoot::FindPackage being called as
    // FindPackage(DAT_10fe3178 + 0xd4, &fileName), +0xd4 being the package-list root.
    constexpr uintptr_t kEngineSingletonRva = 0x10FE3178;

    // magma::Engine::LoadPackage(FileName const*, FileName const*, LoadErrorId&), dispatched at
    // vtable +0xc. NOTE this is slot 3 on MSVC where the ELF build uses slot 5 (+0x14): gcc and
    // MSVC order the vtable differently, so the slot index does not port between the two builds.
    constexpr size_t kLoadPackageVtableSlot = 3;

    // magma::CFileReaderNomad - the reason a loose file cannot simply be loaded by path.
    //
    // Open() does not open anything: it takes the FileName's CPathID (a bare 4-byte hash at
    // FileName+0x04), asks CResourceManager for a resource under that id, and on success points
    // the reader at that resource's already-loaded bytes. The manager is hash-keyed and
    // archive-backed, so a file that is not in a mounted archive can never resolve - by absolute
    // path or relative, since no path string survives into the lookup at all.
    //
    // So FCSE supplies the bytes itself. Read() is left completely alone: it memcpy's from the
    // cursor at +0x0c and advances it, which is exactly what it would do for a real resource.
    //
    //   +0x00 vtable   +0x04 resource   +0x08 flag   +0x0c cursor   +0x10 bytes consumed
    constexpr uintptr_t kReaderOpenRva = 0x1061D480;    // vtable +0x04
    constexpr uintptr_t kReaderGetFileSizeRva = 0x1061D3F0; // vtable +0x10, reads *(res + 0x4c)
    constexpr ptrdiff_t kReaderCursorOffset = 0x0c;
    constexpr ptrdiff_t kReaderConsumedOffset = 0x10;
    constexpr ptrdiff_t kFileNamePathIdOffset = 0x04;

    // The identity the *engine* knows this package by - deliberately not the real file path.
    //
    // Every shipped package is named like "UI\common.mgb", and a material's texture path is stored
    // UI-root-relative with a leading backslash ("\textures\hud\notebook.png"). Resolving that
    // against an absolute Windows path finds nothing, which is why materials declared here rendered
    // as untextured white quads. Naming the package the way the engine names its own puts texture
    // resolution back on the same footing.
    //
    // Nothing has to exist at this path: the reader hook below matches on the CPathID computed from
    // this string and serves FCSE's own bytes, so the identifier is pure identity.
    constexpr char kVirtualIdentifier[] = "UI\\fcse.mgb";

    // Deliberately NOT taking magma::CEngineNomad::Lock/Unlock (0x1000e160 / 0x1000e170). Both are
    // two-instruction Enter/LeaveCriticalSection wrappers that dereference *this as the critical
    // section - but the engine object holds its vtable at +0, so passing the engine would enter a
    // "critical section" that is really a vtable pointer. They are xport tier-medium ports at 0.729
    // confidence, and a symbol at 0.81 in this same family already turned out to be misattributed.
    // The lock guards concurrent magma access; this runs on the UI thread from the Options screen,
    // where nothing else is touching the engine, so skipping it is the safer of the two risks.

    using FileNameCtorFn = void*(__thiscall*)(void* self);
    using FileNameSetIdentifierFn = void(__thiscall*)(void* self, const char* path);
    // magma::Engine::LoadPackage(FileName const*, FileName const*, LoadErrorId&) - THREE arguments,
    // and the first two are the same FileName. Both of the engine's own convenience forwarders
    // (0x10ac3e20, 0x10ac3e70) call this slot as `(param_2, param_2, param_3)`, duplicating it.
    //
    // Getting this wrong is not a soft failure: passing two arguments made the callee read the
    // error-code pointer as the second FileName and dereference it, and because __thiscall is
    // callee-cleanup it then popped 12 bytes where 8 were pushed - shifting ESP by 4, so the *next*
    // call (the destructor) faulted too. Two access violations, one cause.
    using LoadPackageFn = void*(__thiscall*)(void* engine, void* fileName, void* fileNameAgain,
                                             int* loadError);

    bool g_attempted = false;
    void* g_package = nullptr;

    // --- the reader detours -----------------------------------------------------------------
    //
    // Both are *armed* only for the duration of FCSE's own LoadPackage call. There is no unhook
    // API, so the detours stay installed for the process lifetime; disarmed they forward
    // immediately, and a reader instance reused later for a genuine resource can never be answered
    // with our bytes.
    using ReaderOpenFn = char(__thiscall*)(void* self, void* fileName);
    using ReaderGetFileSizeFn = uint32_t(__thiscall*)(void* self);

    ReaderOpenFn g_originalReaderOpen = nullptr;
    ReaderGetFileSizeFn g_originalReaderGetFileSize = nullptr;
    bool g_readerHooksInstalled = false;

    bool g_armed = false;
    uint32_t g_armedPathId = 0;
    void* g_servedReader = nullptr; // the one instance we answered Open for

    // The package, embedded in FCSE.exe and pointing into its own image - see page_assets.h. Valid
    // for the process lifetime with nothing to free, which is what the reader below needs: the
    // visitor copies what it wants during the load, but the package keeps no reference we can prove
    // is absent.
    //
    // These bytes are read-only (an image mapping, where this used to be a heap buffer). Safe,
    // because they are only ever a memcpy *source*: CFileReaderNomad::Read copies out of the cursor
    // and advances the reader's own fields, so BinaryLoadVisitor parses its own copies and is never
    // handed a pointer into ours to fix up in place.
    PackageBytes g_bytes;

    // MSVC will not let a free function be __thiscall, so the detours are real member functions on
    // a throwaway type - `this` is the engine's reader, never an instance of these. Same trick
    // mods_tab.cpp uses for its own __thiscall detour.
    struct ReaderOpenThunk {
        char Detour(void* fileName);
    };

    char ReaderOpenThunk::Detour(void* fileName) {
        void* self = reinterpret_cast<void*>(this);
        if (g_armed && fileName != nullptr) {
            uint32_t pathId = 0;
            bool matched = false;
            __try {
                pathId = *reinterpret_cast<uint32_t*>(reinterpret_cast<char*>(fileName) +
                                                      kFileNamePathIdOffset);
                if (pathId == g_armedPathId) {
                    *reinterpret_cast<const unsigned char**>(reinterpret_cast<char*>(self) +
                                                             kReaderCursorOffset) = g_bytes.data;
                    *reinterpret_cast<uint32_t*>(reinterpret_cast<char*>(self) +
                                                 kReaderConsumedOffset) = 0;
                    matched = true;
                }
            } __except (EXCEPTION_EXECUTE_HANDLER) {
                matched = false;
            }
            if (matched) {
                g_servedReader = self;
                return 1;
            }
        }
        return g_originalReaderOpen(self, fileName);
    }

    struct ReaderGetFileSizeThunk {
        uint32_t Detour();
    };

    uint32_t ReaderGetFileSizeThunk::Detour() {
        void* self = reinterpret_cast<void*>(this);
        if (g_armed && self == g_servedReader) {
            return static_cast<uint32_t>(g_bytes.size);
        }
        return g_originalReaderGetFileSize(self);
    }

    template <typename MemberFn>
    void* RawFunctionPointer(MemberFn fn) {
        union {
            MemberFn member;
            void* raw;
        } converter;
        converter.member = fn;
        return converter.raw;
    }

    // Each function below wraps exactly one native touchpoint in SEH and holds no C++ object with a
    // destructor - MSVC disallows mixing __try/__except with automatic unwinding in one function,
    // the same constraint fcse_page.cpp works within.

    bool SafeReadPointer(void* address, void** outValue, DWORD* outCode) {
        __try {
            *outValue = *reinterpret_cast<void**>(address);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    bool SafeBuildFileName(FileNameCtorFn ctor, FileNameSetIdentifierFn setIdentifier, void* storage,
                           const char* path, DWORD* outCode) {
        __try {
            ctor(storage);
            setIdentifier(storage, path);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    bool SafeLoadPackage(void* engine, void* fileName, void** outPackage, int* outError,
                         DWORD* outCode) {
        __try {
            void** vtable = *reinterpret_cast<void***>(engine);
            auto load = reinterpret_cast<LoadPackageFn>(vtable[kLoadPackageVtableSlot]);
            *outPackage = load(engine, fileName, fileName, outError);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    // Reads back what SetIdentifier actually stored. Purely diagnostic: the engine reported
    // LoadErrorId 2, which BinaryLoadVisitor::Open returns when the file stream would not open -
    // before ReadHeader ever runs - and the two candidate causes are "the path is wrong" and "the
    // engine will not open an absolute path". This distinguishes them.
    bool SafeReadPathId(void* storage, uint32_t* outValue, DWORD* outCode) {
        __try {
            *outValue = *reinterpret_cast<uint32_t*>(reinterpret_cast<char*>(storage) +
                                                     kFileNamePathIdOffset);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    bool SafeDescribeFileName(void* storage, char* out, size_t outSize, DWORD* outCode) {
        __try {
            auto base = reinterpret_cast<char*>(storage);
            uint32_t size = *reinterpret_cast<uint32_t*>(base + kPathSizeOffset);
            uint32_t capacity = *reinterpret_cast<uint32_t*>(base + kPathCapacityOffset);
            // Short strings live inline at +0x0c; long ones put a pointer there instead.
            const char* text = capacity < 0x10 ? base + kPathBufferOffset
                                               : *reinterpret_cast<char**>(base + kPathBufferOffset);
            sprintf_s(out, outSize, "size=%u capacity=%u path=\"%.100s\"", size, capacity,
                      text != nullptr ? text : "(null)");
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    std::string Hex(DWORD value) {
        char buffer[16];
        sprintf_s(buffer, "0x%08lX", value);
        return buffer;
    }

    bool InstallReaderHooks(uintptr_t slide) {
        if (g_readerHooksInstalled) {
            return true;
        }
        void* openTarget = reinterpret_cast<void*>(kReaderOpenRva + slide);
        void* sizeTarget = reinterpret_cast<void*>(kReaderGetFileSizeRva + slide);
        if (!HookManager::Hook(openTarget, RawFunctionPointer(&ReaderOpenThunk::Detour),
                               reinterpret_cast<void**>(&g_originalReaderOpen))) {
            Log::Loader("Magma package: could not hook CFileReaderNomad::Open");
            return false;
        }
        if (!HookManager::Hook(sizeTarget, RawFunctionPointer(&ReaderGetFileSizeThunk::Detour),
                               reinterpret_cast<void**>(&g_originalReaderGetFileSize))) {
            // Open is now detoured with no matching size hook. Harmless while disarmed, and we
            // never arm, so the engine keeps its own behaviour throughout.
            Log::Loader("Magma package: could not hook CFileReaderNomad::GetFileSize");
            return false;
        }
        g_readerHooksInstalled = true;
        return true;
    }
}

bool MagmaPackage::Load() {
    if (g_attempted) {
        return g_package != nullptr;
    }
    g_attempted = true;

    g_bytes = PageAssets::Locate();
    if (!g_bytes) {
        return false; // PageAssets already logged the specific reason
    }

    uintptr_t base = DuniaApi::Base();
    if (base == 0) {
        Log::Loader("Magma package: Dunia.dll not resolved, cannot load fcse.mgb");
        return false;
    }
    const uintptr_t slide = base - kDuniaPreferredBase;

    void* engine = nullptr;
    DWORD code = 0;
    if (!SafeReadPointer(reinterpret_cast<void*>(kEngineSingletonRva + slide), &engine, &code)) {
        Log::Loader("Magma package: faulted reading the engine singleton (" + Hex(code) + ")");
        return false;
    }
    if (engine == nullptr) {
        // Expected if this ever runs before the engine is up. The Options-screen hook fires well
        // after that, so treat it as a real failure worth seeing rather than a retry.
        Log::Loader("Magma package: the magma engine singleton is null - too early to load fcse.mgb");
        return false;
    }

    auto ctor = reinterpret_cast<FileNameCtorFn>(kFileNameCtorRva + slide);
    auto setIdentifier = reinterpret_cast<FileNameSetIdentifierFn>(kFileNameSetIdentifierRva + slide);

    // The engine reads the extension off this identifier to choose its parser: GetFileType
    // lowercases and compares, "mgb" -> 2 -> BinaryLoadVisitor. The real file is read separately,
    // below - this string only has to look like a shipped package's name.
    const char* path = kVirtualIdentifier;

    alignas(4) unsigned char fileName[kFileNameSize] = {};
    if (!SafeBuildFileName(ctor, setIdentifier, fileName, path, &code)) {
        Log::Loader("Magma package: faulted building the CFileNameNomad (" + Hex(code) + ")");
        return false;
    }

    char described[192] = {};
    DWORD describeCode = 0;
    if (SafeDescribeFileName(fileName, described, sizeof(described), &describeCode)) {
        Log::Loader(std::string("Magma package: FileName holds ") + described);
    } else {
        Log::Loader("Magma package: faulted reading the FileName back (" + Hex(describeCode) + ")");
    }

    // The CPathID SetIdentifier just computed is what the reader will be asked for, so read it
    // back rather than recomputing the hash ourselves - no risk of disagreeing with the engine.
    uint32_t pathId = 0;
    if (!SafeReadPathId(fileName, &pathId, &code)) {
        Log::Loader("Magma package: faulted reading the FileName's CPathID (" + Hex(code) + ")");
        return false;
    }

    if (!InstallReaderHooks(slide)) {
        return false;
    }

    void* package = nullptr;
    int loadError = 0;

    g_armedPathId = pathId;
    g_servedReader = nullptr;
    g_armed = true;
    bool called = SafeLoadPackage(engine, fileName, &package, &loadError, &code);
    g_armed = false;
    bool served = g_servedReader != nullptr;
    g_servedReader = nullptr;

    if (!called) {
        Log::Loader("Magma package: faulted inside Engine::LoadPackage (" + Hex(code) + ")");
        return false;
    }
    if (!served) {
        // The reader never asked for our path id, so the failure is upstream of the file bytes.
        Log::Loader("Magma package: the reader never requested our CPathID - the detour was not "
                    "reached, so the load failed before it got to opening a file");
    }
    if (package == nullptr) {
        // LoadErrorId 2 specifically means BinaryLoadVisitor::Open could not open the file stream -
        // it is returned before ReadHeader runs, so it says nothing about the package's contents.
        const char* meaning = loadError == 2 ? " - the engine could not open that path"
                            : loadError == 1 ? " - no load visitor for this file type"
                            : loadError == 4 ? " - bad \"MAGMA\" magic"
                            : loadError == 5 ? " - wrong format version"
                            : loadError == 6 ? " - bad endian sentinel"
                                             : "";
        Log::Loader("Magma package: the engine declined to load fcse.mgb (LoadErrorId " +
                    std::to_string(loadError) + meaning + ")");
        return false;
    }

    g_package = package;
    char message[160];
    sprintf_s(message,
              "Magma package: fcse.mgb loaded (magma::Package* = 0x%08zX) - \"FCSE_PAGE\" now resolves",
              reinterpret_cast<size_t>(package));
    Log::Loader(message);
    return true;
}

bool MagmaPackage::Loaded() {
    return g_package != nullptr;
}

} // namespace FCSE
