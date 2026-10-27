// The Papers page: letters and photos carried in Items, and the Jackal tapes found.
#include "pages.h"

#include "engine/story.h"
#include "items.h"

#include <algorithm>

namespace InventoryOverhaul::Page {

namespace {
    // A paper the Papers page knows by its Items id: its name, its picture, what it is, where it was
    // found and what it says. Sample papers for now, which the test window's Fill gives.
    struct Paper {
        const char* id;
        const wchar_t* name;
        const char* picture;
        const wchar_t* kind;
        const wchar_t* found;
        const wchar_t* text;
    };
    constexpr Paper kPapers[] = {
        {"Torn_notebook_page", L"Torn notebook page", "jackal_photo", L"a letter, a photo clipped to it",
         L"in a burnt-out truck",
         L"The Jackal is gone. He missed our meeting yesterday & today. He never missed a meeting. I don't "
         L"think I'll ever see him again."},
        {"Dictionary_page", L"Dictionary page: jackal", "jackal_definition", L"a page torn from a dictionary",
         L"in the same notebook", L""},
        {"APR_poster", L"APR poster", "apr_01", L"a poster", L"on a wall in Pala", L"\"APR UNITE!\""},
        {"UFLL_poster", L"UFLL poster", "ufll_01", L"a poster", L"on a checkpoint wall",
         L"\"United Front for Liberation and Labour\""},
    };

    constexpr const char* kTapes = "JackalTapes";
    constexpr size_t kLetterLines = 10;
    constexpr size_t kTapeLines = 4;
    size_t g_firstLetter = 0;
    size_t g_firstTape = 0;

    const Paper* PaperOf(const std::string& id) {
        for (const Paper& paper : kPapers) {
            if (id == paper.id) {
                return &paper;
            }
        }
        return nullptr;
    }

    void PaperPostIt(Model& model, const Paper& paper) {
        model.Show("IO_POSTIT_PIC_" + Upper(paper.picture), true);
        model.Text("IO_POSTIT_NAME", paper.name);
        model.Text("IO_POSTIT_SUB", paper.kind);
        model.Text("IO_POSTIT_LINE1", L"Found");
        model.Text("IO_POSTIT_STATE", paper.found);
        model.Text("IO_POSTIT_QUOTE", paper.text);
    }

    // The game words a tape as "RECORDED 17/09/2008 - <what it is about>".
    void TapePostIt(Model& model, const std::string& key, const std::wstring& title) {
        std::wstring about = Story::Localize(kTapes, key + "_DESC");
        model.Show("IO_POSTIT_PIC_TAPE", true);
        model.Text("IO_POSTIT_NAME", title);
        model.Text("IO_POSTIT_SUB", L"a Jackal tape");
        if (const size_t dash = about.find(L" - "); dash != std::wstring::npos) {
            const std::wstring recorded = about.substr(0, dash);
            model.Text("IO_POSTIT_LINE1", L"Recorded");
            model.Text("IO_POSTIT_STATE", recorded.substr(recorded.find_last_of(L' ') + 1));
            about = about.substr(dash + 3);
        }
        model.Text("IO_POSTIT_QUOTE", about);
    }
}

bool IsPaper(const std::string& id) { return PaperOf(id) != nullptr; }

int PapersPage(Model& model, int& selected) {
    std::vector<const Paper*> papers;
    std::vector<std::wstring> letters;
    for (const Items::Record& item : Items::Load()) {
        if (const Paper* paper = PaperOf(item.id)) {
            papers.push_back(paper);
            letters.push_back(paper->name);
        }
    }
    const std::vector<std::string> tapes = Story::FoundTapes();
    std::vector<std::wstring> titles;
    for (const std::string& key : tapes) {
        titles.push_back(Story::Localize(kTapes, key));
    }
    const int rows = static_cast<int>(papers.size() + tapes.size());
    selected = std::clamp(selected, 0, std::max(rows - 1, 0));

    // The letters on the left page come before the tapes on the right.
    const int tape = selected - static_cast<int>(papers.size());
    List(model, "LETTER", kLetterLines, g_firstLetter, letters, selected);
    List(model, "TAPE", kTapeLines, g_firstTape, titles, tape);
    for (size_t line = 0; line < kTapeLines; ++line) {
        model.Show(Name("IO_TAPE", line + 1, "_PIC"), g_firstTape + line < tapes.size());
    }

    if (tape < 0) {
        PaperPostIt(model, *papers[selected]);
    } else if (tape < static_cast<int>(tapes.size())) {
        TapePostIt(model, tapes[tape], titles[tape]);
    }
    return rows;
}

}
