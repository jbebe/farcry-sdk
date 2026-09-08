// Tests for the arithmetic behind the settings page's scroll window - the part of the scroll that
// runs without a game attached, and the part that decides which setting each control is bound to.
// It fails quietly both ways: a window left past the end of a plan that shrank shows nothing, and a
// line-to-row mapping that is off by one puts every control under the wrong label.
#include "ui/page_window.h"

#include <gtest/gtest.h>

namespace {

using FCSE::page::Window;

constexpr size_t kLines = Window::kLines;

TEST(PageWindowTest, APlanThatFitsOnScreenDoesNotScroll) {
    Window window{kLines, 0};
    EXPECT_FALSE(window.Scrolls());
    EXPECT_EQ(window.Visible(), kLines);

    bool joined = true;
    EXPECT_FALSE(window.Scroll(1, &joined));
    EXPECT_EQ(window.top, 0u) << "a refused scroll must not move the window";
    EXPECT_FALSE(joined);
}

TEST(PageWindowTest, AShortPlanFillsOnlyTheLinesItHas) {
    Window window{3, 0};
    EXPECT_EQ(window.Visible(), 3u);
    EXPECT_EQ(window.RowOf(2), 2u);
    EXPECT_EQ(window.RowOf(3), Window::kNoRow);
    EXPECT_EQ(window.LineOf(3), -1);
}

TEST(PageWindowTest, ScrollingMovesOneRowInEitherDirection) {
    Window window{kLines + 5, 2};
    bool joined = true;

    ASSERT_TRUE(window.Scroll(1, &joined));
    EXPECT_EQ(window.top, 3u);
    EXPECT_FALSE(joined);

    ASSERT_TRUE(window.Scroll(-1, &joined));
    EXPECT_EQ(window.top, 2u);
    EXPECT_FALSE(joined);
}

TEST(PageWindowTest, ScrollingPastEitherEndJoinsToTheOther) {
    const size_t last = 5;
    Window window{kLines + last, last};
    bool joined = false;

    ASSERT_TRUE(window.Scroll(1, &joined));
    EXPECT_TRUE(joined);
    EXPECT_EQ(window.top, 0u);

    ASSERT_TRUE(window.Scroll(-1, &joined));
    EXPECT_TRUE(joined);
    EXPECT_EQ(window.top, last);
}

TEST(PageWindowTest, ClampPullsTheWindowBackWhenThePlanShrinks) {
    Window window{kLines + 10, 10};

    window.total = kLines + 4;
    window.Clamp();
    EXPECT_EQ(window.top, 4u);

    window.total = kLines - 1;
    window.Clamp();
    EXPECT_EQ(window.top, 0u) << "a plan that now fits has no window to offset";
    EXPECT_EQ(window.Visible(), kLines - 1);
}

TEST(PageWindowTest, EveryVisibleLineRoundTripsToItsOwnRow) {
    Window window{kLines * 3, 7};
    for (size_t line = 0; line < window.Visible(); ++line) {
        size_t row = window.RowOf(line);
        EXPECT_EQ(row, 7 + line);
        EXPECT_EQ(window.LineOf(row), static_cast<int>(line));
    }
    EXPECT_EQ(window.LineOf(6), -1) << "the row above the window is off screen";
    EXPECT_EQ(window.LineOf(7 + kLines), -1) << "and so is the one below it";
    EXPECT_EQ(window.LineOf(Window::kNoRow), -1) << "a row that was never on screen has no line";
}

}
