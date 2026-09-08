// The options, as data.
//
// The same shape as src/commands/catalog.h and for the same reason: an option is described once, and
// src/main.cpp builds the FCSE settings rows from that description rather than repeating it. Every
// value is an int - a toggle is 0 or 1, a slider is its own range, a key is an index into
// Keys::ChoiceLabels - so one table covers all three kinds.
//
// FCSE owns the stored value and writes it to bin\fcse.ini, so `get` is what the option currently
// is, not where it is kept.
#pragma once

#include <span>

namespace DevTools::Options {

enum class Kind { Toggle, Slider, Key };

struct Option {
    const char* name;
    Kind kind;
    int defaultValue;
    int minValue;
    int maxValue;
    int (*get)();
    void (*set)(int value);
};

std::span<const Option> All();

}
