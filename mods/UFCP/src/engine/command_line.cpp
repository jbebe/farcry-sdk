// The command line the engine parses, with switches added to it.
//
// Ported from FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) - the RunGame detour in
// source/display/maxfps.ixx, generalised so more than one option can share it.
//
// Dunia exports RunGame and FCSE calls it once, after every plugin has loaded, handing it the
// loader's own command line. Detouring it is the last seam at which anything can still change what
// CFCXGameCmdLineParser sees - and the engine reads these two switches exactly once, while it is
// starting, so there is no later opportunity and nothing to keep in sync afterwards.
#include "engine/command_line.h"

#include "fcse_api.h"

#include <windows.h>

#include <cstdio>
#include <cstring>

namespace {
    // Dunia's own export, as the launcher imports it.
    const char* const kRunGameExport = "?RunGame@@YA_NPAUHINSTANCE__@@PBD@Z";

    using RunGameFn = bool(__cdecl*)(HINSTANCE* instance, const char* commandLine);

    RunGameFn g_originalRunGame = nullptr;

    // What the switches below are appended to. Sized for the loader's command line and the handful
    // of short switches this plugin can add; a longer one is left alone rather than truncated.
    char g_commandLine[1024];

    // The switches set so far, keyed by name so the last call for one wins. Two is what this plugin
    // has; a third would be a line here.
    struct Switch {
        const char* name;
        char value[32];
        bool set;
    };

    Switch g_switches[2] = {};

    // RunGame is the engine thread, and does not return until the game does.
    DWORD g_engineThread = 0;

    // A switch the player typed themselves wins: they wrote it after installing this, and the
    // engine's first-match-wins parsing would otherwise take ours over theirs.
    bool PlayerPassed(const char* name) {
        const char* commandLine = GetCommandLineA();
        const size_t length = std::strlen(name);

        for (const char* at = commandLine; *at != '\0'; ++at) {
            if (*at != '-') {
                continue;
            }
            if (_strnicmp(at + 1, name, length) == 0) {
                const char after = at[1 + length];
                if (after == '\0' || after == ' ' || after == '\t') {
                    return true;
                }
            }
        }
        return false;
    }

    Switch* Find(const char* name) {
        for (Switch& entry : g_switches) {
            if (entry.name != nullptr && _stricmp(entry.name, name) == 0) {
                return &entry;
            }
        }
        for (Switch& entry : g_switches) {
            if (entry.name == nullptr) {
                entry.name = name;
                return &entry;
            }
        }
        return nullptr;
    }

    // Builds "<original> -name value ..." into g_commandLine. Returns false if it would not fit.
    bool BuildCommandLine(const char* original) {
        int written = std::snprintf(g_commandLine, sizeof(g_commandLine), "%s", original);
        if (written < 0 || static_cast<size_t>(written) >= sizeof(g_commandLine)) {
            return false;
        }

        for (const Switch& entry : g_switches) {
            if (!entry.set) {
                continue;
            }

            const int added = std::snprintf(g_commandLine + written, sizeof(g_commandLine) - written,
                                            entry.value[0] != '\0' ? " -%s %s" : " -%s", entry.name,
                                            entry.value);
            if (added < 0 || static_cast<size_t>(written + added) >= sizeof(g_commandLine)) {
                return false;
            }
            written += added;
        }

        return true;
    }

    bool __cdecl RunGameDetour(HINSTANCE* instance, const char* commandLine) {
        const FCSE_PluginAPI* api = FCSE::ApiPointer();

        g_engineThread = GetCurrentThreadId();

        const char* original = commandLine != nullptr ? commandLine : "";

        if (!BuildCommandLine(original)) {
            api->Log("command line: the engine's command line is too long to add to - passing it "
                     "through unchanged");
            return g_originalRunGame(instance, original);
        }

        if (std::strcmp(g_commandLine, original) != 0) {
            char line[512];
            std::snprintf(line, sizeof(line), "command line:%s",
                          g_commandLine + std::strlen(original));
            api->Log(line);
        }

        return g_originalRunGame(instance, g_commandLine);
    }
}

namespace UFCP {

void SetCommandLineSwitch(const char* name, const char* value) {
    if (PlayerPassed(name)) {
        return;
    }

    Switch* entry = Find(name);
    if (entry == nullptr) {
        return;
    }

    entry->set = value != nullptr;
    entry->value[0] = '\0';

    if (entry->set) {
        std::snprintf(entry->value, sizeof(entry->value), "%s", value);
    }
}

bool IsEngineThread() {
    return g_engineThread != 0 && GetCurrentThreadId() == g_engineThread;
}

// Installed even when no switch was requested: it is also what identifies the engine thread, which
// an option changed mid-session needs before it may touch the engine.
void InstallRunGameHook() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    void* runGame = reinterpret_cast<void*>(
        GetProcAddress(static_cast<HMODULE>(api->duniaModule), kRunGameExport));

    if (runGame == nullptr) {
        api->Log("command line: Dunia's RunGame export was not found - the options that add a "
                 "switch cannot apply");
        return;
    }

    api->Hook(runGame, reinterpret_cast<void*>(&RunGameDetour),
              reinterpret_cast<void**>(&g_originalRunGame));
}

}
