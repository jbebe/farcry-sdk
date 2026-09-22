using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using JackAll.Tools.Fc2Model;
using JackAll.Tools.Mab;
using JackAll.Tools.Skeleton;
using JackAll.Tools.Xbg;

namespace JackAll.App.FileHandlers.Mab;

/// <summary>
/// The file handler for .mab animation banks - the rigs a bank drives, drawn as stick figures and
/// played through.
/// </summary>
/// <remarks>
/// No mesh: the bones alone show where a hand goes and where a magazine travels. Each prop the tag
/// table names is hung from the bone its record says, on the bank's own figure.
/// </remarks>
public partial class MabFileHandler : UserControl
{
    private const float BoneWidth = 0.008f;
    private static readonly Color OwnerColour = Color.FromRgb(0x8F, 0xB4, 0xE0);
    private static readonly Color PropColour = Color.FromRgb(0xE0, 0x9A, 0x4A);

    /// <summary>A rig with the clip driving it, and the owner's bone it hangs from, if any.</summary>
    private sealed record Figure(SkeletonFile Rig, MabPose Pose, int? Anchor, MeshGeometry3D Bones);

    private readonly List<Figure> _figures = [];
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly OrbitViewport _orbit;
    private DateTime _lastTick;

    public MabFileHandler(string fileName, byte[] content, Func<MabFile, BankRigs> findRigs)
    {
        InitializeComponent();
        _orbit = new OrbitViewport(Viewport);
        _clock.Tick += (_, _) => Advance();
        Unloaded += (_, _) => _clock.Stop();
        try
        {
            MabFile bank = MabFile.Parse(content);
            Load(fileName, bank, findRigs(bank));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't read this file: {ex.Message}";
        }
    }

    private void Load(string fileName, MabFile bank, BankRigs rigs)
    {
        List<string> missing = [];
        if (rigs.Owner is not null && MabPose.Fits(rigs.Owner, bank))
        {
            Add(rigs.Owner, bank, null, OwnerColour);
        }
        else
        {
            missing.Add("the bank's own rig");
        }

        foreach ((MabParticipant participant, MabClip clip) in bank.ParticipantClips())
        {
            if (!participant.IsPrimary)
            {
                continue;
            }
            if (!rigs.Participants.TryGetValue(participant.Name, out SkeletonFile? rig) || !MabPose.Fits(rig, clip))
            {
                missing.Add(participant.Name);
                continue;
            }

            Add(rig, clip, rigs.Owner?.BoneByName(participant.Parent)?.Id, PropColour);
        }

        StatusText.Text = $"{fileName}: {_figures.Count} figure(s) drawn"
            + (missing.Count > 0 ? $"\nNo rig found for: {string.Join(", ", missing)}" : "");
        if (_figures.Count == 0)
        {
            return;
        }

        FrameSlider.Maximum = _figures.Max(f => f.Pose.LastFrame);
        Toolbar.Visibility = Visibility.Visible;
        Pose(0);
        ResetView();
        _lastTick = DateTime.UtcNow;
        _clock.Start();
    }

    private void Add(SkeletonFile rig, MabClip clip, int? anchor, Color colour)
    {
        var bones = new MeshGeometry3D { TriangleIndices = new Int32Collection() };
        // Two quads per bone with a parent, so the topology is fixed and only positions move.
        foreach (int first in Enumerable.Range(0, rig.Bones.Count(b => b.Parent < rig.Bones.Count) * 2).Select(q => q * 4))
        {
            foreach (int i in new[] { 0, 1, 2, 0, 2, 3 })
            {
                bones.TriangleIndices.Add(first + i);
            }
        }
        _figures.Add(new Figure(rig, new MabPose(rig, clip), anchor, bones));

        var material = new DiffuseMaterial(new SolidColorBrush(colour));
        Viewport.Children.Add(new ModelVisual3D
        {
            Content = new Model3DGroup
            {
                Children = { new AmbientLight(Colors.White), new GeometryModel3D(bones, material) { BackMaterial = material } },
            },
        });
    }

    private void Advance()
    {
        DateTime now = DateTime.UtcNow;
        float frame = (float)FrameSlider.Value + ((float)(now - _lastTick).TotalSeconds * _figures[0].Pose.Rate);
        _lastTick = now;
        FrameSlider.Value = FrameSlider.Maximum > 0 ? frame % FrameSlider.Maximum : 0;
    }

    private void FrameSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_figures.Count > 0)
        {
            Pose((float)e.NewValue);
        }
    }

    /// <summary>Rebuilds every figure's bones at one frame; the owner comes first, so props find it posed.</summary>
    private void Pose(float frame)
    {
        FrameText.Text = $"{frame:0} / {FrameSlider.Maximum}";
        Matrix4x4[]? owner = null;
        foreach (Figure figure in _figures)
        {
            Matrix4x4[] world = figure.Pose.WorldAt(frame);
            if (figure.Anchor is { } bone && owner is not null)
            {
                for (int i = 0; i < world.Length; i++)
                {
                    world[i] *= owner[bone];
                }
            }
            owner ??= world;

            var positions = new Point3DCollection(figure.Bones.TriangleIndices.Count / 6 * 4);
            foreach (SkeletonBone b in figure.Rig.Bones)
            {
                if (b.Parent < world.Length)
                {
                    Segment(positions, world[b.Id].Translation, world[b.Parent].Translation);
                }
            }
            figure.Bones.Positions = positions;
        }
    }

    /// <summary>Two crossed quads from one joint to the next, so the bone reads from any angle.</summary>
    private static void Segment(Point3DCollection positions, Vector3 from, Vector3 to)
    {
        Vector3 axis = to - from;
        Vector3 side = Vector3.Cross(axis, Math.Abs(axis.Z) < Math.Abs(axis.X) ? Vector3.UnitZ : Vector3.UnitX);
        side = side.LengthSquared() > 0 ? Vector3.Normalize(side) * BoneWidth : Vector3.UnitY * BoneWidth;
        Vector3 up = Vector3.Cross(axis, side);
        up = up.LengthSquared() > 0 ? Vector3.Normalize(up) * BoneWidth : Vector3.UnitZ * BoneWidth;
        Quad(side);
        Quad(up);

        void Quad(Vector3 width)
        {
            positions.Add(Point(from - width));
            positions.Add(Point(from + width));
            positions.Add(Point(to + width));
            positions.Add(Point(to - width));
        }
    }

    private static Point3D Point(Vector3 v) => new(v.X, v.Y, v.Z);

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_clock.IsEnabled)
        {
            _clock.Stop();
            PlayButton.Content = "Play";
        }
        else
        {
            _lastTick = DateTime.UtcNow;
            _clock.Start();
            PlayButton.Content = "Pause";
        }
    }

    private void ResetViewButton_Click(object sender, RoutedEventArgs e) => ResetView();

    private void ResetView()
    {
        (Vector3 min, Vector3 max) = XbgModel.Bounds(_figures
            .SelectMany(f => f.Bones.Positions)
            .Select(p => new Vector3((float)p.X, (float)p.Y, (float)p.Z)));
        _orbit.Frame(min, max);
    }
}
