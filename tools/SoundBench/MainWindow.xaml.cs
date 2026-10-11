using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using JackAll.Tools.Spk;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SoundBench.Dare;
using SoundBench.Game;
using SoundBench.Vehicle;
using SoundBench.Views;

namespace SoundBench;

public partial class MainWindow : Window
{
    private readonly Settings _settings = Settings.Load();
    private readonly Bench _bench = new();
    private readonly ObservableCollection<SlotView> _slots = [];
    private readonly Dictionary<string, SlotView> _slotByField = [];
    private readonly List<(NodeView Play, SlotView Slot)> _playing = [];
    private readonly List<CurveEditor> _editors = [];
    private readonly Stack<CurveEdit> _undo = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private IReadOnlyList<ParameterView> _parameters = [];
    private GameFiles? _files;
    private SoundRegistry? _registry;
    private WasapiOut? _output;
    private NodeView? _selected;

    /// <summary>Points for a curve to have: an edit, or what it had before one.</summary>
    private sealed record CurveEdit(SpkCurve Curve, SpkPoint[] Points);

    public MainWindow()
    {
        InitializeComponent();
        GamePath.Text = _settings.GamePath;
        LayerPaths.Text = string.Join("; ", _settings.Layers);
        Voices.ItemsSource = _slots;
        OnControls(this, new RoutedEventArgs());
        _timer.Tick += (_, _) => RefreshViews();
        Loaded += (_, _) => OnOpen(this, new RoutedEventArgs());
        Closing += (_, e) => e.Cancel = !Release("closing");
        Closed += (_, _) =>
        {
            _output?.Dispose();
            _files?.Dispose();
        };
    }

    private async void OnOpen(object sender, RoutedEventArgs e)
    {
        if (!Release("opening the game again"))
        {
            return;
        }
        _settings.GamePath = GamePath.Text.Trim();
        _settings.Layers = [.. LayerPaths.Text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
        _settings.Save();

        OpenButton.IsEnabled = false;
        var progress = new Progress<string>(s => Status.Text = s);
        try
        {
            (GameFiles files, IReadOnlyList<Car> cars) = await Task.Run(() =>
            {
                GameFiles opened = GameFiles.Open(_settings.GamePath, _settings.Layers, progress);
                return (opened, CarCatalog.Load(opened, progress));
            });
            _files?.Dispose();
            _files = files;
            Cars.ItemsSource = cars;
            Cars.SelectedItem = cars.FirstOrDefault(c => c.Real?.Name == "Datsun 1200") ?? cars.FirstOrDefault();
            _output ??= StartOutput();
            _timer.Start();
        }
        catch (Exception ex)
        {
            Status.Text = ex.Message;
        }
        finally
        {
            OpenButton.IsEnabled = true;
        }
    }

    private WasapiOut StartOutput()
    {
        var output = new WasapiOut(AudioClientShareMode.Shared, useEventSync: true, latency: 40);
        output.Init(_bench);
        output.Play();
        return output;
    }

    private async void OnCarSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Cars.SelectedItem is not Car car || _files is not { } files || car == _bench.Car)
        {
            return;
        }
        if (!Release("switching car"))
        {
            Cars.SelectedItem = _bench.Car;
            return;
        }

        Status.Text = $"Loading {car.Label}…";
        OverhaulTuning tuning = OverhaulTuning.Read(Path.Combine(files.Install.BinDir, "vehicle-overhaul.ini"), car.Real);
        SoundRegistry registry = await Task.Run(() => Prepare(files, car));
        lock (_bench.Sync)
        {
            _bench.Select(car, tuning, registry);
        }

        UseRegistry(registry);
        ShowSlots(car);
        ShiftTime.Value = tuning.ShiftTime;
        CarInfo.Text = $"{string.Join(", ", car.Archetypes)}\n{car.Physics.Mass:0} kg, {car.Physics.EnginePower:0} N·m, " +
                       $"{car.Physics.Gears} gears, top {car.Physics.TopSpeedMph:0} mph · tuning [{car.Real?.Name ?? OverhaulTuning.OtherCars}]: " +
                       $"power {tuning.EnginePower}x, top speed {tuning.TopSpeed}x, brakes {tuning.BrakeTorque}x";
        BankProblems.Text = string.Join("\n", registry.Problems);
        Parameters.ItemsSource = _parameters = [.. _bench.Parameters.Select(p => new ParameterView(_bench, p))];
        ShowSurfaces(car, registry);
        Status.Text = $"{car.Label}: start the engine";
    }

    private async void OnReload(object sender, RoutedEventArgs e)
    {
        if (_files is not { } files || _bench.Car is not { } car || !Release("reloading the banks"))
        {
            return;
        }
        SoundRegistry registry = await Task.Run(() =>
        {
            files.Reload();
            return Prepare(files, car);
        });
        lock (_bench.Sync)
        {
            _bench.Load(registry);
        }
        UseRegistry(registry);
        ShowSlots(car);
        BankProblems.Text = string.Join("\n", registry.Problems);
        Status.Text = $"Banks reloaded at {DateTime.Now:T}";
    }

    /// <summary>A registry with the car's banks loaded, off the audio thread.</summary>
    private static SoundRegistry Prepare(GameFiles files, Car car)
    {
        var registry = new SoundRegistry(files);
        foreach (SoundSlot slot in CarSlots.Of(car).All)
        {
            registry.Hold(slot.Id);
        }
        return registry;
    }

    /// <summary>One row per event the car plays, in a fixed order, empty until it plays.</summary>
    private void ShowSlots(Car car)
    {
        _slots.Clear();
        _slotByField.Clear();
        _playing.Clear();
        foreach (SoundSlot slot in CarSlots.Of(car).All)
        {
            var view = new SlotView(slot);
            _slots.Add(view);
            _slotByField[slot.Field] = view;
        }
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        lock (_bench.Sync)
        {
            _bench.StartEngine();
        }
        Status.Text = "";
    }

    private void OnStop(object sender, RoutedEventArgs e)
    {
        lock (_bench.Sync)
        {
            _bench.Sounds?.StopEngine();
        }
    }

    private void OnControls(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }
        lock (_bench.Sync)
        {
            _bench.Throttle = Accelerate.IsChecked == true ? (float)ThrottleSlider.Value / 100 : 0;
            _bench.Brake = Brake.IsChecked == true ? 1 : 0;
            _bench.ShiftTime = (float)ShiftTime.Value;
            _bench.HoldGear = HoldGear.IsChecked == true;
            _bench.Distance = (float)Distance.Value;
            _bench.Volume = MathF.Pow(10, (float)Master.Value / 20);
        }
    }

    private void OnSurface(object sender, SelectionChangedEventArgs e)
    {
        uint? surface = (Surfaces.SelectedItem as Surface)?.Value;
        lock (_bench.Sync)
        {
            if (_bench.Surface != surface)
            {
                _bench.Surface = surface;
                _bench.Sounds?.RestartTyres();
            }
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }
        RadioButton? pedal = e.Key switch
        {
            Key.W => Accelerate,
            Key.Space => Coast,
            Key.S => Brake,
            _ => null,
        };
        if (pedal is not null)
        {
            pedal.IsChecked = true;
            e.Handled = true;
        }
        else if (e.Key == Key.H)
        {
            HoldGear.IsChecked = HoldGear.IsChecked != true;
            e.Handled = true;
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Undo();
            e.Handled = true;
        }
    }

    /// <summary>One editor per curve of the selected layer, each over its sibling layers' curves on the same parameter.</summary>
    private void OnVoiceSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _selected = e.NewValue as NodeView;
        _editors.Clear();
        CurveEditors.Children.Clear();
        if (_selected is not { Parent: { } owner } layer)
        {
            return;
        }
        foreach (SpkCurve curve in layer.Node.Curves)
        {
            SpkCurve[] background =
            [
                .. owner.Children.Where(c => c != layer).SelectMany(c => c.Node.Curves)
                    .Where(c => c.Target == curve.Target && c.Parameter == curve.Parameter),
            ];
            GameParameter? parameter = _bench.Parameters.FirstOrDefault(p => p.Id == curve.Parameter);
            var editor = new CurveEditor(curve, background, parameter is null ? null : (parameter.Min, parameter.Max));
            editor.Editing += () => _undo.Push(new CurveEdit(curve, [.. curve.Points]));
            editor.Edited += points => Apply(new CurveEdit(curve, points));

            var title = new TextBlock
            {
                Text = $"{(curve.Target == 0 ? "volume (dB)" : "pitch (ratio)")} on {ParameterName(curve.Parameter)}",
                Margin = new Thickness(6, 2, 0, 0),
            };
            DockPanel.SetDock(title, Dock.Top);
            CurveEditors.Children.Add(new DockPanel { Children = { title, editor } });
            _editors.Add(editor);
        }
    }

    /// <summary>Gives a curve its points, heard from the next block on.</summary>
    private void Apply(CurveEdit edit)
    {
        lock (_bench.Sync)
        {
            edit.Curve.Points.Clear();
            edit.Curve.Points.AddRange(edit.Points);
        }
        if (_registry?.Edited(edit.Curve) == true)
        {
            ShowUnsaved();
        }
    }

    private void Undo()
    {
        if (_undo.TryPop(out CurveEdit? edit))
        {
            Apply(edit);
            _editors.ForEach(e => e.Refit());
        }
    }

    private void OnSave(object sender, RoutedEventArgs e) => Save();

    private void Save()
    {
        if (_registry is { } registry)
        {
            Status.Text = string.Join("; ", registry.Save().Select(s => s.Layer is null ? $"{s.Bank}: no layer folder to save it into" : $"Saved {s.Bank} into {s.Layer}"));
            ShowUnsaved();
        }
    }

    /// <summary>The banks with edits not yet saved, by name.</summary>
    private string Unsaved => string.Join(", ", _registry?.Unsaved ?? []);

    private void ShowUnsaved()
    {
        string unsaved = Unsaved;
        UnsavedText.Text = unsaved.Length > 0 ? $"unsaved: {unsaved}" : "";
        SaveButton.IsEnabled = unsaved.Length > 0;
    }

    /// <summary>Lets go of the banks being edited, offering to save their edits first; false when the user cancels.</summary>
    private bool Release(string action)
    {
        string unsaved = Unsaved;
        if (unsaved.Length > 0)
        {
            MessageBoxResult answer = MessageBox.Show(
                this, $"Save the curve edits to {unsaved} before {action}?", Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel)
            {
                return false;
            }
            if (answer == MessageBoxResult.Yes)
            {
                Save();
            }
        }
        UseRegistry(null);
        return true;
    }

    /// <summary>Edits from here on go to this registry's banks, with nothing to undo.</summary>
    private void UseRegistry(SoundRegistry? registry)
    {
        _registry = registry;
        _undo.Clear();
        ShowUnsaved();
    }

    private void RefreshViews()
    {
        List<(NodeView Play, SlotView Slot)> started = [];
        float[] inputs;
        lock (_bench.Sync)
        {
            foreach (SoundInstance instance in _bench.TakeStarted())
            {
                if (_slotByField.TryGetValue(instance.Field, out SlotView? slot))
                {
                    started.Add((new NodeView(_bench, instance, ParameterName), slot));
                }
            }
            _playing.AddRange(started);
            foreach ((NodeView play, _) in _playing)
            {
                play.CapturePlay();
            }
            foreach (ParameterView parameter in _parameters)
            {
                parameter.Capture();
            }
            inputs = _selected?.Node.CurveInputs.ToArray() ?? [];
            ShowGauges();
        }

        foreach ((NodeView play, SlotView slot) in started)
        {
            slot.Add(play);
        }
        foreach ((NodeView play, SlotView slot) in _playing)
        {
            play.PublishPlay();
            if (play.Ended)
            {
                slot.Prune();
            }
        }
        _playing.RemoveAll(p => p.Play.Ended);
        foreach (ParameterView parameter in _parameters)
        {
            parameter.Publish();
        }
        for (int i = 0; i < Math.Min(_editors.Count, inputs.Length); i++)
        {
            _editors[i].Mark(inputs[i]);
        }
    }

    /// <summary>Call under <see cref="Bench.Sync"/>.</summary>
    private void ShowGauges()
    {
        if (_bench is { Havok: { } car, Drivetrain: { } drivetrain, Sounds: { } sounds })
        {
            SpeedText.Text = $"{car.Speed * 3.6f:0} km/h  ({car.Speed:0.0} m/s)";
            RpmText.Text = $"{drivetrain.Rpm:0}  (idle {drivetrain.Idle:0}, redline {drivetrain.Redline:0})";
            RpmBar.Value = drivetrain.Rpm / (drivetrain.Redline * 1.05) * 100;
            GearText.Text = $"{car.Gear + 1}";
            HavokText.Text = $"{car.Rpm:0}";
            ClutchText.Text = $"{(car.ClutchOut ? "out" : "in")}{(drivetrain.Lifted ? ", off the throttle for the shift" : "")}";
            EngineState.Text = sounds.Engine.ToString().ToLowerInvariant();
        }

        float peak = _bench.Mixer.TakeOutputPeak();
        OutputText.Text = peak > 0 ? $"{peak:+0.0} dBFS: clipping, lower Master" : $"{peak:0.0} dBFS peak";
        OutputText.Foreground = (System.Windows.Media.Brush)FindResource(peak > 0 ? "ProblemBrush" : "TextMutedBrush");
    }

    private string ParameterName(uint id) => _bench.Parameters.FirstOrDefault(p => p.Id == id)?.Name ?? $"0x{id:x8}";

    /// <summary>A material switch value the tyres can roll on.</summary>
    private sealed record Surface(uint? Value)
    {
        public override string ToString() => Value is { } v ? $"0x{v:x8}" : "each switch's default";
    }

    private void ShowSurfaces(Car car, SoundRegistry registry)
    {
        IEnumerable<uint> values = car.Sounds.WheelRolls
            .Select(id => registry.Find(id)?.Record)
            .Where(r => r?.Layout == SpkLayout.SwitchEvent)
            .SelectMany(r => r!.Entries.Select(e => e.Value))
            .Distinct()
            .Order();
        Surfaces.ItemsSource = new[] { new Surface(null) }.Concat(values.Select(v => new Surface(v))).ToList();
        Surfaces.SelectedIndex = 0;
    }
}
