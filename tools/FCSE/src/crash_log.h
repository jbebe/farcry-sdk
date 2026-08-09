#pragma once

// Turns a crash-to-desktop into a line in bin\fcse.log naming the faulting instruction as
// `module+RVA`, plus registers and a scanned return-address chain.
//
// This exists because the alternative is guessing. FCSE reaches deep into Dunia.dll through
// hand-declared __thiscall signatures and byte offsets, and when one of those is wrong the game
// simply vanishes - the engine's own input dispatch has no FCSE frame on the stack to catch it, so
// the __try/__except wrappers in fcse_page.cpp see nothing. Every RVA logged here pastes straight
// into Ghidra or IDA and lands on the instruction that faulted, which is the one fact that static
// analysis of a wrong assumption can never produce.
//
// Purely an observer: the handler logs and then returns EXCEPTION_CONTINUE_SEARCH, so whatever
// would have happened still happens. It does not turn crashes into non-crashes, and it does not
// change the game's own crash handling.
namespace FCSE {

class CrashLog {
public:
    // Registers a vectored exception handler at the front of the chain. Call once, after Log::Init
    // so there is somewhere to write, and before any engine code runs. Never fatal; logs and
    // carries on if registration fails.
    static void Install();

    static void Shutdown();
};

} // namespace FCSE
