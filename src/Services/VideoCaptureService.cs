using System.Diagnostics;
using System.Text.Json.Serialization;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace CaptureView.Services;

public sealed record VideoDevice(string Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record VideoFormat(uint Width, uint Height, uint FrameRateNumerator, uint FrameRateDenominator, string Subtype)
{
    [JsonIgnore]
    public double FrameRate => FrameRateDenominator == 0 ? 0 : (double)FrameRateNumerator / FrameRateDenominator;

    public override string ToString() => $"{Width}×{Height} @ {FrameRate:0.##} fps  {Subtype}";
}

/// <summary>
/// Opens a capture device through MediaCapture and feeds its color stream into a single
/// long-lived <see cref="MediaPlayer"/> that the UI attaches to once.
/// </summary>
public sealed class VideoCaptureService : IDisposable
{
    // MF_E_HW_MFT_FAILED_START_STREAMING: typically the device is held by another app.
    private const int DeviceInUseHResult = unchecked((int)0xC00D3704);

    private MediaCapture? _capture;
    private MediaFrameSource? _source;
    private MediaSource? _mediaSource;
    private Dictionary<VideoFormat, MediaFrameFormat> _formats = [];

    public VideoCaptureService()
    {
        Player = new MediaPlayer
        {
            RealTimePlayback = true,
            IsMuted = true,
            AutoPlay = false,
        };
        Player.CommandManager.IsEnabled = false;
    }

    public MediaPlayer Player { get; }

    public IReadOnlyList<VideoFormat> Formats { get; private set; } = [];

    public VideoFormat? CurrentFormat { get; private set; }

    /// <summary>Raised from a background thread when the device fails or disappears.</summary>
    public event EventHandler<string>? Failed;

    public static async Task<IReadOnlyList<VideoDevice>> GetDevicesAsync()
    {
        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        foreach (var d in devices)
            Debug.WriteLine($"Video device: {d.Name} ({d.Id})");
        return devices.Select(d => new VideoDevice(d.Id, d.Name)).ToList();
    }

    public static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException =>
            "Camera access is blocked. Turn on Settings → Privacy & security → Camera → \"Let desktop apps access your camera\".",
        _ when ex.HResult == DeviceInUseHResult =>
            "The capture device is in use by another app (OBS, Elgato software, Camera). Close it and select the device again.",
        _ => $"Video error: {ex.Message}",
    };

    /// <summary>Initializes the device and reads its supported formats. Does not start the preview.</summary>
    public async Task OpenAsync(string deviceId)
    {
        Close();

        var capture = new MediaCapture();
        try
        {
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = deviceId,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl,
                MemoryPreference = MediaCaptureMemoryPreference.Auto,
            });
        }
        catch
        {
            capture.Dispose();
            throw;
        }

        capture.Failed += (_, e) => Failed?.Invoke(this, $"Video device failed: {e.Message}");
        _capture = capture;

        _source = capture.FrameSources.Values
            .Where(s => s.Info.SourceKind == MediaFrameSourceKind.Color)
            .OrderBy(s => s.Info.MediaStreamType switch
            {
                MediaStreamType.VideoPreview => 0,
                MediaStreamType.VideoRecord => 1,
                _ => 2,
            })
            .FirstOrDefault()
            ?? throw new InvalidOperationException("The device has no color video stream.");

        _formats = [];
        foreach (var f in _source.SupportedFormats)
        {
            var format = new VideoFormat(
                f.VideoFormat.Width, f.VideoFormat.Height,
                f.FrameRate.Numerator, f.FrameRate.Denominator,
                f.Subtype);
            _formats.TryAdd(format, f);
        }

        Formats = _formats.Keys
            .OrderByDescending(f => f.Width * f.Height)
            .ThenByDescending(f => f.FrameRate)
            .ThenBy(f => f.Subtype == "NV12" ? 0 : 1)
            .ToList();

        foreach (var f in Formats)
            Debug.WriteLine($"  format: {f}");
    }

    /// <summary>
    /// Applies <paramref name="preferred"/> if the device supports it (otherwise a sensible default)
    /// and (re)starts playback on <see cref="Player"/>.
    /// </summary>
    public async Task StartAsync(VideoFormat? preferred)
    {
        if (_source is null)
            throw new InvalidOperationException("No device is open.");

        StopPlayback();

        var format = preferred is not null && _formats.ContainsKey(preferred) ? preferred : PickDefaultFormat();
        if (format is not null)
        {
            await _source.SetFormatAsync(_formats[format]);
            CurrentFormat = format;
            Debug.WriteLine($"Using format: {format}");
        }

        _mediaSource = MediaSource.CreateFromMediaFrameSource(_source);
        Player.Source = _mediaSource;
        Player.Play();
    }

    private VideoFormat? PickDefaultFormat() =>
        Formats.FirstOrDefault(f => f is { Width: 1920, Height: 1080, Subtype: "NV12" } && f.FrameRate >= 59)
        ?? Formats.FirstOrDefault(f => f is { Width: 1920, Height: 1080 } && f.FrameRate >= 59)
        ?? Formats.FirstOrDefault();

    private void StopPlayback()
    {
        Player.Pause();
        Player.Source = null;
        _mediaSource?.Dispose();
        _mediaSource = null;
    }

    public void Close()
    {
        StopPlayback();
        _source = null;
        _formats = [];
        Formats = [];
        CurrentFormat = null;
        _capture?.Dispose();
        _capture = null;
    }

    public void Dispose()
    {
        Close();
        Player.Dispose();
    }
}
