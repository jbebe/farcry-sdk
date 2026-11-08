# JackAll

**Before writing or editing any C#, XAML or PowerShell file in this tree, invoke the
`implementation` skill** (`.claude/skills/implementation/SKILL.md`). Tests follow its Tests section.

## What this is

Far Cry 2's mod manager, and the home of every Dunia format: the WPF app (`src/JackAll.App`),
`jackall-cli` (`src/JackAll.Cli`), which every mod's build and deploy runs, `jackall-mi`
(`src/JackAll.ModInstaller`) for the Vortex extension, and the libraries they share. `README.md`
covers what it does and why a build is always safe; the user docs are in `docs/jackall/`.

## Rules specific to this tree

### New worlds

- **Authored content goes to the workspace as fragments**, the same as an edit to a retail world.
  Never edit a mod folder in place, and never remember which mod was opened.
- **Files New map creates are real game files**, written to the workspace and edited whole there:
  no definition file, no opaque package, no base to regenerate.
- **Derived files come from `world pack`, at author time**: sectors, the entity library, the sector
  list, the navmesh, terrain and vegetation. A feature that derives a file adds it to the pack.
- **Installing only copies.** `mod build` and the App's Deploy copy whole files, and a new world adds
  no install-time work. `docs/jackall/world/new-worlds.md` is the user's side of this.

### WPF

- **`Themes/Theme.xaml` opens with the design system's contract**; every view follows it.
- **An inline `<Style TargetType="X">` is based on the implicit one**,
  `BasedOn="{StaticResource {x:Type X}}"`, or it falls back to light Aero chrome. A DataGrid row
  style is the exception; `Themes/Controls/DataGrids.xaml` says why.
- **A new implicit style for a control that hosts content sets `Foreground`.** Foreground inherits
  through the logical parent, so a stock style's black (Expander, TreeViewItem) reaches everything
  inside it otherwise.
- **A TabControl template names its ContentPresenter `PART_SelectedContentHost`**, or UI Automation
  and screen readers see nothing inside the tabs.
- **A ComboBox template sets `ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"` on
  its selection box**, or `DisplayMemberPath` shows the type name.
- **Every editor in a DataGrid cell sets `UpdateSourceTrigger`**: `PropertyChanged` for a combo,
  `LostFocus` for text. The default joins the row's BindingGroup and writes nothing until the row
  commits.
- **Bound button text goes in a `<TextBlock Text="{Binding ...}"/>` inside the button.** The
  templates set `RecognizesAccessKey="True"`, which eats an underscore of `Content="{Binding ...}"`.
- **Replacing an ItemsSource with new row objects captures the selection first and ignores what the
  control pushes back during the swap**: it sets `SelectedItem` to null mid-swap. `Swap` in
  `Move/MoveRulesViewModel.cs` does this.
- **A UserControl's view model is created in MainWindow's constructor.** Until it exists, the control
  inherits MainWindow's DataContext.
- **`SplitterSize` is a GridLength**, valid only as a row or column size.
- **No Fluent `ThemeMode`.** Its styles are app resources that our implicit styles replace, and its
  metrics break the dense toolbars.
- **Two things stay as they are on purpose.** The Domino canvas is light paper (`CanvasBrush`) in the
  dark theme, because `PortBrushConverter` and `NodeBrushConverter` colour for a light ground.
  MessageBox and file dialogs stay OS-styled.

### Checking a UI change

- **Check a UI change in the running app, driven by UI Automation from PowerShell.** Build, launch
  `src\JackAll.App\bin\Debug\net10.0-windows\JackAll.exe`, wait about 15 s for the archives, find
  tabs by name and controls by `x:Name` (WPF's AutomationId), screenshot the window's rectangle, then
  `Stop-Process` it.
- **Back up the `config.ini` beside that exe before the run and restore it after.** Picking a theme
  saves it at once.
- **Rows of a templated ItemsControl have no automation peer.** Pair a label with its control by
  screen Y. A record item's automation name is its `ToString()`.
- **Match non-ASCII text by an ASCII prefix.** Windows PowerShell 5.1 reads a script in the ANSI code
  page, so "Open value editor…" is found as "Open value editor".

### Tutorial screenshots

`screenshots/README.md` covers what a run touches and how to write a script.

- **Ask the user before a run.** It sends real clicks and keys and needs the mouse.
- **A failed assertion means the bug it holds is back: fix the code, never loosen the script.** The
  tutorial pages no longer carry workarounds for those bugs.
- **"The handle is invalid" or "Dialog ... did not accept" means the screensaver is up.** Wait for the
  user; never kill it.
- **Pass several slugs through `-Command`**, as
  `powershell -Command "& '.\run-all.ps1' -Only first-mod,saves -SkipBuild"`. Under `-File`,
  `-Only a,b` arrives as one string.

### Builds and moves

- **Run `dotnet build-server shutdown` before a `git mv` of a project folder.** MSBuild nodes and the
  editor's C# extension hold handles on it, and the move fails with "Permission denied". Never kill
  the `dotnet` processes one by one: one of them is the editor's language server.
- **`BG1002 ... .baml cannot be found` or `CS2001 ... .g.cs could not be found` is an `obj\` race,
  not the code.** Delete only `src\JackAll.App\obj\<config>` and rebuild with
  `-m:1 -nodeReuse:false`.
- **`Clean.ps1` deletes every project's `bin\` as well as its `obj\`.** That includes the `config.ini`
  and `workspace\` beside the App's exe, and the Release `jackall-cli.exe` the mod builds run. Delete
  one `obj\<config>` instead where that is enough, and rebuild the CLI after a clean.
- **While the user has JackAll open, its `bin\` is locked.** Build the App to a scratch folder with
  `-o` instead of closing it.
- **A change to `Tools/Audio/ImaAdpcm.cs` is followed by a Release build of the CLI.** SoundBench
  compiles the encoder directly and the vehicle sound scripts run it through `jackall-cli`; the two
  must encode alike.
- **A change to Domino decoding passes `jackall-cli domino check`** over the retail scripts before it
  ships.

### Map tab rendering

- **Bind a layer's own VAO before uploading its indices.** An element-buffer bind lands in whichever
  VAO is bound, and indices uploaded under another layer's VAO crashed the NVIDIA driver.
- **Measure VRAM with the process's GPU "Total Committed" counter**, not by summing uploads.

## Build

From this folder:

```
dotnet build                              # the solution, JackAll.slnx
dotnet build src\JackAll.Cli -c Release   # src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe
dotnet test                               # under a minute
.\screenshots\run-all.ps1                 # the tutorial screenshots, after asking the user
```
