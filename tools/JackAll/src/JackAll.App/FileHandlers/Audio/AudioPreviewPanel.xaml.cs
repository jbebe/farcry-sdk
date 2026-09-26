using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using JackAll.App.Audio;

namespace JackAll.App.FileHandlers.Audio;

/// <summary>
/// The play/pause/stop/seek player every audio preview shares. Hosts either hand it a media file path
/// via <see cref="Open"/>, or a decoder via <see cref="Play"/>; everything else —
/// transport buttons, seeking, the position timer — is self-contained.
/// </summary>
public partial class AudioPreviewPanel : UserControl
{
    private readonly DispatcherTimer _timer;
    private bool _isUserSeeking;
    private bool _updatingSlider;
    private string? _ownedWav;
    private int _playRequest;

    public AudioPreviewPanel()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += OnTimerTick;
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            Reset();
        };
    }

    /// <summary>Decodes a sound with <paramref name="makeWav"/> off the UI thread and plays it, deleting the
    /// temp .wav on the next request. A newer request supersedes one still decoding.</summary>
    public async void Play(Func<Task<string>> makeWav)
    {
        int request = ++_playRequest;
        Reset();
        Status.Text = "Decoding…";

        try
        {
            string wav = await Task.Run(makeWav);
            if (request != _playRequest)
            {
                SoundPreview.TryDelete(wav);
                return;
            }
            _ownedWav = wav;
            Status.Text = "";
            Open(wav);
            Player.Play();
        }
        catch (Exception ex) when (request == _playRequest)
        {
            Status.Text = ex.Message;
        }
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
        SoundPreview.TryDelete(_ownedWav);
        _ownedWav = null;
        Status.Text = "";
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
