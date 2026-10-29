// The Papers page: letters and photos carried in Items, and the Jackal and Predecessor tapes found.
#include "pages.h"

#include "engine/story.h"
#include "items.h"

#include <algorithm>

namespace InventoryOverhaul::Page {

namespace {
    // A paper the Papers page knows by its Items id: its name, its picture if any, what it is, where it
    // was found and what it says, a long letter's sheets split by \f. A picture named photo<n> is a
    // loading-screen picture. The test window's Fill gives them all; notebook\papers.py places them.
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
        // Dummies for the reader: a letter longer than one sheet, and a photo too detailed for the post-it.
        {"Dummy_note", L"Dummy note", nullptr, L"a letter, three sheets long", L"dummy: in a dead man's pocket",
         L"Dummy note, sheet one. Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor "
         L"incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco "
         L"laboris nisi ut aliquip ex ea commodo consequat.\n\nDuis aute irure dolor in reprehenderit in voluptate "
         L"velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt "
         L"in culpa qui officia deserunt mollit anim id est laborum.\f"
         L"Sheet two. Sed ut perspiciatis unde omnis iste natus error sit voluptatem accusantium doloremque "
         L"laudantium, totam rem aperiam, eaque ipsa quae ab illo inventore veritatis et quasi architecto beatae "
         L"vitae dicta sunt explicabo.\n\nNemo enim ipsam voluptatem quia voluptas sit aspernatur aut odit aut "
         L"fugit, sed quia consequuntur magni dolores eos qui ratione voluptatem sequi nesciunt.\f"
         L"Sheet three. Neque porro quisquam est, qui dolorem ipsum quia dolor sit amet, consectetur, adipisci "
         L"velit.\n\nThe end of the dummy note."},
        {"Dummy_photo", L"Dummy photo", "photo05", L"a photo", L"dummy: in a looted car",
         L"Dummy caption. Two burnt cars under a tree; somebody wrote on the back."},
    };

    // The post-it has room for this much of a paper's first sheet, cut at a word.
    constexpr size_t kOpening = 130;

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

    // A paper's sheets, one at least.
    std::vector<std::wstring> Sheets(const Paper& paper) {
        std::vector<std::wstring> sheets(1);
        for (const wchar_t* c = paper.text; *c != L'\0'; ++c) {
            if (*c == L'\f') {
                sheets.emplace_back();
            } else {
                sheets.back() += *c;
            }
        }
        return sheets;
    }

    void PaperPostIt(Model& model, const Paper& paper) {
        if (paper.picture != nullptr) {
            model.Show("IO_POSTIT_PIC_" + Upper(paper.picture), true);
        }
        model.Text("IO_POSTIT_NAME", paper.name);
        model.Text("IO_POSTIT_SUB", paper.kind);
        model.Text("IO_POSTIT_LINE1", L"Found");
        model.Text("IO_POSTIT_STATE", paper.found);
        std::wstring opening = Sheets(paper).front();
        if (opening.size() > kOpening) {
            const size_t word = opening.rfind(L' ', kOpening);
            opening.erase(word != std::wstring::npos ? word : kOpening);
            opening += L"\u2026";
        }
        model.Text("IO_POSTIT_QUOTE", std::move(opening));
    }

    // The paper read whole, on the sheet over the right page.
    void Reader(Model& model, const Paper& paper, int& sheet) {
        const std::vector<std::wstring> sheets = Sheets(paper);
        sheet = std::clamp(sheet, 0, static_cast<int>(sheets.size()) - 1);
        const bool pictured = paper.picture != nullptr && sheet == 0;
        for (const Paper& other : kPapers) {
            if (other.picture != nullptr) {
                model.Show("IO_READER_PIC_" + Upper(other.picture), false);
            }
        }
        if (pictured) {
            model.Show("IO_READER_PIC_" + Upper(paper.picture), true);
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

bool IsPaper(const std::string& id) { return PaperOf(id) != nullptr; }

int PapersPage(Model& model, int& selected, bool used, int& sheet) {
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

    if (used) {
        sheet = sheet < 0 ? 0 : -1;
    }
    if (tape >= 0) {
        sheet = -1;
    }
    if (sheet >= 0) {
        Reader(model, *papers[selected], sheet);
    } else if (tape < 0) {
        PaperPostIt(model, *papers[selected]);
    } else if (tape < static_cast<int>(tapes.size())) {
        TapePostIt(model, tapes[tape], titles[tape]);
    }
    return rows;
}

}
