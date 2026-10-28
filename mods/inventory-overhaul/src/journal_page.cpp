// The Journal page: the player's reputation in the words of the pause menu's Journal, and the current
// job as the pause menu's main page showed it.
#include "pages.h"

#include "engine/inventory.h"
#include "engine/jobs.h"
#include "engine/story.h"

#include <algorithm>
#include <chrono>

namespace InventoryOverhaul::Page {

namespace {
    // The markers are read again at most this often, as reading them walks every game element.
    constexpr std::chrono::seconds kJobsEvery{1};
}

void JournalPage(Model& model) {
    const int level = std::max(Inventory::Reputation(), 0);
    std::wstring heading = L"REPUTATION LEVEL " + std::to_wstring(level);
    std::wstring text = Story::Localize("PauseMenu", "REPUTATION_INFAMY_DESCRIPTION_" + std::to_string(level));
    // Above level 0 the game's text opens with its own heading line.
    if (const size_t newline = text.find(L'\n'); newline != std::wstring::npos) {
        heading = text.substr(0, newline);
        text.erase(0, newline + 1);
    }
    model.Text("IO_REPUTATION", std::move(heading));
    model.Text("IO_REPUTATION_PROSE", std::move(text));

    static Jobs::Current s_jobs;
    static std::chrono::steady_clock::time_point s_read;
    if (const auto now = std::chrono::steady_clock::now(); now - s_read >= kJobsEvery) {
        s_jobs = Jobs::Find();
        s_read = now;
    }
    model.Text("IO_JOB", s_jobs.job);
    model.Show("IO_JOBS_NONE", s_jobs.job.empty() && s_jobs.underground.empty());
    model.Show("IO_OBJECTIVES", !s_jobs.objectives[0].empty());
    for (size_t i = 0; i < 2; ++i) {
        model.Text(Name("IO_OBJECTIVE", i + 1), s_jobs.objectives[i]);
    }
    model.Show("IO_UNDERGROUND", !s_jobs.underground.empty());
    model.Text("IO_UNDERGROUND_JOB", s_jobs.underground);
}

}
