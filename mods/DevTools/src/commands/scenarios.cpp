#include "commands/scenarios.h"

namespace {
    using DevTools::Scenarios::Arena;
    using DevTools::Scenarios::Scenario;
    using DevTools::Scenarios::Soldier;

    constexpr const char* kRifleman = "enemy_archetypes.Red_Faction.Assault_Caucasian";
    constexpr const char* kMachineGunner = "enemy_archetypes.Red_Faction.LightMachineGunner_Caucasian";
    constexpr const char* kShotgunner = "enemy_archetypes.Red_Faction.ShotgunMan_Caucasian";
    constexpr const char* kSniper = "enemy_archetypes.Red_Faction.Sniper_Caucasian";
    // Only the sniper holds a post: a soldier spawned onto one is blind while he settles onto it,
    // seconds a scenario that starts in plain sight cannot spare.
    constexpr const char* kSniperPost = "STP_archetypes.Special.SniperPoint";

    // A soldier on the duel lane, `metres` east of the player's start at (30, 110).
    constexpr Soldier Lane(const char* archetype, float metres, float y = 110) {
        return {archetype, {30 + metres, y, 90}};
    }

    constexpr Soldier kRiflemanAt10[] = {Lane(kRifleman, 10)};
    constexpr Soldier kRiflemanAt20[] = {Lane(kRifleman, 20)};
    constexpr Soldier kRiflemanAt50[] = {Lane(kRifleman, 50)};
    constexpr Soldier kMachineGunnerAt20[] = {Lane(kMachineGunner, 20)};
    constexpr Soldier kTwoAt20[] = {Lane(kRifleman, 20, 107), Lane(kRifleman, 20, 113)};
    constexpr Soldier kShotgunnerAt10[] = {Lane(kShotgunner, 10)};
    constexpr Soldier kShotgunnerAt40[] = {Lane(kShotgunner, 40)};

    constexpr Scenario kDuelLane[] = {
        {"Rifleman at 10 m", "4, 7", kRiflemanAt10,
         "Step out in front of him. Then strafe across his line, and run straight at him.",
         "First shots miss while his aim settles. Far fewer hits while you cross his line."},
        {"Rifleman at 20 m", "2, 6, 7", kRiflemanAt20,
         "Take about 20 hits with invincibility on, then take hits with it off.",
         "About a fifth of the hits on your head. About 4 torso hits kill you; no softer last bar."},
        {"Rifleman at 50 m", "4", kRiflemanAt50,
         "Step out in front of him and stay in the open.",
         "He takes visibly longer to settle than at 10 m."},
        {"Machine gunner at 20 m", "5", kMachineGunnerAt20,
         "Stand still in the open while he fires full auto.",
         "Hits spread out within a burst; no shot is certain, point blank included."},
        {"Two riflemen at 20 m", "3", kTwoAt20,
         "Shoot one in the torso and the other in the arms and legs.",
         "About 2 torso hits down the first; about 4 limb hits the second."},
        {"Shotgunner at 10 m", "8", kShotgunnerAt10,
         "Stand in the open.",
         "His shot lines' chance, to compare with the 40 m run."},
        {"Shotgunner at 40 m", "8", kShotgunnerAt40,
         "Stand in the open.",
         "A much lower chance than at 10 m, and than a rifleman's at 40 m."},
    };

    constexpr Soldier kLowWallRifleman[] = {{kRifleman, {100, 70, 180}}};
    constexpr Scenario kLowWall[] = {
        {"Rifleman over the low wall", "9", kLowWallRifleman,
         "Crouch behind the low concrete wall. Then go out to the side and lie at the edge of his view.",
         "No shots until he sees you clearly."},
    };

    constexpr Soldier kLongWallSquad[] = {{kRifleman, {55, 400, 0}}, {kRifleman, {64, 400, 0}}, {kRifleman, {73, 400, 0}}};
    constexpr Scenario kLongWall[] = {
        {"Squad at the long wall", "11, 13, 14, 15", kLongWallSquad,
         "Get seen past a corner, duck back and move along the wall. Stay hidden 30 s. Peek twice from "
         "one corner, then 3 m along. Last, slip back unseen to the wall 20 m behind.",
         "They lose your true position within about 1 s; 'squad memory: still hunted'; quick shots at "
         "the same corner and a re-aim at the new spot ('watched spot: aim restarts'); 10 to 25 s "
         "after you slip away they search."},
    };

    constexpr Soldier kSuppressionPair[] = {{kRifleman, {464, 56, 90}}, {kRifleman, {464, 64, 90}}};
    constexpr Scenario kSuppression[] = {
        {"Two riflemen behind cover", "12", kSuppressionPair,
         "Fire near one of them without hitting him.",
         "He holds his fire for about 1 s, then misses at first; 'suppressed:' lines."},
    };

    constexpr Soldier kLoneSniper[] = {{kSniper, {480, 480, 137.7f}, kSniperPost}};
    constexpr Scenario kSniperLane[] = {
        {"Sniper at 150 m", "9, 15", kLoneSniper,
         "Stay behind the wall, then peek past one end, then past the other.",
         "No shots without a clear line of sight; he waits for the peek, and re-aims when you show "
         "at the other end."},
    };

    constexpr Arena kArenas[] = {
        {"Duel lane", {30, 110, 270}, kDuelLane},
        {"Low wall", {100, 37, 0}, kLowWall},
        {"Long wall", {64, 444, 180}, kLongWall},
        {"Suppression", {416, 60, 270}, kSuppression},
        {"Sniper", {380, 370, 317.7f}, kSniperLane},
    };
}

namespace DevTools::Scenarios {

std::span<const Arena> Arenas() { return kArenas; }

}
