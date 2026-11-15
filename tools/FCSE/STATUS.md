# FCSE

Purpose: Load DLL and Lua plugins into Far Cry 2 so mods change engine behaviour in Dunia.dll without shipping a patched copy of it.
Stage: maintenance

## Features

- [done] FCSE.exe launcher - FarCry2.exe's WinMain and its 12 stock debug handlers reimplemented
- [done] DLL plugins (FCSE_Load) and Lua scripts loaded from bin\plugins at any depth
- [done] AddFunctionCB named callbacks with per-name ownership tracking
- [done] Hook and MidHook on safetyhook - hooks at one address stack since 1.3.0
- [done] Patch with overlap rejection between modules
- [done] Byte-pattern scan - sees other mods' hook jumps
- [done] Address library keyed by symbol - both v1.03 builds, Steam/Uplay and GOG/retail; retail 1.00 refused
- [done] Settings registry and bin\fcse.ini - checkbox, choice, slider and text
- [done] Mod Configuration Menu in Options - scrolls past 17 rows; disabled and hidden rows
- [done] Settings page layouts encoded by jackall-cli at build time and embedded in FCSE.exe
- [done] Entity data component CFCSEDataComponent, API v8 - Persist(entity) not tried in game
- [done] fcse.log and crash report - each fault reported once since 1.4.2
- [done] FCSE::Logf and FCSE::Crc32 header helpers
- [done] Black and white splash screen
- [done] Example plugin and example script, the same effects side by side
- [new] FATAL report from an unhandled-exception filter installed after the engine's own and chained to it - agreed for 1.4.3
- [done] SetSetting, API v9: any plugin's setting set from text for this session, the file untouched - DevTools' setting set UFCP's field of view in game 2026-11-09
- [done] Messaging between plugins, API v10: a message on a named channel reaches every plugin listening, and the send says whether anyone heard - DO to DevTools in game, GOG 2026-11-11
- [scrapped] Hand-rolled Mods page on CGameMenu - crashed; the page intercepts .mgb loads instead

## Approvals

- played: 14fc24e 2026-10-21 - implied by the fcse-1.4.2 release
- page text: 14fc24e 2026-10-21 - implied by the fcse-1.4.2 release

## Checks

- simplify: c4fcf5f 2026-11-15 - since fcse-1.4.2

## Release order

## Collisions
