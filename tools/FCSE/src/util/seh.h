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

// Reads a scalar field out of an engine object.
template <typename T>
bool SehRead(void* base, ptrdiff_t offset, T* outValue, DWORD* outCode) {
    static_assert(std::is_trivially_copyable_v<T>, "an SEH-guarded read cannot need a constructor");
    __try {
        *outValue = *reinterpret_cast<const T*>(reinterpret_cast<const char*>(base) + offset);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        *outCode = GetExceptionCode();
        return false;
    }
}

template <typename T>
bool SehWrite(void* base, ptrdiff_t offset, T value, DWORD* outCode) {
    static_assert(std::is_trivially_copyable_v<T>, "an SEH-guarded write cannot need a destructor");
    __try {
        *reinterpret_cast<T*>(reinterpret_cast<char*>(base) + offset) = value;
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        *outCode = GetExceptionCode();
        return false;
    }
}

inline bool SehReadPointer(void* base, ptrdiff_t offset, void** outValue, DWORD* outCode) {
    return SehRead<void*>(base, offset, outValue, outCode);
}

inline bool SehWritePointer(void* base, ptrdiff_t offset, void* value, DWORD* outCode) {
    return SehWrite<void*>(base, offset, value, outCode);
}

inline bool SehWriteByte(void* base, ptrdiff_t offset, unsigned char value, DWORD* outCode) {
    return SehWrite<unsigned char>(base, offset, value, outCode);
}

}
