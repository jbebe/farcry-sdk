// Checks the address-library decoder against a table the generator actually
// produced.
//
// The encoder lives in tools/FCSE/tools/addrlib/emit.py and the decoder in
// src/engine/address_library.cpp, so nothing but a test like this would notice
// them drifting apart. A disagreement would not fail loudly at runtime either:
// it would hand out addresses that look entirely plausible and jump into the
// middle of unrelated engine code.
//
// The pairs below were each verified independently - see
// tools/FCSE/tools/addrlib/out/validation_report.txt for the export and
// string-literal ground truths, and overrides.csv for the two derived by hand.

#include <cstdint>
#include <fstream>
#include <string>
#include <vector>

#include <gtest/gtest.h>

#include "engine/address_library.h"
#include "engine/build_id.h"

namespace {

// Deliberately the committed source-tree copy, not the one linked into an exe:
// this is a test of the table FCSE ships, and reading it from disk keeps the
// test independent of whether the resource was embedded correctly (which
// fcse.rc covers separately).
std::vector<unsigned char> ReadTable() {
    const char* candidates[] = {
        "assets/address_table.bin",
        "../assets/address_table.bin",
        "../../assets/address_table.bin",
        FCSE_TEST_ADDRESS_TABLE,
    };
    for (const char* path : candidates) {
        std::ifstream file(path, std::ios::binary);
        if (!file) {
            continue;
        }
        return std::vector<unsigned char>((std::istreambuf_iterator<char>(file)),
                                          std::istreambuf_iterator<char>());
    }
    return {};
}

FCSE::BuildInfo MakeBuild(FCSE::DuniaBuild which) {
    FCSE::BuildInfo info;
    info.build = which;
    info.supported = true;
    info.id = FCSE::ToString(which);
    return info;
}

// A fabricated module base. Nothing is dereferenced; Address only has to add.
constexpr uintptr_t kFakeBase = 0x10000000u;

struct Pair {
    uint32_t uplay;
    uint32_t retail;
};

// Sampled across the address space, including both hand-derived overrides. Note
// how little the two columns resemble each other - there is no offset that maps
// one to the other, which is the reason this library exists.
constexpr Pair kPairs[] = {
    {0x0081E9C0u, 0x00811C00u},  // CFCXOptionGamePage ctor
    {0x00CDE0D0u, 0x00CA4E80u},  // AddBoolSetting
    {0x00FE3178u, 0x00F32878u},  // magma engine singleton (.data)
    {0x01606360u, 0x01555A60u},  // tick source time block (.data)
    {0x005E8CE0u, 0x005DB3C0u},  // CFileNameNomad::SetIdentifier (override)
    {0x0061D3F0u, 0x0060F800u},  // reader vtable +0x10 (override)
};

class AddressLibraryTest : public ::testing::Test {
protected:
    void TearDown() override { FCSE::AddressLibrary::Reset(); }

    std::vector<unsigned char> Load(FCSE::DuniaBuild build) {
        std::vector<unsigned char> blob = ReadTable();
        EXPECT_FALSE(blob.empty());
        EXPECT_TRUE(FCSE::AddressLibrary::LoadFromMemory(blob.data(), blob.size(),
                                                         MakeBuild(build), kFakeBase));
        return blob;
    }
};

TEST_F(AddressLibraryTest, TableIsPresent) {
    EXPECT_FALSE(ReadTable().empty())
        << "assets/address_table.bin was not found. Regenerate it with "
           "tools/FCSE/tools/addrlib (python build_addrlib.py).";
}

// Running on Steam: an address named from either build must land on the Steam one.
TEST_F(AddressLibraryTest, ResolvesToUplayWhateverBuildNamedIt) {
    Load(FCSE::DuniaBuild::Uplay103);
    for (const Pair& p : kPairs) {
        EXPECT_EQ(FCSE::AddressLibrary::AddressFrom(FCSE::DuniaBuild::Uplay103, p.uplay),
                  kFakeBase + p.uplay);
        EXPECT_EQ(FCSE::AddressLibrary::AddressFrom(FCSE::DuniaBuild::Retail103, p.retail),
                  kFakeBase + p.uplay)
            << "a GOG address must resolve to the Steam address while Steam runs";
        // Address() is the reference-build shorthand address_symbols.h relies on.
        EXPECT_EQ(FCSE::AddressLibrary::Address(p.uplay), kFakeBase + p.uplay);
    }
}

// Running on GOG: the same two names must now land on the GOG one.
TEST_F(AddressLibraryTest, ResolvesToRetailWhateverBuildNamedIt) {
    Load(FCSE::DuniaBuild::Retail103);
    for (const Pair& p : kPairs) {
        EXPECT_EQ(FCSE::AddressLibrary::AddressFrom(FCSE::DuniaBuild::Retail103, p.retail),
                  kFakeBase + p.retail);
        EXPECT_EQ(FCSE::AddressLibrary::AddressFrom(FCSE::DuniaBuild::Uplay103, p.uplay),
                  kFakeBase + p.retail)
            << "a Steam address must resolve to the GOG address while GOG runs";
        EXPECT_EQ(FCSE::AddressLibrary::Address(p.uplay), kFakeBase + p.retail);
    }
}

// Both columns are readable regardless of which build is running - that is what
// makes the reverse direction work at all.
TEST_F(AddressLibraryTest, BothColumnsReadableFromEitherBuild) {
    for (FCSE::DuniaBuild running :
         {FCSE::DuniaBuild::Uplay103, FCSE::DuniaBuild::Retail103}) {
        Load(running);
        for (const Pair& p : kPairs) {
            EXPECT_EQ(FCSE::AddressLibrary::RvaIn(FCSE::DuniaBuild::Uplay103, p.uplay),
                      p.uplay);
            EXPECT_EQ(FCSE::AddressLibrary::RvaIn(FCSE::DuniaBuild::Retail103, p.uplay),
                      p.retail);
        }
        FCSE::AddressLibrary::Reset();
    }
}

TEST_F(AddressLibraryTest, UnknownAddressesResolveToZero) {
    Load(FCSE::DuniaBuild::Uplay103);
    // Inside the image but not a mapped address, and outside it entirely.
    EXPECT_EQ(FCSE::AddressLibrary::Address(0x0081E9C1u), 0u)
        << "an address one byte off a real one must fail, not round to it";
    EXPECT_EQ(FCSE::AddressLibrary::Address(0x7FFFFFFFu), 0u);
    EXPECT_EQ(FCSE::AddressLibrary::Address(0u), 0u);
    EXPECT_EQ(FCSE::AddressLibrary::AddressFrom(FCSE::DuniaBuild::Unknown, 0x0081E9C0u), 0u);
}

TEST_F(AddressLibraryTest, ResolveAllRequiresEveryAddress) {
    Load(FCSE::DuniaBuild::Uplay103);
    const uint32_t good[] = {kPairs[0].uplay, kPairs[1].uplay};
    const uint32_t mixed[] = {kPairs[0].uplay, 0x7FFFFFFFu};
    EXPECT_TRUE(FCSE::AddressLibrary::ResolveAll(good, 2));
    EXPECT_FALSE(FCSE::AddressLibrary::ResolveAll(mixed, 2));
}

// An unsupported build must not get a table at all. Binding one would mean
// resolving addresses against a layout they were never generated for.
TEST_F(AddressLibraryTest, RejectsUnknownBuild) {
    const std::vector<unsigned char> blob = ReadTable();
    ASSERT_FALSE(blob.empty());
    FCSE::BuildInfo unknown;
    unknown.build = FCSE::DuniaBuild::Retail100;
    unknown.supported = false;
    unknown.id = "fc2_100_retail";
    EXPECT_FALSE(FCSE::AddressLibrary::LoadFromMemory(blob.data(), blob.size(),
                                                      unknown, kFakeBase));
    EXPECT_FALSE(FCSE::AddressLibrary::Ready());
}

TEST_F(AddressLibraryTest, RejectsTruncatedAndCorruptTables) {
    std::vector<unsigned char> blob = ReadTable();
    ASSERT_FALSE(blob.empty());
    const FCSE::BuildInfo build = MakeBuild(FCSE::DuniaBuild::Uplay103);

    EXPECT_FALSE(FCSE::AddressLibrary::LoadFromMemory(blob.data(), 8, build, kFakeBase));

    std::vector<unsigned char> shortened(blob.begin(), blob.begin() + blob.size() / 2);
    EXPECT_FALSE(FCSE::AddressLibrary::LoadFromMemory(shortened.data(),
                                                      shortened.size(), build, kFakeBase));

    std::vector<unsigned char> badMagic = blob;
    badMagic[0] = 'X';
    EXPECT_FALSE(FCSE::AddressLibrary::LoadFromMemory(badMagic.data(),
                                                      badMagic.size(), build, kFakeBase));

    std::vector<unsigned char> badFormat = blob;
    badFormat[4] = 0x7F;
    EXPECT_FALSE(FCSE::AddressLibrary::LoadFromMemory(badFormat.data(),
                                                      badFormat.size(), build, kFakeBase));

    EXPECT_FALSE(FCSE::AddressLibrary::Ready());
}

} // namespace
