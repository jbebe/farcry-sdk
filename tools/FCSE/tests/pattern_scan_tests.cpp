// The pattern compiler and the search, tested over buffers rather than over a
// running game.
//
// Both halves are worth pinning down because both fail quietly if they are
// wrong: a compiler that mis-reads "??" produces a pattern that matches the
// wrong length, and a search that returns the first of several matches hands a
// plugin an address in some unrelated function. Neither shows up as a crash
// where the mistake was made.

#include <cstdint>
#include <string>
#include <vector>

#include <gtest/gtest.h>

#include "api/pattern_scan.h"

namespace {

using FCSE::PatternScan;

std::vector<size_t> Find(const std::vector<uint8_t>& hay, const char* pattern,
                         size_t limit = PatternScan::kMaxMatches) {
    const PatternScan::Compiled c = PatternScan::Compile(pattern);
    EXPECT_TRUE(c.valid) << pattern << " -> " << c.error;
    return PatternScan::Search(hay.data(), hay.size(), c, limit);
}

TEST(PatternCompile, ParsesPlainBytes) {
    const PatternScan::Compiled c = PatternScan::Compile("8B 41 04");
    ASSERT_TRUE(c.valid) << c.error;
    EXPECT_EQ(c.size(), 3u);
    EXPECT_EQ(c.bytes[0], 0x8Bu);
    EXPECT_EQ(c.bytes[2], 0x04u);
    EXPECT_TRUE(c.mask[0]);
}

// "??" is one wildcard byte. Reading it as two would shift every byte after it.
TEST(PatternCompile, DoubleQuestionMarkIsOneByte) {
    const PatternScan::Compiled c = PatternScan::Compile("8B ?? 04");
    ASSERT_TRUE(c.valid) << c.error;
    EXPECT_EQ(c.size(), 3u);
    EXPECT_TRUE(c.mask[0]);
    EXPECT_FALSE(c.mask[1]);
    EXPECT_TRUE(c.mask[2]);
}

// Only "??" - one spelling, and the one whose written width equals its byte
// length. A lone '?' is far likelier a typo than an intention, and silently
// accepting it would let a pattern read as one width and parse as another.
TEST(PatternCompile, RejectsOtherWildcardSpellings) {
    for (const char* text : {"8B ? 04", "8B * 04", "8B ?", "?"}) {
        const PatternScan::Compiled c = PatternScan::Compile(text);
        EXPECT_FALSE(c.valid) << text << " should not compile";
        EXPECT_FALSE(c.error.empty()) << text << " must say why";
    }
}

// Consecutive wildcards stay one byte each rather than running together.
TEST(PatternCompile, ConsecutiveWildcardsCountSeparately) {
    const PatternScan::Compiled c = PatternScan::Compile("E8 ?? ?? ?? ?? 33");
    ASSERT_TRUE(c.valid) << c.error;
    EXPECT_EQ(c.size(), 6u);
    for (size_t i = 1; i <= 4; ++i) {
        EXPECT_FALSE(c.mask[i]) << "byte " << i;
    }
    EXPECT_TRUE(c.mask[5]);
    EXPECT_EQ(c.bytes[5], 0x33u);
}

TEST(PatternCompile, IsWhitespaceAndCaseInsensitive) {
    const PatternScan::Compiled a = PatternScan::Compile("8b4104");
    const PatternScan::Compiled b = PatternScan::Compile("  8B\t41   04 ");
    ASSERT_TRUE(a.valid);
    ASSERT_TRUE(b.valid);
    EXPECT_EQ(a.bytes, b.bytes);
}

TEST(PatternCompile, RejectsWhatCannotBeMeant) {
    // Empty, all-wildcard (matches everywhere), a lone hex digit (a typo for a
    // byte far more often than an intentional nibble), and junk.
    EXPECT_FALSE(PatternScan::Compile("").valid);
    EXPECT_FALSE(PatternScan::Compile("   ").valid);
    EXPECT_FALSE(PatternScan::Compile("?? ?? ??").valid);
    EXPECT_FALSE(PatternScan::Compile("8B 4").valid);
    EXPECT_FALSE(PatternScan::Compile("8B ZZ").valid);
    EXPECT_FALSE(PatternScan::Compile(nullptr).valid);
}

TEST(PatternSearch, FindsExactAndWildcardMatches) {
    const std::vector<uint8_t> hay = {0x00, 0x8B, 0x41, 0x04, 0xC3, 0x8B, 0x99, 0x04};
    EXPECT_EQ(Find(hay, "8B 41 04"), (std::vector<size_t>{1}));
    EXPECT_EQ(Find(hay, "8B ?? 04"), (std::vector<size_t>{1, 5}));
    EXPECT_EQ(Find(hay, "C3"), (std::vector<size_t>{4}));
    EXPECT_TRUE(Find(hay, "DE AD BE EF").empty());
}

TEST(PatternSearch, HandlesEdgesOfTheBuffer) {
    const std::vector<uint8_t> hay = {0xAA, 0xBB, 0xCC};
    EXPECT_EQ(Find(hay, "AA"), (std::vector<size_t>{0}));
    EXPECT_EQ(Find(hay, "CC"), (std::vector<size_t>{2}));
    EXPECT_EQ(Find(hay, "AA BB CC"), (std::vector<size_t>{0}));
    // Longer than the buffer must not read past it.
    EXPECT_TRUE(Find(hay, "AA BB CC DD").empty());
}

// Overlapping matches all count: the scan advances one byte past a hit, not a
// whole pattern length, so a repeating run reports every site rather than every
// other one.
TEST(PatternSearch, ReportsOverlappingMatches) {
    const std::vector<uint8_t> hay = {0x90, 0x90, 0x90, 0x90};
    EXPECT_EQ(Find(hay, "90 90"), (std::vector<size_t>{0, 1, 2}));
}

TEST(PatternSearch, StopsAtTheLimit) {
    const std::vector<uint8_t> hay(64, 0x90);
    EXPECT_EQ(Find(hay, "90", 4).size(), 4u);
}

// A pattern beginning with a wildcard still has to anchor on some fixed byte;
// getting that offset wrong is an easy way to report matches one byte early.
TEST(PatternSearch, AnchorsCorrectlyWhenLeadingBytesAreWildcards) {
    const std::vector<uint8_t> hay = {0x11, 0x22, 0xE8, 0x44, 0x55, 0xE8};
    EXPECT_EQ(Find(hay, "?? ?? E8"), (std::vector<size_t>{0, 3}));
    EXPECT_EQ(Find(hay, "?? E8"), (std::vector<size_t>{1, 4}));
}

TEST(PatternSearch, EmptyHaystackIsNotAMatch) {
    const PatternScan::Compiled c = PatternScan::Compile("90");
    ASSERT_TRUE(c.valid);
    EXPECT_TRUE(PatternScan::Search(nullptr, 0, c, 8).empty());
}

// An invalid pattern must never match anything, rather than degrading into a
// zero-length pattern that matches at every offset.
TEST(PatternSearch, InvalidPatternMatchesNothing) {
    const std::vector<uint8_t> hay(16, 0x90);
    const PatternScan::Compiled bad = PatternScan::Compile("nonsense");
    ASSERT_FALSE(bad.valid);
    EXPECT_TRUE(PatternScan::Search(hay.data(), hay.size(), bad, 8).empty());
}

} // namespace
