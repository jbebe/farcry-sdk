// Two threads meet here. Window messages arrive on the thread the game pumps them on, and drawing
// happens on whichever thread presents the frame - not necessarily the same one. So a message is
// only queued where it arrives, and is handed to ImGui from the drawing thread, which is the only
// thread that ever touches ImGui state.
//
// Running a command crosses a third: the click is on the drawing thread, and Commands::Run queues
// it for the engine's own. Nothing here calls into the game directly.
#include "overlay/overlay.h"

#include "commands/catalog.h"
#include "engine/game_thread.h"
#include "engine/input.h"
#include "engine/renderer.h"
#include "fcse_api.h"

#include "imgui.h"
#include "imgui_impl_dx9.h"
#include "imgui_impl_win32.h"
#include "misc/cpp/imgui_stdlib.h"

#include <cstring>
#include <d3d9.h>
#include <deque>
#include <mutex>
#include <span>
#include <string>
#include <vector>
#include <windows.h>

extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND window, UINT message,
                                                             WPARAM wparam, LPARAM lparam);

namespace {
    using DevTools::Commands::Arg;
    using DevTools::Commands::Command;

    constexpr int kToggleKey = VK_HOME;
    constexpr size_t kHistoryMax = 12;

    struct QueuedMessage {
        HWND window;
        UINT message;
        WPARAM wparam;
        LPARAM lparam;
    };

    std::mutex g_messageLock;
    std::vector<QueuedMessage> g_messages;

    // Whether starting has been tried, not whether it worked: a failure that is retried is a
    // failure logged once a frame and an ImGui context leaked every time.
    bool g_startAttempted = false;
    bool g_running = false;

    // One editable argument per command, in catalog order, so what was typed survives a change of
    // category and a scroll away.
    std::vector<std::string> g_arguments;
    std::deque<std::string> g_history;

    // Which category tab is open, as an index into the labels below. 0 is "All".
    int g_category = 0;
    std::vector<const char*> g_categories;

    // Whether the overlay is up is the same question as whether it has the input, so it is only
    // stored once - two flags for one state can disagree, and did.
    bool Visible() { return DevTools::Input::IsCaptured(); }

    void SetVisible(bool visible) { DevTools::Input::SetCaptured(visible); }

    bool IsInterestingMessage(UINT message) {
        switch (message) {
        case WM_KEYDOWN:
        case WM_KEYUP:
        case WM_SYSKEYDOWN:
        case WM_SYSKEYUP:
        case WM_CHAR:
        case WM_SYSCHAR:
        case WM_MOUSEMOVE:
        case WM_LBUTTONDOWN:
        case WM_LBUTTONUP:
        case WM_LBUTTONDBLCLK:
        case WM_RBUTTONDOWN:
        case WM_RBUTTONUP:
        case WM_RBUTTONDBLCLK:
        case WM_MBUTTONDOWN:
        case WM_MBUTTONUP:
        case WM_XBUTTONDOWN:
        case WM_XBUTTONUP:
        case WM_MOUSEWHEEL:
        case WM_MOUSEHWHEEL:
        case WM_SETFOCUS:
        case WM_KILLFOCUS:
            return true;
        default:
            return false;
        }
    }

    // On the game's message thread. Decides what the game sees; ImGui is told later, on the thread
    // that draws.
    bool OnMessage(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
        if (message == WM_KEYDOWN && wparam == kToggleKey) {
            SetVisible(!Visible());
            return true;
        }

        if (!Visible() || !IsInterestingMessage(message)) {
            return false;
        }

        {
            std::lock_guard<std::mutex> held(g_messageLock);
            g_messages.push_back({window, message, wparam, lparam});
        }
        return message != WM_SETFOCUS && message != WM_KILLFOCUS;
    }

    void PumpQueuedMessages() {
        std::vector<QueuedMessage> ready;
        {
            std::lock_guard<std::mutex> held(g_messageLock);
            ready.swap(g_messages);
        }
        for (const QueuedMessage& queued : ready) {
            ImGui_ImplWin32_WndProcHandler(queued.window, queued.message, queued.wparam,
                                           queued.lparam);
        }
    }

    // The buttons and modifiers the message queue never carries, because the game holds the mouse
    // and because this is not the thread messages arrive on.
    void FeedRawKeys() {
        const DevTools::Input::RawKeys keys = DevTools::Input::PollRawKeys();

        ImGuiIO& io = ImGui::GetIO();
        for (int button = 0; button < 3; ++button) {
            io.AddMouseButtonEvent(button, keys.mouse[button]);
        }
        io.AddKeyEvent(ImGuiMod_Ctrl, keys.control);
        io.AddKeyEvent(ImGuiMod_Shift, keys.shift);
        io.AddKeyEvent(ImGuiMod_Alt, keys.alt);
    }

    void BuildCategories() {
        g_categories.push_back("All");
        for (const Command& command : DevTools::Commands::All()) {
            bool seen = false;
            for (const char* known : g_categories) {
                seen = seen || std::strcmp(known, command.category) == 0;
            }
            if (!seen) {
                g_categories.push_back(command.category);
            }
        }
    }

    bool Start(IDirect3DDevice9* device) {
        const FCSE_PluginAPI* api = FCSE::ApiPointer();

        D3DDEVICE_CREATION_PARAMETERS created{};
        if (FAILED(device->GetCreationParameters(&created))) {
            api->Log("overlay: the device would not name its window - the overlay cannot take "
                     "input");
            return false;
        }

        ImGui::CreateContext();
        ImGui::StyleColorsDark();

        ImGuiIO& io = ImGui::GetIO();
        // No imgui.ini: the overlay leaves nothing in the game folder, and there is no layout worth
        // the file.
        io.IniFilename = nullptr;
        // The game hides the system cursor and never puts it back, so the overlay draws its own.
        io.MouseDrawCursor = true;

        if (!ImGui_ImplWin32_Init(created.hFocusWindow) || !ImGui_ImplDX9_Init(device)) {
            api->Log("overlay: ImGui would not attach to the game's device - there is no overlay "
                     "this run");
            ImGui_ImplWin32_Shutdown();
            ImGui::DestroyContext();
            return false;
        }

        DevTools::Input::Install(created.hFocusWindow, &OnMessage);

        g_arguments.resize(DevTools::Commands::All().size());
        BuildCategories();

        // Whether presenting and updating happen on one thread is an open question in
        // docs/docs/engine-internals/architecture.md, which reads the dedicated-server binary where
        // there is no renderer to place. This answers it for the client, once.
        api->Log(DevTools::GameThread::IsCurrent()
                     ? "overlay: ready - Home opens it. Presenting is on the engine's own thread"
                     : "overlay: ready - Home opens it. Presenting is on a thread of its own");
        return true;
    }

    // Category 0 is "All".
    bool Matches(const Command& command) {
        return g_category == 0 || std::strcmp(command.category, g_categories[g_category]) == 0;
    }

    void Send(const Command& command, const char* argument) {
        DevTools::Commands::Run(command, argument);

        // Formatted to the same width Run sends, so the history is the line that went rather than a
        // shorter one that looks like it.
        char line[512];
        DevTools::Commands::Format(command, argument, line, sizeof(line));
        g_history.push_front(line);
        while (g_history.size() > kHistoryMax) {
            g_history.pop_back();
        }
    }

    // The control a command's argument deserves, and the button that sends it.
    void DrawCommandRow(const Command& command, size_t index) {
        ImGui::PushID(static_cast<int>(index));

        ImGui::TableNextRow();
        ImGui::TableNextColumn();
        ImGui::TextUnformatted(command.name);
        if (ImGui::IsItemHovered()) {
            ImGui::SetTooltip("%s%s%s", command.id, command.notes != nullptr ? "\n\n" : "",
                              command.notes != nullptr ? command.notes : "");
        }

        ImGui::TableNextColumn();
        std::string& argument = g_arguments[index];
        bool send = false;

        if (command.arg == Arg::None) {
            ImGui::TextDisabled("-");
        } else if (command.arg == Arg::Enum) {
            for (size_t choice = 0; choice < command.choices.size(); ++choice) {
                if (choice > 0) {
                    ImGui::SameLine();
                }
                if (ImGui::SmallButton(command.choices[choice].label)) {
                    Send(command, command.choices[choice].value);
                }
            }
        } else {
            ImGui::SetNextItemWidth(-1.0f);
            send = ImGui::InputText("##argument", &argument, ImGuiInputTextFlags_EnterReturnsTrue);
        }

        ImGui::TableNextColumn();
        if (ImGui::Button("Run")) {
            send = true;
        }

        if (send) {
            Send(command, argument.empty() ? nullptr : argument.c_str());
        }

        ImGui::PopID();
    }

    // Leaves room under the table for the history, which stays put while the categories change.
    void DrawCommandTable() {
        const float historyHeight = ImGui::GetTextLineHeightWithSpacing() * 6.0f;
        if (!ImGui::BeginTable("commands", 3,
                               ImGuiTableFlags_ScrollY | ImGuiTableFlags_RowBg |
                                   ImGuiTableFlags_BordersInnerH,
                               ImVec2(0.0f, -historyHeight))) {
            return;
        }

        ImGui::TableSetupColumn("Command", ImGuiTableColumnFlags_WidthFixed, 240.0f);
        ImGui::TableSetupColumn("Argument", ImGuiTableColumnFlags_WidthStretch);
        ImGui::TableSetupColumn("##run", ImGuiTableColumnFlags_WidthFixed, 48.0f);

        const std::span<const Command> all = DevTools::Commands::All();
        for (size_t index = 0; index < all.size(); ++index) {
            if (Matches(all[index])) {
                DrawCommandRow(all[index], index);
            }
        }
        ImGui::EndTable();
    }

    void DrawWindow() {
        bool open = true;
        ImGui::SetNextWindowSize(ImVec2(720.0f, 520.0f), ImGuiCond_FirstUseEver);
        const bool drawing = ImGui::Begin("DevTools", &open);

        // Closing from the window's own title bar has to hand the game its input back, the same as
        // the key does.
        if (!open) {
            SetVisible(false);
        }

        if (!drawing) {
            ImGui::End();
            return;
        }

        // Sixteen categories will not fit across the window, so they scroll rather than shrink, and
        // the popup button is the way to reach one that has scrolled off.
        if (ImGui::BeginTabBar("categories", ImGuiTabBarFlags_FittingPolicyScroll |
                                                 ImGuiTabBarFlags_TabListPopupButton)) {
            for (size_t category = 0; category < g_categories.size(); ++category) {
                if (ImGui::BeginTabItem(g_categories[category])) {
                    g_category = static_cast<int>(category);
                    DrawCommandTable();
                    ImGui::EndTabItem();
                }
            }
            ImGui::EndTabBar();
        }

        ImGui::Separator();
        if (g_history.empty()) {
            ImGui::TextDisabled("Nothing run yet. Commands run on the engine's next frame.");
        } else {
            for (const std::string& line : g_history) {
                ImGui::TextUnformatted(line.c_str());
            }
        }

        ImGui::End();
    }

    void Draw(IDirect3DDevice9* device) {
        if (!g_startAttempted) {
            g_startAttempted = true;
            g_running = Start(device);
        }
        if (!g_running || !Visible()) {
            return;
        }

        PumpQueuedMessages();

        ImGui_ImplDX9_NewFrame();
        ImGui_ImplWin32_NewFrame();
        FeedRawKeys();
        ImGui::NewFrame();

        DrawWindow();

        ImGui::Render();
        ImGui_ImplDX9_RenderDrawData(ImGui::GetDrawData());
    }

    void OnDeviceLost() {
        if (g_running) {
            ImGui_ImplDX9_InvalidateDeviceObjects();
        }
    }
}

namespace DevTools::Overlay {

bool Install() { return Renderer::Install(&Draw, &OnDeviceLost); }

}
