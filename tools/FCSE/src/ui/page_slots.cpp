#include "ui/page_internal.h"

#include "log.h"
#include "util/seh.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>

namespace FCSE {
namespace page {

    // Every cell of both banks, resolved once and cached, so the ones a display does not use can be
    // hidden. Indexed by row; null for anything that did not resolve.
    //
    // The layout authors 20 value cells and 20 slider cells at the *same* twenty row positions,
    // because a row's type is not known until a plugin registers. Without this, all forty draw at
    // once and every row shows a slider sitting on top of a spinner.
    //
    // Both banks are authored visible, and this only ever moves bit 0 of element+0x34 - the same bit
    // ShowElementNomad and HideElementNomad move. The opposite arrangement (author HIDDEN, reveal
    // from code) was tried and does not work: `HIDDEN` is bit *1* of that byte, magma's draw
    // collection skips any element with bit 1 set (`(flags & 2) == 0`, at 0x10ad3fb0), and
    // SetVisible cannot clear it - so the cell was never drawn and the engine dereferenced a null
    // the frame after it was "shown".
    namespace {

    struct SlotCells {
        void* value[kSlotCount];
        void* slider[kSlotCount];
        void* edit[kSlotCount];
    };

    SlotCells g_slotCells{};
    bool g_slotCellsCached = false;

    bool MakeNarrowString(NarrowString* out, const char* text) {
        size_t length = std::strlen(text);
        if (length >= sizeof(out->buffer)) {
            return false; // every slot name is well inside the SSO buffer; nothing else is passed
        }
        std::memset(out, 0, sizeof(*out));
        out->proxy = g_emptyStringProxy;
        std::memcpy(out->buffer, text, length + 1);
        out->size = static_cast<uint32_t>(length);
        out->capacity = static_cast<uint32_t>(sizeof(out->buffer) - 1);
        return true;
    }

    void* ResolveSlotCell(void* magmaPage, const char* slotParam) {
        NarrowString name{};
        if (!MakeNarrowString(&name, slotParam)) {
            return nullptr;
        }
        void* element = nullptr;
        DWORD code = 0;
        if (!SafeGetUserDataElement(magmaPage, &name, &element, &code)) {
            if (code != 0) {
                LogFailed("magma::UserData::GetUserDataElement", code);
            }
            return nullptr;
        }
        return element;
    }

    }

    void SlotParamName(size_t row, CellKind kind, char (&out)[kSlotParamMax]) {
        const char* format = kind == CellKind::Value    ? "FCSE_SLOT_%02zu"
                             : kind == CellKind::Slider ? "FCSE_SLIDER_%02zu"
                                                        : "FCSE_EDIT_%02zu";
        std::snprintf(out, kSlotParamMax, format, row + 1);
    }

    // Resolves all forty cells against the page's own UserData. Runs once, after Init has bound the
    // magma page - before that there is nothing to resolve against.
    void CacheSlotCells(void* page) {
        DWORD code = 0;
        void* magmaPage = nullptr;
        if (!SehReadPointer(page, kBoundMagmaPageOffset, &magmaPage, &code) ||
            magmaPage == nullptr) {
            Log::Loader("FcsePage: no magma::Page to resolve slot cells against - unused cells will "
                        "stay on screen");
            return;
        }

        size_t resolved = 0;
        for (size_t i = 0; i < kSlotCount; ++i) {
            char slotParam[kSlotParamMax];
            SlotParamName(i, CellKind::Value, slotParam);
            g_slotCells.value[i] = ResolveSlotCell(magmaPage, slotParam);
            SlotParamName(i, CellKind::Slider, slotParam);
            g_slotCells.slider[i] = ResolveSlotCell(magmaPage, slotParam);
            SlotParamName(i, CellKind::Edit, slotParam);
            g_slotCells.edit[i] = ResolveSlotCell(magmaPage, slotParam);
            resolved += (g_slotCells.value[i] != nullptr) + (g_slotCells.slider[i] != nullptr) +
                        (g_slotCells.edit[i] != nullptr);
        }
        g_slotCellsCached = true;
        Log::Loader("FcsePage: resolved " + std::to_string(resolved) + " of " +
                    std::to_string(kSlotCount * 3) +
                    " slot cells - the unused ones are hidden per display");

        // Called out because it is the one link shape nothing in the shipped corpus uses: the text
        // fields are bare EditBox elements, so their links are a 3-id chain rather than the 5-id
        // through-an-instance form. If they did not resolve, that is why, and the fix is to wrap the
        // EditBox in a local area and instance it like the other two banks.
        if (g_slotCells.edit[0] == nullptr) {
            Log::Loader("FcsePage: FCSE_EDIT_01 did not resolve - the engine does not accept a "
                        "3-id FullLink to a bare element. Text rows will show their value but not "
                        "be editable.");
        }
    }

    void HideAllSlotCells() {
        if (!g_slotCellsCached) {
            return;
        }
        for (size_t i = 0; i < kSlotCount; ++i) {
            DWORD code = 0;
            if (g_slotCells.value[i] != nullptr) {
                SafeSetElementVisible(g_slotCells.value[i], false, &code);
            }
            if (g_slotCells.slider[i] != nullptr) {
                SafeSetElementVisible(g_slotCells.slider[i], false, &code);
            }
            if (g_slotCells.edit[i] != nullptr) {
                SafeSetElementVisible(g_slotCells.edit[i], false, &code);
            }
        }
    }

    void* SlotCellElement(size_t row, CellKind kind) {
        if (!g_slotCellsCached || row >= kSlotCount) {
            return nullptr;
        }
        switch (kind) {
        case CellKind::Value:
            return g_slotCells.value[row];
        case CellKind::Slider:
            return g_slotCells.slider[row];
        case CellKind::Edit:
            return g_slotCells.edit[row];
        }
        return nullptr;
    }

    // `row` is the zero-based row index, which is also the slot index - see AppendPluginBlock.
    void ShowSlotCell(size_t row, CellKind kind) {
        void* element = SlotCellElement(row, kind);
        if (element == nullptr) {
            return;
        }
        DWORD code = 0;
        if (!SafeSetElementVisible(element, true, &code)) {
            LogFailed("magma::Element::SetVisible(true)", code);
        }
    }

    // The widget a magma::Element owns.
    constexpr ptrdiff_t kElementWidgetOffset = 0x14;

    // A magma::EditBox keeps several std::wstrings. Two matter, and both offsets are *observed*, not
    // derived: a probe that scanned the widget for string-shaped members while a player typed showed
    //
    //     +0x8C="kilimanjaro"   +0xA8="kilimanjaro"
    //     +0x8C="kilimanjaroa"  +0xA8="kilimanjaro"
    //
    // so +0x8c is the live text the player is editing and +0xa8 is the committed copy.
    //
    // Worth stating because it was got wrong twice. The server build lays these out as three
    // consecutive strings, and it is tempting to convert an ELF offset by scaling for MSVC's 0x1c
    // -byte wstring - that gives +0x70 here, and it is wrong, because the surrounding sub-objects
    // hold strings too and therefore differ in size by different amounts. Nothing about that
    // mistake is loud: +0x70 reads as a perfectly valid empty string, so the symptom was a setting
    // that silently saved as blank rather than anything that faulted.

    // Reads one candidate std::wstring at `offset` into `out`, reporting whether it looks like a
    // string at all. Used by the probe below, which exists because the displayed-text offset was
    // derived arithmetically from the committed one rather than observed - and a wrong offset here
    // fails silently, returning a stale value instead of faulting.
    //
    // Into a fixed buffer inside the guard, because nothing with a destructor may live in a
    // function that uses __try.
    bool SafeReadStringAt(void* widget, ptrdiff_t offset, wchar_t* out, size_t max, DWORD* outCode) {
        __try {
            auto text = reinterpret_cast<const char*>(widget) + offset;
            uint32_t size = *reinterpret_cast<const uint32_t*>(text + 0x14);
            uint32_t capacity = *reinterpret_cast<const uint32_t*>(text + 0x18);
            // What an MSVC wstring always satisfies, and arbitrary memory rarely does.
            if (capacity < 7 || size > capacity || capacity > 0x10000) {
                return false;
            }
            const wchar_t* characters = capacity < 8
                                             ? reinterpret_cast<const wchar_t*>(text + 4)
                                             : *reinterpret_cast<const wchar_t* const*>(text + 4);
            if (characters == nullptr) {
                return false;
            }
            if (size >= max) {
                size = static_cast<uint32_t>(max) - 1;
            }
            for (uint32_t i = 0; i < size; ++i) {
                out[i] = characters[i];
            }
            out[size] = L'\0';
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    bool SafeReadEditText(void* widget, wchar_t* out, DWORD* outCode) {
        return SafeReadStringAt(widget, kEditBoxDisplayedTextOffset, out, kEditTextMax, outCode);
    }

    // Seeds a Text row's field from the registry and reveals it. The widget hangs off the element at
    // +0x14, same as every other magma widget.
    void BindEditCell(SettingsRegistry::Setting* setting, size_t row) {
        void* element = SlotCellElement(row, CellKind::Edit);
        if (element == nullptr) {
            return; // logged once at cache time
        }

        DWORD code = 0;
        void* widget = nullptr;
        if (!SehReadPointer(element, kElementWidgetOffset, &widget, &code) || widget == nullptr) {
            Log::Loader("FcsePage: text field for row " + std::to_string(row + 1) +
                        " has no widget - leaving it hidden");
            return;
        }

        // Seeded through the EditBox's own setter. The first attempt used
        // magma::TextBase::SetText and took the game down twice over - an EditBox derives from
        // Widget, not TextBase, so that wrote through the wrong layout and the fault surfaced once
        // in the CRT's string code and once in magma's draw pass.
        WideString seed{};
        std::wstring wide = WidenAscii(setting->text);
        if (wide.size() < 8) {
            seed.proxy = g_emptyStringProxy;
            std::memcpy(seed.buffer, wide.c_str(), (wide.size() + 1) * sizeof(wchar_t));
            seed.size = static_cast<uint32_t>(wide.size());
            seed.capacity = 7;
        } else {
            // Longer than the inline buffer, so the string has to point at a heap block. Leaked
            // deliberately: the engine copies out of it and never owns it, and a settings value is a
            // few bytes once per display.
            auto* buffer = new wchar_t[wide.size() + 1];
            std::memcpy(buffer, wide.c_str(), (wide.size() + 1) * sizeof(wchar_t));
            seed.proxy = g_emptyStringProxy;
            *reinterpret_cast<wchar_t**>(&seed.buffer[0]) = buffer;
            seed.size = static_cast<uint32_t>(wide.size());
            seed.capacity = static_cast<uint32_t>(wide.size());
        }
        if (!SafeEditBoxSetText(widget, &seed, &code)) {
            LogFailed("magma::EditBox::SetText", code);
        }

        ShowSlotCell(row, CellKind::Edit);

        wchar_t committed[kEditTextMax] = {};
        DWORD probeCode = 0;
        SafeReadStringAt(widget, kEditBoxCommittedTextOffset, committed, kEditTextMax, &probeCode);
        LiveRows().push_back({setting, widget, committed});
    }

}
}
