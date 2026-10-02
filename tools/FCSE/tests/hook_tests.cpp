// The hook manager's stacking rule, on code assembled in this process: hooks at one address all
// run, the newest first, and teardown gives back the bytes they displaced.

#include <windows.h>

#include <cstdint>
#include <cstring>
#include <string>

#include <gtest/gtest.h>

#include "api/hook.h"

namespace {

using FCSE::HookManager;

// mov eax, 1 / add eax, 2 / ret - two 5-byte instructions, so each is a hook site of its own.
constexpr uint8_t kCode[] = {0xB8, 0x01, 0x00, 0x00, 0x00, 0x05, 0x02, 0x00, 0x00, 0x00, 0xC3};
constexpr size_t kAdd = 5;

using TargetFn = int(__cdecl*)();

std::string g_trace;

template <char Tag>
TargetFn g_original = nullptr;

template <char Tag>
int __cdecl Detour() {
    g_trace += Tag;
    return g_original<Tag>();
}

template <char Tag>
void Trace(FCSE_MidHookContext*) {
    g_trace += Tag;
}

// Appends a digit to eax, so stacked handlers spell out the order they ran in.
template <int Digit>
void Append(FCSE_MidHookContext* ctx) {
    ctx->eax = ctx->eax * 10 + Digit;
}

class HookStacking : public ::testing::Test {
protected:
    void SetUp() override {
        code_ = static_cast<uint8_t*>(VirtualAlloc(nullptr, sizeof(kCode), MEM_COMMIT | MEM_RESERVE,
                                                   PAGE_EXECUTE_READWRITE));
        ASSERT_NE(code_, nullptr);
        std::memcpy(code_, kCode, sizeof(kCode));
        g_trace.clear();
    }

    void TearDown() override {
        HookManager::Shutdown();
        VirtualFree(code_, 0, MEM_RELEASE);
    }

    template <char Tag>
    bool Hook(size_t offset) {
        return HookManager::Hook(code_ + offset, reinterpret_cast<void*>(&Detour<Tag>),
                                 reinterpret_cast<void**>(&g_original<Tag>));
    }

    bool MidHook(size_t offset, FCSE_MidHookHandler handler) {
        return HookManager::MidHook(code_ + offset, handler);
    }

    int Call() const { return reinterpret_cast<TargetFn>(code_)(); }

    uint8_t* code_ = nullptr;
};

TEST_F(HookStacking, TwoHooksBothRunNewestFirst) {
    ASSERT_TRUE(Hook<'A'>(0));
    ASSERT_TRUE(Hook<'B'>(0));

    EXPECT_EQ(Call(), 3);
    EXPECT_EQ(g_trace, "BA");
}

TEST_F(HookStacking, MidHookStacksOverHook) {
    ASSERT_TRUE(Hook<'A'>(0));
    ASSERT_TRUE(MidHook(0, &Trace<'b'>));

    EXPECT_EQ(Call(), 3);
    EXPECT_EQ(g_trace, "bA");
}

TEST_F(HookStacking, HookStacksOverMidHook) {
    ASSERT_TRUE(MidHook(0, &Trace<'a'>));
    ASSERT_TRUE(Hook<'B'>(0));

    EXPECT_EQ(Call(), 3);
    EXPECT_EQ(g_trace, "Ba");
}

// eax is 1, then 15 from the newer handler, 154 from the older, and 156 after the add.
TEST_F(HookStacking, MidHooksSeeEarlierRegisterWrites) {
    ASSERT_TRUE(MidHook(kAdd, &Append<4>));
    ASSERT_TRUE(MidHook(kAdd, &Append<5>));

    EXPECT_EQ(Call(), 156);
}

// Inside another hook's displaced bytes, only its start takes a second hook.
TEST_F(HookStacking, OverlapAtAnotherStartIsRejected) {
    ASSERT_TRUE(Hook<'A'>(0));
    uint8_t hooked[sizeof(kCode)];
    std::memcpy(hooked, code_, sizeof(kCode));

    EXPECT_FALSE(Hook<'B'>(2));
    EXPECT_EQ(std::memcmp(code_, hooked, sizeof(kCode)), 0);

    ASSERT_TRUE(MidHook(kAdd, &Trace<'b'>));
    EXPECT_EQ(Call(), 3);
    EXPECT_EQ(g_trace, "Ab");
}

TEST_F(HookStacking, ShutdownRestoresTheOriginalBytes) {
    ASSERT_TRUE(Hook<'A'>(0));
    ASSERT_TRUE(MidHook(0, &Trace<'b'>));
    ASSERT_TRUE(Hook<'B'>(0));

    HookManager::Shutdown();

    // Bytes left over from a hook jump into its freed trampoline, so they must not be called.
    ASSERT_EQ(std::memcmp(code_, kCode, sizeof(kCode)), 0);
    EXPECT_EQ(Call(), 3);
    EXPECT_TRUE(g_trace.empty());
}

}
