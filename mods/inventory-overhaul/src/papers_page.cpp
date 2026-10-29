// The Papers page: the notes, photos and cassettes other mods' pickups put in Items, and the Jackal and
// Predecessor tapes found.
#include "pages.h"

#include "engine/magma.h"
#include "engine/sound.h"
#include "engine/story.h"
#include "items.h"

#include <algorithm>

namespace InventoryOverhaul::Page {

namespace {
    // What an Items id is by its first part: the post-it's word for it, and the images its picture is
    // shown in, IO_POSTIT_PIC_<slot> and IO_READER_PIC_<slot>, a photo's wider than a note's. A
    // cassette has none; the post-it shows the tape.
    struct Kind {
        const char* prefix;
        const wchar_t* word;
        const char* slot;
    };
    constexpr Kind kKinds[] = {{"Note.", L"a note", "NOTE"}, {"Photo.", L"a photo", "PHOTO"}, {"Tape.", L"a cassette", nullptr}};

    // A paper's words are the string table's Papers/<id>, its name, <id>_FOUND, where it was found,
    // and <id>_1, _2... its sheets: a photo's caption or a cassette's description the first. Its
    // picture is the image IO_PAPER_<ID>, its dots made underscores.
    struct Paper {
        const Kind* kind;
        const Items::Record* item;
        std::wstring name;
        std::string picture;
    };
    constexpr const char* kSection = "Papers";

    // The post-it has room for this much of a paper's first sheet, cut at a word.
    constexpr size_t kOpening = 130;

    constexpr const char* kTapes = "JackalTapes";
    constexpr size_t kLetterLines = 10;
    constexpr size_t kTapeLines = 4;
    size_t g_firstLetter = 0;
    size_t g_firstTape = 0;

    const Kind* KindOf(const std::string& id) {
        for (const Kind& kind : kKinds) {
            if (id.rfind(kind.prefix, 0) == 0) {
                return &kind;
            }
        }
        return nullptr;
    }

    // Papers/<key>, empty when the table has none, which it gives as a space.
    std::wstring PaperString(const std::string& key) {
        const std::wstring& text = Story::Localize(kSection, key);
        return text == L" " ? L"" : text;
    }

    Paper PaperOf(const Kind& kind, const Items::Record& item) {
        std::wstring name = PaperString(item.id);
        std::string picture = "IO_PAPER_" + Upper(item.id);
        std::replace(picture.begin(), picture.end(), '.', '_');
        return {&kind, &item, name.empty() ? PlainName(item.id) : std::move(name),
                Magma::Find(picture.c_str()) != nullptr ? picture : ""};
    }

    // A paper's sheets, one at least.
    std::vector<std::wstring> Sheets(const Paper& paper) {
        std::vector<std::wstring> sheets;
        for (std::wstring sheet; !(sheet = PaperString(Name(paper.item->id + "_", sheets.size() + 1))).empty();) {
            sheets.push_back(std::move(sheet));
        }
        if (sheets.empty()) {
            sheets.emplace_back();
        }
        return sheets;
    }

    // A paper's or a cassette's name, what it is, where it was found and its first sheet's opening.
    void PaperPostIt(Model& model, const Paper& paper) {
        if (paper.kind->slot == nullptr) {
            model.Show("IO_POSTIT_PIC_TAPE", true);
        } else if (!paper.picture.empty()) {
            const std::string slot = std::string("IO_POSTIT_PIC_") + paper.kind->slot;
            model.Show(slot, true);
            model.Picture(slot, paper.picture);
        }
        model.Text("IO_POSTIT_NAME", paper.name);
        model.Text("IO_POSTIT_SUB", paper.kind->word);
        if (std::wstring found = PaperString(paper.item->id + "_FOUND"); !found.empty()) {
            model.Text("IO_POSTIT_LINE1", L"Found");
            model.Text("IO_POSTIT_STATE", std::move(found));
        }
        std::wstring opening = Sheets(paper).front();
        if (opening.size() > kOpening) {
            const size_t word = opening.rfind(L' ', kOpening);
            opening.erase(word != std::wstring::npos ? word : kOpening);
            opening += L"…";
        }
        model.Text("IO_POSTIT_QUOTE", std::move(opening));
    }

    // The paper read whole, on the sheet over the right page.
    void Reader(Model& model, const Paper& paper, int& sheet) {
        const std::vector<std::wstring> sheets = Sheets(paper);
        sheet = std::clamp(sheet, 0, static_cast<int>(sheets.size()) - 1);
        const bool pictured = !paper.picture.empty() && sheet == 0;
        model.Show("IO_READER_PIC_NOTE", false);
        model.Show("IO_READER_PIC_PHOTO", false);
        if (pictured) {
            const std::string slot = std::string("IO_READER_PIC_") + paper.kind->slot;
            model.Show(slot, true);
            model.Picture(slot, paper.picture);
        }
        model.Text("IO_READER_NAME", paper.name);
        model.Text("IO_READER_TEXT", pictured ? L"" : sheets[sheet]);
        model.Text("IO_READER_CAPTION", pictured ? sheets[sheet] : L"");
        model.Text("IO_READER_SHEET", sheets.size() > 1 ? std::to_wstring(sheet + 1) + L" / " + std::to_wstring(sheets.size())
                                                        : L"");
    }

    // The game words a tape as "RECORDED 17/09/2008 - <what it is about>".
    void TapePostIt(Model& model, const std::string& key, const std::wstring& title) {
        std::wstring about = Story::Localize(kTapes, key + "_DESC");
        model.Show("IO_POSTIT_PIC_TAPE", true);
        model.Text("IO_POSTIT_NAME", title);
        if (const size_t dash = about.find(L" - "); dash != std::wstring::npos) {
            const std::wstring recorded = about.substr(0, dash);
            model.Text("IO_POSTIT_LINE1", L"Recorded");
            model.Text("IO_POSTIT_STATE", recorded.substr(recorded.find_last_of(L' ') + 1));
            about = about.substr(dash + 3);
        }
        model.Text("IO_POSTIT_QUOTE", about);
    }
}

bool IsPaper(const std::string& id) { return KindOf(id) != nullptr; }

int PapersPage(Model& model, int& selected, bool used, int& sheet) {
    const std::vector<Items::Record> items = Items::Load();
    std::vector<Paper> papers;
    std::vector<Paper> cassettes;
    for (const Items::Record& item : items) {
        if (const Kind* kind = KindOf(item.id); kind != nullptr && !item.gun) {
            (kind->slot == nullptr ? cassettes : papers).push_back(PaperOf(*kind, item));
        }
    }
    std::vector<std::wstring> letters;
    for (const Paper& paper : papers) {
        letters.push_back(paper.name);
    }
    // The game's tapes, then the cassettes, whose banks are held so that a click finds them loaded.
    const std::vector<std::string> tapes = Story::FoundTapes();
    std::vector<std::wstring> titles;
    for (const std::string& key : tapes) {
        titles.push_back(Story::Localize(kTapes, key));
    }
    for (const Paper& cassette : cassettes) {
        titles.push_back(cassette.name);
        if (cassette.item->sound != 0) {
            Sound::Hold(cassette.item->sound);
        }
    }
    const int rows = static_cast<int>(papers.size() + titles.size());
    selected = std::clamp(selected, 0, std::max(rows - 1, 0));

    // The letters on the left page come before the tapes on the right.
    const int tape = selected - static_cast<int>(papers.size());
    const Paper* cassette = tape >= static_cast<int>(tapes.size()) && tape < static_cast<int>(titles.size())
                                ? &cassettes[tape - tapes.size()]
                                : nullptr;
    List(model, "LETTER", kLetterLines, g_firstLetter, letters, selected);
    List(model, "TAPE", kTapeLines, g_firstTape, titles, tape);
    for (size_t line = 0; line < kTapeLines; ++line) {
        model.Show(Name("IO_TAPE", line + 1, "_PIC"), g_firstTape + line < titles.size());
    }

    if (used && tape >= 0 && tape < static_cast<int>(titles.size())) {
        const std::string key = cassette != nullptr ? cassette->item->id : tapes[tape];
        if (Story::PlayingTape() == key) {
            Story::StopTape();
        } else if (cassette == nullptr) {
            Story::PlayTape(key);
        } else if (Sound::Length(cassette->item->sound) >= 0.0f) {
            Story::PlayTape(key, cassette->item->sound);
        }
    } else if (used) {
        sheet = sheet < 0 ? 0 : -1;
    }
    if (tape >= 0) {
        sheet = -1;
    }
    if (sheet >= 0) {
        Reader(model, papers[selected], sheet);
    } else if (tape < 0) {
        PaperPostIt(model, papers[selected]);
    } else if (cassette != nullptr) {
        PaperPostIt(model, *cassette);
    } else if (tape < static_cast<int>(tapes.size())) {
        TapePostIt(model, tapes[tape], titles[tape]);
    }
    return rows;
}

}
