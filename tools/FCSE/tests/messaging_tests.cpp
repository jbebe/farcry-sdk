// Tests for Messaging, the channels behind FCSE_PluginAPI::Listen and Send.
#include "api/messaging.h"
#include "caller_identity.h"

#include <gtest/gtest.h>

#include <atomic>
#include <string>
#include <thread>
#include <vector>

namespace {

using FCSE::Messaging;

// What one listener heard, in order.
struct Heard {
    std::vector<std::string> senders;
    std::vector<std::string> texts;
};

void Record(const char* sender, const void* data, size_t size, void* userdata) {
    auto* heard = static_cast<Heard*>(userdata);
    heard->senders.emplace_back(sender);
    heard->texts.emplace_back(static_cast<const char*>(data), size);
}

class MessagingTest : public ::testing::Test {
protected:
    void SetUp() override { Messaging::ResetForTesting(); }
};

TEST_F(MessagingTest, NobodyHearsAChannelWithoutListeners) {
    EXPECT_EQ(Messaging::Send("combat", "x", 1), 0u);
}

TEST_F(MessagingTest, EveryListenerHearsTheBytesAndTheSender) {
    Heard first;
    Heard second;
    ASSERT_TRUE(Messaging::Listen("combat", &Record, &first));
    ASSERT_TRUE(Messaging::Listen("combat", &Record, &second));

    const FCSE::ScopedCallerIdentity sender("difficultyoverhaul");
    EXPECT_EQ(Messaging::Send("combat", "hit", 3), 2u);

    for (const Heard* heard : {&first, &second}) {
        ASSERT_EQ(heard->texts.size(), 1u);
        EXPECT_EQ(heard->texts[0], "hit");
        EXPECT_EQ(heard->senders[0], "difficultyoverhaul");
    }
}

TEST_F(MessagingTest, ChannelsAreApart) {
    Heard heard;
    ASSERT_TRUE(Messaging::Listen("combat", &Record, &heard));

    EXPECT_EQ(Messaging::Send("weather", "rain", 4), 0u);
    EXPECT_TRUE(heard.texts.empty());
}

TEST_F(MessagingTest, ListenersHearInTheOrderTheyListened) {
    std::vector<int> order;
    const auto mark = [](const char*, const void*, size_t, void* userdata) {
        auto* entry = static_cast<std::pair<std::vector<int>*, int>*>(userdata);
        entry->first->push_back(entry->second);
    };
    std::pair<std::vector<int>*, int> one{&order, 1};
    std::pair<std::vector<int>*, int> two{&order, 2};
    ASSERT_TRUE(Messaging::Listen("combat", mark, &one));
    ASSERT_TRUE(Messaging::Listen("combat", mark, &two));

    Messaging::Send("combat", nullptr, 0);

    EXPECT_EQ(order, (std::vector<int>{1, 2}));
}

TEST_F(MessagingTest, AnEmptyChannelOrNoCallbackIsRefused) {
    Heard heard;
    EXPECT_FALSE(Messaging::Listen("", &Record, &heard));
    EXPECT_FALSE(Messaging::Listen(nullptr, &Record, &heard));
    EXPECT_FALSE(Messaging::Listen("combat", nullptr, &heard));
    EXPECT_EQ(Messaging::Send("combat", "x", 1), 0u);
}

// A listener that passes each message on to a second channel, and listens once more as it does.
Heard g_relayed;
void Relay(const char*, const void* data, size_t size, void*) {
    Messaging::Listen("combat", &Record, &g_relayed);
    Messaging::Send("relayed", data, size);
}

TEST_F(MessagingTest, AListenerCanSendAndListenInTurn) {
    g_relayed = {};
    Heard heard;
    ASSERT_TRUE(Messaging::Listen("combat", &Relay, nullptr));
    ASSERT_TRUE(Messaging::Listen("relayed", &Record, &heard));

    EXPECT_EQ(Messaging::Send("combat", "hit", 3), 1u);

    ASSERT_EQ(heard.texts.size(), 1u);
    EXPECT_EQ(heard.texts[0], "hit");
    EXPECT_TRUE(g_relayed.texts.empty());
    EXPECT_EQ(Messaging::Send("combat", "miss", 4), 2u);
    EXPECT_EQ(g_relayed.texts.size(), 1u);
}

TEST_F(MessagingTest, SendsFromThreadsWhileAnotherListens) {
    std::atomic<int> count = 0;
    const auto tally = [](const char*, const void*, size_t, void* userdata) {
        ++*static_cast<std::atomic<int>*>(userdata);
    };
    ASSERT_TRUE(Messaging::Listen("combat", tally, &count));

    constexpr int kSends = 2000;
    const auto sendAll = [] {
        for (int i = 0; i < kSends; ++i) {
            Messaging::Send("combat", nullptr, 0);
        }
    };
    std::thread first(sendAll);
    std::thread second(sendAll);
    std::atomic<int> late = 0;
    for (int i = 0; i < 50; ++i) {
        Messaging::Listen("other", tally, &late);
    }
    first.join();
    second.join();

    EXPECT_EQ(count, 2 * kSends);
}

}
