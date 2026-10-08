# Changelog

Notable changes to Sound Overhaul, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Changed
- **Needs FCSE 1.4.0**, rebuilt for its new plugin interface. Older FCSE no longer loads it.

## [1.0.0] - 2026-10-01

### Added
- **The game's own reverb, back.** Far Cry 2 authored reverb for its buildings, jungle, savannah and
  desert, but it only plays on an EAX sound card. The bundled DSOAL provides one in software, and the
  reverb now changes as you move between places.
- **Every building's reverb fits it**, chosen by its type and size: small, medium and large rooms, a
  hall, a hangar and a ringing metal box. Shots in a small room have a short, hard slap off its walls.
- **Walls muffle by their material**, from concrete and mud down to a bus's open windows. Open doors
  and windows let the outside in, so a house with its windows open is barely muffled.
- **New gunshots** for every single-player weapon, first and third person, from Dark Signal Weapon
  Soundscape. Full-auto weapons play a shot on every round.
- **Echoes** after every unsuppressed shot, big or small by calibre. Enemy shots echo too, and only
  the last echo of a burst rings out.
- **Distant gunfire is a crack.** From about 40 m an enemy's shot turns into its crack and echo; past
  80 m the crack is all you hear.
- **New grenade and rocket explosions**, close, medium and far.
- **A Combat music setting** in the Mod Configuration menu. With it off, fights play without music, so
  enemies can be heard, and the calm music comes back afterwards.

### Changed
- **Voices fall off like real speech**: quieter by 20 m, gone by 50 m.
- **Your own shots fill the room** with a stronger reverb send.
- **Your own pickups are never muffled.** Grabbing ammo in a safehouse sounded as if behind a wall.
- **No more distant dogs and jackals** in the ambience. Other animals stay.
- **A rougher, quieter idle** for the Datsun pickup.
