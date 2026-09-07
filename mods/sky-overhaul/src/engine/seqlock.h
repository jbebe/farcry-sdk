// A snapshot one thread publishes and another reads, with neither ever waiting on the other.
#pragma once

#include <atomic>
#include <cstdint>

namespace SkyOverhaul {

// An odd sequence means a write is in flight. A reader that keeps finding one gives up rather than
// spinning: a frame without a snapshot costs less than a stalled render thread.
template <class T>
class Seqlock {
public:
    void Publish(const T& value) {
        const uint32_t sequence = m_sequence.load(std::memory_order_relaxed);
        m_sequence.store(sequence + 1, std::memory_order_relaxed);
        std::atomic_thread_fence(std::memory_order_release);
        m_value = value;
        m_sequence.store(sequence + 2, std::memory_order_release);
    }

    // False until the first publish, and on a read a publish tore.
    bool Latest(T& out) const {
        for (int attempt = 0; attempt < 8; attempt++) {
            const uint32_t before = m_sequence.load(std::memory_order_acquire);
            if (before == 0 || (before & 1u) != 0) {
                continue;
            }

            const T copy = m_value;
            std::atomic_thread_fence(std::memory_order_acquire);
            if (m_sequence.load(std::memory_order_relaxed) == before) {
                out = copy;
                return true;
            }
        }
        return false;
    }

private:
    std::atomic<uint32_t> m_sequence{0};
    T m_value{};
};

}
