#include "engine/address_library.h"

#include <algorithm>
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

    // Column order in the encoded table: 0 is the reference build
    // (fc2_103_uplay), whose RVAs are the lookup key, 1 is the other.
    constexpr int kColumnCount = 2;

    struct State {
        bool ready = false;

        // *Both* columns are always decoded, not just the running one. Someone
        // holding a GOG address needs the GOG column to look it up even while
        // Steam is running, and vice versa - that reverse direction is the
        // whole point of the by-build API. Two columns plus their sort indices
        // is about 1.4 MB, which is nothing next to being unable to answer.
        std::vector<uint32_t> column[kColumnCount];   // by row
        std::vector<uint32_t> sorted[kColumnCount];   // rows, ordered by that column

        int running = -1;                             // which column is live
        std::string mappingVersion;
        uintptr_t base = 0;
    };

    // DuniaBuild -> column index, or -1 for a build this table has no column for.
    int ColumnFor(DuniaBuild build) {
        switch (build) {
            case DuniaBuild::Uplay103:  return 0;
            case DuniaBuild::Retail103: return 1;
            default:                    return -1;
        }
    }

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

    // Column order is fixed by the generator, but verify it rather than assume:
    // a regenerated table that swapped the two columns would otherwise resolve
    // every address to the other build's layout, silently.
    const std::string wanted = ToString(build.build);
    int column = -1;
    for (int i = 0; i < kColumnCount; ++i) {
        const std::string name = Field(header.buildId[i], sizeof(header.buildId[i]));
        const int expected = ColumnFor(
            name == "fc2_103_uplay" ? DuniaBuild::Uplay103
                                    : (name == "fc2_103_retail" ? DuniaBuild::Retail103
                                                                : DuniaBuild::Unknown));
        if (expected != i) {
            Log::Loader("address library: embedded table column " + std::to_string(i) +
                        " is '" + name + "', which is not where this build expects it");
            return false;
        }
        if (name == wanted) {
            column = i;
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

    for (int c = 0; c < kColumnCount; ++c) {
        state.column[c].assign(header.entryCount, 0);
    }
    int64_t reference = 0;
    int64_t slide = 0;
    uint32_t absent = 0;

    const auto fail = [&state](const std::string& why) {
        Log::Loader("address library: " + why);
        for (int c = 0; c < kColumnCount; ++c) {
            state.column[c].clear();
        }
        return false;
    };

    for (uint32_t i = 0; i < header.entryCount; ++i) {
        uint64_t raw = 0;
        if (!readVarint(&raw)) {
            return fail("payload ended early at entry " + std::to_string(i));
        }
        reference += unzigzag(raw);
        if (reference <= 0 || reference > 0xFFFFFFFF) {
            return fail("entry " + std::to_string(i) +
                        " decoded to an impossible reference RVA");
        }
        state.column[0][i] = static_cast<uint32_t>(reference);

        if (!readVarint(&raw)) {
            return fail("payload ended early at entry " + std::to_string(i));
        }
        if (raw == 0) {
            // "Absent" describes the second column only: the reference column is
            // the key, so it always has a value. 0 cannot be a valid RVA, so it
            // marks absence.
            state.column[1][i] = 0;
            ++absent;
            continue;
        }
        slide += unzigzag(raw - 1);
        const int64_t target = reference + slide;
        if (target <= 0 || target > 0xFFFFFFFF) {
            return fail("entry " + std::to_string(i) +
                        " decoded to an impossible RVA");
        }
        state.column[1][i] = static_cast<uint32_t>(target);
    }

    // Sort indices for the lookups. The reference column arrives ascending, but
    // is sorted anyway rather than trusted - and the target column genuinely is
    // reordered, by the layout differences this whole library exists to bridge.
    for (int c = 0; c < kColumnCount; ++c) {
        std::vector<uint32_t>& order = state.sorted[c];
        order.clear();
        order.reserve(header.entryCount);
        for (uint32_t i = 0; i < header.entryCount; ++i) {
            if (state.column[c][i] != 0) {
                order.push_back(i);
            }
        }
        const std::vector<uint32_t>& col = state.column[c];
        std::sort(order.begin(), order.end(),
                  [&col](uint32_t a, uint32_t b) { return col[a] < col[b]; });
    }

    state.ready = true;
    state.running = column;
    state.base = duniaBase;
    Log::Loader("address library: mapping v" + state.mappingVersion + ", " +
                std::to_string(header.entryCount) + " addresses for " + wanted + ", " +
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
    for (int c = 0; c < kColumnCount; ++c) {
        state.column[c].clear();
        state.sorted[c].clear();
    }
    state.running = -1;
    state.mappingVersion.clear();
    state.base = 0;
}

bool AddressLibrary::Ready() {
    return Get().ready;
}

namespace {

    // Row index whose `sourceBuild` RVA is `rva`, or -1. The encoded table is
    // sorted by the reference column, and each column carries its own sort
    // order, so both directions are a binary search over ~89k entries.
    int IndexOf(DuniaBuild sourceBuild, uint32_t rva) {
        const State& state = Get();
        const int c = ColumnFor(sourceBuild);
        if (!state.ready || c < 0 || rva == 0) {
            return -1;
        }
        const std::vector<uint32_t>& col = state.column[c];
        const std::vector<uint32_t>& order = state.sorted[c];
        const auto it = std::lower_bound(order.begin(), order.end(), rva,
                                         [&col](uint32_t row, uint32_t value) {
                                             return col[row] < value;
                                         });
        if (it == order.end() || col[*it] != rva) {
            return -1;
        }
        return static_cast<int>(*it);
    }

} // namespace

uintptr_t AddressLibrary::AddressFrom(DuniaBuild sourceBuild, uint32_t rva) {
    const State& state = Get();
    const int row = IndexOf(sourceBuild, rva);
    if (row < 0 || state.running < 0 || state.base == 0) {
        return 0;
    }
    const uint32_t here = state.column[state.running][row];
    return here == 0 ? 0 : state.base + here;
}

uintptr_t AddressLibrary::Address(uint32_t referenceRva) {
    return AddressFrom(DuniaBuild::Uplay103, referenceRva);
}

uint32_t AddressLibrary::RvaIn(DuniaBuild build, uint32_t referenceRva) {
    const State& state = Get();
    const int row = IndexOf(DuniaBuild::Uplay103, referenceRva);
    const int c = ColumnFor(build);
    if (row < 0 || c < 0) {
        return 0;
    }
    return state.column[c][row];
}

uint32_t AddressLibrary::Count() {
    return static_cast<uint32_t>(Get().column[0].size());
}

const std::string& AddressLibrary::MappingVersion() {
    return Get().mappingVersion;
}

bool AddressLibrary::ResolveAll(const uint32_t* referenceRvas, size_t count) {
    for (size_t i = 0; i < count; ++i) {
        if (Address(referenceRvas[i]) == 0) {
            return false;
        }
    }
    return true;
}

} // namespace FCSE
