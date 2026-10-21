// The player's pawn: its data, and the flags in it for what it wants and what it is doing.
#pragma once

#include "engine/memory.h"

#include <cstddef>
#include <cstdint>

namespace AimingOverhaul::Pawn {

// CPawnInputListener's pawn; CPawn's data; and in the data the desired and effective flags.
inline constexpr ptrdiff_t kListenerPawn = 0x20;
inline constexpr ptrdiff_t kData = 0x10;
inline constexpr ptrdiff_t kDesiredFlags = 0x144;
inline constexpr ptrdiff_t kEffectiveFlags = 0x2D4;
inline constexpr uint8_t kIronsight = 0x08;
inline constexpr uint8_t kSprint = 0x40;

inline uint8_t* Data(uint8_t* pawn) {
    return Field<uint8_t*>(pawn, kData);
}

inline bool Is(uint8_t* pawn, uint8_t flag) {
    return (Field<uint8_t>(Data(pawn), kEffectiveFlags) & flag) != 0;
}

// Lets go of the sights the player asked for, as releasing the aim key does.
inline void DropSights(uint8_t* pawn) {
    Field<uint8_t>(Data(pawn), kDesiredFlags) &= ~kIronsight;
}

}
