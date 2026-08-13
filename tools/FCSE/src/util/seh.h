#pragma once

#include <type_traits>
#include <windows.h>

// SEH guards for the calls that reach into engine memory. A wrong offset or a stale address faults
// rather than misbehaving, and a fault inside a menu click would otherwise take the process down
// with nothing in the log.
//
// Everything here takes its arguments by value and requires them to be trivially destructible:
// MSVC refuses to compile __try in a function that also needs C++ unwinding, which is the whole
// reason these are separate functions rather than a block around each call site.
namespace FCSE {

// Calls `fn`, discarding whatever it returns. False if it faulted, with the code in `outCode`.
template <typename Fn, typename... Args>
bool SehCall(DWORD* outCode, Fn fn, Args... args) {
    static_assert((std::is_trivially_destructible_v<Args> && ...),
                  "an SEH-guarded call cannot take an argument that needs unwinding");
    __try {
        fn(args...);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        *outCode = GetExceptionCode();
        return false;
    }
}

// The same, keeping the return value.
template <typename Ret, typename Fn, typename... Args>
bool SehCallRet(DWORD* outCode, Ret* outResult, Fn fn, Args... args) {
    static_assert((std::is_trivially_destructible_v<Args> && ...),
                  "an SEH-guarded call cannot take an argument that needs unwinding");
    __try {
        *outResult = fn(args...);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        *outCode = GetExceptionCode();
        return false;
    }
}

// Reads a pointer field out of an engine object.
inline bool SehReadPointer(void* base, ptrdiff_t offset, void** outValue, DWORD* outCode) {
    __try {
        *outValue = *reinterpret_cast<void**>(reinterpret_cast<char*>(base) + offset);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        *outCode = GetExceptionCode();
        return false;
    }
}

inline bool SehWritePointer(void* base, ptrdiff_t offset, void* value, DWORD* outCode) {
    __try {
        *reinterpret_cast<void**>(reinterpret_cast<char*>(base) + offset) = value;
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        *outCode = GetExceptionCode();
        return false;
    }
}

inline bool SehWriteByte(void* base, ptrdiff_t offset, unsigned char value, DWORD* outCode) {
    __try {
        *(reinterpret_cast<unsigned char*>(base) + offset) = value;
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        *outCode = GetExceptionCode();
        return false;
    }
}

}
