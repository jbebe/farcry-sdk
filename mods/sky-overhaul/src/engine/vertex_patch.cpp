#include "engine/vertex_patch.h"

#include <algorithm>
#include <cstring>
#include <iterator>

namespace {
    constexpr DWORD kEnd = 0x0000FFFF;
    constexpr DWORD kComment = 0xFFFE;
    constexpr DWORD kMov = 1;
    constexpr DWORD kAdd = 2;
    constexpr DWORD kMad = 4;
    constexpr DWORD kMul = 5;
    constexpr DWORD kRcp = 6;
    constexpr DWORD kRsq = 7;
    constexpr DWORD kDp4 = 9;
    constexpr DWORD kDcl = 31;
    constexpr DWORD kDef = 81;
    constexpr DWORD kDefi = 82;
    constexpr DWORD kDefb = 83;

    constexpr DWORD kTemp = 0;
    constexpr DWORD kConst = 2;
    constexpr DWORD kOutput = 6;
    constexpr DWORD kTexcoordUsage = 5;
    constexpr UINT kOutputRegisters = 12;
    constexpr UINT kTempRegisters = 32;
    constexpr UINT kConstRegisters = 256;

    constexpr DWORD kParameter = 0x80000000u;
    constexpr DWORD kRelative = 0x00002000u;
    constexpr DWORD kNegate = 1u << 24;
    constexpr DWORD kSaturate = 1u << 20;
    // A register token's type and number, without its relative-addressing flag.
    constexpr DWORD kRegisterBits = 0x70001FFFu;

    constexpr DWORD kXyzw = 0xE4u;
    constexpr DWORD kXxxx = 0x00u;
    constexpr DWORD kYyyy = 0x55u;
    constexpr DWORD kZzzz = 0xAAu;
    constexpr DWORD kWwww = 0xFFu;

    // The register the engine binds the camera's position to, for every shader in the frame.
    constexpr UINT kCameraPosition = 45;

    DWORD Type(DWORD token) {
        return ((token >> 28) & 7) | ((token >> 8) & 0x18);
    }

    UINT Number(DWORD token) {
        return token & 0x7FF;
    }

    DWORD Destination(DWORD type, UINT number, DWORD mask, DWORD modifier = 0) {
        return kParameter | ((type & 7) << 28) | ((type & 0x18) << 8) | (mask << 16) | modifier |
               number;
    }

    DWORD Source(DWORD type, UINT number, DWORD swizzle = kXyzw, DWORD modifier = 0) {
        return kParameter | ((type & 7) << 28) | ((type & 0x18) << 8) | (swizzle << 16) | modifier |
               number;
    }

    DWORD Instruction(DWORD opcode, DWORD length) {
        return (length << 24) | opcode;
    }

    // Where a shader writes its clip position: four dp4 of one temp into o0.
    struct Scan {
        size_t afterDeclarations = 0;
        size_t firstProjection = 0;
        size_t afterProjection = 0;
        size_t end = 0;
        size_t projections[4] = {};
        int count = 0;
        DWORD projected = 0;
        // The lowest constant register the four read the matrix from, or -1 if not four in a row.
        int matrix = -1;
        UINT freeTemp = 0;
        bool outputUsed[kOutputRegisters] = {};
        bool constUsed[kConstRegisters] = {};
        bool relativeConst = false;
        int texcoordOutput[16];
        DWORD texcoordMask[16];
    };

    bool Read(const DWORD* tokens, size_t count, Scan& scan) {
        std::fill(std::begin(scan.texcoordOutput), std::end(scan.texcoordOutput), -1);
        int matrixMin = kConstRegisters;
        unsigned matrixRows = 0;
        size_t i = 1;
        while (i < count) {
            const DWORD token = tokens[i];
            if (token == kEnd) {
                scan.end = i;
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
                return false;
            }

            if (opcode == kDcl && length == 2) {
                scan.afterDeclarations = next;
                if (Type(parameters[1]) == kOutput && Number(parameters[1]) < kOutputRegisters) {
                    scan.outputUsed[Number(parameters[1])] = true;
                    const DWORD usage = parameters[0] & 0x1F;
                    const UINT index = (parameters[0] >> 16) & 0xF;
                    if (usage == kTexcoordUsage) {
                        scan.texcoordOutput[index] = static_cast<int>(Number(parameters[1]));
                        scan.texcoordMask[index] = (parameters[1] >> 16) & 0xF;
                    }
                }
            } else {
                // A def's values are floats, not registers.
                const size_t registers = opcode == kDef || opcode == kDefi || opcode == kDefb
                                             ? (length > 0 ? 1 : 0)
                                             : length;
                for (size_t p = 0; p < registers; p++) {
                    const DWORD type = Type(parameters[p]);
                    if (type == kTemp) {
                        scan.freeTemp = (std::max)(scan.freeTemp, Number(parameters[p]) + 1);
                    } else if (type == kConst && Number(parameters[p]) < kConstRegisters) {
                        scan.constUsed[Number(parameters[p])] = true;
                        scan.relativeConst |= (parameters[p] & kRelative) != 0;
                    }
                }
                if (opcode == kDp4 && length == 3 && Type(parameters[0]) == kOutput &&
                    Number(parameters[0]) == 0) {
                    const DWORD source = parameters[1] & kRegisterBits;
                    if (scan.count >= 4 || (scan.count > 0 && source != scan.projected)) {
                        return false;
                    }
                    if (scan.count == 0) {
                        scan.firstProjection = i;
                    }
                    scan.projected = source;
                    scan.projections[scan.count++] = i;
                    scan.afterProjection = next;
                    if (Type(parameters[2]) == kConst && (parameters[2] & kRelative) == 0) {
                        const int row = static_cast<int>(Number(parameters[2]));
                        matrixMin = (std::min)(matrixMin, row);
                        matrixRows |= 1u << (row & 31);
                    }
                }
            }
            i = next;
        }
        if (scan.end == 0 || scan.count != 4 || Type(scan.projected) != kTemp) {
            return false;
        }
        const unsigned rows = 0xFu << (matrixMin & 31);
        scan.matrix = matrixMin < static_cast<int>(kConstRegisters) - 3 && matrixRows == rows
                          ? matrixMin
                          : -1;
        return true;
    }
}

std::vector<DWORD> SkyOverhaul::VertexPatch::WithPosition(const DWORD* tokens, size_t count,
                                                          UINT semantic) {
    Scan scan;
    if (!Read(tokens, count, scan) || scan.freeTemp >= kTempRegisters) {
        return {};
    }

    int target = semantic < 16 ? scan.texcoordOutput[semantic] : -1;
    const DWORD targetMask = target >= 0 ? scan.texcoordMask[semantic] : 0xF;
    std::vector<DWORD> declaration;
    if (target < 0) {
        for (UINT o = 0; o < kOutputRegisters && target < 0; o++) {
            if (!scan.outputUsed[o]) {
                target = static_cast<int>(o);
            }
        }
        if (target < 0) {
            return {};
        }
        declaration = {Instruction(kDcl, 2), kParameter | (semantic << 16) | kTexcoordUsage,
                       Destination(kOutput, static_cast<UINT>(target), 0xF)};
    }
    const DWORD copy[] = {Instruction(kMov, 2), Destination(kTemp, scan.freeTemp, 0xF),
                          kParameter | (kXyzw << 16) | scan.projected};
    const DWORD write[] = {Instruction(kMov, 2),
                           Destination(kOutput, static_cast<UINT>(target), targetMask),
                           Source(kTemp, scan.freeTemp)};

    std::vector<DWORD> patched(tokens, tokens + scan.afterDeclarations);
    patched.insert(patched.end(), declaration.begin(), declaration.end());
    patched.insert(patched.end(), tokens + scan.afterDeclarations, tokens + scan.afterProjection);
    patched.insert(patched.end(), std::begin(copy), std::end(copy));
    patched.insert(patched.end(), tokens + scan.afterProjection, tokens + scan.end);
    patched.insert(patched.end(), std::begin(write), std::end(write));
    patched.push_back(kEnd);
    return patched;
}

std::vector<DWORD> SkyOverhaul::VertexPatch::Flattened(const DWORD* tokens, size_t count,
                                                       const Flatten& flatten) {
    Scan scan;
    if (!Read(tokens, count, scan) || scan.freeTemp + 2 > kTempRegisters ||
        (scan.matrix != kWorldMatrix && scan.matrix != kCameraMatrix)) {
        return {};
    }
    // A constant register the shader never reads, to hold the curve; the top one when it indexes
    // its constants and so might read any.
    UINT curve = kConstRegisters - 1;
    if (!scan.relativeConst) {
        while (curve > 0 && scan.constUsed[curve]) {
            curve--;
        }
    }
    if (scan.constUsed[curve] || curve == kCameraPosition) {
        return {};
    }

    const UINT position = scan.freeTemp;
    const UINT work = scan.freeTemp + 1;
    const bool world = scan.matrix == kWorldMatrix;
    const float range = (std::max)(flatten.end - flatten.start, 1.0f);
    const float values[4] = {1.0f / range, -flatten.start / range, flatten.stretch - 1.0f, 1.0f};
    DWORD bits[4];
    std::memcpy(bits, values, sizeof(bits));

    const DWORD def[] = {Instruction(kDef, 5), Destination(kConst, curve, 0xF), bits[0], bits[1],
                         bits[2], bits[3]};
    std::vector<DWORD> code = {Instruction(kMov, 2), Destination(kTemp, position, 0xF),
                               kParameter | (kXyzw << 16) | scan.projected};
    if (world) {
        code.insert(code.end(), {Instruction(kAdd, 3), Destination(kTemp, position, 0x7),
                                 Source(kTemp, position), Source(kConst, kCameraPosition, kXyzw,
                                                                 kNegate)});
    }
    // The distance across the ground, then how far toward the eye's height it is drawn.
    code.insert(code.end(), {
        Instruction(kMul, 3), Destination(kTemp, work, 0x3), Source(kTemp, position),
        Source(kTemp, position),
        Instruction(kAdd, 3), Destination(kTemp, work, 0x1), Source(kTemp, work, kXxxx),
        Source(kTemp, work, kYyyy),
        Instruction(kRsq, 2), Destination(kTemp, work, 0x2), Source(kTemp, work, kXxxx),
        Instruction(kRcp, 2), Destination(kTemp, work, 0x1), Source(kTemp, work, kYyyy),
        Instruction(kMad, 4), Destination(kTemp, work, 0x1, kSaturate), Source(kTemp, work, kXxxx),
        Source(kConst, curve, kXxxx), Source(kConst, curve, kYyyy),
        Instruction(kMul, 3), Destination(kTemp, work, 0x1), Source(kTemp, work, kXxxx),
        Source(kTemp, work, kXxxx),
        Instruction(kMad, 4), Destination(kTemp, work, 0x1), Source(kTemp, work, kXxxx),
        Source(kConst, curve, kZzzz), Source(kConst, curve, kWwww),
        Instruction(kRcp, 2), Destination(kTemp, work, 0x1), Source(kTemp, work, kXxxx),
        Instruction(kMul, 3), Destination(kTemp, position, 0x4), Source(kTemp, position),
        Source(kTemp, work, kXxxx)});
    if (world) {
        code.insert(code.end(), {Instruction(kAdd, 3), Destination(kTemp, position, 0x7),
                                 Source(kTemp, position), Source(kConst, kCameraPosition)});
    }

    // The def goes straight after the version token, ahead of every instruction.
    std::vector<DWORD> patched(tokens, tokens + 1);
    patched.insert(patched.end(), std::begin(def), std::end(def));
    patched.insert(patched.end(), tokens + 1, tokens + scan.firstProjection);
    patched.insert(patched.end(), code.begin(), code.end());
    const size_t offset = patched.size() - scan.firstProjection;
    patched.insert(patched.end(), tokens + scan.firstProjection, tokens + scan.end + 1);
    for (size_t projection : scan.projections) {
        DWORD& source = patched[projection + offset + 2];
        source = (source & ~kRegisterBits) | (Source(kTemp, position) & kRegisterBits);
    }
    return patched;
}
