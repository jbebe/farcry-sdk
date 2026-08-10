#include "engine/address_library.h"

#include <cstring>
#include <vector>

#include "log.h"

namespace FCSE {

namespace {

    // Must match tools/FCSE/tools/addrlib/emit.py. The header is fixed-size on
    // purpose: no length-prefixed strings to walk, so a truncated resource is
    // caught by one size check instead of failing somewhere in the middle.
    constexpr char kResourceName[] = "FCSE_ADDRLIB";
    constexpr uint32_t kMagic = 0x52444146u;   // 'FADR' little-endian
    constexpr uint16_t kFormatVersion = 1;
    constexpr uint16_t kCodecVarintDelta = 0;
    constexpr uint32_t kMissing = 0xFFFFFFFFu;

#pragma pack(push, 1)
    struct Header {
        uint32_t magic;
        uint16_t format;
        uint16_t codec;
        uint32_t entryCount;
        uint32_t payloadBytes;
        uint32_t missing;
        char mappingVersion[16];
        char buildId[2][24];
    };
#pragma pack(pop)
    static_assert(sizeof(Header) == 84, "address table header layout changed");

    struct State {
        bool ready = false;
        std::vector<uint32_t> rva;      // indexed by ID
        std::string mappingVersion;
        uintptr_t base = 0;
    };

    State& Get() {
        static State state;
        return state;
    }

    // Reads a NUL-padded fixed-size field without running off the end of it.
    std::string Field(const char* text, size_t capacity) {
        size_t length = 0;
        while (length < capacity && text[length] != '\0') {
            ++length;
        }
        return std::string(text, length);
    }

    bool FindResourceBlob(const unsigned char** data, size_t* size) {
        // The table is in FCSE.exe's own image, not in Dunia.dll.
        HMODULE self = GetModuleHandleW(nullptr);
        // MAKEINTRESOURCEW(10) rather than RT_RCDATA: this target does not
        // define UNICODE, so RT_RCDATA expands to the ANSI form and will not
        // pass to FindResourceW. Same reason as lua_host.cpp.
        HRSRC found = FindResourceW(self, L"FCSE_ADDRLIB", MAKEINTRESOURCEW(10));
        if (found == nullptr) {
            return false;
        }
        HGLOBAL block = LoadResource(self, found);
        if (block == nullptr) {
            return false;
        }
        const void* bytes = LockResource(block);
        DWORD length = SizeofResource(self, found);
        if (bytes == nullptr || length == 0) {
            return false;
        }
        // Nothing to free: LockResource points into the mapped image.
        *data = static_cast<const unsigned char*>(bytes);
        *size = length;
        return true;
    }

} // namespace

bool AddressLibrary::LoadFromMemory(const void* data, size_t size,
                                    const BuildInfo& build, uintptr_t duniaBase) {
    State& state = Get();
    const auto* blob = static_cast<const unsigned char*>(data);

    if (size < sizeof(Header)) {
        Log::Loader("address library: resource is smaller than its header");
        return false;
    }
    Header header;
    std::memcpy(&header, blob, sizeof(header));

    if (header.magic != kMagic) {
        Log::Loader("address library: bad magic in embedded table");
        return false;
    }
    if (header.format != kFormatVersion) {
        Log::Loader("address library: table format v" +
                    std::to_string(header.format) + ", this build understands v" +
                    std::to_string(kFormatVersion));
        return false;
    }
    if (header.codec != kCodecVarintDelta) {
        Log::Loader("address library: table uses codec " +
                    std::to_string(header.codec) + ", which this build cannot decode");
        return false;
    }
    if (size - sizeof(Header) < header.payloadBytes) {
        Log::Loader("address library: table is truncated");
        return false;
    }

    // Column order is fixed by the generator: 0 is the reference build the IDs
    // were minted against, 1 is the other. Match on the recorded id rather than
    // assuming, so a regenerated table that swapped them cannot go unnoticed.
    const std::string wanted = ToString(build.build);
    int column = -1;
    for (int i = 0; i < 2; ++i) {
        if (Field(header.buildId[i], sizeof(header.buildId[i])) == wanted) {
            column = i;
            break;
        }
    }
    if (column < 0) {
        Log::Loader("address library: embedded table has no column for build '" +
                    wanted + "'");
        return false;
    }

    state.mappingVersion = Field(header.mappingVersion, sizeof(header.mappingVersion));

    const unsigned char* p = blob + sizeof(Header);
    const unsigned char* end = p + header.payloadBytes;

    auto readVarint = [&p, end](uint64_t* out) -> bool {
        uint64_t value = 0;
        unsigned shift = 0;
        while (p < end) {
            const unsigned char byte = *p++;
            value |= static_cast<uint64_t>(byte & 0x7F) << shift;
            if ((byte & 0x80) == 0) {
                *out = value;
                return true;
            }
            shift += 7;
            if (shift > 63) {
                return false;
            }
        }
        return false;
    };
    auto unzigzag = [](uint64_t v) -> int64_t {
        return static_cast<int64_t>(v >> 1) ^ -static_cast<int64_t>(v & 1);
    };

    state.rva.assign(header.entryCount, 0);
    int64_t reference = 0;
    int64_t slide = 0;
    uint32_t absent = 0;

    for (uint32_t i = 0; i < header.entryCount; ++i) {
        uint64_t raw = 0;
        if (!readVarint(&raw)) {
            Log::Loader("address library: payload ended early at entry " +
                        std::to_string(i));
            state.rva.clear();
            return false;
        }
        reference += unzigzag(raw);

        if (!readVarint(&raw)) {
            Log::Loader("address library: payload ended early at entry " +
                        std::to_string(i));
            state.rva.clear();
            return false;
        }
        if (raw == 0) {
            // "Absent" describes the *target* build only: the ID was minted
            // against the reference build, so a reference RVA always exists for
            // it. Treating this as absent for column 0 would blank out every
            // entry the other build happens to lack.
            if (column == 0) {
                state.rva[i] = static_cast<uint32_t>(reference);
            } else {
                state.rva[i] = 0;   // 0 cannot be a valid RVA, so it marks absence
                ++absent;
            }
            continue;
        }
        slide += unzigzag(raw - 1);
        const int64_t target = reference + slide;

        // Both columns are stored as one stream, so which one this entry wants
        // is a choice between the reference RVA and the reference plus slide.
        const int64_t chosen = (column == 0) ? reference : target;
        if (chosen <= 0 || chosen > 0xFFFFFFFF) {
            Log::Loader("address library: entry " + std::to_string(i) +
                        " decoded to an impossible RVA");
            state.rva.clear();
            return false;
        }
        state.rva[i] = static_cast<uint32_t>(chosen);
    }

    state.ready = true;
    state.base = duniaBase;
    Log::Loader("address library: mapping v" + state.mappingVersion + ", " +
                std::to_string(header.entryCount) + " ids for " + wanted + ", " +
                std::to_string(absent) + " absent on this build (" +
                std::to_string(sizeof(Header) + header.payloadBytes) +
                " bytes embedded)");
    return true;
}

bool AddressLibrary::Init(HMODULE duniaModule, const BuildInfo& build) {
    State& state = Get();
    if (state.ready) {
        return true;
    }
    if (!build.supported) {
        Log::Loader("address library: refusing to bind to unsupported build '" +
                    std::string(build.id) + "'");
        return false;
    }

    const unsigned char* blob = nullptr;
    size_t size = 0;
    if (!FindResourceBlob(&blob, &size)) {
        Log::Loader("address library: FCSE_ADDRLIB resource is missing from "
                    "FCSE.exe - this build was linked without an address table");
        return false;
    }
    return LoadFromMemory(blob, size, build,
                          reinterpret_cast<uintptr_t>(duniaModule));
}

void AddressLibrary::Reset() {
    State& state = Get();
    state.ready = false;
    state.rva.clear();
    state.mappingVersion.clear();
    state.base = 0;
}

bool AddressLibrary::Ready() {
    return Get().ready;
}

uint32_t AddressLibrary::Rva(uint32_t id) {
    const State& state = Get();
    if (!state.ready || id >= state.rva.size()) {
        return 0;
    }
    return state.rva[id];
}

uintptr_t AddressLibrary::Address(uint32_t id) {
    const State& state = Get();
    const uint32_t rva = Rva(id);
    if (rva == 0 || state.base == 0) {
        return 0;
    }
    return state.base + rva;
}

uint32_t AddressLibrary::Count() {
    return static_cast<uint32_t>(Get().rva.size());
}

const std::string& AddressLibrary::MappingVersion() {
    return Get().mappingVersion;
}

bool AddressLibrary::ResolveAll(const uint32_t* ids, size_t count) {
    for (size_t i = 0; i < count; ++i) {
        if (Address(ids[i]) == 0) {
            return false;
        }
    }
    return true;
}

} // namespace FCSE
