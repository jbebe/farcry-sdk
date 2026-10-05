---
title: One physics thread and one job thread
kind: component
claims: []
status: located
systems: [engine]
match:
  - "engine/settings/defaultthreadingconfig.xml#**"
exclude: []
requires: []
existing: mods/UFCP — src/fixes/utilisation.cpp (sizes the job pool the other way) and the Processor affinity option
verified: diff
---

# One physics thread and one job thread

The engine gets exactly one physics worker thread, where the base game configured none, and one
job worker on any machine. No line of the feature list names it.

## How

`engine/settings/defaultthreadingconfig.xml` (the patch archive's copy). The file's own comment
gives the rule: with `RelativeToCoreCnt` 1 the count is the core count minus `ThreadCnt`, else
`ThreadCnt`, clamped to `MinThreadCnt`..`MaxThreadCnt`.

- `PHYSIC_TIMESTEP_THREAD`: gains `RelativeToCoreCnt` 0, `MinThreadCnt` 0, `MaxThreadCnt` 1,
  `ThreadCnt` 1 (it had only `Active` 1).
- `PHYSIC_THREADS`: `MaxThreadCnt` 3 -> 1, `ThreadCnt` 0 -> 1. The base game asked for none.
- `JOB_THREADS`: `RelativeToCoreCnt` 1 -> 0, `ThreadCnt` 3 -> 1, so always one worker. The base
  game's rule (cores minus 3, at most 1) already gave one on four or more cores and none below.

## Uncertain

- The purpose is not stated. UFCP's notes tie the bouncing-NPC symptom to machines with many cores,
  so this may belong with `bouncing-npcs`; it may as well be a stability or stutter tweak.
