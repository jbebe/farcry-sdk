#include "engine/vertex_patch.h"

#include <algorithm>
#include <iterator>

namespace {
    constexpr DWORD kEnd = 0x0000FFFF;
    constexpr DWORD kComment = 0xFFFE;
    constexpr DWORD kMov = 1;
    constexpr DWORD kDp4 = 9;
    constexpr DWORD kDcl = 31;
    constexpr DWORD kDef = 81;
    constexpr DWORD kDefi = 82;
    constexpr DWORD kDefb = 83;

    constexpr DWORD kTemp = 0;
    constexpr DWORD kOutput = 6;
    constexpr DWORD kTexcoordUsage = 5;
    constexpr UINT kOutputRegisters = 12;
    constexpr UINT kTempRegisters = 32;

    constexpr DWORD kParameter = 0x80000000u;
    constexpr DWORD kIdentitySwizzle = 0xE4u << 16;
    // A register token's type and number, without its relative-addressing flag.
    constexpr DWORD kRegisterBits = 0x70001FFFu;

    DWORD Type(DWORD token) {
        return ((token >> 28) & 7) | ((token >> 8) & 0x18);
    }

    UINT Number(DWORD token) {
        return token & 0x7FF;
    }

    DWORD Destination(DWORD type, UINT number, DWORD mask) {
        return kParameter | ((type & 7) << 28) | ((type & 0x18) << 8) | (mask << 16) | number;
    }
}

std::vector<DWORD> SkyOverhaul::VertexPatch::WithPosition(const DWORD* tokens, size_t count,
                                                          UINT semantic) {
    size_t afterDeclarations = 0;
    size_t afterProjection = 0;
    size_t end = 0;
    int projections = 0;
    DWORD projected = 0;
    bool outputUsed[kOutputRegisters] = {};
    int target = -1;
    DWORD targetMask = 0xF;
    UINT freeTemp = 0;

    size_t i = 1;
    while (i < count) {
        const DWORD token = tokens[i];
        if (token == kEnd) {
            end = i;
            break;
        }
        const DWORD opcode = token & 0xFFFF;
        if (opcode == kComment) {
            i += 1 + ((token >> 16) & 0x7FFF);
            continue;
        }
        const size_t length = (token >> 24) & 0xF;
        const DWORD* parameters = tokens + i + 1;
        const size_t next = i + 1 + length;
        if (next > count) {
            return {};
        }

        if (opcode == kDcl && length == 2) {
            afterDeclarations = next;
            if (Type(parameters[1]) == kOutput && Number(parameters[1]) < kOutputRegisters) {
                outputUsed[Number(parameters[1])] = true;
                const DWORD usage = parameters[0] & 0x1F;
                const UINT index = (parameters[0] >> 16) & 0xF;
                if (usage == kTexcoordUsage && index == semantic) {
                    target = static_cast<int>(Number(parameters[1]));
                    targetMask = (parameters[1] >> 16) & 0xF;
                }
            }
        } else {
            // A def's values are floats, not registers.
            const size_t registers = opcode == kDef || opcode == kDefi || opcode == kDefb
                                         ? (length > 0 ? 1 : 0)
                                         : length;
            for (size_t p = 0; p < registers; p++) {
                if (Type(parameters[p]) == kTemp) {
                    freeTemp = (std::max)(freeTemp, Number(parameters[p]) + 1);
                }
            }
            if (opcode == kDp4 && length == 3 && Type(parameters[0]) == kOutput &&
                Number(parameters[0]) == 0) {
                const DWORD source = parameters[1] & kRegisterBits;
                if (projections > 0 && source != projected) {
                    return {};
                }
                projected = source;
                projections++;
                afterProjection = next;
            }
        }
        i = next;
    }

    if (end == 0 || projections != 4 || Type(projected) != kTemp || freeTemp >= kTempRegisters) {
        return {};
    }

    std::vector<DWORD> declaration;
    if (target < 0) {
        for (UINT o = 0; o < kOutputRegisters && target < 0; o++) {
            if (!outputUsed[o]) {
                target = static_cast<int>(o);
            }
        }
        if (target < 0) {
            return {};
        }
        declaration = {(2u << 24) | kDcl, kParameter | (semantic << 16) | kTexcoordUsage,
                       Destination(kOutput, static_cast<UINT>(target), 0xF)};
    }
    const DWORD copy[] = {(2u << 24) | kMov, Destination(kTemp, freeTemp, 0xF),
                          kParameter | kIdentitySwizzle | projected};
    const DWORD write[] = {(2u << 24) | kMov,
                           Destination(kOutput, static_cast<UINT>(target), targetMask),
                           kParameter | kIdentitySwizzle | freeTemp};

    std::vector<DWORD> patched(tokens, tokens + afterDeclarations);
    patched.insert(patched.end(), declaration.begin(), declaration.end());
    patched.insert(patched.end(), tokens + afterDeclarations, tokens + afterProjection);
    patched.insert(patched.end(), std::begin(copy), std::end(copy));
    patched.insert(patched.end(), tokens + afterProjection, tokens + end);
    patched.insert(patched.end(), std::begin(write), std::end(write));
    patched.push_back(kEnd);
    return patched;
}
