#pragma once

namespace FCSE {

// The code address behind a pointer-to-member-function, for the hand-rolled vtables and detour
// thunks that hand a member function to the engine as a plain function pointer.
//
// Valid only for a class with no bases and no virtuals of its own, where MSVC represents the
// pointer as a single address - the static_assert is what holds that.
template <typename MemberFn>
void* RawFunctionPointer(MemberFn fn) {
    static_assert(sizeof(MemberFn) == sizeof(void*),
                  "member pointer is not a plain code address - the class has bases or virtuals");
    union {
        MemberFn member;
        void* raw;
    } converter;
    converter.member = fn;
    return converter.raw;
}

}
