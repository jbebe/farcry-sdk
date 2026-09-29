// The engine's own benchmark, run with -benchmark on the command line: whether it is counting frames
// right now, and when it has finished a loop.
#pragma once

#include <string>

namespace DevTools::Benchmark {

// Called on the engine's thread each time the benchmark has written a loop's report.
using ReportFn = void (*)();

// Hooks the benchmark's frame collector. A site not found is logged, and nothing is measured.
void Install(ReportFn onReport);

// True while the benchmark counts frames: from the end of its warm-up to its report. Safe from any
// thread.
bool Measuring();

// -benchmarkid as passed on the command line, or empty.
const std::string& Id();

}
