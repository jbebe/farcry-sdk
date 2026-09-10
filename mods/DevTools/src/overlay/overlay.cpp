// Two threads meet here. Window messages arrive on the thread the game pumps them on, and drawing
// happens on whichever thread presents the frame - not necessarily the same one. So a message is
// only queued where it arrives, and is handed to ImGui from the drawing thread, which is the only
// thread that ever touches ImGui state.
//
// Running a command crosses a third: the click is on the drawing thread, and Commands::Run queues
// it for the engine's own. Nothing here calls into the game directly.
#include "overlay/overlay.h"

#include "commands/catalog.h"
#include "devtools_api.h"
#include "engine/game_thread.h"
#include "engine/input.h"
#include "engine/renderer.h"
#include "fcse_api.h"

#include "imgui.h"
#include "imgui_impl_dx9.h"
#include "imgui_impl_win32.h"
#include "imgui_internal.h"
#include "misc/cpp/imgui_stdlib.h"

#include <cstdio>
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

    // One window in the overlay, DevTools' own or another plugin's.
    struct Window {
        std::string title;
        DevTools_BindImGuiFn bind;
        DevTools_DrawWindowFn draw;
        void* userData;
        // The size it was last drawn at, which the next layout spaces the windows by.
        ImVec2 size;
        bool open = true;
    };

    std::mutex g_messageLock;
    std::vector<QueuedMessage> g_messages;

    // Every window added, in order, from whichever thread added it. Nothing is ever removed.
    std::mutex g_registryLock;
    std::vector<Window> g_registered;

    // The drawing thread's copy of that list, which is where a window's size and open state live.
    std::vector<Window> g_windows;

    // Whether starting has been tried, not whether it worked: a failure that is retried is a
    // failure logged once a frame and an ImGui context leaked every time.
    bool g_startAttempted = false;
    bool g_running = false;

    // Whether the overlay is on the frame at all, which is what a window is accepted against.
    bool g_installed = false;

    // Whether the overlay was up last frame. ImGuiCond_Appearing cannot see it reopen, because no
    // frame runs while it is hidden.
    bool g_wasVisible = false;

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

    // Logs why a window is not added, and refuses it.
    bool Refuse(const char* reason, const char* title) {
        char line[256];
        std::snprintf(line, sizeof(line), "overlay: %s - the '%s' window is not added", reason,
                      title);
        FCSE::ApiPointer()->Log(line);
        return false;
    }

    bool RegisterWindow(const DevTools_ImGuiLayout* imgui, DevTools_BindImGuiFn bind,
                        const char* title, float width, float height, DevTools_DrawWindowFn draw,
                        void* userData) {
        if (title == nullptr || title[0] == '\0' || bind == nullptr || draw == nullptr) {
            return Refuse("a window needs a title, a bind and a draw function",
                          title == nullptr ? "" : title);
        }
        if (!g_installed) {
            return Refuse("the overlay is not running this launch", title);
        }

        const DevTools_ImGuiLayout own = DevTools::Overlay::ImGuiLayout();
        if (imgui == nullptr || std::memcmp(imgui, &own, sizeof(own)) != 0) {
            char reason[128];
            std::snprintf(reason, sizeof(reason),
                          "built against Dear ImGui %u where DevTools has %u, or with a different "
                          "layout",
                          imgui == nullptr ? 0u : imgui->versionNum, own.versionNum);
            return Refuse(reason, title);
        }

        // Compared the way ImGui names a window, so two titles it would draw as one are caught.
        const ImGuiID id = ImHashStr(title);
        std::lock_guard<std::mutex> held(g_registryLock);
        for (const Window& window : g_registered) {
            if (ImHashStr(window.title.c_str()) == id) {
                return Refuse("the overlay already has a window by that title", title);
            }
        }
        g_registered.push_back({title, bind, draw, userData, ImVec2(width, height)});

        char line[256];
        std::snprintf(line, sizeof(line), "overlay: added the '%s' window", title);
        FCSE::ApiPointer()->Log(line);
        return true;
    }

    void AdoptRegistered() {
        std::lock_guard<std::mutex> held(g_registryLock);
        g_windows.insert(g_windows.end(), g_registered.begin() + g_windows.size(),
                         g_registered.end());
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
        // A mistake in another plugin's window is reported on that window, not as an assert that
        // stops the game or a line in a debug log nothing reads.
        io.ConfigErrorRecoveryEnableAssert = false;
        io.ConfigErrorRecoveryEnableDebugLog = false;

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

    // DevTools' own window: the catalog, a tab per category, and what was run.
    void DrawCatalog(void*) {
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
    }

    // The bar across the top, listing each closed window until a click opens it again.
    void DrawBar() {
        if (!ImGui::BeginMainMenuBar()) {
            return;
        }

        bool anyClosed = false;
        for (Window& window : g_windows) {
            if (!window.open) {
                anyClosed = true;
                if (ImGui::MenuItem(window.title.c_str())) {
                    window.open = true;
                }
            }
        }
        if (!anyClosed) {
            ImGui::TextDisabled("Closed windows are listed here");
        }

        ImGui::EndMainMenuBar();
    }

    // Every open window, laid out side by side on the frame the overlay opens, and left wherever it
    // is dragged after that.
    void DrawWindows(bool opened) {
        // The screen under the bar, which BeginMainMenuBar has already taken its height out of.
        const ImRect area =
            static_cast<ImGuiViewportP*>(ImGui::GetMainViewport())->GetBuildWorkRect();
        const ImVec2 gap = ImGui::GetStyle().WindowPadding;
        const float rowStart = area.Min.x + gap.x;
        ImVec2 next(rowStart, area.Min.y + gap.y);
        float rowHeight = 0.0f;
        bool closedOne = false;
        bool anyOpen = false;

        DevTools_ImGuiBinding binding{ImGui::GetCurrentContext()};
        ImGui::GetAllocatorFunctions(&binding.MemAlloc, &binding.MemFree, &binding.memUserData);

        for (Window& window : g_windows) {
            if (!window.open) {
                continue;
            }

            if (opened) {
                // A window that would cross the right edge starts a new row under the tallest of
                // this one, unless it is already the first in its row.
                if (next.x > rowStart && next.x + window.size.x > area.Max.x) {
                    next = ImVec2(rowStart, next.y + rowHeight + gap.y);
                    rowHeight = 0.0f;
                }
                ImGui::SetNextWindowPos(next, ImGuiCond_Always);
                next.x += window.size.x + gap.x;
                rowHeight = ImMax(rowHeight, window.size.y);
            }

            ImGui::SetNextWindowSize(window.size, ImGuiCond_FirstUseEver);
            const bool drawing = ImGui::Begin(window.title.c_str(), &window.open);
            window.size = ImGui::GetWindowSize();
            if (drawing) {
                // Whatever the draw leaves unbalanced is closed here, so it cannot reach the windows
                // drawn after it.
                ImGuiErrorRecoveryState state;
                ImGui::ErrorRecoveryStoreState(&state);
                window.bind(&binding);
                window.draw(window.userData);
                ImGui::ErrorRecoveryTryToRecoverState(&state);
            }
            ImGui::End();

            closedOne = closedOne || !window.open;
            anyOpen = anyOpen || window.open;
        }

        // Closing the last window closes the overlay, rather than holding the game's input for a bar.
        if (closedOne && !anyOpen) {
            SetVisible(false);
        }
    }

    void Draw(IDirect3DDevice9* device) {
        if (!g_startAttempted) {
            g_startAttempted = true;
            g_running = Start(device);
        }

        const bool visible = g_running && Visible();
        const bool opened = visible && !g_wasVisible;
        g_wasVisible = visible;
        if (!visible) {
            return;
        }

        PumpQueuedMessages();

        ImGui_ImplDX9_NewFrame();
        ImGui_ImplWin32_NewFrame();
        FeedRawKeys();
        ImGui::NewFrame();

        AdoptRegistered();
        DrawBar();
        DrawWindows(opened);

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

bool Install() {
    g_installed = Renderer::Install(&Draw, &OnDeviceLost);

    // DevTools' own window is added the way any plugin's is, and first, since every FCSE_Load runs
    // before any plugin can add one.
    const DevTools_ImGuiLayout layout = ImGuiLayout();
    return g_installed && RegisterWindow(&layout, &BindImGui, "DevTools", 720.0f, 520.0f,
                                         &DrawCatalog, nullptr);
}

}

// How another plugin reaches the overlay: it finds DevTools.dll and asks for this by name.
extern "C" __declspec(dllexport) const DevTools_OverlayAPI* DevTools_GetOverlayAPI() {
    static const DevTools_OverlayAPI api{DEVTOOLS_OVERLAY_API_VERSION, &RegisterWindow};
    return &api;
}
