# Vehicle Overhaul

Purpose: Off-road driving: the car you drive handles, revs and sounds like the real vehicle it depicts, with touches to the cars' looks.
Stage: active

## Features

- [done] Off-road physics on the car you drive: no limiter, weak locking brakes, the ground's grip, soft springs - user: "feel good"
- [done] Each car tuned as the real vehicle it depicts, its own start values per car - Datsun, Liberty, Wrangler, Buggy "good"; Rover "golden"
- [done] Real centre of mass and front-axle weight per car, from the real vehicle's tip-over g - Datsun's 54% "perfect"
- [done] Solid axles on the Land Rover, ZIL-130 and Unimog, damped at the wheels, the wheels hanging and leaning with the beam
- [done] The big truck's inertia at 3.6 times the game's, so it rocks and turns slowly
- [done] A wider steering lock, at a crawl and at speed
- [done] A real engine: revs from Havok's gearbox over each car's idle and redline, a slipping clutch - user: "sounds more realistic"
- [done] Gear changes as in Far Cry 3: never with the clutch out, down in 0.1 s, a car climbing a hill holds its gear
- [done] A rev limiter at the redline, in top gear and reverse
- [done] The ZIL-130's real five-speed gearbox
- [done] An automatic gearbox (shift times 0) on the Jeep Liberty - unfelt
- [done] Cars known by their archetype's iAnimVehicleType, so mods that retune cars keep them known - 1.0.1 fixed wrong-tuning entries
- [done] Datsun 1200 engine sound, sliced from rising sweeps - user: "perfect" (7485455e)
- [done] ZIL-130 engine sound from its own V8 takes, spread up the revs - user: "it was better now!"; ZIL done for now (2026-10-17)
- [done] Unimog 406 engine sound, an idle and sweep slices - user: "good" (ca2b4e4b)
- [done] Land Rover engine sound - user: "perfect!" (49bb31c0)
- [done] Buggy engine sound - user: "good like this" (3423d93d)
- [done] Quad engine sound - user: "perfect on first try" (53651baa)
- [done] Jeep Wrangler and taxi engine sound, from a Cummins six (9a1a4884)
- [done] Jeep Liberty engine sound, from a Volvo diesel five - no listening verdict recorded (67debd02)
- [done] Recorded starts the engine fades in after as it catches
- [done] A gear-change clunk on the Datsun, ZIL-130 and Unimog - clunk at peaks: "perfect"
- [done] A recorded engine-off on the ZIL-130, Unimog and quad
- [done] Quieter tyres, 10 dB down
- [done] Headlamps you can see: a bright lens and glare on every lit lamp - user: "the angle is good"; oval and lens tweaks unseen
- [done] New muted paints for the Datsun, Land Rover, big truck and buggy - user: "flawless"
- [done] The Datsun's chrome: bumpers, door handles, rims, grille frame and badge
- [done] The big truck's grille with thirteen openings - user: "perfect!" (b4ce9584)
- [done] The big truck's radiator fan spinning with the engine - user: "perfect!" (859d1f21)
- [done] Tuning file and DevTools window, a set of values per car, with a Sounds tab
- [new] Chassis centre of mass baked into each car's .hkx in place of the plugin's override - user's rule 2026-10-13, not started
- [new] Engine sound of cars NPCs drive, now stuck at top revs and deafening - #37
- [postponed] Shooting from the windows, ported from a legacy mod - post-1.0 #11
- [postponed] Wrecks (bus, UAZ, a car) revived as drivable vehicles - the user's own work, post-1.0 #12
- [postponed] Further revamps of the existing car models, next the Datsun's grille - post-1.0 #10
- [scrapped] Third-person view and chase camera - moved to Third Person (db5067a); user: "vehicle overhaul should not have a third person view"
- [scrapped] The driver's full body at the wheel and the quad's centred steering - moved to Third Person with the view
- [scrapped] Headlights lighting the grass - moved to UFCP as a bug fix (82a964ad)
- [scrapped] Off-road physics on AI cars - user: AI cars keep retail physics for good, their driving logic was tuned for it
- [scrapped] Far Cry 3's engine sound layout - A/B'd worse; user: "keep everything as before FC3"
- [scrapped] Engine braking sound from a down-sweep - user: "get rid of the decelerate idea"
- [scrapped] Undone-upshift memory against hill hunting - discarded by the user
- [scrapped] ZIL-130 bank from the bigtruck2 sweeps - user: "keep the original"
- [scrapped] Kickdown, throttle-dependent shift points and creep for the automatic - offered, not wanted
- [scrapped] Formant-preserved in-between engine takes - the user heard no difference

## Approvals

- played: ccf0eed 2026-11-08 - implied by the vehicle-overhaul-1.1.0 release
- page text: ccf0eed 2026-11-08 - implied by the vehicle-overhaul-1.1.0 release

## Checks

## Release order

## Collisions

- flashlight: deploy apart - user 2026-10-08: "flashlight is not needed, dont deploy it"; VO's input hook beside Flashlight's left with db5067a
- third-person: fixed by db5067a - both hook SetDriver, the call that gives a car its driver (patterns wildcard its first 6 bytes)
- sound-overhaul: Vehicle Overhaul's copy wins - both ship mods/soundbinary/0045cbd8.spk, the Datsun idle; user 2026-10-18: "ignore sound overhaul"
