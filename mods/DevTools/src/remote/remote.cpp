// Commands arrive on a thread of the pipe's own and run at the end of a frame, on the engine's thread,
// which this one waits for. So a command that loads a world answers when the world has loaded.
#include "remote/remote.h"

#include "commands/guns.h"
#include "engine/actions.h"
#include "engine/ai_sight.h"
#include "engine/camera.h"
#include "engine/clock.h"
#include "engine/console.h"
#include "engine/entity.h"
#include "engine/frame_capture.h"
#include "engine/game_thread.h"
#include "engine/input.h"
#include "engine/inventory.h"
#include "engine/lua.h"
#include "engine/player.h"
#include "engine/spawn.h"
#include "engine/weather.h"
#include "engine/world.h"
#include "fcse_api.h"

#include <windows.h>

#include <objbase.h>

#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <functional>
#include <future>
#include <memory>
#include <optional>
#include <string>
#include <utility>
#include <vector>

namespace {
    using namespace DevTools;
    using std::chrono::seconds;

    constexpr char kSwitch[] = "-devtoolsremote";
    constexpr char kPipe[] = R"(\\.\pipe\DevToolsRemote)";
    constexpr DWORD kBufferSize = 64 * 1024;

    // How long `state` waits for a frame before saying the game is busy, how long a frame may take in
    // a menu or a world, and how long anything that loads one may take.
    constexpr seconds kBusy{1};
    constexpr seconds kFrame{5};
    constexpr seconds kLoad{180};

    std::string Quote(const std::string& text) {
        std::string quoted = "\"";
        for (const char c : text) {
            if (c == '"' || c == '\\') {
                quoted += '\\';
                quoted += c;
            } else if (static_cast<uint8_t>(c) < 0x20) {
                char escaped[8];
                std::snprintf(escaped, sizeof(escaped), "\\u%04x", c);
                quoted += escaped;
            } else {
                quoted += c;
            }
        }
        return quoted + "\"";
    }

    std::string Ok(const std::string& fields = "") {
        return "{\"ok\":true" + (fields.empty() ? "" : "," + fields) + "}";
    }

    std::string Fail(const std::string& error) { return "{\"ok\":false,\"error\":" + Quote(error) + "}"; }

    // Empty, or why a thing could not be done.
    std::string Result(const std::string& error) { return error.empty() ? Ok() : Fail(error); }

    const char* Bool(bool value) { return value ? "true" : "false"; }

    // Runs `job` at the end of a frame and answers with what it returns, or `missed` if no frame ends
    // within `timeout`. The job still runs when its frame comes, so it owns everything it touches.
    std::string OnFrame(std::function<std::string()> job, seconds timeout = kFrame,
                        const std::string& missed = Fail("no frame reached the end in time")) {
        auto answer = std::make_shared<std::promise<std::string>>();
        std::future<std::string> future = answer->get_future();
        GameThread::Post([answer, job = std::move(job)] { answer->set_value(job()); });
        return future.wait_for(timeout) == std::future_status::ready ? future.get() : missed;
    }

    // The first word of `text`, and what follows it.
    std::pair<std::string, std::string> FirstWord(const std::string& text) {
        const size_t split = text.find(' ');
        return {text.substr(0, split), split == std::string::npos ? "" : text.substr(split + 1)};
    }

    std::vector<float> Floats(const std::string& text) {
        std::vector<float> values;
        const char* at = text.c_str();
        char* end = nullptr;
        for (float value = std::strtof(at, &end); end != at; value = std::strtof(at, &end)) {
            values.push_back(value);
            at = end;
        }
        return values;
    }

    std::string Vec(const float* v) {
        char text[64];
        std::snprintf(text, sizeof(text), "[%.3f,%.3f,%.3f]", v[0], v[1], v[2]);
        return text;
    }

    std::string Placement(void* entity) {
        char yaw[32];
        std::snprintf(yaw, sizeof(yaw), ",\"yaw\":%.2f", Entity::Degrees(Entity::Yaw(entity)));
        return "\"pos\":" + Vec(Entity::Matrix(entity) + 12) + yaw;
    }

    std::string CameraState(void* manager) {
        Camera::View view{};
        if (!Camera::CurrentView(manager, view)) {
            return "";
        }
        char fields[160];
        std::snprintf(fields, sizeof(fields), ",\"yaw\":%.2f,\"pitch\":%.2f,\"roll\":%.2f,\"fov\":%.2f}",
                      Entity::Degrees(view.yaw), Entity::Degrees(view.pitch), Entity::Degrees(view.roll),
                      Entity::Degrees(view.fov));
        return ",\"camera\":{\"pos\":" + Vec(view.position) + fields;
    }

    std::string State() {
        const bool inWorld = Player::Local() != nullptr;
        const bool loading = World::LoadingScreen();
        const bool streaming = World::Streaming();

        char fields[256];
        std::snprintf(fields, sizeof(fields),
                      "\"window\":%zu,\"in_world\":%s,\"loading\":%s,\"streaming\":%s,\"ready\":%s,\"recording\":%s,\"playing\":%s",
                      reinterpret_cast<size_t>(Input::Window()), Bool(inWorld), Bool(loading), Bool(streaming),
                      Bool(inWorld && !loading && !streaming), Bool(Actions::Recording()), Bool(Actions::Playing()));
        std::string state = fields;

        void* manager = inWorld ? Player::CameraManager() : nullptr;
        void* ref = Entity::AcquireFocusRef(manager);
        if (void* entity = Entity::Of(ref)) {
            std::snprintf(fields, sizeof(fields), ",\"pitch\":%.2f,\"crouched\":%s,\"in_vehicle\":%s",
                          Entity::Degrees(Player::LookPitch()), Bool(Player::Crouched()),
                          Bool(Player::Vehicle(Player::Pawn()) != nullptr));
            state += "," + Placement(entity) + fields + CameraState(manager);
        }
        Entity::ReleaseRef(ref);

        const Inventory::Held held = Inventory::InHandNow();
        std::snprintf(fields, sizeof(fields), ",\"magazine\":%d,\"spare\":%d,\"capacity\":%d,\"health\":%.1f",
                      held.magazine, held.spare, held.capacity, held.health);
        return Ok(state + ",\"gun\":" + Quote(held.name) + fields);
    }

    std::string LuaAnswer(const Lua::Outcome& outcome) {
        std::string lines;
        for (const std::string& line : outcome.lines) {
            lines += (lines.empty() ? "" : ",") + Quote(line);
        }
        if (!outcome.ran) {
            return "{\"ok\":false,\"error\":\"the chunk did not run\",\"lines\":[" + lines + "]}";
        }
        return Ok("\"value\":" + Quote(outcome.value) + ",\"lines\":[" + lines + "]");
    }

    std::string Shot(const std::string& path) {
        std::future<FrameCapture::Image> next = FrameCapture::Next();
        if (next.wait_for(kFrame) != std::future_status::ready) {
            return Fail("no frame was presented in time");
        }
        const FrameCapture::Image image = next.get();
        const std::string error = image.error.empty() ? FrameCapture::SavePng(image, path) : image.error;
        if (!error.empty()) {
            return Fail(error);
        }
        return Ok("\"width\":" + std::to_string(image.width) + ",\"height\":" + std::to_string(image.height));
    }

    std::string GunList() {
        std::string list;
        for (const Guns::Class& gunClass : Guns::All()) {
            for (const Guns::Gun& gun : gunClass.guns) {
                list += (list.empty() ? "{" : ",{");
                list += "\"label\":" + Quote(gun.label) + ",\"archetype\":" + Quote(gun.archetype) +
                        ",\"class\":" + Quote(gunClass.name) + "}";
            }
        }
        return Ok("\"guns\":[" + list + "]");
    }

    std::string Give(const std::string& archetype) {
        for (const Guns::Class& gunClass : Guns::All()) {
            for (const Guns::Gun& gun : gunClass.guns) {
                if (archetype == gun.archetype) {
                    return OnFrame([archetype = gun.archetype, category = gunClass.category] {
                        return Inventory::GiveNow(archetype, category) ? Ok() : Fail(Inventory::LastResult());
                    });
                }
            }
        }
        return Fail(archetype + " is not one of the Armory's guns");
    }

    // The state a frame after a turn or a move that went through, once the body has taken the look's
    // facing; or why it did not.
    std::string StateAfter(const std::string& done) { return done == Ok() ? OnFrame(State) : done; }

    std::string Goto(const std::string& arguments) {
        const std::vector<float> at = Floats(arguments);
        if (at.size() < 3) {
            return Fail("goto takes x y z, then a yaw in degrees if wanted");
        }
        const std::optional<float> yaw = at.size() > 3 ? std::optional<float>(at[3]) : std::nullopt;
        return StateAfter(OnFrame([at, yaw] {
            return World::Teleport(at[0], at[1], at[2], yaw) ? Ok()
                                                             : Fail("there is no player to move, or no teleport in this build");
        }, kLoad));
    }

    std::string Look(const std::string& arguments) {
        const std::vector<float> at = Floats(arguments);
        if (at.size() != 2) {
            return Fail("look takes a yaw and a pitch, in degrees");
        }
        return StateAfter(OnFrame([at] { return Player::FaceLook(at[0], at[1]) ? Ok() : Fail("there is no player to turn"); }));
    }

    // Spawns into the table `clear` empties, and answers with the entity's id.
    std::string SpawnKept(const std::string& arguments) {
        const auto [archetype, rest] = FirstWord(arguments);
        const std::vector<float> at = Floats(rest);
        if (archetype.empty() || at.size() < 3) {
            return Fail("spawn takes an archetype, x y z, then a yaw in degrees if wanted");
        }
        return OnFrame([archetype, at] {
            const float yaw = at.size() > 3 ? at[3] : 0.0f;
            const Lua::Outcome spawned = Lua::Run(Spawn::Kept(archetype.c_str(), at[0], at[1], at[2], yaw));
            const Lua::Outcome id = Lua::Evaluate("TestRange[TestRange.n]");
            if (!spawned.ran || !id.ran) {
                return LuaAnswer(spawned.ran ? id : spawned);
            }
            void* ref = Entity::AcquireRef(std::strtoull(id.value.c_str(), nullptr, 10));
            const bool made = Entity::Of(ref) != nullptr;
            Entity::ReleaseRef(ref);
            return made ? Ok("\"id\":" + Quote(id.value)) : Fail(archetype + " made no entity");
        }, kLoad);
    }

    // An entity by its id, or the player: its name, position, yaw and health, and whether it has a
    // component of the class named, when one is.
    std::string EntityState(const std::string& arguments) {
        const auto [which, component] = FirstWord(arguments);
        return OnFrame([which, component] {
            void* ref = which == "player" ? Entity::AcquireFocusRef(Player::CameraManager())
                                          : Entity::AcquireRef(std::strtoull(which.c_str(), nullptr, 10));
            void* entity = Entity::Of(ref);
            if (entity == nullptr) {
                Entity::ReleaseRef(ref);
                return Fail("no entity " + which);
            }
            std::string answer = "\"name\":" + Quote(Entity::Name(entity)) + "," + Placement(entity);
            if (const std::optional<Entity::Health> health = Entity::HealthOf(entity)) {
                char fields[64];
                std::snprintf(fields, sizeof(fields), ",\"health\":%.2f,\"max_health\":%.2f", health->value, health->max);
                answer += fields;
            }
            if (!component.empty()) {
                answer += std::string(",\"has\":") + Bool(Entity::Component(entity, component.c_str()) != nullptr);
            }
            Entity::ReleaseRef(ref);
            return Ok(answer);
        });
    }

    // Each soldier whose sight of the player updated in the last second. Copying starts when this is
    // first asked, so the first answer can be empty.
    std::string Sights() {
        std::vector<AiSight::Sight> sights;
        AiSight::Snapshot(sights);
        std::string list;
        for (const AiSight::Sight& sight : sights) {
            const float distance = std::hypot(sight.target[0] - sight.eye[0], sight.target[1] - sight.eye[1],
                                              sight.target[2] - sight.eye[2]);
            char fields[160];
            std::snprintf(fields, sizeof(fields), ",\"distance\":%.2f,\"known\":%s,\"visibility\":%.3f,\"plainly_seen\":%s}",
                          distance, Bool(sight.known), sight.visibility, Bool(sight.plainlySeen));
            list += (list.empty() ? "{\"pos\":" : ",{\"pos\":") + Vec(sight.feet.data()) + fields;
        }
        return Ok("\"soldiers\":[" + list + "]");
    }

    std::string Trim(const std::string& text) {
        const size_t first = text.find_first_not_of(' ');
        return first == std::string::npos ? "" : text.substr(first, text.find_last_not_of(' ') - first + 1);
    }

    // Any mod's setting for this launch, spelled as in fcse.ini: "[DevTools] Invincibility = true".
    std::string ModSetting(const std::string& arguments) {
        const size_t close = arguments.find(']');
        const size_t equals = close == std::string::npos ? std::string::npos : arguments.find('=', close);
        if (arguments.rfind('[', 0) != 0 || equals == std::string::npos) {
            return Fail("setting takes [plugin] name = value, as fcse.ini spells it");
        }
        const std::string plugin = arguments.substr(1, close - 1);
        const std::string name = Trim(arguments.substr(close + 1, equals - close - 1));
        const std::string value = Trim(arguments.substr(equals + 1));
        return OnFrame([plugin, name, value] {
            return FCSE::ApiPointer()->SetSetting(plugin.c_str(), name.c_str(), value.c_str())
                       ? Ok()
                       : Fail("[" + plugin + "] " + name + " = " + value + " was refused; fcse.log says why");
        });
    }

    std::string Pause(const std::string& choice) {
        if (choice != "on" && choice != "off") {
            return Fail("pause takes on or off");
        }
        if (!Clock::Found()) {
            return Fail("this build's game clock was not found");
        }
        return OnFrame([stopped = choice == "on"] {
            Clock::SetStopped(stopped);
            return Ok();
        });
    }

    std::string Action(const std::string& arguments) {
        const auto [name, values] = FirstWord(arguments);
        if (name.empty()) {
            return Fail("action takes a signal's name, then its values");
        }
        Actions::Send(FCSE::Crc32(name.c_str()), Floats(values));
        return Ok();
    }

    // Clear is no storm, rain or wind. Engine hands them back.
    std::string SetWeather(const std::string& choice) {
        Weather::Wanted wanted;
        if (choice == "clear") {
            wanted.storm = Weather::Mode::Off;
            wanted.rain = Weather::Mode::Off;
            wanted.wind = Weather::Mode::Off;
        } else if (choice != "engine") {
            return Fail("weather takes clear or engine");
        }
        Weather::Set(wanted);
        return Ok();
    }

    std::string Handle(const std::string& line) {
        const auto [verb, rest] = FirstWord(line);

        if (verb == "state") {
            return OnFrame(&State, kBusy, Ok("\"ready\":false,\"busy\":true"));
        }
        if (verb == "lua") {
            return OnFrame([code = rest] { return LuaAnswer(Lua::Run(code)); }, kLoad);
        }
        if (verb == "eval") {
            return OnFrame([expression = rest] { return LuaAnswer(Lua::Evaluate(expression)); }, kLoad);
        }
        if (verb == "console") {
            return OnFrame([text = rest] { return Console::Execute(text.c_str()) ? Ok() : Fail("the console is not ready"); }, kLoad);
        }
        if (verb == "action") {
            return Action(rest);
        }
        if (verb == "shot") {
            return Shot(rest);
        }
        if (verb == "goto") {
            return Goto(rest);
        }
        if (verb == "guns") {
            return GunList();
        }
        if (verb == "give") {
            return Give(rest);
        }
        if (verb == "spawnpickup") {
            Spawn::Pickup(rest.c_str());
            return Ok();
        }
        if (verb == "rounds") {
            Inventory::SetRounds(std::atoi(rest.c_str()));
            return Ok();
        }
        if (verb == "gunhealth") {
            Inventory::SetHealth(std::strtof(rest.c_str(), nullptr));
            return Ok();
        }
        if (verb == "look") {
            return Look(rest);
        }
        if (verb == "spawn") {
            return SpawnKept(rest);
        }
        if (verb == "clear") {
            return OnFrame([] { return LuaAnswer(Lua::Run(Spawn::kClear)); }, kLoad);
        }
        if (verb == "entity") {
            return EntityState(rest);
        }
        if (verb == "sight") {
            return Sights();
        }
        if (verb == "setting") {
            return ModSetting(rest);
        }
        if (verb == "pause") {
            return Pause(rest);
        }
        if (verb == "weather") {
            return SetWeather(rest);
        }
        if (verb == "record") {
            Actions::StartRecording();
            return Ok();
        }
        if (verb == "stoprecord") {
            return Result(Actions::StopRecording(rest));
        }
        if (verb == "play") {
            return Result(Actions::Play(rest));
        }
        return Fail("unknown command: " + verb);
    }

    void ServeClient(HANDLE pipe) {
        std::string received;
        char buffer[4096];
        DWORD read = 0;
        while (ReadFile(pipe, buffer, sizeof(buffer), &read, nullptr) && read > 0) {
            received.append(buffer, read);
            for (size_t end = received.find('\n'); end != std::string::npos; end = received.find('\n')) {
                std::string line = received.substr(0, end);
                received.erase(0, end + 1);
                if (!line.empty() && line.back() == '\r') {
                    line.pop_back();
                }

                const std::string reply = Handle(line) + "\n";
                DWORD written = 0;
                if (!WriteFile(pipe, reply.data(), static_cast<DWORD>(reply.size()), &written, nullptr)) {
                    return;
                }
            }
        }
    }

    DWORD WINAPI Serve(void*) {
        // SavePng's image encoder is a COM object.
        CoInitializeEx(nullptr, COINIT_MULTITHREADED);

        while (true) {
            HANDLE pipe = CreateNamedPipeA(kPipe, PIPE_ACCESS_DUPLEX, PIPE_TYPE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
                                           1, kBufferSize, kBufferSize, 0, nullptr);
            if (pipe == INVALID_HANDLE_VALUE) {
                FCSE::Logf("remote: the pipe could not be created, error %lu", GetLastError());
                return 1;
            }
            if (ConnectNamedPipe(pipe, nullptr) || GetLastError() == ERROR_PIPE_CONNECTED) {
                ServeClient(pipe);
            }
            DisconnectNamedPipe(pipe);
            CloseHandle(pipe);
        }
    }
}

namespace DevTools::Remote {

void Start() {
    if (std::strstr(GetCommandLineA(), kSwitch) == nullptr) {
        return;
    }
    Actions::Install();
    World::Install();

    HANDLE thread = CreateThread(nullptr, 0, &Serve, nullptr, 0, nullptr);
    if (thread == nullptr) {
        FCSE::ApiPointer()->Log("remote: the pipe's thread could not be started");
        return;
    }
    CloseHandle(thread);
    FCSE::Logf("remote: serving on %s", kPipe);
}

}
