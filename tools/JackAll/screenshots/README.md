# JackAll tutorial screenshots

Scripts that drive JackAll through UI Automation and write the annotated screenshots of the JackAll
docs (`docs/jackall/`) into `docs/static/img/jackall/<tutorial>/`. Re-run them whenever the UI
changes; an image is only rewritten when its pixels changed.

```powershell
.\run-all.ps1                         # build, then every tutorial in order.psd1
.\run-all.ps1 -Only first-mod -SkipBuild
.\restore.ps1                         # after a crash: put the game folder back
```

## What a run touches

The screenshots are taken against the real game in `C:\Games\Far Cry 2` (override with
`$env:JACKALL_SHOTS_GAME`), because Deploy and Revert have to really run.

- **The game folder.** `Enter-ShotSession` first copies `patch.dat`/`patch.fat`, their `.vanilla`
  backups, `.jackallcache` and all of `bin\plugins\` to `%LOCALAPPDATA%\JackAllShots\snapshots\`, and
  checks the copy by SHA-256. Each tutorial starts from the vanilla patch with no plugins. At the end
  everything is copied back and checked again, and the snapshot is deleted once it verifies. If
  `patch.dat` changed in a way the session didn't cause (another deploy in the meantime), nothing is
  restored and `restore.ps1 -Force` decides. A deploy in a script goes through `Invoke-ShotDeploy`,
  or `Add-SessionPatch` after it, so the session knows the patch it built.
- **JackAll.** A separate build in `bin\app` and `bin\cli` (`build.ps1`) with its own `config.ini`
  and `workspace\`. Your own JackAll, its settings and its workspace are never touched.
- **Saves.** The Saves tab shows your real saves. The only file written there is a purged copy,
  which is deleted again.
- **Mouse and keyboard.** Most captures use `PrintWindow`, so the window may be covered. Popups and
  menus are separate windows and need the app in front, and many steps send real clicks and keys
  (file dialogs, context menus, the Map tab), so leave the mouse alone while a run is going.

Nothing starts the game.

## Writing a tutorial script

`shots\<slug>.ps1` runs inside a session with `JackAllShots.psm1` loaded. Start with
`Reset-ShotState` and `Start-ShotApp`, find elements with `Find-Ui` / `Find-UiByText` /
`Find-UiField`, and capture with `Save-Shot <slug> <NN-name> -Callouts ... -Region ...`: callouts
are numbered in the order given, so give them in the order the page's text mentions them. Assert
the end state (a staged file, a deployed plugin), so a run that drifted from the app fails instead
of shooting the wrong thing. Keep scripts ASCII: Windows PowerShell 5.1 reads them in the ANSI code
page.
