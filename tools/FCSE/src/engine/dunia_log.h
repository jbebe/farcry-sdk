#pragma once

// Routes the engine's own output into bin\Dunia.log. Retail Dunia.dll still receives every
// System:Log call, writes console lines, formats every script error, and prints to stdout, stderr
// and OutputDebugStringA - but with nowhere for any of it to go. Each sink
// is intercepted at one point; which points, and what reaches them, is in
// docs/docs/engine-internals/engine-logging.md.
namespace FCSE {

class DuniaLog {
public:
    // Installs the hooks and import detours. After AddressLibrary::Init. A sink missing on this
    // build is logged and skipped; the others still install.
    static void Install();

    // Writes out any partial stdout/stderr line and puts the import slots back.
    static void Shutdown();
};

} // namespace FCSE
