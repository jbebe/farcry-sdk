using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace JackAll.App.FileHandlers.Audio;

/// <summary>
/// The play/pause/stop/seek player the Sbao and Spk handlers share. Hosts hand it a media file path
/// via <see cref="Open"/>; everything else — transport buttons, seeking, the position timer — is
/// self-contained.
/// </summary>
public partial class AudioPreviewPanel : UserControl
{
    private readonly DispatcherTimer _timer;
    private bool _isUserSeeking;
    private bool _updatingSlider;

    public AudioPreviewPanel()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += OnTimerTick;
        _timer.Start();
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            Reset();
        };
    }

    /// <summary>Points the player at a playable file (a temp .wav) and enables Play. The caller
    /// keeps ownership of the file; call <see cref="Reset"/> before deleting it.</summary>
    public void Open(string mediaPath)
    {
        Player.Source = new Uri(mediaPath);
        PlayButton.IsEnabled = true;
    }

    /// <summary>Stops playback, releases the media file, and disables the transport.</summary>
    public void Reset()
    {
        Player.Stop();
        Player.Close();
        Player.Source = null;
        SeekBar.Value = 0;
        SeekBar.IsEnabled = false;
        TimeText.Text = "0:00 / 0:00";
        PlayButton.IsEnabled = false;
        PauseButton.IsEnabled = false;
        StopButton.IsEnabled = false;
    }

    private void Play_Click(object sender, RoutedEventArgs e) => Player.Play();

    private void Pause_Click(object sender, RoutedEventArgs e) => Player.Pause();

    private void Stop_Click(object sender, RoutedEventArgs e) => Player.Stop();

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (Player.NaturalDuration.HasTimeSpan)
        {
            SeekBar.Maximum = Player.NaturalDuration.TimeSpan.TotalSeconds;
        }

        SeekBar.IsEnabled = true;
        PauseButton.IsEnabled = true;
        StopButton.IsEnabled = true;
    }

    private void Player_MediaEnded(object sender, RoutedEventArgs e) => Player.Stop();

    private void SeekBar_PreviewMouseDown(object sender, MouseButtonEventArgs e) => _isUserSeeking = true;

    private void SeekBar_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isUserSeeking = false;
        Player.Position = TimeSpan.FromSeconds(SeekBar.Value);
    }

    private void SeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSlider || !_isUserSeeking)
        {
            return;
        }

        TimeText.Text = FormatTime(TimeSpan.FromSeconds(SeekBar.Value)) + " / " + FormatTime(TotalDuration());
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_isUserSeeking || Player.Source is null || !Player.NaturalDuration.HasTimeSpan)
        {
            return;
        }

        _updatingSlider = true;
        SeekBar.Value = Player.Position.TotalSeconds;
        _updatingSlider = false;
        TimeText.Text = FormatTime(Player.Position) + " / " + FormatTime(TotalDuration());
    }

    private TimeSpan TotalDuration()
        => Player.NaturalDuration.HasTimeSpan ? Player.NaturalDuration.TimeSpan : TimeSpan.Zero;

    private static string FormatTime(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
}
