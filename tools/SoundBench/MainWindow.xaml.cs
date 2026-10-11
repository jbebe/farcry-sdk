using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using JackAll.App;
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
    private readonly ObservableCollection<NodeView> _voices = [];
    private readonly Dictionary<SoundInstance, NodeView> _views = [];
    private readonly List<ParameterView> _parameters = [];
    private readonly List<(SpkCurve Curve, CurvePlot Plot)> _plots = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private GameFiles? _files;
    private WasapiOut? _output;
    private NodeView? _selected;

    public MainWindow()
    {
        InitializeComponent();
        GamePath.Text = _settings.GamePath;
        LayerPaths.Text = string.Join("; ", _settings.Layers);
        Voices.ItemsSource = _voices;
        Parameters.ItemsSource = _parameters;
        ApplyListening();
        _timer.Tick += (_, _) => RefreshViews();
        Loaded += (_, _) => OnOpen(this, new RoutedEventArgs());
        Closed += (_, _) =>
        {
            _output?.Dispose();
            _files?.Dispose();
        };
    }

    private async void OnOpen(object sender, RoutedEventArgs e)
    {
        _settings.GamePath = GamePath.Text.Trim();
        _settings.Layers = [.. LayerPaths.Text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
        _settings.Save();

        OpenButton.IsEnabled = false;
        var progress = new Progress<string>(s => Status.Text = s);
        try
        {
            Settings settings = _settings;
            (GameFiles files, IReadOnlyList<Car> cars) = await Task.Run(() =>
            {
                GameFiles opened = GameFiles.Open(settings.GamePath, settings.Layers, progress);
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
        if (Cars.SelectedItem is not Car car || _files is not { } files)
        {
            return;
        }

        Status.Text = $"Loading {car.Label}…";
        OverhaulTuning tuning = OverhaulTuning.Read(
            Path.Combine(files.Install.BinDir, "vehicle-overhaul.ini"), car.Real?.Name ?? OverhaulTuning.OtherCars);
        SoundRegistry registry = await Task.Run(() => Prepare(files, car));
        lock (_bench.Sync)
        {
            _bench.Select(car, tuning, registry);
        }

        ShiftTime.Value = tuning.ShiftTime;
        CarInfo.Text = $"{string.Join(", ", car.Archetypes)}\n{car.Physics.Mass:0} kg, {car.Physics.EnginePower:0} N·m, " +
                       $"{car.Physics.Gears} gears, top {car.Physics.TopSpeedMph:0} mph · tuning [{car.Real?.Name ?? OverhaulTuning.OtherCars}]: " +
                       $"power {tuning.EnginePower}x, top speed {tuning.TopSpeed}x, brakes {tuning.BrakeTorque}x";
        ShowParameters(car);
        ShowSurfaces(car, registry);
        OnDrive(this, new RoutedEventArgs());
        Status.Text = $"{car.Label}: start the engine";
    }

    private async void OnReload(object sender, RoutedEventArgs e)
    {
        if (_files is not { } files || _bench.Car is not { } car)
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
        Status.Text = $"Banks reloaded at {DateTime.Now:T}";
    }

    /// <summary>A registry with the car's banks loaded and decoded, so the audio thread never waits on a file.</summary>
    private static SoundRegistry Prepare(GameFiles files, Car car)
    {
        var registry = new SoundRegistry(files);
        foreach (uint id in VehicleSounds.Events(car))
        {
            registry.Hold(id);
        }
        registry.DecodeAll();
        return registry;
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        lock (_bench.Sync)
        {
            _bench.Sounds?.StartEngine();
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

    private void OnDrive(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }
        lock (_bench.Sync)
        {
            _bench.Throttle = Accelerate.IsChecked == true ? (float)ThrottleSlider.Value / 100 : 0;
            _bench.Brake = Brake.IsChecked == true ? 1 : 0;
        }
    }

    private void OnShiftTime(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        lock (_bench.Sync)
        {
            _bench.ShiftTime = (float)e.NewValue;
        }
    }

    private void OnListening(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyListening();

    private void ApplyListening()
    {
        if (!IsInitialized)
        {
            return;
        }
        lock (_bench.Sync)
        {
            _bench.Distance = (float)Distance.Value;
            _bench.Volume = MathF.Pow(10, (float)Master.Value / 20);
        }
    }

    private void OnSurface(object sender, SelectionChangedEventArgs e)
    {
        lock (_bench.Sync)
        {
            _bench.Surface = (Surfaces.SelectedItem as Surface)?.Value;
            _bench.RestartWheels();
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
    }

    private void OnVoiceSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _selected = e.NewValue as NodeView;
        _plots.Clear();
        CurvePlots.Children.Clear();
        foreach (SpkCurve curve in _selected?.Node.Curves ?? [])
        {
            var plot = new CurvePlot();
            var title = new TextBlock
            {
                Text = $"{(curve.Target == 0 ? "volume (dB)" : "pitch (ratio)")} on {ParameterName(curve.Parameter)}",
                Margin = new Thickness(6, 2, 0, 0),
            };
            DockPanel.SetDock(title, Dock.Top);
            CurvePlots.Children.Add(new DockPanel { Children = { title, plot } });
            _plots.Add((curve, plot));
        }
    }

    private void RefreshViews()
    {
        float[] inputs;
        float outputPeak;
        string speed, rpm, gear, havok, clutch, engine;
        double rpmShare;
        var added = new List<NodeView>();
        var removed = new List<NodeView>();
        lock (_bench.Sync)
        {
            foreach (SoundInstance instance in _bench.Mixer.Playing.Where(i => !_views.ContainsKey(i)))
            {
                var view = new NodeView(_bench, instance.Root, ParameterName, instance.Field);
                _views[instance] = view;
                added.Add(view);
            }
            foreach (SoundInstance gone in _views.Keys.Where(i => !_bench.Mixer.Playing.Contains(i)).ToList())
            {
                removed.Add(_views[gone]);
                _views.Remove(gone);
            }
            foreach (NodeView node in _views.Values.SelectMany(v => v.Subtree()))
            {
                node.Refresh();
            }
            _parameters.ForEach(p => p.Refresh());
            inputs = _selected?.Node.CurveInputs.ToArray() ?? [];

            HavokStandIn? car = _bench.Havok;
            Drivetrain? drivetrain = _bench.Drivetrain;
            speed = car is null ? "" : $"{car.Speed * 3.6f:0} km/h  ({car.Speed:0.0} m/s)";
            rpm = drivetrain is null ? "" : $"{drivetrain.Rpm:0}  (idle {drivetrain.Idle:0}, redline {drivetrain.Redline:0})";
            rpmShare = drivetrain is null ? 0 : drivetrain.Rpm / (drivetrain.Redline * 1.05);
            gear = car is null ? "" : $"{car.Gear + 1}";
            havok = car is null ? "" : $"{car.Rpm:0}";
            clutch = car is null || drivetrain is null ? "" : $"{(car.ClutchOut ? "out" : "in")}{(drivetrain.Lifted ? ", off the throttle for the shift" : "")}";
            engine = _bench.Sounds is not { } sounds ? "" : sounds.Running ? "running" : sounds.Starting ? "starting…" : "off";
            outputPeak = _bench.Mixer.TakeOutputPeak();
        }

        removed.ForEach(v => _voices.Remove(v));
        foreach (NodeView view in added)
        {
            // The tyres roll the whole time, so the engine's events go above them.
            int tyres = _voices.TakeWhile(v => !IsTyre(v)).Count();
            _voices.Insert(IsTyre(view) ? _voices.Count : tyres, view);
        }
        foreach (NodeView node in _voices.SelectMany(v => v.Subtree()))
        {
            node.Publish();
        }
        _parameters.ForEach(p => p.Publish());
        for (int i = 0; i < _plots.Count && i < inputs.Length; i++)
        {
            (SpkCurve curve, CurvePlot plot) = _plots[i];
            plot.Show(NodeView.Plot(curve, inputs[i], curve.Target == 0 ? "VolumeCurveBrush" : "PitchCurveBrush"));
        }

        SpeedText.Text = speed;
        RpmText.Text = rpm;
        RpmBar.Value = rpmShare * 100;
        GearText.Text = gear;
        HavokText.Text = havok;
        ClutchText.Text = clutch;
        EngineState.Text = engine;
        OutputText.Text = outputPeak > 0
            ? $"{outputPeak:+0.0} dBFS: clipping, lower Master"
            : $"{outputPeak:0.0} dBFS peak";
        OutputText.Foreground = (System.Windows.Media.Brush)FindResource(outputPeak > 0 ? "ProblemBrush" : "TextMutedBrush");
    }

    private void ShowParameters(Car car)
    {
        CarParameters p = car.Sounds.Parameters;
        lock (_bench.Sync)
        {
            _bench.Pins.Clear();
        }
        _parameters.Clear();
        _parameters.AddRange(
        [
            new ParameterView(_bench, "Speed (m/s)", p.Speed, 0, 20),
            new ParameterView(_bench, "RPM", p.Rpm, 0, 10000),
            new ParameterView(_bench, "Pedal", p.ThrustPedal, -100, 100),
            new ParameterView(_bench, "Extra torque", p.ExtraTorque, 0, 100),
            new ParameterView(_bench, "Wheel slip", p.WheelSlip, 0, 15),
            new ParameterView(_bench, "Damage", p.Damage, 0, 100),
        ]);
        Parameters.ItemsSource = null;
        Parameters.ItemsSource = _parameters;
    }

    private static bool IsTyre(NodeView view) => view.Field?.StartsWith("sndWheelRoll") == true;

    private string ParameterName(uint id) => _parameters.FirstOrDefault(p => p.Id == id)?.Name ?? $"0x{id:x8}";

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
