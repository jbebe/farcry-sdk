// The Papers page: the notes, photos and cassettes other mods' pickups put in Items, and the Jackal and
// Predecessor tapes found.
#include "pages.h"

#include "engine/magma.h"
#include "engine/story.h"
#include "items.h"

#include <algorithm>
#include <utility>

namespace InventoryOverhaul::Page {

namespace {
    // What an Items id is by its first part: the post-it's word for it, what the post-it shows when it has
    // no picture, and whether a click plays it rather than reads it.
    struct Kind {
        const char* prefix;
        const wchar_t* word;
        const char* icon;
        bool plays;
    };
    constexpr Kind kKinds[] = {{"Note.", L"NOTE", "IO_POSTIT_PIC_ENVELOPE", false},
                               {"Photo.", L"PHOTO", nullptr, false},
                               {"Tape.", L"CASSETTE", "IO_POSTIT_PIC_TAPE", true}};

    // Examples of what other mods add, for everyone and never saved, until Tales of Africa ships papers.
    const Items::Record kExamples[] = {
        {.id = "Note.Example_note"}, {.id = "Photo.Example_photo"}, {.id = "Tape.Example_cassette", .sound = 0x00FD0002}};

    // A paper's words are the string table's Papers/<id>, its name, <id>_FOUND, where it was found,
    // and <id>_1, _2... its sheets: a photo's caption or a cassette's description the first.
    struct Paper {
        const Kind* kind;
        const Items::Record* item;
        std::wstring name;
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

    Paper PaperOf(const Kind& kind, const Items::Record& item) {
        const std::wstring& name = Story::Localize(kSection, item.id);
        return {&kind, &item, name.empty() ? PlainName(item.id) : name};
    }

    // The image a paper's picture is copied from, IO_PAPER_<ID> with the id's dots made underscores, or
    // empty when there is none.
    std::string PictureOf(const Paper& paper) {
        std::string picture = "IO_PAPER_" + Upper(paper.item->id);
        std::replace(picture.begin(), picture.end(), '.', '_');
        return Magma::Find(picture.c_str()) != nullptr ? picture : "";
    }

    // A paper's sheets, one at least.
    std::vector<std::wstring> Sheets(const Paper& paper) {
        std::vector<std::wstring> sheets;
        while (true) {
            const std::wstring& sheet = Story::Localize(kSection, Name(paper.item->id + "_", sheets.size() + 1));
            if (sheet.empty()) {
                break;
            }
            sheets.push_back(sheet);
        }
        if (sheets.empty()) {
            sheets.emplace_back();
        }
        return sheets;
    }

    // A paper's or a cassette's name, what it is, where it was found and its first sheet's opening.
    void PaperPostIt(Model& model, const Paper& paper) {
        if (const std::string picture = PictureOf(paper); !picture.empty()) {
            model.Picture("IO_POSTIT_PIC_PAPER", picture);
        } else if (paper.kind->icon != nullptr) {
            model.Show(paper.kind->icon, true);
        }
        model.Text("IO_POSTIT_NAME", paper.name);
        model.Text("IO_POSTIT_SUB", paper.kind->word);
        if (const std::wstring& found = Story::Localize(kSection, paper.item->id + "_FOUND"); !found.empty()) {
            model.Text("IO_POSTIT_LINE1", L"Found");
            model.Text("IO_POSTIT_STATE", found);
        }
        std::wstring opening = Story::Localize(kSection, paper.item->id + "_1");
        if (opening.size() > kOpening) {
            const size_t word = opening.rfind(L' ', kOpening);
            opening.erase(word != std::wstring::npos ? word : kOpening);
            opening += L"\u2026";
        }
        model.Text("IO_POSTIT_QUOTE", std::move(opening));
    }

    // The paper read whole, on the sheet over the right page.
    void Reader(Model& model, const Paper& paper, int& sheet) {
        std::vector<std::wstring> sheets = Sheets(paper);
        const std::string picture = PictureOf(paper);
        // Words alone open with the paper's name; a picture has only its caption under it.
        if (picture.empty()) {
            sheets[0].insert(0, paper.name + L"\n\n");
        }
        sheet = std::clamp(sheet, 0, static_cast<int>(sheets.size()) - 1);
        const bool pictured = !picture.empty() && sheet == 0;
        if (pictured) {
            model.Picture("IO_READER_PIC", picture);
        } else {
            model.Show("IO_READER_PIC", false);
        }
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
    std::vector<Items::Record> items = Items::Load();
    items.insert(items.end(), std::begin(kExamples), std::end(kExamples));
    std::vector<Paper> papers;
    std::vector<std::wstring> letters;
    for (const Items::Record& item : items) {
        if (const Kind* kind = KindOf(item.id); kind != nullptr && !item.gun) {
            papers.push_back(PaperOf(*kind, item));
            letters.push_back(papers.back().name);
            // A cassette's bank is held so that a click finds it loaded.
            if (kind->plays) {
                Story::HoldCassette(item.sound);
            }
        }
    }
    const std::vector<std::string> tapes = Story::FoundTapes();
    std::vector<std::wstring> titles;
    for (const std::string& key : tapes) {
        titles.push_back(Story::Localize(kTapes, key));
    }
    const int rows = static_cast<int>(papers.size() + tapes.size());
    selected = std::clamp(selected, 0, std::max(rows - 1, 0));

    // The papers on the left page come before the tapes on the right.
    const int tape = selected - static_cast<int>(papers.size());
    const Paper* paper = tape < 0 ? &papers[selected] : nullptr;
    List(model, "LETTER", kLetterLines, g_firstLetter, letters, selected);
    List(model, "TAPE", kTapeLines, g_firstTape, titles, tape);
    for (size_t line = 0; line < kTapeLines; ++line) {
        model.Show(Name("IO_TAPE", line + 1, "_PIC"), g_firstTape + line < tapes.size());
    }
    if (rows == 0) {
        sheet = -1;
        return 0;
    }

    // A tape or a cassette plays where a note or a photo is read.
    const bool plays = paper == nullptr || paper->kind->plays;
    if (used && plays) {
        const std::string& key = paper != nullptr ? paper->item->id : tapes[tape];
        if (Story::PlayingTape() == key) {
            Story::StopTape();
        } else if (paper != nullptr) {
            Story::PlayCassette(key, paper->item->sound);
        } else {
            Story::PlayTape(key);
        }
    } else if (used) {
        sheet = sheet < 0 ? 0 : -1;
    }
    if (plays) {
        sheet = -1;
    }
    if (sheet >= 0) {
        Reader(model, *paper, sheet);
    } else if (paper != nullptr) {
        PaperPostIt(model, *paper);
    } else {
        TapePostIt(model, tapes[tape], titles[tape]);
    }
    return rows;
}

}
