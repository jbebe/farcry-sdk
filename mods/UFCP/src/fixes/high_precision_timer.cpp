// Loading screens run far below the rate they were built for.
//
// Ported from FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) - the timer half of
// source/ui/loadingscreen.ixx.
//
// The loading screen animates on a sleep-driven loop rather than on the render loop, so its pacing
// is decided by how precisely Windows can wake a thread. The default timer resolution is around
// 15.6 ms, which is half of the 33 ms the screen is paced to, and every sleep overshoots to the
// next tick - so the screen visibly stutters at a fraction of its intended 30 FPS.
//
// Asking for 1 ms resolution costs nothing here and needs no engine knowledge at all, which is why
// this is one of the two features that work on any build. Windows drops the request when the
// process exits, so there is nothing to undo: timeEndPeriod exists to release the resolution early,
// and this wants it for the whole session.
#include "fcse_api.h"

#include <windows.h>

#include <timeapi.h>

void ApplyHighPrecisionTimerFix() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (timeBeginPeriod(1) != TIMERR_NOERROR) {
        api->Log("high precision timer: Windows refused a 1ms timer resolution - loading screens "
                 "keep their default pacing");
        return;
    }

    api->Log("high precision timer raised to 1ms");
}
