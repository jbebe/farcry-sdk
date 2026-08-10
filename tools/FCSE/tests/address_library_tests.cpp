// Checks the address-library decoder against a table the generator actually
// produced.
//
// The encoder lives in tools/FCSE/tools/addrlib/emit.py and the decoder in
// src/engine/address_library.cpp, so nothing but a test like this would notice
// them drifting apart. A disagreement would not fail loudly at runtime either:
// it would hand out addresses that look entirely plausible and jump into the
// middle of unrelated engine code.
//
// The expected values below were each verified independently - see
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
        "../../../assets/address_table.bin",
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

// A fabricated module base. Nothing is dereferenced; Address() only has to add.
constexpr uintptr_t kFakeBase = 0x10000000u;

struct Expected {
    uint32_t id;
    uint32_t uplayRva;
    uint32_t retailRva;
};

// Sampled across the ID space, including both hand-derived overrides at the end
// (89351/89352) - those are appended out of RVA order, which is exactly the case
// an encoder assuming an ascending reference column would corrupt.
constexpr Expected kExpected[] = {
    {33624u, 0x0081E9C0u, 0x00811C00u},  // CFCXOptionGamePage ctor
    {52074u, 0x00CDE0D0u, 0x00CA4E80u},  // AddBoolSetting
    {81510u, 0x00FE3178u, 0x00F32878u},  // magma engine singleton (.data)
    {82943u, 0x01606360u, 0x01555A60u},  // tick source time block (.data)
    {89351u, 0x005E8CE0u, 0x005DB3C0u},  // CFileNameNomad::SetIdentifier (override)
    {89352u, 0x0061D3F0u, 0x0060F800u},  // reader vtable +0x10 (override)
};

class AddressLibraryTest : public ::testing::Test {
protected:
    void TearDown() override { FCSE::AddressLibrary::Reset(); }
};

TEST_F(AddressLibraryTest, TableIsPresent) {
    EXPECT_FALSE(ReadTable().empty())
        << "assets/address_table.bin was not found. Regenerate it with "
           "tools/FCSE/tools/addrlib (python build_addrlib.py).";
}

TEST_F(AddressLibraryTest, DecodesUplayColumn) {
    const std::vector<unsigned char> blob = ReadTable();
    ASSERT_FALSE(blob.empty());
    ASSERT_TRUE(FCSE::AddressLibrary::LoadFromMemory(
        blob.data(), blob.size(), MakeBuild(FCSE::DuniaBuild::Uplay103), kFakeBase));

    for (const Expected& e : kExpected) {
        EXPECT_EQ(FCSE::AddressLibrary::Rva(e.id), e.uplayRva)
            << "id " << e.id << " decoded to the wrong Steam/Uplay RVA";
        EXPECT_EQ(FCSE::AddressLibrary::Address(e.id), kFakeBase + e.uplayRva);
    }
}

TEST_F(AddressLibraryTest, DecodesRetailColumn) {
    const std::vector<unsigned char> blob = ReadTable();
    ASSERT_FALSE(blob.empty());
    ASSERT_TRUE(FCSE::AddressLibrary::LoadFromMemory(
        blob.data(), blob.size(), MakeBuild(FCSE::DuniaBuild::Retail103), kFakeBase));

    for (const Expected& e : kExpected) {
        EXPECT_EQ(FCSE::AddressLibrary::Rva(e.id), e.retailRva)
            << "id " << e.id << " decoded to the wrong GOG/retail RVA";
    }
}

// The reference column is complete by construction: IDs are minted against it,
// so "absent" can only ever describe the other build. A decoder that treated the
// absence marker as applying to both would silently blank ~48 reference entries.
TEST_F(AddressLibraryTest, ReferenceColumnHasNoGaps) {
    const std::vector<unsigned char> blob = ReadTable();
    ASSERT_FALSE(blob.empty());
    ASSERT_TRUE(FCSE::AddressLibrary::LoadFromMemory(
        blob.data(), blob.size(), MakeBuild(FCSE::DuniaBuild::Uplay103), kFakeBase));

    const uint32_t count = FCSE::AddressLibrary::Count();
    ASSERT_GT(count, 80000u);
    for (uint32_t id = 0; id < count; ++id) {
        ASSERT_NE(FCSE::AddressLibrary::Rva(id), 0u)
            << "reference RVA missing for id " << id;
    }
}

TEST_F(AddressLibraryTest, OutOfRangeIdsResolveToZero) {
    const std::vector<unsigned char> blob = ReadTable();
    ASSERT_FALSE(blob.empty());
    ASSERT_TRUE(FCSE::AddressLibrary::LoadFromMemory(
        blob.data(), blob.size(), MakeBuild(FCSE::DuniaBuild::Retail103), kFakeBase));

    const uint32_t count = FCSE::AddressLibrary::Count();
    EXPECT_EQ(FCSE::AddressLibrary::Rva(count), 0u);
    EXPECT_EQ(FCSE::AddressLibrary::Rva(count + 1000u), 0u);
    EXPECT_EQ(FCSE::AddressLibrary::Address(count), 0u);
    EXPECT_EQ(FCSE::AddressLibrary::Rva(0xFFFFFFFFu), 0u);
}

// An unsupported build must not get a table at all. Binding one would mean
// resolving IDs against a layout they were never generated for.
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
