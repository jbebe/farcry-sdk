"""Difficulty Overhaul's blood on the test range: a hit sprays blood only as its real round would, on a
soldier and on the player. The player shoots a rifleman 10 m off, standing so the round lands high and
crouched so it lands on his body, with a pistol, a rifle, a shotgun and the AS50; then a rifleman and a
shotgunner shoot the player, who faces them. A shot is taken straight after each round, and again a
moment later; each note names the body part hit.

    python blood.py
"""

import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from damage import PLAYER_HIT, SHOOTER_LIMIT, SOLDIERS_GUN, face_shooter, first_match, shoot_at
from last_bar import SHOTGUNNER
from perception import CLEAR, LANE_MIDDLE, RIFLEMAN, SAVE, Log, put, spawn_yaw

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from fc2drive import Run, launch

GUNS = ("weapons.Secondary.Makarov", "weapons.Primary.AK47", "weapons.Primary.SPAS12", "weapons.Primary.AS50")
SHOOTERS = (("rifleman", RIFLEMAN), ("shotgunner", SHOTGUNNER))
TRIES = 8
LATER = 0.3


def shots(run, game, label):
    run.shot(game, label)
    time.sleep(LATER)
    run.shot(game, label + " later")


def shoot_soldier(run, game, log, archetype, crouched):
    label = f"{archetype} {'crouched' if crouched else 'standing'}"
    hit = shoot_at(game, log, crouched, TRIES, lambda attempt: shots(run, game, f"{label} round {attempt}"))
    run.note(f"{label}: no hit in {TRIES} rounds" if hit is None else
             f"{label}: hit, {hit.group(2)} at {hit.group(3)} m, on the last round shot")


def shot_by(run, game, log, label, archetype):
    game.lua(CLEAR)
    game.give(SOLDIERS_GUN)
    game.option("Invincibility", 0)
    face_shooter(game, log, archetype)
    # He is alerted by a shot fired away from him; now the player faces him.
    put(game, *LANE_MIDDLE, spawn_yaw(1.0, 0.0))
    hit = first_match(log, PLAYER_HIT, SHOOTER_LIMIT)
    # Before the shots, or a rifleman's next rounds kill the player while they are taken.
    game.option("Invincibility", 1)
    if hit:
        shots(run, game, f"{label} hits the player")
        run.note(f"{label} on the player: {hit.group(2)} at {hit.group(3)} m")
    else:
        run.note(f"{label} on the player: never hit him")


def main():
    run = Run("blood")
    log = Log()
    with launch(SAVE) as game:
        game.option("Invincibility", 1)
        for archetype in GUNS:
            game.give(archetype)
            for crouched in (False, True):
                shoot_soldier(run, game, log, archetype, crouched)
        for label, archetype in SHOOTERS:
            shot_by(run, game, log, label, archetype)
    print(run.folder)


if __name__ == "__main__":
    main()
