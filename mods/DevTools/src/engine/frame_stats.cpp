// A frame is timed from one Present to the next on the CPU, and between two GPU timestamps on the
// GPU: one issued as Present returns, one as the next is entered, before the overlay draws. So the
// overlay is never in the GPU's time, and while the CPU is the bottleneck the GPU's time includes
// what it spent waiting for commands.
//
// Timestamps are read a few frames after they are issued, never waited on: a set still in flight
// when its turn comes round again is dropped rather than stalled for.
#include "engine/frame_stats.h"

#include "fcse_api.h"

#include <array>
#include <cmath>
#include <cstdint>
#include <d3d9.h>
#include <windows.h>

namespace {
    using DevTools::FrameStats::History;
    using DevTools::FrameStats::kUnmeasured;

    constexpr int kHistory = 512;
    constexpr size_t kQuerySets = 4;

    struct QuerySet {
        IDirect3DQuery9* disjoint = nullptr;
        IDirect3DQuery9* frequency = nullptr;
        IDirect3DQuery9* begin = nullptr;
        IDirect3DQuery9* end = nullptr;
        // Open between its begin and end timestamps, pending until it has been read.
        enum class State { Idle, Open, Pending } state = State::Idle;
    };

    struct Series {
        std::array<float, kHistory> values{};
        int next = 0;

        void Push(float value) {
            values[next] = value;
            next = (next + 1) % kHistory;
        }

        History View() const { return {values.data(), kHistory, next}; }
    };

    std::array<QuerySet, kQuerySets> g_sets;
    // The set the next frame begins, which is also the oldest one still pending.
    size_t g_current = 0;
    bool g_queriesBuilt = false;
    bool g_unsupported = false;

    LARGE_INTEGER g_ticksPerSecond{};
    LARGE_INTEGER g_lastEntry{};

    Series g_frames;
    Series g_gpu;
    DevTools::FrameStats::Display g_display;

    void Release(IDirect3DQuery9*& query) {
        if (query != nullptr) {
            query->Release();
            query = nullptr;
        }
    }

    void ReleaseQueries() {
        for (QuerySet& set : g_sets) {
            Release(set.disjoint);
            Release(set.frequency);
            Release(set.begin);
            Release(set.end);
            set = {};
        }
        g_current = 0;
        g_queriesBuilt = false;
    }

    void BuildQueries(IDirect3DDevice9* device) {
        for (QuerySet& set : g_sets) {
            if (FAILED(device->CreateQuery(D3DQUERYTYPE_TIMESTAMPDISJOINT, &set.disjoint)) ||
                FAILED(device->CreateQuery(D3DQUERYTYPE_TIMESTAMPFREQ, &set.frequency)) ||
                FAILED(device->CreateQuery(D3DQUERYTYPE_TIMESTAMP, &set.begin)) ||
                FAILED(device->CreateQuery(D3DQUERYTYPE_TIMESTAMP, &set.end))) {
                ReleaseQueries();
                g_unsupported = true;
                FCSE::ApiPointer()->Log("frame stats: the device has no timestamp queries - the "
                                        "GPU is not timed");
                return;
            }
        }
        g_queriesBuilt = true;

        static bool logged = false;
        if (!logged) {
            logged = true;
            FCSE::ApiPointer()->Log("frame stats: timing the GPU with timestamp queries");
        }
    }

    void LoadDisplay(IDirect3DDevice9* device) {
        IDirect3DSwapChain9* chain = nullptr;
        if (FAILED(device->GetSwapChain(0, &chain))) {
            return;
        }
        D3DPRESENT_PARAMETERS params{};
        if (SUCCEEDED(chain->GetPresentParameters(&params))) {
            g_display = {true, params.BackBufferWidth, params.BackBufferHeight,
                         params.PresentationInterval != D3DPRESENT_INTERVAL_IMMEDIATE};
        }
        chain->Release();
    }

    template <typename T> bool Ready(IDirect3DQuery9* query, T& value) {
        return query->GetData(&value, sizeof(value), 0) == S_OK;
    }

    // The GPU time of the oldest pending set, NaN when it is not ready or was disjoint. Newer sets
    // wait behind it, so each is read in the order it was issued.
    float ReadOldest() {
        for (size_t step = 0; step < kQuerySets; ++step) {
            QuerySet& set = g_sets[(g_current + step) % kQuerySets];
            if (set.state != QuerySet::State::Pending) {
                continue;
            }

            BOOL disjoint = TRUE;
            UINT64 frequency = 0;
            UINT64 begin = 0;
            UINT64 end = 0;
            if (!Ready(set.end, end) || !Ready(set.begin, begin) ||
                !Ready(set.frequency, frequency) || !Ready(set.disjoint, disjoint)) {
                return kUnmeasured;
            }

            set.state = QuerySet::State::Idle;
            if (disjoint || frequency == 0 || end < begin) {
                return kUnmeasured;
            }
            return static_cast<float>(static_cast<double>(end - begin) * 1000.0 /
                                      static_cast<double>(frequency));
        }
        return kUnmeasured;
    }
}

namespace DevTools::FrameStats {

Frame Presenting() {
    if (g_ticksPerSecond.QuadPart == 0) {
        QueryPerformanceFrequency(&g_ticksPerSecond);
    }

    LARGE_INTEGER now{};
    QueryPerformanceCounter(&now);

    Frame frame{kUnmeasured, kUnmeasured};
    if (g_lastEntry.QuadPart != 0) {
        frame.ms = static_cast<float>(static_cast<double>(now.QuadPart - g_lastEntry.QuadPart) *
                                      1000.0 / static_cast<double>(g_ticksPerSecond.QuadPart));
        g_frames.Push(frame.ms);
    }
    g_lastEntry = now;

    QuerySet& set = g_sets[g_current];
    if (set.state == QuerySet::State::Open) {
        set.end->Issue(D3DISSUE_END);
        set.frequency->Issue(D3DISSUE_END);
        set.disjoint->Issue(D3DISSUE_END);
        set.state = QuerySet::State::Pending;
        g_current = (g_current + 1) % kQuerySets;
    }

    frame.gpuMs = ReadOldest();
    if (!std::isnan(frame.gpuMs)) {
        g_gpu.Push(frame.gpuMs);
    }
    return frame;
}

void Presented(IDirect3DDevice9* device) {
    if (!g_display.known) {
        LoadDisplay(device);
    }
    if (!g_queriesBuilt && !g_unsupported) {
        BuildQueries(device);
    }
    if (!g_queriesBuilt) {
        return;
    }

    QuerySet& set = g_sets[g_current];
    set.disjoint->Issue(D3DISSUE_BEGIN);
    set.begin->Issue(D3DISSUE_END);
    set.state = QuerySet::State::Open;
}

void DeviceLost() {
    ReleaseQueries();
    g_display.known = false;
}

History FrameTimes() { return g_frames.View(); }

History GpuTimes() { return g_gpu.View(); }

Display ReadDisplay() { return g_display; }

bool GpuTimed() { return !g_unsupported; }

}
