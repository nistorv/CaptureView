using CaptureView.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace CaptureView;

public sealed partial class MainWindow : Window
{
    private static readonly AudioDevice NoAudioInput = new("", "None");
    private static readonly AudioDevice DefaultAudioOutput = new("", "Default output");

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly VideoCaptureService _video = new();
    private readonly AudioPassthrough _audio = new();

    // Set while the code fills controls, so their change handlers don't react.
    private bool _populating;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(1280, 760));

        VideoView.SetMediaPlayer(_video.Player);

        _video.Failed += (_, message) => DispatcherQueue.TryEnqueue(() => ShowStatus(message));
        _audio.Failed += (_, message) => DispatcherQueue.TryEnqueue(() => ShowStatus(message));

        Root.Loaded += async (_, _) => await InitializeAsync();
        Closed += OnClosed;
    }

    private async Task InitializeAsync()
    {
        _populating = true;
        VolumeSlider.Value = _settings.Volume * 100;
        MuteSwitch.IsOn = _settings.Muted;
        AspectSwitch.IsOn = _settings.KeepAspectRatio;
        TopmostSwitch.IsOn = _settings.AlwaysOnTop;
        _audio.Volume = (float)_settings.Volume;
        _audio.Muted = _settings.Muted;
        ApplyAspect();
        ApplyAlwaysOnTop();

        PopulateAudioDevices();
        var videoDevices = await VideoCaptureService.GetDevicesAsync();
        VideoDeviceBox.ItemsSource = videoDevices;
        var videoDevice = videoDevices.FirstOrDefault(d => d.Id == _settings.VideoDeviceId) ?? videoDevices.FirstOrDefault();
        VideoDeviceBox.SelectedItem = videoDevice;
        _populating = false;

        RestartAudio();

        if (videoDevice is null)
            ShowStatus("No video capture device found.");
        else
            await OpenVideoAsync(videoDevice);
    }

    private void PopulateAudioDevices()
    {
        var inputs = AudioPassthrough.GetInputDevices().Prepend(NoAudioInput).ToList();
        var outputs = AudioPassthrough.GetOutputDevices().Prepend(DefaultAudioOutput).ToList();

        AudioInputBox.ItemsSource = inputs;
        AudioInputBox.SelectedItem = inputs.FirstOrDefault(d => d.Id == _settings.AudioInputId)
            ?? inputs.FirstOrDefault(d => d.Name.Contains("Line", StringComparison.OrdinalIgnoreCase))
            ?? NoAudioInput;

        AudioOutputBox.ItemsSource = outputs;
        AudioOutputBox.SelectedItem = outputs.FirstOrDefault(d => d.Id == _settings.AudioOutputId) ?? DefaultAudioOutput;
    }

    // ---- Video ----

    private async Task OpenVideoAsync(VideoDevice device)
    {
        ShowStatus($"Opening {device.Name}…");
        try
        {
            await _video.OpenAsync(device.Id);

            _populating = true;
            FormatBox.ItemsSource = _video.Formats;
            _populating = false;

            await StartVideoAsync(device.Id == _settings.VideoDeviceId ? _settings.VideoFormat : null);
            _settings.VideoDeviceId = device.Id;
        }
        catch (Exception ex)
        {
            ShowStatus(VideoCaptureService.Describe(ex));
        }
    }

    private async Task StartVideoAsync(VideoFormat? format)
    {
        try
        {
            await _video.StartAsync(format);

            _populating = true;
            FormatBox.SelectedItem = _video.CurrentFormat;
            _populating = false;

            _settings.VideoFormat = _video.CurrentFormat;
            HideStatus();
        }
        catch (Exception ex)
        {
            ShowStatus(VideoCaptureService.Describe(ex));
        }
    }

    private async void VideoDeviceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_populating || VideoDeviceBox.SelectedItem is not VideoDevice device)
            return;
        await OpenVideoAsync(device);
    }

    private async void FormatBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_populating || FormatBox.SelectedItem is not VideoFormat format || format == _video.CurrentFormat)
            return;
        await StartVideoAsync(format);
    }

    // ---- Audio ----

    private void RestartAudio()
    {
        var input = AudioInputBox.SelectedItem as AudioDevice ?? NoAudioInput;
        var output = AudioOutputBox.SelectedItem as AudioDevice ?? DefaultAudioOutput;
        _settings.AudioInputId = input == NoAudioInput ? null : input.Id;
        _settings.AudioOutputId = output == DefaultAudioOutput ? null : output.Id;

        _audio.Stop();
        if (input == NoAudioInput)
            return;

        try
        {
            _audio.Start(input.Id, output == DefaultAudioOutput ? null : output.Id);
        }
        catch (Exception ex)
        {
            ShowStatus($"Audio error: {ex.Message}");
        }
    }

    private void AudioDeviceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_populating)
            RestartAudio();
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_populating)
            return;
        _settings.Volume = e.NewValue / 100;
        _audio.Volume = (float)_settings.Volume;
    }

    private void MuteSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_populating)
            return;
        _settings.Muted = MuteSwitch.IsOn;
        _audio.Muted = _settings.Muted;
    }

    // ---- Window ----

    private void AspectSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_populating)
            return;
        _settings.KeepAspectRatio = AspectSwitch.IsOn;
        ApplyAspect();
    }

    private void TopmostSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_populating)
            return;
        _settings.AlwaysOnTop = TopmostSwitch.IsOn;
        ApplyAlwaysOnTop();
    }

    private void ApplyAspect() =>
        VideoView.Stretch = _settings.KeepAspectRatio ? Stretch.Uniform : Stretch.Fill;

    private void ApplyAlwaysOnTop()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = _settings.AlwaysOnTop;
    }

    private bool IsFullscreen => AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;

    private void SetFullscreen(bool fullscreen)
    {
        if (fullscreen == IsFullscreen)
            return;
        AppWindow.SetPresenter(fullscreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);
        if (!fullscreen)
            ApplyAlwaysOnTop();
    }

    private void FullscreenButton_Click(object sender, RoutedEventArgs e) => SetFullscreen(!IsFullscreen);

    private void FullscreenAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SetFullscreen(!IsFullscreen);
        args.Handled = true;
    }

    private void EscapeAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!IsFullscreen)
            return;
        SetFullscreen(false);
        args.Handled = true;
    }

    private void Root_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (IsInside(e.OriginalSource, SettingsButton))
            return;
        SetFullscreen(!IsFullscreen);
    }

    private static bool IsInside(object element, DependencyObject ancestor)
    {
        for (var current = element as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current == ancestor)
                return true;
        }
        return false;
    }

    private void SettingsFlyout_Closed(object sender, object e) => _settings.Save();

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }

    private void HideStatus() => StatusText.Visibility = Visibility.Collapsed;

    private void OnClosed(object sender, WindowEventArgs args)
    {
        // hides window then tears everything down, delay for closing was angering me
        AppWindow.Hide();

        _settings.Save();
        _audio.Dispose();
        _video.Dispose();

        Environment.Exit(0);
    }
}
