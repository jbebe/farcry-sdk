"""Difficulty Overhaul's hard-to-hit soldiers on the test range. Three riflemen fight the player across the
low wall, invincibility on. Watched first: how long each cover attack stands him up to fire, and how often a
pop-up comes up and where. Then, each time one stands to fire, a burst past him 2 m to his side, and how
long he ducks. Last, an ambush: one of an idle group shot dead, each hit's health, and what the other two
do after it: the plans they start, how near they come to the body, and when they fight.

    python hard_to_hit.py [ambush | hidden]     only an ambush: on open ground at 25 m, or from tall grass at 80 m
    python hard_to_hit.py endure                single AK-47 shots at a rifleman's body, then another's legs
"""

import math
import re
import sys
import time
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import brain
from damage import SOLDIER_HIT, first_match
from perception import BLIND, CLEAR, SAVE, TRACE_SWITCH, WARM_UP, Log, put, run_case, spawn, spawn_yaw

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import Run, launch

# The player west of the suppression arena's east line, x 460 from y 51 to 69; the squad right behind it.
PLAYER = (432.0, 60.0)
SQUAD = ((464.0, 54.0), (464.0, 60.0), (464.0, 66.0))
EYE = 1.6
GUN = "weapons.Primary.AK47"
START_LIMIT = 30.0
WATCH = 30.0
DUCK_LIMIT = 60.0
DUCKS = 4
KILL_LIMIT = 30.0
# How far beside a soldier the burst passes, inside the game's 4 m crack and outside its 1 m capsule.
MISS = 2.0
BURST = 0.25
AFTER = 4.0
# Each cover attack and the wait he stands firing through.
FIRING = {"PopUpAttack": "WaitStand", "StrafeAttack": "CTaskWait1", "StandUpAttack": "CTaskWait1"}
# An ambush on an idle group of three, the nearest with his back to the player and the other two facing
# him: (across, ahead, yaw) from the nearest, with the player's facing as ahead and as yaw 0.
GROUP = ((0.0, 0.0, 0.0), (2.5, 1.5, 121.0), (-2.5, 1.5, 239.0))
# Each ambush: where the player crouches, the way he faces, how far off the group stands, how long the
# others are watched after the kill, and how high on the target he aims, the shot landing lower with
# range. On the open ground east of the hub, or 15 m into the grass field from its east edge, where a
# crouched player is hidden beyond 30 m.
AMBUSHES = {"ambush": ((440.0, 180.0), (0.0, 1.0), 25.0, 3.0, 1.2),
            "hidden": ((305.0, 340.0), (1.0, 0.0), 80.0, 20.0, 1.6)}
AMBUSH_GUN = "weapons.Primary.AS50"
CROUCHED_EYE = 1.0
SHOTS = 6
# Endurance: single AK-47 shots at one idle rifleman on the open ground 25 m off, aimed at his body or his
# legs, until he dies.
ENDURE_HEIGHTS = {"body": 1.2, "legs": 0.8}
ENDURE_SHOTS = 14
TIME = re.compile(r"^(\d\d):(\d\d):(\d\d\.\d+)")
SHOT = re.compile(r"shot at player:")
WHERE = re.compile(r"soldier (\w{8}) perception trace: .*he stands at (\S+), (\S+)")
KILLED = re.compile(r"soldier (\w{8}) hit: .*, killed")
COMBAT = brain.COMBAT.rstrip("/")


def seconds(line):
    m = TIME.match(line)
    return int(m.group(1)) * 3600 + int(m.group(2)) * 60 + float(m.group(3)) if m else None


class Fight:
    """Every soldier's tasks with their times, and where each last stood."""

    def __init__(self, known):
        self.known = known
        self.tasks = defaultdict(list)
        self.track = defaultdict(list)
        self.lines = []

    def read(self, lines):
        self.lines += lines
        for line in lines:
            t = seconds(line)
            if m := brain.LINE.search(line):
                task = int(m.group(2), 16)
                self.tasks[m.group(1)].append((t, self.known.get(task, f"{task:08X}")))
            elif m := WHERE.search(line):
                self.track[m.group(1)].append((t, float(m.group(2)), float(m.group(3))))

    def at(self, soldier, t=None):
        """Where he stood at `t`, or last."""
        track = self.track[soldier]
        if not track:
            return None
        _, x, y = track[-1] if t is None else min(track, key=lambda p: abs(p[0] - t))
        return x, y

    def alive(self):
        killed = {m.group(1) for m in map(KILLED.search, self.lines) if m}
        return [soldier for soldier in self.track if soldier not in killed]


def attack_of(task):
    """The cover attack a task runs under, as `<band path>/<attack>`, or None."""
    for attack in FIRING:
        head, found, _ = task.partition(f"/{attack}")
        if found:
            return head + "/" + attack
    return None


def ended(plan, task):
    """Whether a task started means the cover attack `plan` has ended: its parent's stop task, or combat over."""
    return task == plan.rsplit("/", 1)[0] + "/SetDontReload1" or task == COMBAT


def runs(tasks):
    """Each run of a cover attack: its plan, start, end, and its own tasks, all as (time, task)."""
    out, current = [], None
    for t, task in tasks:
        if current and ended(current[0], task):
            current[2] = t
            out.append(current)
            current = None
        plan = attack_of(task)
        if plan and task == plan and current is None:
            current = [plan, t, None, []]
        elif current and plan == current[0] and "/ScanReloadIsNeeded_BP1/" not in task:
            current[3].append((t, task))
    return out


def stood(run):
    """Seconds he stood firing each time in a run: from the firing wait to the next task of the attack."""
    plan, _, _, tasks = run
    wait = plan + "/" + FIRING[plan.rsplit("/", 1)[-1]]
    return [t2 - t for (t, task), (t2, _) in zip(tasks, tasks[1:]) if task == wait]


def report_watch(run_, fight):
    for soldier, tasks in fight.tasks.items():
        last = None
        for run in runs(tasks):
            plan, start, end, _ = run
            ups = stood(run)
            here = fight.at(soldier, start)
            moved = "" if last is None or here is None else f", {math.dist(here, last):.1f} m from the last"
            last = here or last
            length = "running at the end" if end is None else f"{end - start:.1f} s"
            run_.note(f"{soldier}: {plan.rsplit('/', 1)[-1]} {length}, stood to fire {len(ups)} times for "
                      f"{', '.join(f'{u:.2f}' for u in ups) or '-'} s{moved}")


def aim(game, player, at, miss=0.0, height=1.3, eye=EYE):
    """Points the view at a spot `miss` m to the side of `at`, `height` m above the ground."""
    dx, dy = at[0] - player[0], at[1] - player[1]
    flat = math.hypot(dx, dy)
    tx, ty = at[0] - dy / flat * miss, at[1] + dx / flat * miss
    pitch = math.degrees(math.atan2(height - eye, math.hypot(tx - player[0], ty - player[1])))
    game.look(spawn_yaw(tx - player[0], ty - player[1]), pitch)


def ducks(run_, game, log, fight):
    """A burst past each soldier who stands to fire; how soon he ducks and how long until the attack ends."""
    done = 0
    end = time.monotonic() + DUCK_LIMIT
    seen = {soldier: len(tasks) for soldier, tasks in fight.tasks.items()}
    while done < DUCKS and time.monotonic() < end:
        fight.read(log.new_lines())
        target = None
        for soldier, tasks in fight.tasks.items():
            for _, task in tasks[seen.get(soldier, 0):]:
                plan = attack_of(task)
                if plan and task == plan + "/" + FIRING[plan.rsplit("/", 1)[-1]] and fight.at(soldier):
                    target = soldier
            seen[soldier] = len(tasks)
        if target is None:
            time.sleep(0.05)
            continue
        aim(game, PLAYER, fight.at(target), MISS)
        before, read = len(fight.tasks[target]), len(fight.lines)
        game.fire(BURST)
        time.sleep(AFTER)
        fight.read(log.new_lines())
        after = fight.tasks[target][before:]
        done += 1
        hit = "; a round hit him" if any(f"soldier {target} hit:" in line for line in fight.lines[read:]) else ""
        duck = next(((t, task) for t, task in after if task.endswith("/Duck")), None)
        if duck is None:
            first = [task.rsplit("/", 1)[-1] for _, task in after if attack_of(task)][:6]
            run_.note(f"burst {done} past {target}: no duck; his attack's tasks then {', '.join(first) or 'none'}{hit}")
        else:
            plan = attack_of(duck[1])
            crack = next(t for t, task in after if task.endswith("/CrackAtPlayer"))
            out = next((t for t, task in after if t >= duck[0] and ended(plan, task)), None)
            down = "past the watch" if out is None else f"{out - duck[0]:.2f} s"
            run_.note(f"burst {done} past {target}: ducked at his {plan.rsplit('/', 1)[-1]}, "
                      f"{crack - after[0][0]:.2f} s after the burst's first task; the attack ended {down} later{hit}")
        seen = {soldier: len(tasks) for soldier, tasks in fight.tasks.items()}


def set_up(game, log, known, spot, facing, distance, gun, group):
    """The player crouched at `spot`, facing `facing`, with `gun`; `group` spawned `distance` ahead of him."""
    game.lua(CLEAR)
    game.give(gun)
    turn = spawn_yaw(*facing)
    put(game, *spot, turn)
    game.action("crouch", 1.0)
    spawn(game, BLIND, 180.0)
    time.sleep(3.0)
    log.new_lines()
    c, s = math.cos(math.radians(turn)), math.sin(math.radians(turn))
    for across, ahead, yaw in group:
        ahead += distance
        spawn(game, (spot[0] + across * c - ahead * s, spot[1] + across * s + ahead * c), yaw + turn)
    time.sleep(3.0)
    fight = Fight(known)
    fight.read(log.new_lines())
    return fight


def shoot(game, log, fight, spot, distance, height, shots):
    """Single shots at the nearest soldier of the group, `height` m up, until he dies or `shots` are fired;
    returns the last one shot at, or None, and whether he died."""
    target = None
    for _ in range(shots):
        fight.read(log.new_lines())
        group = [s for s in fight.alive() if math.dist(fight.at(s), spot) < distance + 35.0]
        if not group:
            break
        target = min(group, key=lambda s: math.dist(fight.at(s), spot))
        aim(game, spot, fight.at(target), 0.0, height, CROUCHED_EYE)
        game.fire(0.05)
        time.sleep(0.4)
        fight.read(log.new_lines())
        if target not in fight.alive():
            return target, True
    return target, False


def hits_on(fight, soldier):
    """The combat log's lines of each hit on `soldier`."""
    return [line for line in fight.lines if f"soldier {soldier} hit:" in line]


def brief(hit):
    """A hit line as its level, body part, distance and health."""
    m = SOLDIER_HIT.search(hit)
    return f"level {m.group(1)}, {m.group(2)}, at {m.group(3)} m, {m.group(4)}"


def endure(run_, game, log):
    """Single AK-47 shots at one idle rifleman's body, then another's legs, until each dies; every hit."""
    spot, facing, distance = AMBUSHES["ambush"][:3]
    for part, height in ENDURE_HEIGHTS.items():
        fight = set_up(game, log, {}, spot, facing, distance, GUN, GROUP[:1])
        target, dead = shoot(game, log, fight, spot, distance, height, ENDURE_SHOTS)
        if not dead:
            time.sleep(1.0)
            fight.read(log.new_lines())
        hits = "; ".join(map(brief, hits_on(fight, target)))
        run_.note(f"endure, {part}: {'dead' if dead else 'alive'} after: {hits or 'no hit'}")


def ambush(run_, game, log, known, plans, name):
    """Single shots at the nearest of an idle group until he dies; then each other soldier's plans with their
    times from the death, his distance from the body then and at his nearest, and combat's start."""
    spot, facing, distance, watch, height = AMBUSHES[name]
    fight = set_up(game, log, known, spot, facing, distance, AMBUSH_GUN, GROUP)
    victim, dead = shoot(game, log, fight, spot, distance, height, SHOTS)
    if not dead:
        run_.note(f"{name}: nobody died")
        return
    hits = hits_on(fight, victim)
    t_dead = seconds(hits[-1])
    body = fight.at(victim, t_dead)
    run_.note(f"{name}: {victim} dropped after {len(hits)} hits: " + "; ".join(map(brief, hits)))
    time.sleep(watch)
    fight.read(log.new_lines())
    for soldier, tasks in fight.tasks.items():
        if soldier == victim:
            continue
        ran = [(t - t_dead, "combat" if task == COMBAT else task.rsplit("/", 1)[-1])
               for t, task in tasks if t >= t_dead and (task in plans or task == COMBAT)]
        ran = [r for i, r in enumerate(ran) if i == 0 or r[1] != ran[i - 1][1]]
        track = [math.dist((x, y), body) for t, x, y in fight.track[soldier] if t >= t_dead]
        where = f"{track[0]:.0f} m from the body, at his nearest {min(track):.1f} m" if track else "never placed"
        run_.note(f"{name}, {soldier}: {where}; {', '.join(f'{p} +{t:.1f} s' for t, p in ran) or 'no plan started'}")
    (run_.folder / f"{name}.log").write_text("\n".join(fight.lines) + "\n", encoding="utf-8")


def watch(run_, game, log, known):
    """The squad fighting the player across the barriers: each cover attack, then the ducks."""
    run_case(game, log, WARM_UP)
    game.lua(CLEAR)
    put(game, *PLAYER, spawn_yaw(1.0, 0.0))
    log.new_lines()
    for at in SQUAD:
        spawn(game, at, spawn_yaw(-1.0, 0.0))
    fight = Fight(known)
    lines = []
    if first_match(log, SHOT, START_LIMIT, lines) is None:
        run_.note("they never opened fire")
    fight.read(lines)
    time.sleep(WATCH)
    fight.read(log.new_lines())
    report_watch(run_, fight)
    ducks(run_, game, log, fight)
    (run_.folder / "fight.log").write_text("\n".join(fight.lines) + "\n", encoding="utf-8")


def main():
    run_ = Run("hard-to-hit")
    log = Log()
    only = next((name for name in (*AMBUSHES, "endure") if name in sys.argv[1:]), None)
    traced = only != "endure"
    known, plans = brain.names() if traced else ({}, set())
    with launch(SAVE, [TRACE_SWITCH, brain.SWITCH] if traced else [TRACE_SWITCH]) as game:
        game.option("Invincibility", 1)
        game.option("Infinite ammo", 1)
        game.give(GUN)
        if not traced:
            endure(run_, game, log)
        else:
            if only is None:
                watch(run_, game, log, known)
            ambush(run_, game, log, known, plans, only or "ambush")
    print(run_.folder)


if __name__ == "__main__":
    main()
