// The engine leaves both processors and the GPU idling.
//
// Ported from FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) - source/system/utilisation.ixx.
//
// Four separate things, all of them the engine being sized for 2008 hardware and none of them
// visible in a frame - which is exactly why they were never noticed as defects:
//
//   - The job pool asks CThreadingConfig for JOB_THREADS and gets a number from a file written for
//     two-core machines, so most of the processors sit out the frame.
//   - A worker whose dequeue comes back empty retries immediately, so an idle pool spends its time
//     in the queue's critical section rather than out of the way.
//   - The GPU fence ring is sized one frame per GPU, so the CPU can never run ahead.
//   - With a frame cap set, the limiter busy-waits on the render thread, burning a whole core to
//     wait for a deadline it could have slept on.
//
// The last is the one worth stating plainly: with a cap set, a core is pinned for no reason at all.
//
// Everything here is a floor rather than an override. The thread count is never lowered below what
// the config asked for, so OverrideThreadingConfig.xml still wins; the ring is never made shallower
// than the engine wanted, nor deeper than it will build; and the limiter still lands on its own
// deadline, having slept out all but a margin.
#include "fcse_api.h"

#include "engine/memory_probe.h"

#include <windows.h>

#include <intrin.h>

#include <algorithm>
#include <bit>
#include <cstdint>
#include <cstring>

namespace {
    // Held back for the main and render threads, which help the queue rather than blocking on it.
    constexpr int32_t kReservedProcessors = 2;

    // Past six workers the extra threads live in the queue's critical section.
    constexpr int32_t kMinWorkers = 1;
    constexpr int32_t kMaxWorkers = 6;

    // Frames of GPU work the CPU may queue ahead, and the engine's own ceiling: above five fences
    // per GPU it rebuilds the ring's queries every frame.
    constexpr int32_t kFrameQueueDepth = 3;
    constexpr int32_t kFrameQueueCeilingPerGpu = 5;

    // The GPU count comes from a virtual call, so bound it before multiplying.
    constexpr int32_t kMaximumGpus = 8;

    // Worker back-off in iterations, not time: the loop body is a critical-section round trip.
    constexpr uint32_t kPauseIterations = 64;
    constexpr uint32_t kPausesPerIteration = 8;

    // Visits further apart than this are separate episodes, and the ramp restarts.
    constexpr int64_t kEpisodeGapMicroseconds = 1000;

    // The renderer, as the frame limiter sees it in ESI.
    constexpr uint32_t kRendererSettings = 0x2C;
    constexpr uint32_t kRendererFrameCounter = 0x338;

    // gfx_MaxFps in CRenderSettings. The limiter clamps below 1 and skips itself at 1000 up.
    constexpr uint32_t kSettingsMaxFps = 0xC8;
    constexpr int32_t kLimiterDisabledFps = 1000;

    // Left for the loop to spin, one per timer accuracy; the third is a stock 15.6 ms tick plus
    // room.
    constexpr int64_t kHighResolutionMarginMicroseconds = 400;
    constexpr int64_t kPlainMarginMicroseconds = 2000;
    constexpr int64_t kCoarseMarginMicroseconds = 20000;

    // A deadline further out than this is spun rather than slept.
    constexpr int64_t kMaximumWaitMicroseconds = 200000;

#ifndef CREATE_WAITABLE_TIMER_HIGH_RESOLUTION
#define CREATE_WAITABLE_TIMER_HIGH_RESOLUTION 0x00000002
#endif

    using ThreadCountFn = int32_t(__fastcall*)(void* self, void* unused, const char* name);

    // The quality table asks for JOB_THREADS too, so the pool's call is told apart by where it
    // returns to.
    uintptr_t g_jobPoolReturnAddress = 0;

    // Thresholds in counter ticks: the hot paths compare rather than divide.
    int64_t g_counterFrequency = 0;
    int64_t g_episodeGapTicks = 0;
    int64_t g_minimumWaitTicks = 0;
    int64_t g_maximumWaitTicks = 0;

    ThreadCountFn g_originalThreadCount = nullptr;

    FCSE::Relocation<uint8_t*> g_jobThreadsSite{
        FCSE::Pattern("8B 0D ?? ?? ?? ?? 68 ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B F0 3B F7 89 74 24 10 7E")};

    FCSE::Relocation<ThreadCountFn> g_threadCount{
        FCSE::Pattern("83 EC 24 53 56 57 8D 44 24 0F 8B F9 50 8D 4C 24 1C 33 DB 51 C7 44 24 1C ?? "
                      "?? ?? ?? C7 44 24 34 0F 00 00 00 88 5C 24 17")};

    FCSE::Relocation<uint8_t*> g_workerStarved{
        FCSE::Pattern("8B CE E8 ?? ?? ?? ?? 8B F8 85 FF 75 0D 8B 96 E0 00 00 00 38 42 14 74 E8")};

    FCSE::Relocation<uint8_t*> g_frameQueue{
        FCSE::Pattern("FF D0 EB 02 33 C0 39 46 34 74 0A 8B CE 89 46 34 E8 ?? ?? ?? ?? E8 ?? ?? ?? "
                      "?? 8B 4E 34 85 C9")};

    FCSE::Relocation<uint8_t*> g_frameLimiter{
        FCSE::Pattern("DF F1 DD D8 76 ?? E8 ?? ?? ?? ?? 8B C8 8B F9 2B BE 38 03 00 00 8B C2 1B 86 "
                      "3C 03 00 00")};

    // The pushed name is wildcarded, so the site is confirmed by what it names.
    constexpr ptrdiff_t kJobThreadsName = 7;
    constexpr ptrdiff_t kJobThreadsReturn = 0x10;

    // The shutdown-flag re-read, the only instruction on the empty-handed path.
    constexpr ptrdiff_t kWorkerStarvedRetry = 0x0D;

    // The ring size comparison, and the limiter's counter call.
    constexpr ptrdiff_t kFrameQueueComparison = 6;
    constexpr ptrdiff_t kFrameLimiterCounter = 6;

    int64_t Counter() {
        LARGE_INTEGER now = {};
        QueryPerformanceCounter(&now);
        return now.QuadPart;
    }

    int64_t ToTicks(int64_t microseconds) {
        return (g_counterFrequency * microseconds) / 1000000;
    }

    // The input is bounded by kMaximumWaitTicks, so the multiply cannot overflow.
    int64_t ToMicroseconds(int64_t ticks) {
        return (ticks * 1000000) / g_counterFrequency;
    }

    // The affinity mask rather than GetSystemInfo, so the processor affinity option narrows the
    // pool along with everything else.
    int32_t WorkerThreadCount() {
        DWORD_PTR processMask = 0;
        DWORD_PTR systemMask = 0;
        int32_t processors = 0;

        if (GetProcessAffinityMask(GetCurrentProcess(), &processMask, &systemMask)) {
            processors = static_cast<int32_t>(std::popcount(processMask));
        }

        if (processors <= 0) {
            SYSTEM_INFO info = {};
            GetSystemInfo(&info);
            processors = static_cast<int32_t>(info.dwNumberOfProcessors);
        }

        return std::clamp(processors - kReservedProcessors, kMinWorkers, kMaxWorkers);
    }

    int32_t __fastcall ThreadCountDetour(void* self, void* unused, const char* name) {
        const uintptr_t returnAddress = reinterpret_cast<uintptr_t>(_ReturnAddress());
        const int32_t stock = g_originalThreadCount(self, unused, name);

        if (returnAddress != g_jobPoolReturnAddress) {
            return stock;
        }

        const int32_t wanted = WorkerThreadCount();
        return wanted > stock ? wanted : stock;
    }

    // The worker's dequeue retry, on the turn the pop came back empty. State is per thread, and the
    // context is left alone so AL stays zero for the compare the loop resumes on.
    void WorkerStarvedHandler(FCSE_MidHookContext* /*ctx*/) {
        static thread_local uint32_t iterations = 0;
        static thread_local int64_t previousVisit = 0;

        if (g_counterFrequency <= 0) {
            return;
        }

        const int64_t visit = Counter();
        if (visit - previousVisit > g_episodeGapTicks) {
            iterations = 0;
        }

        if (++iterations < kPauseIterations) {
            for (uint32_t i = 0; i < kPausesPerIteration; ++i) {
                YieldProcessor();
            }
            // The pauses are bounded and short, so this visit's own reading still measures the loop
            // body rather than the wait.
            previousVisit = visit;
            return;
        }

        // SwitchToThread rather than Sleep(0): it considers threads below this one's priority. Read
        // again afterwards, so the next gap measures the loop body and not the scheduler.
        SwitchToThread();
        previousVisit = Counter();
    }

    // At the ring size comparison, with EAX the GPU count. Zero is the D3D10 path, fences off.
    void FrameQueueHandler(FCSE_MidHookContext* ctx) {
        const int32_t gpus = static_cast<int32_t>(ctx->eax);
        if (gpus <= 0 || gpus > kMaximumGpus) {
            return;
        }

        ctx->eax = static_cast<uintptr_t>(
            std::clamp(kFrameQueueDepth, gpus, gpus * kFrameQueueCeilingPerGpu));
    }

    // One timer per limiter thread. INVALID_HANDLE_VALUE means not tried and null means
    // unavailable, so the create runs once. Never closed: the thread outlives the module.
    HANDLE WaitableTimer(int64_t& margin) {
        static thread_local HANDLE timer = INVALID_HANDLE_VALUE;
        static thread_local int64_t timerMargin = kCoarseMarginMicroseconds;

        if (timer == INVALID_HANDLE_VALUE) {
            timer = CreateWaitableTimerExW(nullptr, nullptr,
                                           CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,
                                           TIMER_ALL_ACCESS);
            timerMargin = kHighResolutionMarginMicroseconds;

            // The flag is rejected before Windows 10 1803, where a plain timer rounds to the global
            // timer period instead.
            if (timer == nullptr) {
                timer = CreateWaitableTimerExW(nullptr, nullptr, 0, TIMER_ALL_ACCESS);
                timerMargin = kPlainMarginMicroseconds;
            }

            if (timer == nullptr) {
                timerMargin = kCoarseMarginMicroseconds;
            }
        }

        margin = timerMargin;
        return timer;
    }

    // Sleeps out all but the margin, leaving the loop to land on the deadline itself.
    void Wait(int64_t remainingMicroseconds) {
        int64_t margin = kCoarseMarginMicroseconds;
        HANDLE timer = WaitableTimer(margin);

        const int64_t wait = remainingMicroseconds - margin;
        if (wait <= 0) {
            return;
        }

        if (timer != nullptr) {
            LARGE_INTEGER due = {};
            // Relative, in 100ns units.
            due.QuadPart = -(wait * 10);

            if (SetWaitableTimer(timer, &due, 0, nullptr, nullptr, FALSE)) {
                WaitForSingleObject(timer, INFINITE);
                return;
            }
        }

        Sleep(static_cast<DWORD>(wait / 1000));
    }

    // In the limiter's spin, just before its QueryPerformanceCounter. ESI is the renderer and is
    // not reloaded across the loop.
    void FrameLimiterHandler(FCSE_MidHookContext* ctx) {
        if (g_counterFrequency <= 0) {
            return;
        }

        const uint8_t* renderer = reinterpret_cast<const uint8_t*>(ctx->esi);
        if (renderer == nullptr) {
            return;
        }

        const uint8_t* settings =
            *reinterpret_cast<const uint8_t* const*>(renderer + kRendererSettings);
        if (settings == nullptr) {
            return;
        }

        const int32_t maxFps = *reinterpret_cast<const int32_t*>(settings + kSettingsMaxFps);
        if (maxFps < 1 || maxFps >= kLimiterDisabledFps) {
            return;
        }

        // Written after the loop, so during it this is the previous frame's release point.
        const int64_t previousFrame =
            *reinterpret_cast<const int64_t*>(renderer + kRendererFrameCounter);
        if (previousFrame <= 0) {
            return;
        }

        const int64_t remaining = (previousFrame + g_counterFrequency / maxFps) - Counter();
        if (remaining <= g_minimumWaitTicks || remaining > g_maximumWaitTicks) {
            return;
        }

        Wait(ToMicroseconds(remaining));
    }
}

void ApplyUtilisationFix() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    // Zero disables the two clock-paced hooks and leaves the other two working.
    LARGE_INTEGER frequency = {};
    if (QueryPerformanceFrequency(&frequency) && frequency.QuadPart > 0) {
        g_counterFrequency = frequency.QuadPart;
        g_episodeGapTicks = ToTicks(kEpisodeGapMicroseconds);
        g_minimumWaitTicks = ToTicks(kHighResolutionMarginMicroseconds);
        g_maximumWaitTicks = ToTicks(kMaximumWaitMicroseconds);
    }

    if (g_jobThreadsSite) {
        const char* const* name =
            reinterpret_cast<const char* const*>(g_jobThreadsSite.address() + kJobThreadsName);
        if (UFCP::IsInDunia(*name) && std::strcmp(*name, "JOB_THREADS") == 0) {
            g_jobPoolReturnAddress = g_jobThreadsSite.address() + kJobThreadsReturn;
        }
    }

    if (g_threadCount && g_jobPoolReturnAddress != 0) {
        api->Hook(reinterpret_cast<void*>(g_threadCount.address()),
                  reinterpret_cast<void*>(&ThreadCountDetour),
                  reinterpret_cast<void**>(&g_originalThreadCount));
    }

    if (g_workerStarved) {
        api->MidHook(reinterpret_cast<void*>(g_workerStarved.address() + kWorkerStarvedRetry),
                     &WorkerStarvedHandler);
    }

    if (g_frameQueue) {
        api->MidHook(reinterpret_cast<void*>(g_frameQueue.address() + kFrameQueueComparison),
                     &FrameQueueHandler);
    }

    if (g_frameLimiter) {
        api->MidHook(reinterpret_cast<void*>(g_frameLimiter.address() + kFrameLimiterCounter),
                     &FrameLimiterHandler);
    }

    api->Log("utilisation improved");
}
